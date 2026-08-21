using Scada.Core.Tags;
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
}
