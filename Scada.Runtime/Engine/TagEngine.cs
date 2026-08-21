using System.Collections.Concurrent;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.Runtime.Engine;

public sealed class TagEngine(TagCache cache)
{
    private readonly ConcurrentDictionary<string, byte> _hasEverValidValue = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<TagValue> Apply(IReadOnlyList<DriverReadResult> results)
    {
        var values = new List<TagValue>(results.Count);
        foreach (var result in results)
        {
            var value = cache.Upsert(new TagUpdate(result.TagId, result.Value, result.Quality, result.Timestamp));
            values.Add(value);

            if (result.Quality == TagQuality.Good)
            {
                _hasEverValidValue[result.TagId] = 0;
            }
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
            if (_hasEverValidValue.ContainsKey(tag.Id) && cache.TryGet(tag.Id, out var current) && current is not null)
            {
                values.Add(cache.Upsert(new TagUpdate(
                    tag.Id,
                    current.Value,
                    TagQuality.Disconnected,
                    current.Timestamp)));
            }
            else
            {
                values.Add(cache.Upsert(new TagUpdate(
                    tag.Id,
                    null,
                    TagQuality.Disconnected,
                    transitionTimestamp)));
            }
        }

        return values;
    }
}
