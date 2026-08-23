using Microsoft.Data.Sqlite;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Infrastructure.Persistence;

namespace Scada.Infrastructure.History.Influx;

public sealed record InfluxOutboxRow(
    long Id,
    string SampleKey,
    string DestinationFingerprint,
    HistorySample Sample,
    long RemoteTimestampNanoseconds,
    DateTimeOffset EnqueuedAtUtc);

public sealed record InfluxSampleRejection(string ErrorCode, string ErrorMessage);

public sealed record InfluxAppendResult(
    int AcceptedCount,
    int DuplicateCount,
    int RejectedCount,
    IReadOnlyList<InfluxSampleRejection> Rejections);

public sealed record InfluxOutboxDiagnostics(
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
    string? LastErrorMessage,
    long? LastKnownRetentionSeconds);

public sealed class InfluxOutboxStore : IAsyncDisposable
{
    private const string PendingState = "Pending";
    private readonly ProjectPath? _projectPath;
    private readonly string _configuredPath;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private string? _databasePath;
    private bool _initialized;
    private bool _disposed;

    public InfluxOutboxStore(ProjectPath? projectPath, string configuredPath)
    {
        _configuredPath = configuredPath;
        _projectPath = projectPath;
    }

    public string? DatabasePath => _databasePath;

    public async Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken)
    {
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            var databasePath = GetDatabasePath();
            if (!File.Exists(databasePath))
            {
                return new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready);
            }

            await using var connection = await OpenConnectionAsync(databasePath, SqliteOpenMode.ReadOnly, cancellationToken)
                .ConfigureAwait(false);
            var version = await ReadUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version > InfluxOutboxSchema.CurrentVersion)
            {
                throw new HistoryStorePermanentException(
                    "INFLUX_OUTBOX_SCHEMA_TOO_NEW",
                    $"Influx outbox schema version {version} is newer than supported version {InfluxOutboxSchema.CurrentVersion}.");
            }

            await ValidateExistingSchemaAsync(connection, version, cancellationToken).ConfigureAwait(false);
            return new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HistoryStorePermanentException exception)
        {
            return new HistoryStorePreflightResult(HistoryStorePreflightStatus.Faulted, exception.Code, exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return new HistoryStorePreflightResult(
                HistoryStorePreflightStatus.Faulted,
                "INFLUX_BUFFER_ACCESS_DENIED",
                "Access to the InfluxDB local buffer was denied.");
        }
        catch (SqliteException exception) when (IsCorrupt(exception))
        {
            return new HistoryStorePreflightResult(
                HistoryStorePreflightStatus.Faulted,
                "INFLUX_OUTBOX_CORRUPT",
                "InfluxDB local buffer is corrupt or is not a valid SQLite database.");
        }
        catch (Exception exception)
        {
            return new HistoryStorePreflightResult(
                HistoryStorePreflightStatus.Recoverable,
                "INFLUX_OUTBOX_PREFLIGHT",
                exception.Message);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_initialized)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                return;
            }

            var databasePath = GetDatabasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            await using var connection = await OpenConnectionAsync(
                databasePath,
                SqliteOpenMode.ReadWriteCreate,
                cancellationToken).ConfigureAwait(false);
            var version = await ReadUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version > InfluxOutboxSchema.CurrentVersion)
            {
                throw new HistoryStorePermanentException(
                    "INFLUX_OUTBOX_SCHEMA_TOO_NEW",
                    $"Influx outbox schema version {version} is newer than supported version {InfluxOutboxSchema.CurrentVersion}.");
            }

            await ValidateExistingSchemaAsync(connection, version, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, InfluxOutboxSchema.CreateTablesSql, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, InfluxOutboxSchema.CreateIndexesSql, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, $"PRAGMA user_version={InfluxOutboxSchema.CurrentVersion};", cancellationToken)
                .ConfigureAwait(false);
            _initialized = true;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new HistoryStorePermanentException(
                "INFLUX_BUFFER_ACCESS_DENIED",
                "Access to the InfluxDB local buffer was denied.",
                exception);
        }
        catch (SqliteException exception) when (IsCorrupt(exception))
        {
            throw new HistoryStorePermanentException(
                "INFLUX_OUTBOX_CORRUPT",
                "InfluxDB local buffer is corrupt or is not a valid SQLite database.",
                exception);
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task<InfluxAppendResult> AppendAsync(
        IReadOnlyList<HistorySample> samples,
        string destinationFingerprint,
        int maxBufferedSamples,
        DateTimeOffset enqueuedAtUtc,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFingerprint);
        if (samples.Count == 0)
        {
            return new InfluxAppendResult(0, 0, 0, []);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(
                GetDatabasePath(),
                SqliteOpenMode.ReadWrite,
                cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var activeCount = await CountPendingAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<PendingCandidate>();
            var rejections = new List<InfluxSampleRejection>();
            var duplicateCount = 0;
            var counterCache = new Dictionary<string, long?>(StringComparer.Ordinal);

            foreach (var sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string sampleKey;
                try
                {
                    InfluxHistoryPointMapper.ValidateSample(sample);
                    sampleKey = InfluxSampleKey.Create(destinationFingerprint, sample);
                }
                catch (ArgumentException exception)
                {
                    rejections.Add(new InfluxSampleRejection("INFLUX_VALUE_TYPE_INVALID", exception.Message));
                    continue;
                }
                catch (HistoryStorePermanentException exception)
                {
                    rejections.Add(new InfluxSampleRejection(exception.Code, exception.Message));
                    continue;
                }

                if (!seenKeys.Add(sampleKey) ||
                    await ExistsAsync(connection, transaction, sampleKey, cancellationToken).ConfigureAwait(false))
                {
                    duplicateCount++;
                    continue;
                }

                if (!TryGetTypedValues(sample, out var typed, out var valueError))
                {
                    rejections.Add(new InfluxSampleRejection("INFLUX_VALUE_TYPE_INVALID", valueError!));
                    continue;
                }

                var counterKey = CreateCounterKey(destinationFingerprint, sample.RuntimeId, sample.TagId);
                if (!counterCache.TryGetValue(counterKey, out var previous))
                {
                    previous = await ReadCounterAsync(
                        connection,
                        transaction,
                        destinationFingerprint,
                        sample.RuntimeId,
                        sample.TagId,
                        cancellationToken).ConfigureAwait(false);
                }

                if (!InfluxPointTimestamp.TryAllocate(sample.RecordedAtUtc, previous, out var remoteTimestamp))
                {
                    rejections.Add(new InfluxSampleRejection(
                        "INFLUX_TIMESTAMP_OUT_OF_RANGE",
                        "History sample RecordedAtUtc cannot be represented as a signed InfluxDB nanosecond timestamp."));
                    continue;
                }

                counterCache[counterKey] = remoteTimestamp;
                candidates.Add(new PendingCandidate(sampleKey, sample, remoteTimestamp, typed));
            }

            if (activeCount + candidates.Count > maxBufferedSamples)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                await IncrementBufferFullRejectionsAsync(destinationFingerprint, cancellationToken).ConfigureAwait(false);
                throw new HistoryStoreTransientException(
                    "INFLUX_BUFFER_FULL",
                    "InfluxDB durable buffer capacity is unavailable for the new history batch.");
            }

            foreach (var rejection in rejections)
            {
                await IncrementMetadataAsync(
                    connection,
                    transaction,
                    destinationFingerprint,
                    "RemoteRejectedSamples",
                    rejection.ErrorCode,
                    rejection.ErrorMessage,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var candidate in candidates)
            {
                await InsertAsync(connection, transaction, destinationFingerprint, candidate, enqueuedAtUtc, cancellationToken)
                    .ConfigureAwait(false);
                await UpsertCounterAsync(
                    connection,
                    transaction,
                    destinationFingerprint,
                    candidate.Sample.RuntimeId,
                    candidate.Sample.TagId,
                    candidate.RemoteTimestampNanoseconds,
                    cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new InfluxAppendResult(
                candidates.Count,
                duplicateCount,
                rejections.Count,
                rejections);
        }
        catch (HistoryStoreTransientException)
        {
            throw;
        }
        catch (HistoryStorePermanentException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new HistoryStorePermanentException(
                "INFLUX_BUFFER_ACCESS_DENIED",
                "Access to the InfluxDB local buffer was denied.",
                exception);
        }
        catch (SqliteException exception) when (IsCorrupt(exception))
        {
            throw new HistoryStorePermanentException(
                "INFLUX_OUTBOX_CORRUPT",
                "InfluxDB local buffer is corrupt or is not a valid SQLite database.",
                exception);
        }
    }

    public async Task<IReadOnlyList<InfluxOutboxRow>> ReadPendingAsync(
        string destinationFingerprint,
        int limit,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadOnly, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SampleKey, DestinationFingerprint, RuntimeId, TagId, DataType, Quality,
                   SourceTimestampUtcTicks, RecordedAtUtcTicks, RemoteTimestampNanoseconds,
                   TagSequence, HasValue, ValueBoolean, ValueInteger, ValueReal, ValueText,
                   EnqueuedAtUtcTicks
            FROM InfluxOutbox
            WHERE DestinationFingerprint = $fingerprint AND State = 'Pending'
            ORDER BY Id ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$fingerprint", destinationFingerprint);
        command.Parameters.AddWithValue("$limit", limit);

        var rows = new List<InfluxOutboxRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    public async Task MarkAttemptAsync(
        string destinationFingerprint,
        IReadOnlyList<long> ids,
        DateTimeOffset attemptedAtUtc,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        ThrowIfDisposed();
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadWrite, cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var id in ids)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE InfluxOutbox
                SET AttemptCount = AttemptCount + 1,
                    LastAttemptUtcTicks = $ticks
                WHERE Id = $id AND DestinationFingerprint = $fingerprint AND State = 'Pending';
                """;
            command.Parameters.AddWithValue("$ticks", attemptedAtUtc.UtcDateTime.Ticks);
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$fingerprint", destinationFingerprint);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AcknowledgeAsync(
        string destinationFingerprint,
        IReadOnlyList<long> ids,
        DateTimeOffset successAtUtc,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        ThrowIfDisposed();
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadWrite, cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var deleted = 0;
        foreach (var id in ids)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM InfluxOutbox WHERE Id = $id AND DestinationFingerprint = $fingerprint;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$fingerprint", destinationFingerprint);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await EnsureMetadataRowAsync(connection, transaction, destinationFingerprint, cancellationToken).ConfigureAwait(false);
        await using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = """
                UPDATE InfluxOutboxMetadata
                SET SyncedSamples = SyncedSamples + $deleted,
                    ConsecutiveFailures = 0,
                    LastRemoteSuccessUtcTicks = $ticks,
                    LastErrorCode = NULL,
                    LastErrorMessage = NULL
                WHERE DestinationFingerprint = $fingerprint;
                """;
            metadata.Parameters.AddWithValue("$deleted", deleted);
            metadata.Parameters.AddWithValue("$ticks", successAtUtc.UtcDateTime.Ticks);
            metadata.Parameters.AddWithValue("$fingerprint", destinationFingerprint);
            await metadata.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task RecordSyncFailureAsync(
        string destinationFingerprint,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken) =>
        UpdateFailureMetadataAsync(destinationFingerprint, errorCode, errorMessage, cancellationToken);

    public async Task MarkTerminalAsync(
        string destinationFingerprint,
        long id,
        bool expired,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadWrite, cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM InfluxOutbox WHERE Id = $id AND DestinationFingerprint = $fingerprint;";
            delete.Parameters.AddWithValue("$id", id);
            delete.Parameters.AddWithValue("$fingerprint", destinationFingerprint);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await IncrementMetadataAsync(
            connection,
            transaction,
            destinationFingerprint,
            expired ? "ExpiredSamples" : "RemoteRejectedSamples",
            errorCode,
            errorMessage,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<InfluxOutboxDiagnostics> ReadDiagnosticsAsync(
        string currentFingerprint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadOnly, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM InfluxOutbox WHERE DestinationFingerprint = $fingerprint),
                (SELECT COUNT(*) FROM InfluxOutbox WHERE DestinationFingerprint <> $fingerprint),
                COALESCE((SELECT SyncedSamples FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint), 0),
                COALESCE((SELECT RemoteRejectedSamples FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint), 0),
                COALESCE((SELECT ExpiredSamples FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint), 0),
                COALESCE((SELECT BufferFullRejections FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint), 0),
                COALESCE((SELECT SyncFailures FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint), 0),
                COALESCE((SELECT ConsecutiveFailures FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint), 0),
                (SELECT LastRemoteSuccessUtcTicks FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint),
                (SELECT LastErrorCode FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint),
                (SELECT LastErrorMessage FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint),
                (SELECT LastKnownRetentionSeconds FROM InfluxOutboxMetadata WHERE DestinationFingerprint = $fingerprint);
            """;
        command.Parameters.AddWithValue("$fingerprint", currentFingerprint);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new InfluxOutboxDiagnostics(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : new DateTimeOffset(reader.GetInt64(8), TimeSpan.Zero),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetInt64(11));
    }

    public async Task<long?> GetLastRemoteTimestampAsync(
        string destinationFingerprint,
        string runtimeId,
        string tagId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadOnly, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT LastRemoteTimestampNanoseconds
            FROM InfluxPointCounters
            WHERE DestinationFingerprint = $fingerprint AND RuntimeId = $runtimeId AND TagId = $tagId;
            """;
        command.Parameters.AddWithValue("$fingerprint", destinationFingerprint);
        command.Parameters.AddWithValue("$runtimeId", runtimeId);
        command.Parameters.AddWithValue("$tagId", tagId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<long> ClearDestinationBufferAsync(
        string destinationFingerprint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadWrite, cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM InfluxOutbox WHERE DestinationFingerprint = $fingerprint;";
        command.Parameters.AddWithValue("$fingerprint", destinationFingerprint);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLastKnownRetentionAsync(
        string destinationFingerprint,
        long? retentionSeconds,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadWrite, cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnsureMetadataRowAsync(connection, transaction, destinationFingerprint, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE InfluxOutboxMetadata SET LastKnownRetentionSeconds = $retention WHERE DestinationFingerprint = $fingerprint;";
        command.Parameters.AddWithValue("$retention", retentionSeconds is null ? DBNull.Value : retentionSeconds);
        command.Parameters.AddWithValue("$fingerprint", destinationFingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _initializeGate.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task IncrementBufferFullRejectionsAsync(string fingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadWrite, cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await IncrementMetadataAsync(connection, transaction, fingerprint, "BufferFullRejections",
            "INFLUX_BUFFER_FULL", "InfluxDB durable buffer capacity is unavailable.", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateFailureMetadataAsync(
        string fingerprint,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        await using var connection = await OpenConnectionAsync(GetDatabasePath(), SqliteOpenMode.ReadWrite, cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await EnsureMetadataRowAsync(connection, transaction, fingerprint, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE InfluxOutboxMetadata
            SET SyncFailures = SyncFailures + 1,
                ConsecutiveFailures = ConsecutiveFailures + 1,
                LastErrorCode = $code,
                LastErrorMessage = $message
            WHERE DestinationFingerprint = $fingerprint;
            """;
        command.Parameters.AddWithValue("$code", errorCode);
        command.Parameters.AddWithValue("$message", errorMessage);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task IncrementMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fingerprint,
        string metricColumn,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var allowedColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "RemoteRejectedSamples",
            "ExpiredSamples",
            "BufferFullRejections"
        };
        if (!allowedColumns.Contains(metricColumn))
        {
            throw new ArgumentOutOfRangeException(nameof(metricColumn));
        }

        await EnsureMetadataRowAsync(connection, transaction, fingerprint, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE InfluxOutboxMetadata
            SET {metricColumn} = {metricColumn} + 1,
                LastErrorCode = $code,
                LastErrorMessage = $message
            WHERE DestinationFingerprint = $fingerprint;
            """;
        command.Parameters.AddWithValue("$code", errorCode);
        command.Parameters.AddWithValue("$message", errorMessage);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureMetadataRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO InfluxOutboxMetadata (DestinationFingerprint) VALUES ($fingerprint);";
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fingerprint,
        PendingCandidate candidate,
        DateTimeOffset enqueuedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO InfluxOutbox
            (
                SampleKey, DestinationFingerprint, State, RuntimeId, TagId, DataType, Quality,
                SourceTimestampUtcTicks, RecordedAtUtcTicks, RemoteTimestampNanoseconds,
                TagSequence, HasValue, ValueBoolean, ValueInteger, ValueReal, ValueText,
                EnqueuedAtUtcTicks
            )
            VALUES
            (
                $sampleKey, $fingerprint, 'Pending', $runtimeId, $tagId, $dataType, $quality,
                $sourceTicks, $recordedTicks, $remoteTimestamp,
                $sequence, $hasValue, $valueBoolean, $valueInteger, $valueReal, $valueText,
                $enqueuedTicks
            );
            """;
        command.Parameters.AddWithValue("$sampleKey", candidate.SampleKey);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$runtimeId", candidate.Sample.RuntimeId);
        command.Parameters.AddWithValue("$tagId", candidate.Sample.TagId);
        command.Parameters.AddWithValue("$dataType", candidate.Sample.DataType.ToString());
        command.Parameters.AddWithValue("$quality", candidate.Sample.Quality.ToString());
        command.Parameters.AddWithValue("$sourceTicks", candidate.Sample.SourceTimestampUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$recordedTicks", candidate.Sample.RecordedAtUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$remoteTimestamp", candidate.RemoteTimestampNanoseconds);
        command.Parameters.AddWithValue("$sequence", candidate.Sample.TagSequence);
        command.Parameters.AddWithValue("$hasValue", candidate.Typed.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("$valueBoolean", candidate.Typed.Boolean is null ? DBNull.Value : candidate.Typed.Boolean.Value ? 1 : 0);
        command.Parameters.AddWithValue("$valueInteger", candidate.Typed.Integer is null ? DBNull.Value : candidate.Typed.Integer);
        command.Parameters.AddWithValue("$valueReal", candidate.Typed.Real is null ? DBNull.Value : candidate.Typed.Real);
        command.Parameters.AddWithValue("$valueText", candidate.Typed.Text is null ? DBNull.Value : candidate.Typed.Text);
        command.Parameters.AddWithValue("$enqueuedTicks", enqueuedAtUtc.UtcDateTime.Ticks);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sampleKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM InfluxOutbox WHERE SampleKey = $sampleKey LIMIT 1;";
        command.Parameters.AddWithValue("$sampleKey", sampleKey);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<long> CountPendingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM InfluxOutbox WHERE State = 'Pending';";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long?> ReadCounterAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fingerprint,
        string runtimeId,
        string tagId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT LastRemoteTimestampNanoseconds
            FROM InfluxPointCounters
            WHERE DestinationFingerprint = $fingerprint AND RuntimeId = $runtimeId AND TagId = $tagId;
            """;
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$runtimeId", runtimeId);
        command.Parameters.AddWithValue("$tagId", tagId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task UpsertCounterAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fingerprint,
        string runtimeId,
        string tagId,
        long remoteTimestamp,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO InfluxPointCounters
                (DestinationFingerprint, RuntimeId, TagId, LastRemoteTimestampNanoseconds)
            VALUES ($fingerprint, $runtimeId, $tagId, $timestamp)
            ON CONFLICT(DestinationFingerprint, RuntimeId, TagId)
            DO UPDATE SET LastRemoteTimestampNanoseconds = excluded.LastRemoteTimestampNanoseconds;
            """;
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$runtimeId", runtimeId);
        command.Parameters.AddWithValue("$tagId", tagId);
        command.Parameters.AddWithValue("$timestamp", remoteTimestamp);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(
        string databasePath,
        SqliteOpenMode mode,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (mode == SqliteOpenMode.ReadOnly)
            {
                await SqliteConnectionConfiguration.ConfigureReadAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SqliteConnectionConfiguration.ConfigureWriteAsync(
                    connection,
                    enableWal: mode == SqliteOpenMode.ReadWriteCreate,
                    cancellationToken).ConfigureAwait(false);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<long> ReadUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
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

    private static async Task ValidateExistingSchemaAsync(
        SqliteConnection connection,
        long version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'InfluxOutbox' LIMIT 1;";
        var exists = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
        if (!exists && version >= InfluxOutboxSchema.CurrentVersion)
        {
            throw new HistoryStorePermanentException(
                "INFLUX_OUTBOX_SCHEMA_INVALID",
                "InfluxDB local buffer is missing the InfluxOutbox table.");
        }

        if (!exists)
        {
            return;
        }

        await using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(InfluxOutbox);";
        await using var reader = await columns.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(1));
        }

        var required = new[] { "Id", "SampleKey", "DestinationFingerprint", "State", "RuntimeId", "TagId", "DataType", "Quality", "RemoteTimestampNanoseconds" };
        if (required.Any(column => !names.Contains(column)))
        {
            throw new HistoryStorePermanentException(
                "INFLUX_OUTBOX_SCHEMA_INVALID",
                "InfluxDB local buffer has an incompatible InfluxOutbox table.");
        }
    }

    private static InfluxOutboxRow ReadRow(SqliteDataReader reader)
    {
        var dataType = Enum.Parse<TagDataType>(reader.GetString(5), ignoreCase: true);
        var hasValue = reader.GetInt64(11) != 0;
        object? value = null;
        if (hasValue)
        {
            value = dataType switch
            {
                TagDataType.Boolean => reader.GetInt64(12) != 0,
                TagDataType.Int32 => checked((int)reader.GetInt64(13)),
                TagDataType.Int64 => reader.GetInt64(13),
                TagDataType.Double => reader.GetDouble(14),
                TagDataType.String => reader.GetString(15),
                _ => throw new InvalidOperationException($"Unsupported stored data type '{dataType}'.")
            };
        }

        return new InfluxOutboxRow(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            new HistorySample(
                reader.GetString(3),
                reader.GetString(4),
                dataType,
                value,
                Enum.Parse<TagQuality>(reader.GetString(6), ignoreCase: true),
                new DateTimeOffset(reader.GetInt64(7), TimeSpan.Zero),
                new DateTimeOffset(reader.GetInt64(8), TimeSpan.Zero),
                reader.GetInt64(10)),
            reader.GetInt64(9),
            new DateTimeOffset(reader.GetInt64(16), TimeSpan.Zero));
    }

    private static bool TryGetTypedValues(
        HistorySample sample,
        out TypedValues typed,
        out string? error)
    {
        typed = new TypedValues(sample.Value is not null, null, null, null, null);
        error = null;
        if (!Enum.IsDefined(sample.DataType) || !Enum.IsDefined(sample.Quality))
        {
            error = "History sample contains an invalid data type or quality.";
            return false;
        }

        if (sample.Value is null)
        {
            return true;
        }

        switch (sample.DataType)
        {
            case TagDataType.Boolean when sample.Value is bool boolean:
                typed = typed with { Boolean = boolean };
                return true;
            case TagDataType.Int32 when sample.Value is int int32:
                typed = typed with { Integer = int32 };
                return true;
            case TagDataType.Int64 when sample.Value is long int64:
                typed = typed with { Integer = int64 };
                return true;
            case TagDataType.Double when sample.Value is double doubleValue && double.IsFinite(doubleValue):
                typed = typed with { Real = doubleValue };
                return true;
            case TagDataType.String when sample.Value is string text:
                typed = typed with { Text = text };
                return true;
            default:
                error = $"Value type '{sample.Value.GetType().FullName}' is incompatible with '{sample.DataType}'.";
                return false;
        }
    }

    private static string CreateCounterKey(string fingerprint, string runtimeId, string tagId) =>
        $"{fingerprint}\n{runtimeId}\n{tagId}";

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Influx outbox has not been initialized.");
        }
    }

    private string GetDatabasePath()
    {
        if (_projectPath is null)
        {
            throw new HistoryStorePermanentException(
                "PROJECT_PATH_REQUIRED",
                "A canonical project path is required for the InfluxDB local buffer.");
        }

        return _databasePath ??= InfluxHistoryPathResolver.Resolve(_projectPath, _configuredPath);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static bool IsCorrupt(SqliteException exception) => exception.SqliteErrorCode is 11 or 26;

    private sealed record PendingCandidate(
        string SampleKey,
        HistorySample Sample,
        long RemoteTimestampNanoseconds,
        TypedValues Typed);

    private sealed record TypedValues(
        bool HasValue,
        bool? Boolean,
        long? Integer,
        double? Real,
        string? Text);
}
