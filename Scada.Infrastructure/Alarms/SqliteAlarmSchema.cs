namespace Scada.Infrastructure.Alarms;

internal static class SqliteAlarmSchema
{
    public const int CurrentVersion = 1;

    public const string CreateSql = """
        CREATE TABLE IF NOT EXISTS AlarmMetadata
        (
            Id INTEGER PRIMARY KEY CHECK (Id = 1),
            CurrentSessionId TEXT NULL,
            CheckpointSessionId TEXT NULL,
            RuntimeId TEXT NULL,
            RecoveryTrusted INTEGER NOT NULL DEFAULT 0,
            ContinuitySequence INTEGER NOT NULL DEFAULT 0,
            UpdatedAtUtcTicks INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS AlarmEvents
        (
            Id INTEGER PRIMARY KEY,
            SessionId TEXT NOT NULL,
            EventSequence INTEGER NOT NULL,
            AlarmId TEXT NOT NULL,
            InstanceId TEXT NOT NULL,
            EventType TEXT NOT NULL,
            Severity TEXT NOT NULL,
            TimestampUtcTicks INTEGER NOT NULL,
            DefinitionFingerprint TEXT NOT NULL,
            SourceSequence INTEGER NULL,
            SourceTimestampUtcTicks INTEGER NULL,
            AcknowledgedBy TEXT NULL,
            UNIQUE(SessionId, EventSequence)
        );

        CREATE TABLE IF NOT EXISTS AlarmInstances
        (
            SessionId TEXT NOT NULL,
            AlarmId TEXT NOT NULL,
            InstanceId TEXT NOT NULL,
            LifecycleState TEXT NOT NULL,
            Severity TEXT NOT NULL,
            DefinitionFingerprint TEXT NOT NULL,
            ActivatedAtUtcTicks INTEGER NOT NULL,
            AcknowledgedAtUtcTicks INTEGER NULL,
            AcknowledgedBy TEXT NULL,
            LastSourceSequence INTEGER NOT NULL,
            LastSourceTimestampUtcTicks INTEGER NOT NULL,
            EvaluationQuality TEXT NOT NULL,
            PRIMARY KEY(SessionId, AlarmId)
        );

        CREATE INDEX IF NOT EXISTS IX_AlarmEvents_Timestamp
        ON AlarmEvents(TimestampUtcTicks, Id);
        CREATE INDEX IF NOT EXISTS IX_AlarmEvents_Alarm_Timestamp
        ON AlarmEvents(AlarmId, TimestampUtcTicks, Id);
        """;
}
