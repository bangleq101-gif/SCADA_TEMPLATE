namespace Scada.Core.Tags;

public sealed record TagValue(
    string TagId,
    object? Value,
    TagQuality Quality,
    DateTimeOffset Timestamp,
    long Sequence);
