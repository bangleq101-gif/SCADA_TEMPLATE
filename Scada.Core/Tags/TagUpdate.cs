namespace Scada.Core.Tags;

public sealed record TagUpdate(
    string TagId,
    object? Value,
    TagQuality Quality,
    DateTimeOffset Timestamp);
