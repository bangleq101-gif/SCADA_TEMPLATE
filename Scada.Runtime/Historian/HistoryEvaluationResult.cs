using Scada.Core.History;

namespace Scada.Runtime.Historian;

public sealed record HistoryEvaluationResult(
    HistorySample? Sample,
    bool Rejected,
    string? RejectionReason,
    long? NextDueTimestamp)
{
    public static HistoryEvaluationResult Suppressed(long? nextDueTimestamp = null) =>
        new(null, false, null, nextDueTimestamp);

    public static HistoryEvaluationResult Invalid(string reason) =>
        new(null, true, reason, null);
}
