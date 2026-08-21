using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;
using Scada.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Scada.Infrastructure.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void ValidatorRejectsTagWithMissingDevice()
    {
        var options = new RuntimeOptions
        {
            Devices = [new DeviceDefinition { Id = "PLC01" }],
            Tags = [new TagDefinition { Id = "T1", DeviceId = "PLC02", Address = "D1" }]
        };

        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(options));
    }

    [Fact]
    public void ValidatorAcceptsValidConfiguration()
    {
        var options = new RuntimeOptions
        {
            Devices = [new DeviceDefinition { Id = "SIM01", DriverType = "Simulator" }],
            Tags = [new TagDefinition { Id = "T1", DeviceId = "SIM01", Address = "A1", DataType = TagDataType.Double }]
        };

        ConfigurationValidator.Validate(options);
    }

    [Fact]
    public void ValidatorRejectsEnabledTagWithMissingScanGroup()
    {
        var options = new RuntimeOptions
        {
            Devices = [new DeviceDefinition { Id = "SIM01", DriverType = "Simulator" }],
            Tags =
            [
                new TagDefinition
                {
                    Id = "T1",
                    DeviceId = "SIM01",
                    Address = "A1",
                    ScanGroup = "Unknown"
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(options));
    }

    [Fact]
    public void ValidatorRejectsDuplicateScanGroupNames()
    {
        var options = new RuntimeOptions
        {
            ScanGroups =
            [
                new() { Name = "Fast", IntervalMilliseconds = 100 },
                new() { Name = "fast", IntervalMilliseconds = 200 }
            ]
        };

        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(options));
    }

    [Fact]
    public void ValidatorRejectsNonPositiveScanGroupInterval()
    {
        var options = new RuntimeOptions
        {
            ScanGroups = [new() { Name = "Fast", IntervalMilliseconds = 0 }]
        };

        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(options));
    }

    [Fact]
    public void ConfigurationBindingDoesNotDuplicateDefaultScanGroups()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scada:RuntimeId"] = "Runtime-Test",
                ["Scada:ScanGroups:0:Name"] = "Fast",
                ["Scada:ScanGroups:0:IntervalMilliseconds"] = "100"
            })
            .Build();
        IServiceCollection services = new TestServiceCollection();

        services.AddScadaConfiguration(configuration);

        var descriptor = Assert.Single(services);
        var options = Assert.IsType<RuntimeOptions>(descriptor.ImplementationInstance);
        var scanGroup = Assert.Single(options.ScanGroups);
        Assert.Equal("Fast", scanGroup.Name);
        Assert.Equal(100, scanGroup.IntervalMilliseconds);
    }

    [Fact]
    public void ConfigurationBindingWithoutScanGroupsKeepsRuntimeDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scada:RuntimeId"] = "Runtime-Test"
            })
            .Build();
        IServiceCollection services = new TestServiceCollection();

        services.AddScadaConfiguration(configuration);

        var descriptor = Assert.Single(services);
        var options = Assert.IsType<RuntimeOptions>(descriptor.ImplementationInstance);
        Assert.Equal(4, options.ScanGroups.Count);
        Assert.Collection(
            options.ScanGroups,
            fast =>
            {
                Assert.Equal("Fast", fast.Name);
                Assert.Equal(100, fast.IntervalMilliseconds);
            },
            normal =>
            {
                Assert.Equal("Normal", normal.Name);
                Assert.Equal(500, normal.IntervalMilliseconds);
            },
            slow =>
            {
                Assert.Equal("Slow", slow.Name);
                Assert.Equal(1_000, slow.IntervalMilliseconds);
            },
            verySlow =>
            {
                Assert.Equal("VerySlow", verySlow.Name);
                Assert.Equal(5_000, verySlow.IntervalMilliseconds);
            });
    }

    private sealed class TestServiceCollection : List<ServiceDescriptor>, IServiceCollection
    {
    }
}
