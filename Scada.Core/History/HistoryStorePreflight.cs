namespace Scada.Core.History;

public enum HistoryStorePreflightStatus
{
    Ready,
    Recoverable,
    Faulted
}

public sealed record HistoryStorePreflightResult(
    HistoryStorePreflightStatus Status,
    string? ErrorCode = null,
    string? ErrorMessage = null);
