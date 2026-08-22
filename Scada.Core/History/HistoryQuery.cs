namespace Scada.Core.History;

public sealed record HistoryQuery(
    string RuntimeId,
    string TagId,
    DateTimeOffset FromRecordedAtUtc,
    DateTimeOffset ToRecordedAtUtc,
    int Limit)
{
    public const int MaximumLimit = 10_000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RuntimeId))
        {
            throw new ArgumentException("RuntimeId is required.", nameof(RuntimeId));
        }

        if (string.IsNullOrWhiteSpace(TagId))
        {
            throw new ArgumentException("TagId is required.", nameof(TagId));
        }

        if (ToRecordedAtUtc <= FromRecordedAtUtc)
        {
            throw new ArgumentException("The query end time must be after the start time.", nameof(ToRecordedAtUtc));
        }

        if (Limit <= 0 || Limit > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(Limit), Limit, $"Limit must be between 1 and {MaximumLimit}.");
        }
    }
}
