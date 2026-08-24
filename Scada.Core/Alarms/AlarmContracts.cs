using Scada.Core.Tags;

namespace Scada.Core.Alarms;

public enum AlarmEventType
{
    Activated,
    Acknowledged,
    Returned,
    Closed,
    Reactivated,
    OrphanedDefinitionMissing,
    RetiredDefinitionDisabled,
    OrphanedConfigurationChanged
}

public enum AlarmAcknowledgementStatus
{
    Acknowledged,
    AlreadyAcknowledged,
    StaleOrNotFound,
    NotEligible,
    RuntimeUnavailable
}

public sealed record AlarmAcknowledgementRequest(Guid InstanceId, string? AcknowledgedBy = null);

public sealed record AlarmAcknowledgementResult(
    Guid InstanceId,
    AlarmAcknowledgementStatus Status,
    DateTimeOffset? AcknowledgedAtUtc = null);

public sealed record AlarmEvent(
    long Sequence,
    string AlarmId,
    Guid InstanceId,
    AlarmEventType Type,
    AlarmSeverity Severity,
    DateTimeOffset TimestampUtc,
    string DefinitionFingerprint,
    long? SourceSequence = null,
    DateTimeOffset? SourceTimestampUtc = null,
    string? AcknowledgedBy = null);

public sealed record AlarmInstanceRecord(
    string AlarmId,
    Guid InstanceId,
    AlarmLifecycleState State,
    AlarmSeverity Severity,
    string DefinitionFingerprint,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    string? AcknowledgedBy,
    long LastSourceSequence,
    DateTimeOffset LastSourceTimestampUtc,
    TagQuality EvaluationQuality);

public sealed record AlarmPersistenceBatch(
    Guid SessionId,
    IReadOnlyList<AlarmEvent> Events,
    IReadOnlyList<AlarmInstanceRecord> OpenInstances,
    long ContinuitySequence);

public sealed record AlarmStoreCheckpoint(
    Guid SessionId,
    bool RecoveryTrusted,
    long ContinuitySequence,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<AlarmInstanceRecord> OpenInstances);

public sealed record AlarmRecoveryResult(
    bool RecoveryTrusted,
    long ContinuitySequence,
    IReadOnlyList<AlarmInstanceRecord> OpenInstances,
    int OrphanedInstanceCount = 0,
    string? DiagnosticCode = null,
    string? DiagnosticMessage = null);

public sealed record AlarmStoreSessionRequest(
    Guid SessionId,
    string RuntimeId,
    DateTimeOffset StartedAtUtc);

public sealed record AlarmEventQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? AlarmId = null,
    int Limit = 1_000);

public interface IAlarmEventStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<AlarmRecoveryResult> LoadRecoveryAsync(CancellationToken cancellationToken);
    Task BeginUntrustedSessionAsync(AlarmStoreSessionRequest request, CancellationToken cancellationToken);
    Task PersistBatchAsync(AlarmPersistenceBatch batch, CancellationToken cancellationToken);
    Task CommitTrustedCheckpointAsync(AlarmStoreCheckpoint checkpoint, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmEventQuery query, CancellationToken cancellationToken);
}
