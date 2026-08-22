using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scada.Core.Configuration;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.Runtime.Historian;

public sealed class HistorianRuntimeService : IHostedService, IAsyncDisposable
{
    private readonly RuntimeOptions _options;
    private readonly ITagCache _tagCache;
    private readonly IHistoryStore _store;
    private readonly ILogger<HistorianRuntimeService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly object _evaluationSync = new();
    private readonly List<IDisposable> _subscriptions = [];
    private CancellationTokenSource? _lifetimeCts;
    private CancellationTokenSource? _writerCts;
    private HistorianQueue? _queue;
    private HistoryProfileEvaluator? _evaluator;
    private HistorianCoordinator? _coordinator;
    private HistoryProfileRegistry? _profileRegistry;
    private Dictionary<string, TagDefinition> _historyTagsById = new(StringComparer.OrdinalIgnoreCase);
    private Task? _writerTask;
    private Task? _coordinatorTask;
    private HistorianRuntimeState _state = HistorianRuntimeState.Disabled;
    private string? _lastErrorCode;
    private string? _lastErrorMessage;
    private DateTimeOffset? _lastWriteUtc;
    private long _enqueuedSamples;
    private long _writtenSamples;
    private long _rejectedSamples;
    private long _droppedSamples;
    private long _abandonedSamples;
    private long _writeFailures;
    private bool _accepting;
    private bool _started;

    public HistorianRuntimeService(
        RuntimeOptions options,
        ITagCache tagCache,
        IHistoryStore store,
        ILogger<HistorianRuntimeService> logger,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tagCache = tagCache ?? throw new ArgumentNullException(nameof(tagCache));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public HistorianRuntimeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new HistorianRuntimeSnapshot(
                    _state,
                    _options.Historian.QueueCapacity,
                    _queue?.Depth ?? 0,
                    Interlocked.Read(ref _enqueuedSamples),
                    Interlocked.Read(ref _writtenSamples),
                    Interlocked.Read(ref _rejectedSamples),
                    Interlocked.Read(ref _droppedSamples),
                    Interlocked.Read(ref _abandonedSamples),
                    Interlocked.Read(ref _writeFailures),
                    _lastWriteUtc,
                    _lastErrorCode,
                    _lastErrorMessage);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_started)
            {
                return Task.CompletedTask;
            }

