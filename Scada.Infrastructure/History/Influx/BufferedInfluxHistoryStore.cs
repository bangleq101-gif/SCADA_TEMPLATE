using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Infrastructure.Persistence;

namespace Scada.Infrastructure.History.Influx;

/// <summary>
/// M6 history store. The existing Runtime historian queue writes to this store,
/// which durably owns the local outbox before any remote operation is attempted.
/// </summary>
public sealed class BufferedInfluxHistoryStore : IHistoryStore, IHistoryStoreDiagnostics, IHistoryStoreMaintenance
{
    private const int MaximumIsolationAttempts = 20_000;
    private const long ExclusiveMaximumNanoseconds = InfluxPointTimestamp.MaxNanoseconds + 1;
    private readonly ProjectPath? _projectPath;
    private readonly InfluxDbOptions _options;
    private readonly ILogger<BufferedInfluxHistoryStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly InfluxOutboxStore _outbox;
    private readonly IInfluxTransport? _injectedTransport;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly SemaphoreSlim _workSignal = new(0, 1);
    private readonly SemaphoreSlim _remoteSyncGate = new(1, 1);
    private readonly object _sync = new();
    private readonly string _destinationFingerprint;
    private CancellationTokenSource? _lifetimeCts;
    private IInfluxTransport? _transport;
    private Task? _workerTask;
    private HistoryStoreDiagnosticsSnapshot _snapshot = new(
        HistoryStoreState.Disabled,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        null,
        null,
        null);
    private bool _initialized;
    private bool _disposed;

    public BufferedInfluxHistoryStore(
        ProjectPath? projectPath,
        HistorianOptions historianOptions,
        ILogger<BufferedInfluxHistoryStore> logger,
        TimeProvider timeProvider,
        IInfluxTransport? transport = null)
    {
        _projectPath = projectPath;
        ArgumentNullException.ThrowIfNull(historianOptions);
        _options = historianOptions.Influx ?? new InfluxDbOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _injectedTransport = transport;
        _destinationFingerprint = InfluxDestinationFingerprint.Create(_options);
        _outbox = new InfluxOutboxStore(_projectPath, _options.BufferPath);
    }

    public string DestinationFingerprint => _destinationFingerprint;

    public string? BufferPath => _outbox.DatabasePath;

    public HistoryStoreDiagnosticsSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public async Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        // Preflight deliberately touches only the local durable buffer. A missing
        // token or an offline Influx endpoint must not prevent local startup.
        return await _outbox.PreflightAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                return;
            }

            SetState(HistoryStoreState.Starting, null, null);
            await _outbox.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _lifetimeCts = new CancellationTokenSource();
            _initialized = true;
            _workerTask = Task.Run(() => ResynchronizationLoopAsync(_lifetimeCts.Token), CancellationToken.None);
            await RefreshDiagnosticsAsync(HistoryStoreState.Buffering, null, null, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task WriteBatchAsync(
        IReadOnlyList<HistorySample> samples,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ThrowIfDisposed();
        EnsureInitialized();
        if (samples.Count == 0)
        {
            return;
        }

        var append = await _outbox.AppendAsync(
                samples,
                _destinationFingerprint,
                _options.MaxBufferedSamples,
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var rejection in append.Rejections)
        {
            _logger.LogWarning(
                "Influx history sample was classified as terminal for {ErrorCode}: {ErrorMessage}",
                rejection.ErrorCode,
                rejection.ErrorMessage);
        }

        if (append.AcceptedCount > 0)
        {
            SignalWork();
        }
    }

    public async Task<IReadOnlyList<HistorySample>> QueryAsync(
        HistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        ThrowIfDisposed();
        EnsureInitialized();

        if (InfluxPointTimestamp.IsAtOrBelowMinimum(query.ToRecordedAtUtc) ||
            InfluxPointTimestamp.IsAboveMaximum(query.FromRecordedAtUtc))
        {
            return [];
        }

        var transport = TryGetTransport(out var configurationError);
        if (transport is null)
        {
            throw new HistoryStoreTransientException(
                configurationError.Code,
                configurationError.Message);
        }

        try
        {
            var lastRemoteTimestamp = await _outbox.GetLastRemoteTimestampAsync(
                    _destinationFingerprint,
                    query.RuntimeId,
                    query.TagId,
                    cancellationToken)
                .ConfigureAwait(false);
            var start = InfluxPointTimestamp.TryGetBaseNanoseconds(query.FromRecordedAtUtc, out var startNanoseconds)
                ? startNanoseconds
                : InfluxPointTimestamp.MinNanoseconds;
            var hasStop = InfluxPointTimestamp.TryGetBaseNanoseconds(query.ToRecordedAtUtc, out var stopNanoseconds);
            if (!hasStop)
            {
                // Flux range stops are exclusive. Use the explicit signed integer
                // epoch-nanosecond form so the valid Influx maximum is included
                // without relying on a duration literal at Int64.MaxValue.
                stopNanoseconds = ExclusiveMaximumNanoseconds;
                hasStop = true;
            }

            if (lastRemoteTimestamp is long previous && previous >= InfluxPointTimestamp.MinNanoseconds && previous < InfluxPointTimestamp.MaxNanoseconds)
            {
                var widenedStop = previous + 1;
                if (!hasStop || widenedStop > stopNanoseconds)
                {
                    stopNanoseconds = widenedStop;
                    hasStop = true;
                }
            }

            var flux = BuildQuery(query, start, hasStop ? stopNanoseconds : null);
            var raw = await transport.QueryRawAsync(flux, _options.Organization, cancellationToken)
                .ConfigureAwait(false);
            var results = DecodeQuery(raw, query);
            SetState(HistoryStoreState.Online, null, null);
            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfluxTransportException exception)
        {
            await RecordTransportFailureAsync(exception, cancellationToken).ConfigureAwait(false);
            throw new HistoryStoreTransientException(exception.Code, exception.Message, exception);
        }
    }

    public async Task<HistoryStoreOperationResult> ProbeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_initialized)
        {
            return Failure("INFLUX_STORE_NOT_INITIALIZED", "Influx history store has not been initialized.");
        }

        var transport = TryGetTransport(out var configurationError);
        if (transport is null)
        {
            SetState(HistoryStoreState.ConfigurationRequired, configurationError.Code, configurationError.Message);
            await RefreshDiagnosticsAsync(
                    HistoryStoreState.ConfigurationRequired,
                    configurationError.Code,
                    configurationError.Message,
                    cancellationToken)
                .ConfigureAwait(false);
            return Failure(configurationError.Code, configurationError.Message);
        }

        try
        {
            SetState(HistoryStoreState.Connecting, null, null);
            await transport.ProbeAsync(cancellationToken).ConfigureAwait(false);
            await RecordVerifiedRetentionAsync(transport, cancellationToken).ConfigureAwait(false);
            await RefreshDiagnosticsAsync(HistoryStoreState.Online, null, null, cancellationToken)
                .ConfigureAwait(false);
            return new HistoryStoreOperationResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfluxTransportException exception)
        {
            await RecordTransportFailureAsync(exception, cancellationToken).ConfigureAwait(false);
            return Failure(exception.Code, exception.Message);
        }
    }

    public async Task<HistoryStoreOperationResult> ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        var transport = TryGetTransport(out var configurationError);
        if (transport is null)
        {
            return Failure(configurationError.Code, configurationError.Message);
        }

        try
        {
            await transport.ApplyRetentionAsync(
                    _options.Organization,
                    _options.Bucket,
                    _options.RetentionSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
            await _outbox.SetLastKnownRetentionAsync(
                    _destinationFingerprint,
                    _options.RetentionSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
            await RefreshDiagnosticsAsync(HistoryStoreState.Online, null, null, cancellationToken)
                .ConfigureAwait(false);
            return new HistoryStoreOperationResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfluxTransportException exception)
        {
            await RecordTransportFailureAsync(exception, cancellationToken).ConfigureAwait(false);
            return Failure(exception.Code, exception.Message);
        }
    }

    public async Task<HistoryStoreOperationResult> ClearCurrentBufferAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        await _remoteSyncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _outbox.ClearDestinationBufferAsync(_destinationFingerprint, cancellationToken).ConfigureAwait(false);
            await RefreshDiagnosticsAsync(Snapshot.State, Snapshot.LastErrorCode, Snapshot.LastErrorMessage, cancellationToken)
                .ConfigureAwait(false);
            return new HistoryStoreOperationResult(true);
        }
        finally
        {
            _remoteSyncGate.Release();
        }
    }

    public async Task<HistoryStoreOperationResult> ClearPreviousDestinationBufferAsync(
        string destinationFingerprint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFingerprint);
        if (string.Equals(destinationFingerprint, _destinationFingerprint, StringComparison.Ordinal))
        {
            return Failure(
                "INFLUX_DESTINATION_IS_CURRENT",
                "The current destination buffer must be cleared with the current-destination action.");
        }

        await _outbox.ClearDestinationBufferAsync(destinationFingerprint, cancellationToken).ConfigureAwait(false);
        await RefreshDiagnosticsAsync(Snapshot.State, Snapshot.LastErrorCode, Snapshot.LastErrorMessage, cancellationToken)
            .ConfigureAwait(false);
        return new HistoryStoreOperationResult(true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts?.Cancel();
        SignalWork();
        if (_workerTask is not null)
        {
            try
            {
                await _workerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        SetState(HistoryStoreState.Stopping, null, null);
        if (_transport is not null)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _transport = null;
        }

        await _outbox.DisposeAsync().ConfigureAwait(false);
        _lifetimeCts?.Dispose();
        _initializeGate.Dispose();
        _workSignal.Dispose();
        _remoteSyncGate.Dispose();
    }

    private async Task ResynchronizationLoopAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        DateTimeOffset? lastProbeUtc = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var transport = TryGetTransport(out var configurationError);
            if (transport is null)
            {
                SetState(HistoryStoreState.ConfigurationRequired, configurationError.Code, configurationError.Message);
                try
                {
                    await RefreshDiagnosticsAsync(
                            HistoryStoreState.ConfigurationRequired,
                            configurationError.Code,
                            configurationError.Message,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await DelayOnlyAsync(
                        TimeSpan.FromMilliseconds(_options.ReconnectMaxDelayMilliseconds),
                        cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            try
            {
                var now = _timeProvider.GetUtcNow();
                if (lastProbeUtc is null ||
                    now - lastProbeUtc.Value >= TimeSpan.FromMilliseconds(_options.HealthProbeIntervalMilliseconds))
                {
                    SetState(HistoryStoreState.Connecting, null, null);
                    await transport.ProbeAsync(cancellationToken).ConfigureAwait(false);
                    await RecordVerifiedRetentionAsync(transport, cancellationToken).ConfigureAwait(false);
                    lastProbeUtc = now;
                }

                SetState(HistoryStoreState.Resynchronizing, null, null);
                var didWork = await SyncPendingAsync(transport, cancellationToken).ConfigureAwait(false);
                consecutiveFailures = 0;
                if (!didWork)
                {
                    SetState(HistoryStoreState.Online, null, null);
                }

                await WaitForWorkOrDelayAsync(
                        TimeSpan.FromMilliseconds(didWork
                            ? _options.SyncIntervalMilliseconds
                            : _options.HealthProbeIntervalMilliseconds),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (InfluxTransportException exception)
            {
                consecutiveFailures++;
                await RecordTransportFailureAsync(exception, cancellationToken).ConfigureAwait(false);
                var delay = exception.RetryAfter ?? GetBackoff(consecutiveFailures);
                await DelayOnlyAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (HistoryStoreTransientException exception)
            {
                consecutiveFailures++;
                await RecordFailureAsync(exception.Code, exception.Message, HistoryStoreState.Offline, cancellationToken)
                    .ConfigureAwait(false);
                await DelayOnlyAsync(GetBackoff(consecutiveFailures), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                _logger.LogWarning(
                    exception,
                    "Influx history synchronization failed with an unexpected provider error.");
                await RecordFailureAsync(
                        "INFLUX_SYNC_FAILED",
                        "InfluxDB synchronization failed; local history remains buffered.",
                        HistoryStoreState.Offline,
                        cancellationToken)
                    .ConfigureAwait(false);
                await DelayOnlyAsync(GetBackoff(consecutiveFailures), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> SyncPendingAsync(
        IInfluxTransport transport,
        CancellationToken cancellationToken)
    {
        await _remoteSyncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SyncPendingCoreAsync(transport, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _remoteSyncGate.Release();
        }
    }

    private async Task<bool> SyncPendingCoreAsync(
        IInfluxTransport transport,
        CancellationToken cancellationToken)
    {
        var diagnostics = await _outbox.ReadDiagnosticsAsync(_destinationFingerprint, cancellationToken)
            .ConfigureAwait(false);
        var rows = await _outbox.ReadPendingAsync(
                _destinationFingerprint,
                Math.Max(1, _options.SyncBatchSize),
                cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
        {
            await RefreshDiagnosticsAsync(HistoryStoreState.Online, null, null, cancellationToken)
                .ConfigureAwait(false);
            return false;
        }

        var pending = new List<InfluxOutboxRow>(rows.Count);
        foreach (var row in rows)
        {
            if (IsExpired(row, diagnostics.LastKnownRetentionSeconds))
            {
                await _outbox.MarkTerminalAsync(
                        _destinationFingerprint,
                        row.Id,
                        expired: true,
                        "INFLUX_RETENTION_EXPIRED",
                        "History sample exceeded the most recently verified remote retention horizon.",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                try
                {
                    _ = InfluxHistoryPointMapper.ToLineProtocol(row, _options.Measurement);
                    pending.Add(row);
                }
                catch (HistoryStorePermanentException exception)
                {
                    _logger.LogWarning(
                        "Influx history row {RowId} was classified as terminal for {ErrorCode}: {ErrorMessage}",
                        row.Id,
                        exception.Code,
                        exception.Message);
                    await _outbox.MarkTerminalAsync(
                            _destinationFingerprint,
                            row.Id,
                            expired: false,
                            exception.Code,
                            exception.Message,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        if (pending.Count == 0)
        {
            await RefreshDiagnosticsAsync(HistoryStoreState.Resynchronizing, null, null, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        await WriteRowsWithIsolationAsync(
                transport,
                pending,
                MaximumIsolationAttempts,
                cancellationToken)
            .ConfigureAwait(false);
        await RefreshDiagnosticsAsync(HistoryStoreState.Online, null, null, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task WriteRowsWithIsolationAsync(
        IInfluxTransport transport,
        IReadOnlyList<InfluxOutboxRow> rows,
        int remainingAttempts,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await _outbox.MarkAttemptAsync(
                _destinationFingerprint,
                rows.Select(row => row.Id).ToArray(),
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var lines = rows
                .Select(row => InfluxHistoryPointMapper.ToLineProtocol(row, _options.Measurement))
                .ToArray();
            await transport.WriteLinesAsync(lines, _options.Bucket, _options.Organization, cancellationToken)
                .ConfigureAwait(false);
            await _outbox.AcknowledgeAsync(
                    _destinationFingerprint,
                    rows.Select(row => row.Id).ToArray(),
                    _timeProvider.GetUtcNow(),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        catch (InfluxTransportException exception) when (
            exception.StatusCode == 400 && exception.IsPointSpecific && rows.Count > 1 && remainingAttempts > 1)
        {
            var midpoint = rows.Count / 2;
            await WriteRowsWithIsolationAsync(
                    transport,
                    rows.Take(midpoint).ToArray(),
                    remainingAttempts - 1,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteRowsWithIsolationAsync(
                    transport,
                    rows.Skip(midpoint).ToArray(),
                    remainingAttempts - 1,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InfluxTransportException exception) when (
            exception.StatusCode == 400 && exception.IsPointSpecific && rows.Count == 1)
        {
            await _outbox.MarkTerminalAsync(
                    _destinationFingerprint,
                    rows[0].Id,
                    expired: false,
                    exception.Code,
                    exception.Message,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RecordVerifiedRetentionAsync(
        IInfluxTransport transport,
        CancellationToken cancellationToken)
    {
        try
        {
            var retention = await transport.ReadRetentionAsync(
                    _options.Organization,
                    _options.Bucket,
                    cancellationToken)
                .ConfigureAwait(false);
            await _outbox.SetLastKnownRetentionAsync(
                    _destinationFingerprint,
                    retention.EverySeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InfluxTransportException exception)
        {
            _logger.LogWarning(
                "InfluxDB retention could not be read ({ErrorCode}); normal history synchronization will continue.",
                exception.Code);
        }
    }

    private async Task RecordTransportFailureAsync(
        InfluxTransportException exception,
        CancellationToken cancellationToken)
    {
        var state = exception.StatusCode is 401 or 403 or 404
            ? HistoryStoreState.ConfigurationRequired
            : HistoryStoreState.Offline;
        await RecordFailureAsync(exception.Code, exception.Message, state, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordFailureAsync(
        string code,
        string message,
        HistoryStoreState state,
        CancellationToken cancellationToken)
    {
        SetState(state, code, message);
        await _outbox.RecordSyncFailureAsync(_destinationFingerprint, code, message, cancellationToken)
            .ConfigureAwait(false);
        await RefreshDiagnosticsAsync(state, code, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshDiagnosticsAsync(
        HistoryStoreState state,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var diagnostics = await _outbox.ReadDiagnosticsAsync(_destinationFingerprint, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = new HistoryStoreDiagnosticsSnapshot(
            state,
            diagnostics.PendingSamples,
            diagnostics.OrphanedDestinationSamples,
            diagnostics.SyncedSamples,
            diagnostics.RemoteRejectedSamples,
            diagnostics.ExpiredSamples,
            diagnostics.BufferFullRejections,
            diagnostics.SyncFailures,
            diagnostics.ConsecutiveFailures,
            diagnostics.LastRemoteSuccessUtc,
            errorCode ?? diagnostics.LastErrorCode,
            errorMessage ?? diagnostics.LastErrorMessage);
        lock (_sync)
        {
            _snapshot = snapshot;
        }
    }

    private IInfluxTransport? TryGetTransport(out (string Code, string Message) configurationError)
    {
        lock (_sync)
        {
            if (_transport is not null)
            {
                configurationError = (string.Empty, string.Empty);
                return _transport;
            }

            if (_injectedTransport is not null)
            {
                _transport = _injectedTransport;
                configurationError = (string.Empty, string.Empty);
                return _transport;
            }

            if (!InfluxSecretResolver.TryResolve(
                    _options.TokenReference,
                    out var token,
                    out var errorCode,
                    out var errorMessage))
            {
                configurationError = (errorCode, errorMessage);
                return null;
            }

            _transport = new InfluxHistoryClient(new InfluxDbClientSettings(
                _options.Url,
                _options.Organization,
                _options.Bucket,
                token!,
                _options.ConnectionTimeoutMilliseconds,
                _options.WriteTimeoutMilliseconds,
                _options.QueryTimeoutMilliseconds));
            configurationError = (string.Empty, string.Empty);
            return _transport;
        }
    }

    private async Task WaitForWorkOrDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _workSignal.WaitAsync(waitCts.Token);
        var delayTask = Task.Delay(delay, _timeProvider, waitCts.Token);
        try
        {
            var completed = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
            waitCts.Cancel();
            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task DelayOnlyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private void SignalWork()
    {
        TryReleaseWorkSignal(_workSignal);
    }

    internal static void TryReleaseWorkSignal(SemaphoreSlim signal)
    {
        try
        {
            signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private TimeSpan GetBackoff(int failureCount)
    {
        var initial = Math.Max(1, _options.ReconnectInitialDelayMilliseconds);
        var maximum = Math.Max(initial, _options.ReconnectMaxDelayMilliseconds);
        var exponent = Math.Min(failureCount - 1, 30);
        var milliseconds = Math.Min(maximum, initial * Math.Pow(2, exponent));
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private bool IsExpired(InfluxOutboxRow row, long? retentionSeconds)
    {
        if (retentionSeconds is not > 0)
        {
            return false;
        }

        return row.Sample.RecordedAtUtc < _timeProvider.GetUtcNow().Subtract(TimeSpan.FromSeconds(retentionSeconds.Value));
    }

    private string BuildQuery(HistoryQuery query, long startNanoseconds, long? stopNanoseconds)
    {
        var start = FluxTime(startNanoseconds);
        var stop = stopNanoseconds is long value
            ? $", stop: {FluxTime(value)}"
            : string.Empty;
        return $"""
            from(bucket: {FluxString(_options.Bucket)})
              |> range(start: {start}{stop})
              |> filter(fn: (r) => r._measurement == {FluxString(_options.Measurement)})
              |> filter(fn: (r) => r.runtime_id == {FluxString(query.RuntimeId)})
              |> filter(fn: (r) => r.tag_id == {FluxString(query.TagId)})
              |> pivot(rowKey: ["_time", "runtime_id", "tag_id"], columnKey: ["_field"], valueColumn: "_value")
              |> filter(fn: (r) => r.recorded_at_utc_ticks >= {query.FromRecordedAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)} and r.recorded_at_utc_ticks < {query.ToRecordedAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)})
              |> sort(columns: ["recorded_at_utc_ticks", "tag_sequence", "_time"])
              |> limit(n: {query.Limit.ToString(CultureInfo.InvariantCulture)})
            """;
    }

    private static string FluxTime(long nanoseconds) =>
        $"time(v: {nanoseconds.ToString(CultureInfo.InvariantCulture)})";

    private static IReadOnlyList<HistorySample> DecodeQuery(string raw, HistoryQuery query)
    {
        var rows = raw.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string[]? headers = null;
        var results = new List<HistorySample>();
        foreach (var row in rows)
        {
            if (row.StartsWith('#'))
            {
                continue;
            }

            var fields = ParseCsvLine(row);
            if (headers is null)
            {
                if (!fields.Contains("recorded_at_utc_ticks", StringComparer.Ordinal))
                {
                    continue;
                }

                headers = fields.ToArray();
                continue;
            }

            if (fields.Count != headers.Length)
            {
                continue;
            }

            var values = headers
                .Select((header, index) => (header, value: fields[index]))
                .ToDictionary(item => item.header, item => item.value, StringComparer.Ordinal);
            if (!values.TryGetValue("data_type", out var dataTypeText) ||
                !Enum.TryParse<TagDataType>(dataTypeText, true, out var dataType) ||
                !values.TryGetValue("quality", out var qualityText) ||
                !Enum.TryParse<TagQuality>(qualityText, true, out var quality) ||
                !long.TryParse(values.GetValueOrDefault("source_timestamp_utc_ticks"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceTicks) ||
                !long.TryParse(values.GetValueOrDefault("recorded_at_utc_ticks"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recordedTicks) ||
                !long.TryParse(values.GetValueOrDefault("tag_sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) ||
                !bool.TryParse(values.GetValueOrDefault("has_value"), out var hasValue))
            {
                continue;
            }

            var recorded = new DateTimeOffset(recordedTicks, TimeSpan.Zero);
            if (recorded < query.FromRecordedAtUtc || recorded >= query.ToRecordedAtUtc)
            {
                continue;
            }

            object? value = null;
            if (hasValue)
            {
                value = dataType switch
                {
                    TagDataType.Boolean when bool.TryParse(values.GetValueOrDefault("value_boolean"), out var boolean) => boolean,
                    TagDataType.Int32 when long.TryParse(values.GetValueOrDefault("value_integer"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var int32) => checked((int)int32),
                    TagDataType.Int64 when long.TryParse(values.GetValueOrDefault("value_integer"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var int64) => int64,
                    TagDataType.Double when double.TryParse(values.GetValueOrDefault("value_real"), NumberStyles.Float, CultureInfo.InvariantCulture, out var real) => real,
                    TagDataType.String => values.GetValueOrDefault("value_text"),
                    _ => null
                };
            }

            results.Add(new HistorySample(
                query.RuntimeId,
                query.TagId,
                dataType,
                value,
                quality,
                new DateTimeOffset(sourceTicks, TimeSpan.Zero),
                recorded,
                sequence));
        }

        return results
            .OrderBy(sample => sample.RecordedAtUtc)
            .ThenBy(sample => sample.TagSequence)
            .Take(query.Limit)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        fields.Add(builder.ToString());
        return fields;
    }

    private static string FluxString(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static HistoryStoreOperationResult Failure(string code, string message) =>
        new(false, code, message);

    private void SetState(HistoryStoreState state, string? errorCode, string? errorMessage)
    {
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                State = state,
                LastErrorCode = errorCode,
                LastErrorMessage = errorMessage
            };
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Influx history store has not been initialized.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
