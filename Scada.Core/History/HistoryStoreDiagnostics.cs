namespace Scada.Core.History;

public enum HistoryStoreState
{
    Disabled,
    Starting,
    Connecting,
    Online,
    Offline,
    Buffering,
    Resynchronizing,
    ConfigurationRequired,
    Faulted,
    Stopping
}

public sealed record HistoryStoreDiagnosticsSnapshot(
    HistoryStoreState State,
    long PendingSamples,
    long OrphanedDestinationSamples,
    long SyncedSamples,
    long RemoteRejectedSamples,
    long ExpiredSamples,
    long BufferFullRejections,
    long SyncFailures,
    long ConsecutiveFailures,
    DateTimeOffset? LastRemoteSuccessUtc,
    string? LastErrorCode,
    string? LastErrorMessage);

public sealed record HistoryStoreOperationResult(
    bool Succeeded,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface IHistoryStoreDiagnostics
{
    HistoryStoreDiagnosticsSnapshot Snapshot { get; }

    Task<HistoryStoreOperationResult> ProbeAsync(CancellationToken cancellationToken);
}

public interface IHistoryStoreMaintenance
{
    Task<HistoryStoreOperationResult> ApplyRetentionAsync(CancellationToken cancellationToken);

    Task<HistoryStoreOperationResult> ClearCurrentBufferAsync(CancellationToken cancellationToken);

    Task<HistoryStoreOperationResult> ClearPreviousDestinationBufferAsync(
        string destinationFingerprint,
        CancellationToken cancellationToken);
}
