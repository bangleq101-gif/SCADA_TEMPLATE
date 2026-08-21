using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;
using Scada.Infrastructure.Configuration;
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
}
