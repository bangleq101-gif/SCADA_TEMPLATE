namespace Scada.Infrastructure.History.Influx;

public static class InfluxOutboxSchema
{
    public const int CurrentVersion = 1;

    public const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS InfluxOutbox
        (
            Id INTEGER PRIMARY KEY,
            SampleKey TEXT NOT NULL UNIQUE,
            DestinationFingerprint TEXT NOT NULL,
            State TEXT NOT NULL,
            RuntimeId TEXT NOT NULL,
            TagId TEXT NOT NULL,
            DataType TEXT NOT NULL,
            Quality TEXT NOT NULL,
            SourceTimestampUtcTicks INTEGER NOT NULL,
            RecordedAtUtcTicks INTEGER NOT NULL,
            RemoteTimestampNanoseconds INTEGER NOT NULL,
            TagSequence INTEGER NOT NULL,
            HasValue INTEGER NOT NULL,
            ValueBoolean INTEGER NULL,
            ValueInteger INTEGER NULL,
            ValueReal REAL NULL,
            ValueText TEXT NULL,
            EnqueuedAtUtcTicks INTEGER NOT NULL,
            LastAttemptUtcTicks INTEGER NULL,
            AttemptCount INTEGER NOT NULL DEFAULT 0,
            LastErrorCode TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS InfluxOutboxMetadata
        (
            DestinationFingerprint TEXT PRIMARY KEY,
            SyncedSamples INTEGER NOT NULL DEFAULT 0,
            RemoteRejectedSamples INTEGER NOT NULL DEFAULT 0,
            ExpiredSamples INTEGER NOT NULL DEFAULT 0,
            BufferFullRejections INTEGER NOT NULL DEFAULT 0,
            SyncFailures INTEGER NOT NULL DEFAULT 0,
            ConsecutiveFailures INTEGER NOT NULL DEFAULT 0,
            LastRemoteSuccessUtcTicks INTEGER NULL,
            LastErrorCode TEXT NULL,
            LastErrorMessage TEXT NULL,
            LastKnownRetentionSeconds INTEGER NULL
        );

        CREATE TABLE IF NOT EXISTS InfluxPointCounters
        (
            DestinationFingerprint TEXT NOT NULL,
            RuntimeId TEXT NOT NULL,
            TagId TEXT NOT NULL,
            LastRemoteTimestampNanoseconds INTEGER NOT NULL,
            PRIMARY KEY (DestinationFingerprint, RuntimeId, TagId)
        );
        """;

    public const string CreateIndexesSql = """
        CREATE INDEX IF NOT EXISTS IX_InfluxOutbox_Destination_State_Enqueued
        ON InfluxOutbox(DestinationFingerprint, State, EnqueuedAtUtcTicks, Id);

        CREATE INDEX IF NOT EXISTS IX_InfluxOutbox_Runtime_Tag_Recorded
        ON InfluxOutbox(RuntimeId, TagId, RecordedAtUtcTicks, Id);
        """;
}
