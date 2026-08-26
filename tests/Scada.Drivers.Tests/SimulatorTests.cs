using Scada.Core.Tags;
using Scada.Core.Devices;
using Scada.Drivers.Simulator;
using Xunit;

namespace Scada.Drivers.Tests;

public sealed class SimulatorTests
{
    [Fact]
    public void GeneratorProducesSmoothAnalogValue()
    {
        var generator = new SimulatorValueGenerator();
        var now = DateTimeOffset.UnixEpoch.AddSeconds(10);

        var value = generator.Generate("T1", "A1", TagDataType.Double, now);

        Assert.IsType<double>(value);
        Assert.InRange((double)value, 25, 75);
    }

    [Fact]
    public void GeneratorUsesStableSeedForFixedInputsAndTimestamp()
    {
        var generator = new SimulatorValueGenerator();
        var intTimestamp = DateTimeOffset.UnixEpoch.AddSeconds(1234);
        var booleanTimestamp = DateTimeOffset.UnixEpoch.AddSeconds(10);

        Assert.Equal(986, generator.Generate("T1", "A1", TagDataType.Int32, intTimestamp));
        Assert.False((bool)generator.Generate("T1", "A1", TagDataType.Boolean, booleanTimestamp));
    }

    [Fact]
    public async Task DriverReturnsOneGoodResultPerRequest()
    {
        var driver = new SimulatorPlcDriver(new SimulatorValueGenerator());
        var device = new Scada.Core.Devices.DeviceDefinition { Id = "SIM01", DriverType = "Simulator" };
        var results = await driver.ReadAsync(device, [new("T1", "A1", TagDataType.Boolean)], CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("T1", result.TagId);
        Assert.Equal(TagQuality.Good, result.Quality);
    }

    [Fact]
    public async Task EngineeringProviderBrowsesDeterministicCandidatesWithoutRuntimeReads()
    {
        var provider = new SimulatorEngineeringProvider();
        var device = new DeviceDefinition { Id = "SIM01", DriverType = "Simulator" };

        var first = await provider.BrowseAddressesAsync(device, CancellationToken.None);
        var second = await provider.BrowseAddressesAsync(device, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(["A1", "B1", "C1", "S1"], first.Select(candidate => candidate.Address));
    }

    [Fact]
    public void EngineeringProviderRejectsInvalidFaultConfiguration()
    {
        var provider = new SimulatorEngineeringProvider();
        var device = new DeviceDefinition
        {
            Id = "SIM01",
            DriverType = "Simulator",
            ConnectionOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SimulatorFaultOptions.FaultModeKey] = "UnknownMode"
            }
        };

        var issues = provider.Validate(device);

        var issue = Assert.Single(issues);
        Assert.Equal("SIMULATOR_OPTION_INVALID", issue.Code);
        Assert.Equal("Device", issue.ObjectType);
        Assert.Equal("SIM01", issue.ObjectId);
    }

    [Fact]
    public async Task BadQualityScenarioPreservesGeneratedValueWithoutWritingToPlc()
    {
        var driver = new SimulatorPlcDriver(new SimulatorValueGenerator());
        var device = new DeviceDefinition
        {
            Id = "SIM01",
            DriverType = "Simulator",
            ConnectionOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SimulatorFaultOptions.FaultModeKey] = nameof(SimulatorFaultMode.BadQuality)
            }
        };

        var result = Assert.Single(await driver.ReadAsync(
            device,
            [new("T1", "A1", TagDataType.Double)],
            CancellationToken.None));

        Assert.Equal(TagQuality.Bad, result.Quality);
        Assert.IsType<double>(result.Value);
    }

    [Fact]
    public async Task ReadFailureScenarioIsDeterministic()
    {
        var driver = new SimulatorPlcDriver(new SimulatorValueGenerator());
        var device = new DeviceDefinition
        {
            Id = "SIM01",
            DriverType = "Simulator",
            ConnectionOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SimulatorFaultOptions.FaultModeKey] = nameof(SimulatorFaultMode.ReadFailure)
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.ReadAsync(
            device,
            [new("T1", "A1", TagDataType.Double)],
            CancellationToken.None));
    }

    [Fact]
    public async Task ConnectFailureScenarioFailsBeforeRuntimeReads()
    {
        var driver = new SimulatorPlcDriver(new SimulatorValueGenerator());
        var device = new DeviceDefinition
        {
            Id = "SIM01",
            DriverType = "Simulator",
            ConnectionOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SimulatorFaultOptions.FaultModeKey] = nameof(SimulatorFaultMode.ConnectFailure)
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.ConnectAsync(device, CancellationToken.None));
    }

    [Fact]
    public async Task DisconnectedScenarioReturnsNullDisconnectedResults()
    {
        var driver = new SimulatorPlcDriver(new SimulatorValueGenerator());
        var device = new DeviceDefinition
        {
            Id = "SIM01",
            DriverType = "Simulator",
            ConnectionOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SimulatorFaultOptions.FaultModeKey] = nameof(SimulatorFaultMode.Disconnected)
            }
        };

        var result = Assert.Single(await driver.ReadAsync(
            device,
            [new("T1", "A1", TagDataType.Double)],
            CancellationToken.None));

        Assert.Null(result.Value);
        Assert.Equal(TagQuality.Disconnected, result.Quality);
    }

    [Fact]
    public void IntermittentFaultUsesStableDeviceAndTimestampPhase()
    {
        var device = new DeviceDefinition { Id = "SIM01", DriverType = "Simulator" };
        var options = new SimulatorFaultOptions(
            SimulatorFaultMode.IntermittentReadFailure,
            PeriodSeconds: 10,
            DurationSeconds: 2,
            PhaseSeconds: 0);

        Assert.False(options.IsFaultActive(device, DateTimeOffset.UnixEpoch));
        Assert.True(options.IsFaultActive(device, DateTimeOffset.UnixEpoch.AddSeconds(5)));
        Assert.True(options.IsFaultActive(device, DateTimeOffset.UnixEpoch.AddSeconds(6)));
        Assert.False(options.IsFaultActive(device, DateTimeOffset.UnixEpoch.AddSeconds(7)));
    }
}
