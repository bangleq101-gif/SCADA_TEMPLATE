using Scada.Core.Alarms;
using Scada.Core.Tags;

namespace Scada.Runtime.Alarms;

public enum AlarmRuntimeState
{
    Disabled,
    Starting,
    Healthy,
    Degraded,
    Faulted,
    Stopping
}

public sealed record AlarmSnapshot(
    string AlarmId,
    string Name,
    string Message,
    Guid? InstanceId,
    AlarmLifecycleState State,
    AlarmSeverity Severity,
    bool IsPendingActivation,
    bool IsEvaluationAvailable,
    TagQuality EvaluationQuality,
    long LastSourceSequence,
    DateTimeOffset? LastSourceTimestampUtc,
    DateTimeOffset? TransitionTimestampUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    string? AcknowledgedBy);

public sealed record AlarmRuntimeSnapshot(
    AlarmRuntimeState State,
    IReadOnlyList<AlarmSnapshot> Alarms,
    int ConfiguredDefinitions,
    int DistinctTagSubscriptions,
    int PendingDeadlines,
    int PersistenceQueueDepth,
    long ActivatedTransitions,
    long AcknowledgedTransitions,
    long ReturnedTransitions,
    long ClosedTransitions,
    long ReactivatedTransitions,
    long RejectedEvaluations,
    long StaleTagUpdates,
    long SubscriberExceptions,
    long PersistedEvents,
    long RejectedPersistenceItems,
    long DroppedPersistenceItems,
    long AbandonedPersistenceItems,
    long PersistenceWriteFailures,
    bool RecoveryTrusted,
    int RecoveryUntrustedInstances,
    int OrphanedInstances,
    string? LastErrorCode,
    string? LastErrorMessage)
{
    public static AlarmRuntimeSnapshot Disabled { get; } = new(
        AlarmRuntimeState.Disabled, [], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, false, 0, 0, null, null);
}
