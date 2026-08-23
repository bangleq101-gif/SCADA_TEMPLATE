namespace Scada.Runtime.Historian;

public enum HistorianRuntimeState
{
    Disabled,
    Starting,
    Healthy,
    Degraded,
    Faulted,
    Stopping
}

public sealed record HistorianRuntimeSnapshot(
    HistorianRuntimeState State,
    int QueueCapacity,
    int QueueDepth,
    long EnqueuedSamples,
    long WrittenSamples,
    long RejectedSamples,
    long DroppedSamples,
    long AbandonedSamples,
    long WriteFailures,
    DateTimeOffset? LastWriteUtc,
    string? LastErrorCode,
    string? LastErrorMessage);
