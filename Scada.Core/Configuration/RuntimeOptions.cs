using Scada.Core.Devices;
using Scada.Core.Tags;

namespace Scada.Core.Configuration;

public sealed class RuntimeOptions
{
    public string RuntimeId { get; set; } = "Runtime01";
    public PollingOptions Polling { get; set; } = new();
    public List<ScanGroupDefinition> ScanGroups { get; set; } =
    [
        new() { Name = "Fast", IntervalMilliseconds = 100 },
        new() { Name = "Normal", IntervalMilliseconds = 500 },
        new() { Name = "Slow", IntervalMilliseconds = 1_000 },
        new() { Name = "VerySlow", IntervalMilliseconds = 5_000 }
    ];
    public List<DeviceDefinition> Devices { get; set; } = [];
    public List<TagDefinition> Tags { get; set; } = [];
}
