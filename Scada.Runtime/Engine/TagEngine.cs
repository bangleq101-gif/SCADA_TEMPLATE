using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.Runtime.Engine;

public sealed class TagEngine(TagCache cache)
{
    public IReadOnlyList<TagValue> Apply(IReadOnlyList<DriverReadResult> results)
    {
        var values = new List<TagValue>(results.Count);
        foreach (var result in results)
        {
            var value = cache.Upsert(new TagUpdate(result.TagId, result.Value, result.Quality, result.Timestamp));
            values.Add(value);
        }

        return values;
    }

    public IReadOnlyList<TagValue> MarkDeviceDisconnected(
        IEnumerable<TagDefinition> tags,
        DateTimeOffset transitionTimestamp)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var values = new List<TagValue>();
        foreach (var tag in tags)
        {
            values.Add(cache.Upsert(new TagUpdate(
                tag.Id,
                null,
                TagQuality.Disconnected,
                transitionTimestamp)));
        }

        return values;
    }
}
