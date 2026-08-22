using Microsoft.Data.Sqlite;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Infrastructure.Persistence;

namespace Scada.Infrastructure.History;

public sealed class SqliteHistoryStore : IHistoryStore
{
    private static readonly string[] RequiredHistoryColumns =
    [
        "Id",
        "RuntimeId",
        "TagId",
        "DataType",
        "Quality",
        "SourceTimestampUtcTicks",
        "RecordedAtUtcTicks",
        "TagSequence",
        "HasValue",
        "ValueInteger",
        "ValueReal",
        "ValueText"
    ];

    private readonly ProjectPath? _projectPath;
    private readonly string _configuredPath;
    private string? _databasePath;

    public SqliteHistoryStore(ProjectPath? projectPath, string configuredPath)
    {
        _projectPath = projectPath;
        _configuredPath = configuredPath;
    }

    public string? DatabasePath => _databasePath;

    public HistoryStorePreflightResult Preflight()
    {
        try
        {
            _databasePath = SqliteHistoryPathResolver.Resolve(_projectPath, _configuredPath);
            if (!File.Exists(_databasePath))
            {
                return new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready);
            }

            using var connection = new SqliteConnection(CreateConnectionString(_databasePath, SqliteOpenMode.ReadOnly));
            connection.Open();
            var version = ReadUserVersion(connection);
            if (version > SqliteHistorySchema.CurrentVersion)
            {
                throw new HistoryStorePermanentException(
                    "HISTORIAN_SQLITE_SCHEMA_TOO_NEW",
                    $"SQLite history schema version {version} is newer than supported version {SqliteHistorySchema.CurrentVersion}.");
            }

            ValidateExistingSchema(connection, version);

            return new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready);
        }
        catch (HistoryStorePermanentException exception)
        {
            return new HistoryStorePreflightResult(
                HistoryStorePreflightStatus.Faulted,
                exception.Code,
                exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new HistoryStorePreflightResult(
                HistoryStorePreflightStatus.Faulted,
                "HISTORIAN_STORAGE_ACCESS_DENIED",
                exception.Message);
        }
        catch (SqliteException exception) when (IsCorrupt(exception))
        {
            return new HistoryStorePreflightResult(
                HistoryStorePreflightStatus.Faulted,
                "HISTORIAN_SQLITE_CORRUPT",
                exception.Message);
        }
        catch (Exception exception)
        {
            return new HistoryStorePreflightResult(
                HistoryStorePreflightStatus.Recoverable,
                "HISTORIAN_SQLITE_PREFLIGHT",
                exception.Message);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _databasePath ??= SqliteHistoryPathResolver.Resolve(_projectPath, _configuredPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        await using var connection = new SqliteConnection(CreateConnectionString(_databasePath, SqliteOpenMode.ReadWriteCreate));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=250;",
            cancellationToken).ConfigureAwait(false);

        var version = ReadUserVersion(connection);
        if (version > SqliteHistorySchema.CurrentVersion)
        {
            throw new HistoryStorePermanentException(
                "HISTORIAN_SQLITE_SCHEMA_TOO_NEW",
                $"SQLite history schema version {version} is newer than supported version {SqliteHistorySchema.CurrentVersion}.");
        }

        ValidateExistingSchema(connection, version);

        await ExecuteAsync(connection, SqliteHistorySchema.CreateTableSql, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, SqliteHistorySchema.CreateIndexSql, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, $"PRAGMA user_version={SqliteHistorySchema.CurrentVersion};", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return;
        }

        _databasePath ??= SqliteHistoryPathResolver.Resolve(_projectPath, _configuredPath);
        await using var connection = new SqliteConnection(CreateConnectionString(_databasePath, SqliteOpenMode.ReadWriteCreate));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var sample in samples)
        {
            await InsertAsync(connection, transaction, sample, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistorySample>> QueryAsync(
        HistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        _databasePath ??= SqliteHistoryPathResolver.Resolve(_projectPath, _configuredPath);

        if (!File.Exists(_databasePath))
        {
            return [];
        }

        await using var connection = new SqliteConnection(CreateConnectionString(_databasePath, SqliteOpenMode.ReadOnly));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RuntimeId, TagId, DataType, Quality,
                   SourceTimestampUtcTicks, RecordedAtUtcTicks,
                   TagSequence, HasValue, ValueInteger, ValueReal, ValueText
            FROM HistorySamples
            WHERE RuntimeId = $runtimeId
              AND TagId = $tagId
              AND RecordedAtUtcTicks >= $fromTicks
              AND RecordedAtUtcTicks < $toTicks
            ORDER BY RecordedAtUtcTicks ASC, Id ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$runtimeId", query.RuntimeId);
        command.Parameters.AddWithValue("$tagId", query.TagId);
        command.Parameters.AddWithValue("$fromTicks", query.FromRecordedAtUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$toTicks", query.ToRecordedAtUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$limit", query.Limit);

        var results = new List<HistorySample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataType = Enum.Parse<TagDataType>(reader.GetString(2), ignoreCase: true);
            var quality = Enum.Parse<TagQuality>(reader.GetString(3), ignoreCase: true);
            var hasValue = reader.GetInt64(7) != 0;
            object? value = null;
            if (hasValue)
            {
                value = dataType switch
                {
                    TagDataType.Boolean => reader.GetInt64(8) != 0,
                    TagDataType.Int32 => checked((int)reader.GetInt64(8)),
                    TagDataType.Int64 => reader.GetInt64(8),
                    TagDataType.Double => reader.GetDouble(9),
                    TagDataType.String => reader.GetString(10),
                    _ => throw new InvalidOperationException($"Unsupported stored data type '{dataType}'.")
                };
            }

            results.Add(new HistorySample(
                reader.GetString(0),
                reader.GetString(1),
                dataType,
                value,
                quality,
                new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero),
                new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
                reader.GetInt64(6)));
        }

        return results;
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistorySample sample,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO HistorySamples
            (
                RuntimeId, TagId, DataType, Quality,
                SourceTimestampUtcTicks, RecordedAtUtcTicks,
                TagSequence, HasValue, ValueInteger, ValueReal, ValueText
            )
            VALUES
            (
                $runtimeId, $tagId, $dataType, $quality,
                $sourceTicks, $recordedTicks,
                $sequence, $hasValue, $valueInteger, $valueReal, $valueText
            );
            """;
        command.Parameters.AddWithValue("$runtimeId", sample.RuntimeId);
        command.Parameters.AddWithValue("$tagId", sample.TagId);
        command.Parameters.AddWithValue("$dataType", sample.DataType.ToString());
        command.Parameters.AddWithValue("$quality", sample.Quality.ToString());
        command.Parameters.AddWithValue("$sourceTicks", sample.SourceTimestampUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$recordedTicks", sample.RecordedAtUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$sequence", sample.TagSequence);
        command.Parameters.AddWithValue("$hasValue", sample.Value is null ? 0 : 1);

        long? valueInteger = sample.DataType switch
        {
            TagDataType.Boolean when sample.Value is bool boolean => boolean ? 1L : 0L,
            TagDataType.Int32 when sample.Value is int int32 => int32,
            TagDataType.Int64 when sample.Value is long int64 => int64,
            _ => null
        };
        double? valueReal = sample.DataType == TagDataType.Double && sample.Value is double doubleValue
            ? doubleValue
            : null;
        var valueText = sample.DataType == TagDataType.String && sample.Value is string text
            ? text
            : null;
        command.Parameters.AddWithValue("$valueInteger", valueInteger is null ? DBNull.Value : valueInteger);
        command.Parameters.AddWithValue("$valueReal", valueReal is null ? DBNull.Value : valueReal);
        command.Parameters.AddWithValue("$valueText", valueText is null ? DBNull.Value : valueText);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static long ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateExistingSchema(SqliteConnection connection, long version)
    {
        using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'HistorySamples' LIMIT 1;";
        var tableExists = existsCommand.ExecuteScalar() is not null;
        if (!tableExists)
        {
            if (version >= SqliteHistorySchema.CurrentVersion)
            {
                throw new HistoryStorePermanentException(
                    "HISTORIAN_SQLITE_SCHEMA_INVALID",
                    "SQLite history database is missing the HistorySamples table.");
            }

            return;
        }

        using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(HistorySamples);";
        using var reader = columnsCommand.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        if (RequiredHistoryColumns.Any(column => !columns.Contains(column)))
        {
            throw new HistoryStorePermanentException(
                "HISTORIAN_SQLITE_SCHEMA_INVALID",
                "SQLite history database has an incompatible HistorySamples table.");
        }
    }

    private static string CreateConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Shared
        }.ToString();

    private static bool IsCorrupt(SqliteException exception) =>
        exception.SqliteErrorCode is 11 or 26;
}
