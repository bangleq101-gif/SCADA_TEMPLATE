using Scada.Core.Tags;

namespace Scada.Core.History;

public sealed record HistorySample(
    string RuntimeId,
    string TagId,
    TagDataType DataType,
    object? Value,
    TagQuality Quality,
    DateTimeOffset SourceTimestampUtc,
    DateTimeOffset RecordedAtUtc,
    long TagSequence);
