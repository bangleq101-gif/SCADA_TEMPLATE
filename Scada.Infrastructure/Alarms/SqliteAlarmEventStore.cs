using Microsoft.Data.Sqlite;
using Scada.Core.Alarms;
using Scada.Core.Tags;
using Scada.Infrastructure.History;
using Scada.Infrastructure.Persistence;

namespace Scada.Infrastructure.Alarms;

public sealed class SqliteAlarmEventStore : IAlarmEventStore
{
    private readonly ProjectPath? _projectPath;
    private readonly string _configuredPath;
    private string? _databasePath;

    public SqliteAlarmEventStore(ProjectPath? projectPath, string configuredPath)
    {
        _projectPath = projectPath;
        _configuredPath = configuredPath;
    }

    public string? DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _databasePath ??= AlarmDatabasePathResolver.Resolve(_projectPath, _configuredPath);
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            await using var connection = await OpenAsync(SqliteOpenMode.ReadWriteCreate, cancellationToken).ConfigureAwait(false);
            var version = await ReadVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version > SqliteAlarmSchema.CurrentVersion)
                throw new AlarmStoreException("ALARM_SQLITE_SCHEMA_TOO_NEW", $"Alarm SQLite schema {version} is newer than supported version {SqliteAlarmSchema.CurrentVersion}.");
            await ExecuteAsync(connection, SqliteAlarmSchema.CreateSql, cancellationToken).ConfigureAwait(false);
            if (version < SqliteAlarmSchema.CurrentVersion && !await HasColumnAsync(connection, "AlarmEvents", "SourceQuality", cancellationToken).ConfigureAwait(false))
                await ExecuteAsync(connection, "ALTER TABLE AlarmEvents ADD COLUMN SourceQuality TEXT NULL;", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "INSERT OR IGNORE INTO AlarmMetadata (Id) VALUES (1);", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, $"PRAGMA user_version={SqliteAlarmSchema.CurrentVersion};", cancellationToken).ConfigureAwait(false);
        }
        catch (AlarmStoreException) { throw; }
        catch (SqliteException exception) when (IsCorrupt(exception))
        {
            throw new AlarmStoreException("ALARM_SQLITE_CORRUPT", "Alarm database is corrupt or not a valid SQLite database.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new AlarmStoreException("ALARM_STORAGE_ACCESS_DENIED", "Access to Alarm persistence was denied.", exception);
        }
    }

    public async Task<AlarmRecoveryResult> LoadRecoveryAsync(CancellationToken cancellationToken)
    {
        _databasePath ??= AlarmDatabasePathResolver.Resolve(_projectPath, _configuredPath);
        if (!File.Exists(_databasePath)) return new(false, 0, []);
        try
        {
            await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken).ConfigureAwait(false);
            await using var metadata = connection.CreateCommand();
            metadata.CommandText = "SELECT CheckpointSessionId, RecoveryTrusted, ContinuitySequence FROM AlarmMetadata WHERE Id=1;";
            await using var reader = await metadata.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new(false, 0, []);
            var checkpointSession = reader.IsDBNull(0) ? null : reader.GetString(0);
            var trusted = reader.GetInt64(1) != 0;
            var continuity = reader.GetInt64(2);
            await reader.DisposeAsync().ConfigureAwait(false);
            var instances = checkpointSession is null
                ? []
                : await ReadInstancesAsync(connection, checkpointSession, cancellationToken).ConfigureAwait(false);
            return new(trusted, continuity, instances);
        }
        catch (SqliteException exception) when (IsCorrupt(exception))
        {
            throw new AlarmStoreException("ALARM_SQLITE_CORRUPT", "Alarm database is corrupt or not a valid SQLite database.", exception);
        }
    }

    public async Task BeginUntrustedSessionAsync(AlarmStoreSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWriteCreate, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE AlarmMetadata
            SET CurrentSessionId=$session, RuntimeId=$runtime, RecoveryTrusted=0, UpdatedAtUtcTicks=$updated
            WHERE Id=1;
            """;
        command.Parameters.AddWithValue("$session", request.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$runtime", request.RuntimeId);
        command.Parameters.AddWithValue("$updated", request.StartedAtUtc.UtcDateTime.Ticks);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new AlarmStoreException("ALARM_RECOVERY_MARKER_FAILED", "Alarm recovery-untrusted marker was not committed.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PersistBatchAsync(AlarmPersistenceBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWriteCreate, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var alarmEvent in batch.Events)
            await InsertEventAsync(connection, transaction, batch.SessionId, alarmEvent, cancellationToken).ConfigureAwait(false);
        await ReplaceInstancesAsync(connection, transaction, batch.SessionId, batch.OpenInstances, cancellationToken).ConfigureAwait(false);
        await using var metadata = connection.CreateCommand();
        metadata.Transaction = transaction;
        metadata.CommandText = "UPDATE AlarmMetadata SET ContinuitySequence=$sequence WHERE Id=1 AND CurrentSessionId=$session;";
        metadata.Parameters.AddWithValue("$sequence", batch.ContinuitySequence);
        metadata.Parameters.AddWithValue("$session", batch.SessionId.ToString("D"));
        if (await metadata.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new AlarmStoreException("ALARM_SESSION_MISMATCH", "Alarm persistence batch does not belong to the current session.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitTrustedCheckpointAsync(AlarmStoreCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!checkpoint.RecoveryTrusted)
            throw new ArgumentException("A final checkpoint must explicitly be trusted.", nameof(checkpoint));
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWriteCreate, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ReplaceInstancesAsync(connection, transaction, checkpoint.SessionId, checkpoint.OpenInstances, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE AlarmMetadata
            SET CheckpointSessionId=$session, RecoveryTrusted=1, ContinuitySequence=$sequence, UpdatedAtUtcTicks=$updated
            WHERE Id=1 AND CurrentSessionId=$session;
            """;
        command.Parameters.AddWithValue("$session", checkpoint.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", checkpoint.ContinuitySequence);
        command.Parameters.AddWithValue("$updated", checkpoint.UpdatedAtUtc.UtcDateTime.Ticks);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new AlarmStoreException("ALARM_SESSION_MISMATCH", "Alarm trusted checkpoint does not belong to the current session.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.ToUtc <= query.FromUtc || query.Limit <= 0) throw new ArgumentException("Alarm event query range and limit are invalid.", nameof(query));
        _databasePath ??= AlarmDatabasePathResolver.Resolve(_projectPath, _configuredPath);
        if (!File.Exists(_databasePath)) return [];
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EventSequence, AlarmId, InstanceId, EventType, Severity, TimestampUtcTicks,
                   DefinitionFingerprint, SourceSequence, SourceTimestampUtcTicks, SourceQuality, AcknowledgedBy
            FROM AlarmEvents
            WHERE TimestampUtcTicks >= $from AND TimestampUtcTicks < $to
              AND ($alarmId IS NULL OR AlarmId = $alarmId)
            ORDER BY TimestampUtcTicks, Id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$from", query.FromUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$to", query.ToUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$alarmId", query.AlarmId is null ? DBNull.Value : query.AlarmId);
        command.Parameters.AddWithValue("$limit", query.Limit);
        var events = new List<AlarmEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(new AlarmEvent(
                reader.GetInt64(0), reader.GetString(1), Guid.Parse(reader.GetString(2)),
                Enum.Parse<AlarmEventType>(reader.GetString(3)), Enum.Parse<AlarmSeverity>(reader.GetString(4)),
                new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : new DateTimeOffset(reader.GetInt64(8), TimeSpan.Zero),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(9) ? null : Enum.Parse<TagQuality>(reader.GetString(9))));
        }
        return events;
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken cancellationToken)
    {
        _databasePath ??= AlarmDatabasePathResolver.Resolve(_projectPath, _configuredPath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (mode == SqliteOpenMode.ReadOnly)
                await SqliteConnectionConfiguration.ConfigureReadAsync(connection, cancellationToken).ConfigureAwait(false);
            else
                await SqliteConnectionConfiguration.ConfigureWriteAsync(connection, enableWal: false, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task InsertEventAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, AlarmEvent alarmEvent, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AlarmEvents
            (SessionId, EventSequence, AlarmId, InstanceId, EventType, Severity, TimestampUtcTicks,
             DefinitionFingerprint, SourceSequence, SourceTimestampUtcTicks, SourceQuality, AcknowledgedBy)
            VALUES ($session,$sequence,$alarm,$instance,$type,$severity,$timestamp,$fingerprint,$sourceSequence,$sourceTimestamp,$sourceQuality,$acknowledgedBy);
            """;
        command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", alarmEvent.Sequence);
        command.Parameters.AddWithValue("$alarm", alarmEvent.AlarmId);
        command.Parameters.AddWithValue("$instance", alarmEvent.InstanceId.ToString("D"));
        command.Parameters.AddWithValue("$type", alarmEvent.Type.ToString());
        command.Parameters.AddWithValue("$severity", alarmEvent.Severity.ToString());
        command.Parameters.AddWithValue("$timestamp", alarmEvent.TimestampUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$fingerprint", alarmEvent.DefinitionFingerprint);
        command.Parameters.AddWithValue("$sourceSequence", alarmEvent.SourceSequence is null ? DBNull.Value : alarmEvent.SourceSequence);
        command.Parameters.AddWithValue("$sourceTimestamp", alarmEvent.SourceTimestampUtc is null ? DBNull.Value : alarmEvent.SourceTimestampUtc.Value.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$sourceQuality", alarmEvent.SourceQuality is null ? DBNull.Value : alarmEvent.SourceQuality.Value.ToString());
        command.Parameters.AddWithValue("$acknowledgedBy", alarmEvent.AcknowledgedBy is null ? DBNull.Value : alarmEvent.AcknowledgedBy);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplaceInstancesAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, IReadOnlyList<AlarmInstanceRecord> instances, CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM AlarmInstances WHERE SessionId=$session;";
            delete.Parameters.AddWithValue("$session", sessionId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (var instance in instances)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO AlarmInstances
                (SessionId, AlarmId, InstanceId, LifecycleState, Severity, DefinitionFingerprint,
                 ActivatedAtUtcTicks, AcknowledgedAtUtcTicks, AcknowledgedBy, LastSourceSequence,
                 LastSourceTimestampUtcTicks, EvaluationQuality)
                VALUES ($session,$alarm,$instance,$state,$severity,$fingerprint,$activated,$acknowledged,$by,$sourceSequence,$sourceTimestamp,$quality);
                """;
            command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
            command.Parameters.AddWithValue("$alarm", instance.AlarmId);
            command.Parameters.AddWithValue("$instance", instance.InstanceId.ToString("D"));
            command.Parameters.AddWithValue("$state", instance.State.ToString());
            command.Parameters.AddWithValue("$severity", instance.Severity.ToString());
            command.Parameters.AddWithValue("$fingerprint", instance.DefinitionFingerprint);
            command.Parameters.AddWithValue("$activated", instance.ActivatedAtUtc.UtcDateTime.Ticks);
            command.Parameters.AddWithValue("$acknowledged", instance.AcknowledgedAtUtc is null ? DBNull.Value : instance.AcknowledgedAtUtc.Value.UtcDateTime.Ticks);
            command.Parameters.AddWithValue("$by", instance.AcknowledgedBy is null ? DBNull.Value : instance.AcknowledgedBy);
            command.Parameters.AddWithValue("$sourceSequence", instance.LastSourceSequence);
            command.Parameters.AddWithValue("$sourceTimestamp", instance.LastSourceTimestampUtc.UtcDateTime.Ticks);
            command.Parameters.AddWithValue("$quality", instance.EvaluationQuality.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<AlarmInstanceRecord>> ReadInstancesAsync(SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AlarmId, InstanceId, LifecycleState, Severity, DefinitionFingerprint,
                   ActivatedAtUtcTicks, AcknowledgedAtUtcTicks, AcknowledgedBy,
                   LastSourceSequence, LastSourceTimestampUtcTicks, EvaluationQuality
            FROM AlarmInstances WHERE SessionId=$session ORDER BY AlarmId;
            """;
        command.Parameters.AddWithValue("$session", sessionId);
        var instances = new List<AlarmInstanceRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            instances.Add(new AlarmInstanceRecord(
                reader.GetString(0), Guid.Parse(reader.GetString(1)), Enum.Parse<AlarmLifecycleState>(reader.GetString(2)),
                Enum.Parse<AlarmSeverity>(reader.GetString(3)), reader.GetString(4),
                new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
                reader.IsDBNull(6) ? null : new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8),
                new DateTimeOffset(reader.GetInt64(9), TimeSpan.Zero), Enum.Parse<TagQuality>(reader.GetString(10))));
        }
        return instances;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> ReadVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsCorrupt(SqliteException exception) => exception.SqliteErrorCode is 11 or 26;
}
