namespace Scada.Infrastructure.History;

public static class SqliteHistorySchema
{
    public const int CurrentVersion = 1;

    public const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS HistorySamples
        (
            Id INTEGER PRIMARY KEY,
            RuntimeId TEXT NOT NULL,
            TagId TEXT NOT NULL,
            DataType TEXT NOT NULL,
            Quality TEXT NOT NULL,
            SourceTimestampUtcTicks INTEGER NOT NULL,
            RecordedAtUtcTicks INTEGER NOT NULL,
            TagSequence INTEGER NOT NULL,
            HasValue INTEGER NOT NULL,
            ValueInteger INTEGER NULL,
            ValueReal REAL NULL,
            ValueText TEXT NULL
        );
        """;

    public const string CreateIndexSql = """
        CREATE INDEX IF NOT EXISTS IX_HistorySamples_Runtime_Tag_Recorded
        ON HistorySamples(RuntimeId, TagId, RecordedAtUtcTicks, Id);
        """;
}
