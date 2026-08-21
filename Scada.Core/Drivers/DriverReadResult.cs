using Scada.Core.Tags;

namespace Scada.Core.Drivers;

public sealed record DriverReadResult(
    string TagId,
    object? Value,
    TagQuality Quality,
    DateTimeOffset Timestamp);
