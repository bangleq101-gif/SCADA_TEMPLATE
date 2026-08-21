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
            Devices = [new DeviceDefinition { Id = "SIM01" }],
            Tags = [new TagDefinition { Id = "T1", DeviceId = "SIM01", Address = "A1", DataType = TagDataType.Double }]
        };

        ConfigurationValidator.Validate(options);
    }
}