            _started = true;
        }

        if (!_options.Historian.Enabled)
        {
            SetState(HistorianRuntimeState.Disabled, null, null);
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var preflight = _store.Preflight();
        if (preflight.Status == HistoryStorePreflightStatus.Faulted)
        {
            StopAccepting();
            SetState(HistorianRuntimeState.Faulted, preflight.ErrorCode, preflight.ErrorMessage);
            return Task.CompletedTask;
        }

        _queue = new HistorianQueue(_options.Historian.QueueCapacity);
        _evaluator = new HistoryProfileEvaluator(_timeProvider);
        _coordinator = new HistorianCoordinator(_timeProvider);
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // The writer must remain alive after intake stops so shutdown can drain
        // accepted samples. Its token is cancelled only by the shutdown budget
        // (or when the writer itself must stop), not when the coordinator stops.
        _writerCts = new CancellationTokenSource();
        _accepting = true;
        SetState(
            preflight.Status == HistoryStorePreflightStatus.Recoverable
                ? HistorianRuntimeState.Degraded
                : HistorianRuntimeState.Starting,
            preflight.ErrorCode,
            preflight.ErrorMessage);

        var registry = new HistoryProfileRegistry(_options.Historian.Profiles);
        var validTags = new List<(TagDefinition Tag, HistoryProfileDefinition Profile)>();
        foreach (var tag in _options.Tags.Where(tag => tag.Enabled && tag.HistoryEnabled))
        {
            if (!registry.TryGet(tag.HistoryProfile, out var profile) || profile is null ||
                !HistoryProfileValidation.IsCompatible(tag.DataType, profile))
            {
                continue;
            }

            validTags.Add((tag, profile));
        }

        _profileRegistry = registry;
        _historyTagsById = validTags.ToDictionary(
            item => item.Tag.Id,
            item => item.Tag,
            StringComparer.OrdinalIgnoreCase);

        if (validTags.Count > _options.Historian.QueueCapacity)
        {
            _logger.LogWarning(
                "Historian queue capacity {Capacity} is below the {TagCount} valid history-enabled tags; startup seed overflow is possible.",
                _options.Historian.QueueCapacity,
                validTags.Count);
            SetState(HistorianRuntimeState.Degraded, "HISTORIAN_QUEUE_CAPACITY_WARNING",
                "Historian queue capacity is below the valid history-enabled tag count.");
        }

        foreach (var (tag, profile) in validTags)
        {
            var subscription = _tagCache.Subscribe(tag.Id, value => OnTagValue(tag, profile, value));
            _subscriptions.Add(subscription);
            if (_tagCache.TryGet(tag.Id, out var cachedValue) && cachedValue is not null)
            {
                OnTagValue(tag, profile, cachedValue);
            }
        }

        _writerTask = Task.Run(() => WriterLoopAsync(_writerCts.Token), CancellationToken.None);
        _coordinatorTask = Task.Run(() => CoordinatorLoopAsync(_lifetimeCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_started || _state is HistorianRuntimeState.Disabled or HistorianRuntimeState.Stopping)
            {
                return;
            }

            _state = HistorianRuntimeState.Stopping;
            _accepting = false;
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _lifetimeCts?.Cancel();
        _queue?.Complete();

        if (_writerTask is not null)
        {
            using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            shutdownCts.CancelAfter(_options.Historian.ShutdownDrainTimeoutMilliseconds);
            try
            {
                await _writerTask.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
            {
                _writerCts?.Cancel();
                var abandoned = _queue?.Drain() ?? 0;
                AddAbandoned(abandoned);
                _logger.LogWarning("Historian shutdown drain exceeded its configured budget.");
            }
        }

        _coordinatorTask = null;
        _writerTask = null;
        _writerCts?.Dispose();
        _writerCts = null;
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        _profileRegistry = null;
        _historyTagsById.Clear();
        _started = false;
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private void OnTagValue(TagDefinition tag, HistoryProfileDefinition profile, TagValue value)
    {
        if (!_accepting || _queue is null || _evaluator is null || _coordinator is null)
        {
            return;
        }

        HistoryEvaluationResult result;
        lock (_evaluationSync)
        {
            result = _evaluator.Evaluate(
                _options.RuntimeId,
                tag,
                profile,
                value,
                _timeProvider.GetUtcNow(),
                _timeProvider.GetTimestamp());
        }
        EnqueueResult(tag.Id, result);
    }

    private void EnqueueResult(string tagId, HistoryEvaluationResult result)
    {
        if (result.Rejected)
        {
            Interlocked.Increment(ref _rejectedSamples);
            _logger.LogWarning("Historian rejected sample for {TagId}: {Reason}", tagId, result.RejectionReason);
            SetState(HistorianRuntimeState.Degraded, "HISTORIAN_SAMPLE_REJECTED", result.RejectionReason);
            return;
        }

        if (result.Sample is null)
        {
            return;
        }

        if (_queue is null)
        {
            AddAbandoned(1);
            return;
        }

        if (!_queue.TryWrite(result.Sample))
        {
            Interlocked.Increment(ref _droppedSamples);
            SetState(HistorianRuntimeState.Degraded, "HISTORIAN_QUEUE_FULL", "Historian queue is full.");
            return;
        }

        Interlocked.Increment(ref _enqueuedSamples);
        if (result.NextDueTimestamp is not null)
        {
            _coordinator?.Schedule(tagId, result.NextDueTimestamp.Value);
        }
    }

    private async Task CoordinatorLoopAsync(CancellationToken cancellationToken)
    {
        if (_coordinator is null || _evaluator is null || _queue is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _timeProvider.GetTimestamp();
            while (_coordinator.TryTakeDue(now, out var tagId) && tagId is not null)
            {
                if (!_historyTagsById.TryGetValue(tagId, out var tag) ||
                    !_tagCache.TryGet(tag.Id, out var value) || value is null)
                {
                    continue;
                }

                if (_profileRegistry is null ||
                    !_profileRegistry.TryGet(tag.HistoryProfile, out var profile) || profile is null)
                {
                    continue;
                }

                HistoryEvaluationResult result;
                lock (_evaluationSync)
                {
                    result = _evaluator.EvaluatePeriodic(
                        _options.RuntimeId,
                        tag,
                        profile,
                        value,
                        _timeProvider.GetUtcNow(),
                        now);
                }
                EnqueueResult(tag.Id, result);
            }

            var delay = _coordinator.GetDelay(now);
            try
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task WriterLoopAsync(CancellationToken cancellationToken)
    {
        if (_queue is null)
        {
            return;
        }

        var initialized = false;
        while (!initialized && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
                initialized = true;
                if (Snapshot.State == HistorianRuntimeState.Starting)
                {
                    SetState(HistorianRuntimeState.Healthy, null, null);
                }
                else if (Snapshot.State == HistorianRuntimeState.Degraded &&
                         Snapshot.LastErrorCode != "HISTORIAN_QUEUE_CAPACITY_WARNING")
                {
                    SetState(HistorianRuntimeState.Healthy, null, null);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (IsPermanent(exception))
                {
                    SetFaulted(
                        exception is HistoryStorePermanentException permanent ? permanent.Code : "HISTORIAN_STORAGE_FAULT",
                        exception.Message);
                    var abandoned = _queue.Drain();
                    AddAbandoned(abandoned);
                    return;
                }

                Interlocked.Increment(ref _writeFailures);
                SetState(HistorianRuntimeState.Degraded, "HISTORIAN_STORAGE_UNAVAILABLE", exception.Message);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), _timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        if (!initialized || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<HistorySample>? batch;
            try
            {
                batch = await _queue.ReadBatchAsync(
                    _options.Historian.BatchSize,
                    TimeSpan.FromMilliseconds(_options.Historian.FlushIntervalMilliseconds),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (batch is null)
            {
                return;
            }

            var attempt = 0;
            while (true)
            {
                try
                {
                    await _store.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                    Interlocked.Add(ref _writtenSamples, batch.Count);
                    lock (_sync)
                    {
                        _lastWriteUtc = _timeProvider.GetUtcNow();
                    }

                    if (Snapshot.State == HistorianRuntimeState.Degraded)
                    {
                        SetState(HistorianRuntimeState.Healthy, null, null);
                    }

                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AddAbandoned(batch.Count);
                    return;
                }
                catch (Exception exception) when (!IsPermanent(exception) && attempt < 3)
                {
                    attempt++;
                    Interlocked.Increment(ref _writeFailures);
                    SetState(HistorianRuntimeState.Degraded, "HISTORIAN_WRITE_FAILED", exception.Message);
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), _timeProvider, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        AddAbandoned(batch.Count);
                        return;
                    }
                }
                catch (Exception exception)
                {
                    if (IsPermanent(exception))
                    {
                        SetFaulted(
                            exception is HistoryStorePermanentException permanent ? permanent.Code : "HISTORIAN_STORAGE_FAULT",
                            exception.Message);

                        AddAbandoned(batch.Count);
                        _queue.Complete();
                        AddAbandoned(_queue.Drain());
                        return;
                    }

                    SetState(HistorianRuntimeState.Degraded, "HISTORIAN_WRITE_FAILED", exception.Message);
                    Interlocked.Increment(ref _writeFailures);
                    AddAbandoned(batch.Count);
                    break;
                }
            }
        }
    }

    private void SetFaulted(string code, string message)
    {
        StopAccepting();
        _lifetimeCts?.Cancel();
        SetState(HistorianRuntimeState.Faulted, code, message);
    }

    private void StopAccepting()
    {
        _accepting = false;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _queue?.Complete();
    }

    private void SetState(HistorianRuntimeState state, string? code, string? message)
    {
        lock (_sync)
        {
            _state = state;
            _lastErrorCode = code;
            _lastErrorMessage = message;
        }
    }

    private void AddAbandoned(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _abandonedSamples, count);
        }
    }

    private static bool IsPermanent(Exception exception) =>
        exception is HistoryStorePermanentException;
}
