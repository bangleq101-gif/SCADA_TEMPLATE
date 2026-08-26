using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;

namespace Scada.Runtime.Polling;

public sealed class DevicePollingPlan
{
    private DevicePollingPlan(
        IReadOnlyList<DeviceScanGroupPlan> groups,
        IReadOnlyList<TagDefinition> tags)
    {
        Groups = groups;
        Tags = tags;
    }

    public IReadOnlyList<DeviceScanGroupPlan> Groups { get; }
    public IReadOnlyList<TagDefinition> Tags { get; }

    public static DevicePollingPlan Create(
        DeviceDefinition device,
        IEnumerable<TagDefinition> tags,
        IEnumerable<ScanGroupDefinition> scanGroups)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(scanGroups);

        var definitions = scanGroups.ToDictionary(
            group => group.Name,
            StringComparer.OrdinalIgnoreCase);
        var deviceTags = tags
            .Where(tag => tag.Enabled && string.Equals(tag.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var groups = new List<DeviceScanGroupPlan>();
        foreach (var group in definitions.Values)
        {
            var requests = deviceTags
                .Where(tag => string.Equals(tag.ScanGroup, group.Name, StringComparison.OrdinalIgnoreCase))
                .Select(tag => new DriverReadRequest(tag.Id, tag.Address, tag.GetEffectiveSourceDataType()))
                .ToArray();

            if (requests.Length > 0)
            {
                groups.Add(new DeviceScanGroupPlan(
                    group.Name,
                    TimeSpan.FromMilliseconds(group.IntervalMilliseconds),
                    requests));
            }
        }

        return new DevicePollingPlan(groups, deviceTags);
    }
}

public sealed record DeviceScanGroupPlan(
    string Name,
    TimeSpan Interval,
    IReadOnlyList<DriverReadRequest> Requests);
