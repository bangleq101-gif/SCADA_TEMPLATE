using Scada.Core.Devices;
using Scada.Core.Tags;

namespace Scada.Core.Configuration;

public sealed class RuntimeOptions
{
    public string RuntimeId { get; set; } = "Runtime01";
    public int PollingIntervalMilliseconds { get; set; } = 500;
    public List<DeviceDefinition> Devices { get; set; } = [];
    public List<TagDefinition> Tags { get; set; } = [];
}
