using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.Runtime.Engine;

public sealed class TagEngine
{
    private readonly TagCache _cache;
    private readonly IReadOnlyDictionary<string, TagDefinition> _definitions;
    private readonly ILogger<TagEngine> _logger;
    private readonly ConcurrentDictionary<string, string> _activeTransformationFailures =
        new(StringComparer.OrdinalIgnoreCase);

    public TagEngine(TagCache cache)
        : this(cache, [], NullLogger<TagEngine>.Instance)
    {
    }

    public TagEngine(TagCache cache, RuntimeOptions options, ILogger<TagEngine> logger)
        : this(cache, options?.Tags ?? [], logger)
    {
    }

    private TagEngine(TagCache cache, IEnumerable<TagDefinition> definitions, ILogger<TagEngine> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _definitions = definitions
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Id))
            .GroupBy(tag => tag.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<TagValue> Apply(IReadOnlyList<DriverReadResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var values = new List<TagValue>(results.Count);
        foreach (var result in results)
        {
            var update = CreateUpdate(result);
            var value = _cache.Upsert(update);
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
            values.Add(_cache.Upsert(new TagUpdate(
                tag.Id,
                null,
                TagQuality.Disconnected,
                transitionTimestamp)));
        }

        return values;
    }

    private TagUpdate CreateUpdate(DriverReadResult result)
    {
        if (result.Quality != TagQuality.Good ||
            !_definitions.TryGetValue(result.TagId, out var definition))
        {
            return new TagUpdate(result.TagId, result.Value, result.Quality, result.Timestamp);
        }

        if (TagValueTransformer.TryTransform(definition, result.Value, out var engineeringValue, out var failure))
        {
            if (_activeTransformationFailures.TryRemove(result.TagId, out var previousFailure))
            {
                _logger.LogInformation(
                    "Tag engineering transform recovered for {TagId} after {PreviousFailure}",
                    result.TagId,
                    previousFailure);
            }

            return new TagUpdate(result.TagId, engineeringValue, TagQuality.Good, result.Timestamp);
        }

        var message = failure ?? "The tag engineering transform failed.";
        if (_activeTransformationFailures.TryAdd(result.TagId, message))
        {
            _logger.LogWarning(
                "Tag engineering transform failed for {TagId}: {Failure}",
                result.TagId,
                message);
        }

        return new TagUpdate(result.TagId, null, TagQuality.Bad, result.Timestamp);
    }
}
