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
            values.Add(cache.Upsert(new TagUpdate(result.TagId, result.Value, result.Quality, result.Timestamp)));
        }

        return values;
    }
}
