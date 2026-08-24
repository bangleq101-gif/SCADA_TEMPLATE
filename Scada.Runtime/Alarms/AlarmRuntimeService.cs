using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scada.Core.Alarms;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.Runtime.Alarms;

public sealed class AlarmRuntimeService : IHostedService, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly RuntimeOptions _runtimeOptions;
    private readonly AlarmOptions _options;
    private readonly ITagCache _tagCache;
    private readonly IAlarmEventStore? _store;
    private readonly ILogger<AlarmRuntimeService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, MutableAlarm> _alarms = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _tagSubscriptions = [];
    private readonly List<Action<AlarmRuntimeSnapshot>> _snapshotSubscribers = [];
    private readonly Channel<bool> _deadlineSignals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private Channel<AlarmPersistenceBatch>? _persistenceChannel;
    private CancellationTokenSource? _deadlineCts;
    private CancellationTokenSource? _persistenceCts;
    private Task? _deadlineTask;
    private Task? _persistenceTask;
    private AlarmRuntimeSnapshot _snapshot = AlarmRuntimeSnapshot.Disabled;
    private Guid _sessionId;
    private long _eventSequence;
    private bool _accepting;
    private bool _persistenceGap;
    private int _persistenceDepth;
    private long _activated;
    private long _acknowledged;
    private long _returned;
    private long _closed;
    private long _reactivated;
    private long _rejectedEvaluations;
    private long _staleUpdates;
    private long _subscriberExceptions;
    private long _persistedEvents;
    private long _rejectedPersistence;
    private long _droppedPersistence;
    private long _abandonedPersistence;
    private long _writeFailures;
    private bool _recoveredFromTrustedCheckpoint;
    private int _recoveryUntrustedInstances;
    private int _orphanedInstances;
    private string? _lastErrorCode;
    private string? _lastErrorMessage;

    public AlarmRuntimeService(
        RuntimeOptions runtimeOptions,
        ITagCache tagCache,
        IAlarmEventStore? store,
        ILogger<AlarmRuntimeService> logger,
        TimeProvider timeProvider)
    {
        _runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
        _options = runtimeOptions.Alarms ?? new AlarmOptions();
        _tagCache = tagCache ?? throw new ArgumentNullException(nameof(tagCache));
        _store = store;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public AlarmRuntimeSnapshot Snapshot
    {
        get { lock (_sync) return _snapshot; }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            SetRuntimeState(AlarmRuntimeState.Disabled);
            return;
        }

        lock (_sync)
        {
            if (_accepting || _snapshot.State == AlarmRuntimeState.Starting) return;
            _snapshot = BuildSnapshotLocked(AlarmRuntimeState.Starting);
        }
        NotifySnapshotSubscribers();

        AlarmRecoveryResult recovery = new(false, 0, []);
        _sessionId = Guid.NewGuid();
        if (_options.PersistenceEnabled)
        {
            if (_store is null)
            {
                FailClosed("ALARM_STORE_REQUIRED", "Alarm persistence is enabled but no event store is configured.");
                return;
            }

            try
            {
                await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
                recovery = await _store.LoadRecoveryAsync(cancellationToken).ConfigureAwait(false);
                await _store.BeginUntrustedSessionAsync(
                    new AlarmStoreSessionRequest(_sessionId, _runtimeOptions.RuntimeId, _timeProvider.GetUtcNow()),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Alarm durable recovery-untrusted startup marker failed.");
                FailClosed("ALARM_RECOVERY_MARKER_FAILED", exception.Message);
                return;
            }
        }

        var enabledDefinitions = _options.Definitions
            .Where(definition => definition.Enabled)
            .OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tagIds = _runtimeOptions.Tags.Where(tag => tag.Enabled)
            .Select(tag => tag.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        enabledDefinitions = enabledDefinitions.Where(definition => tagIds.Contains(definition.TagId)).ToArray();

        lock (_sync)
        {
            _alarms.Clear();
            foreach (var definition in enabledDefinitions)
                _alarms[definition.Id] = new MutableAlarm(definition);
            ApplyRecoveryLocked(recovery);
            _accepting = true;
            _deadlineCts = new CancellationTokenSource();
            if (_options.PersistenceEnabled)
            {
                _persistenceCts = new CancellationTokenSource();
                _persistenceChannel = Channel.CreateBounded<AlarmPersistenceBatch>(new BoundedChannelOptions(_options.QueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false
                });
                _persistenceTask = RunPersistenceAsync(_persistenceCts.Token);
            }
            _deadlineTask = RunDeadlineCoordinatorAsync(_deadlineCts.Token);
            _snapshot = BuildSnapshotLocked(AlarmRuntimeState.Healthy);
        }

        var byTag = enabledDefinitions.GroupBy(definition => definition.TagId, StringComparer.OrdinalIgnoreCase);
        foreach (var group in byTag)
        {
            var tagId = group.Key;
            var subscription = _tagCache.Subscribe(tagId, value => ProcessTagValue(tagId, value));
            var keep = false;
            lock (_sync)
            {
                if (_accepting)
                {
                    _tagSubscriptions.Add(subscription);
                    keep = true;
                    _snapshot = BuildSnapshotLocked(_snapshot.State);
                }
            }
            if (!keep) { subscription.Dispose(); break; }
            if (_tagCache.TryGet(tagId, out var current) && current is not null)
                ProcessTagValue(tagId, current);
        }
        NotifySnapshotSubscribers();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        IDisposable[] subscriptions;
        CancellationTokenSource? deadlineCts;
        CancellationTokenSource? persistenceCts;
        Task? deadlineTask;
        Task? persistenceTask;
        Channel<AlarmPersistenceBatch>? persistenceChannel;
        lock (_sync)
        {
            if (!_accepting && _snapshot.State is AlarmRuntimeState.Disabled or AlarmRuntimeState.Faulted) return;
            _accepting = false;
            subscriptions = _tagSubscriptions.ToArray();
            _tagSubscriptions.Clear();
            deadlineCts = _deadlineCts;
            persistenceCts = _persistenceCts;
            deadlineTask = _deadlineTask;
            persistenceTask = _persistenceTask;
            persistenceChannel = _persistenceChannel;
            _snapshot = BuildSnapshotLocked(AlarmRuntimeState.Stopping);
        }
        NotifySnapshotSubscribers();
        foreach (var subscription in subscriptions) subscription.Dispose();

        deadlineCts?.Cancel();
        persistenceChannel?.Writer.TryComplete();
        if (deadlineTask is not null) await ObserveAsync(deadlineTask).ConfigureAwait(false);

        var drained = true;
        if (persistenceTask is not null)
        {
            try
            {
                await persistenceTask.WaitAsync(
                    TimeSpan.FromMilliseconds(_options.ShutdownDrainTimeoutMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                persistenceCts?.Cancel();
                drained = false;
                lock (_sync)
                {
                    _persistenceGap = true;
                    _abandonedPersistence += Math.Max(0, _persistenceDepth);
                    _lastErrorCode = "ALARM_SHUTDOWN_DRAIN_TIMEOUT";
                    _lastErrorMessage = "Alarm persistence did not drain within the configured shutdown budget.";
                }
            }
        }

        if (_options.PersistenceEnabled && _store is not null && drained && !_persistenceGap)
        {
            var checkpoint = CreateCheckpoint(recoveryTrusted: true);
            try
            {
                await _store.CommitTrustedCheckpointAsync(checkpoint, cancellationToken)
                    .WaitAsync(TimeSpan.FromMilliseconds(_options.ShutdownDrainTimeoutMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lock (_sync)
                {
                    _persistenceGap = true;
                    _writeFailures++;
                    _lastErrorCode = "ALARM_CHECKPOINT_COMMIT_FAILED";
                    _lastErrorMessage = exception.Message;
                }
            }
        }

        lock (_sync)
        {
            _snapshot = BuildSnapshotLocked(_persistenceGap ? AlarmRuntimeState.Degraded : AlarmRuntimeState.Disabled);
            _deadlineCts?.Dispose();
            _deadlineCts = null;
            _persistenceCts?.Dispose();
            _persistenceCts = null;
        }
        NotifySnapshotSubscribers();
    }

    public AlarmAcknowledgementResult Acknowledge(AlarmAcknowledgementRequest request)
    {
        AlarmRuntimeSnapshot? changedSnapshot = null;
        AlarmAcknowledgementResult result;
        lock (_sync)
        {
            if (!_accepting)
                return new(request.InstanceId, AlarmAcknowledgementStatus.RuntimeUnavailable);
            var alarm = _alarms.Values.FirstOrDefault(item => item.InstanceId == request.InstanceId);
            if (alarm is null)
                return new(request.InstanceId, AlarmAcknowledgementStatus.StaleOrNotFound);
            if (alarm.AcknowledgedAtUtc is not null)
                return new(request.InstanceId, AlarmAcknowledgementStatus.AlreadyAcknowledged, alarm.AcknowledgedAtUtc);
            if (alarm.State is not (AlarmLifecycleState.ActiveUnacknowledged or AlarmLifecycleState.ReturnedUnacknowledged))
                return new(request.InstanceId, AlarmAcknowledgementStatus.NotEligible);

            var now = _timeProvider.GetUtcNow();
            alarm.AcknowledgedAtUtc = now;
            alarm.AcknowledgedBy = request.AcknowledgedBy;
            _acknowledged++;
            if (alarm.State == AlarmLifecycleState.ReturnedUnacknowledged)
            {
                alarm.State = AlarmLifecycleState.Normal;
                alarm.TransitionTimestampUtc = now;
                AddEventLocked(alarm, AlarmEventType.Acknowledged, now, request.AcknowledgedBy);
                AddEventLocked(alarm, AlarmEventType.Closed, now);
                _closed++;
            }
            else
            {
                alarm.State = AlarmLifecycleState.ActiveAcknowledged;
                alarm.TransitionTimestampUtc = now;
                AddEventLocked(alarm, AlarmEventType.Acknowledged, now, request.AcknowledgedBy);
            }
            changedSnapshot = _snapshot = BuildSnapshotLocked(CurrentOperationalStateLocked());
            result = new(request.InstanceId, AlarmAcknowledgementStatus.Acknowledged, now);
        }
        NotifySnapshotSubscribers(changedSnapshot);
        return result;
    }

    public IReadOnlyList<AlarmAcknowledgementResult> AcknowledgeAll(string? acknowledgedBy = null)
    {
        Guid[] eligible;
        lock (_sync)
        {
            eligible = _alarms.Values
                .Where(alarm => alarm.InstanceId is not null && alarm.AcknowledgedAtUtc is null &&
                    alarm.State is AlarmLifecycleState.ActiveUnacknowledged or AlarmLifecycleState.ReturnedUnacknowledged)
                .Select(alarm => alarm.InstanceId!.Value)
                .ToArray();
        }
        return eligible.Select(instanceId => Acknowledge(new(instanceId, acknowledgedBy))).ToArray();
    }

    public IDisposable Subscribe(Action<AlarmRuntimeSnapshot> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_sync) _snapshotSubscribers.Add(callback);
        return new Subscription(() => { lock (_sync) _snapshotSubscribers.Remove(callback); });
    }

    internal void ProcessDueDeadlines()
    {
        AlarmRuntimeSnapshot? changed = null;
        lock (_sync)
        {
            if (!_accepting) return;
            var nowTimestamp = _timeProvider.GetTimestamp();
            var nowUtc = _timeProvider.GetUtcNow();
            foreach (var alarm in _alarms.Values.Where(item => item.Pending && item.DeadlineTimestamp <= nowTimestamp).ToArray())
            {
                alarm.Pending = false;
                if (alarm.IsEvaluationAvailable && alarm.ConditionActive)
                {
                    ActivateLocked(alarm, nowUtc);
                    changed = _snapshot = BuildSnapshotLocked(CurrentOperationalStateLocked());
                }
            }
        }
        if (changed is not null) NotifySnapshotSubscribers(changed);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void ProcessTagValue(string tagId, TagValue value)
    {
        AlarmRuntimeSnapshot? changed = null;
        lock (_sync)
        {
            if (!_accepting) return;
            foreach (var alarm in _alarms.Values.Where(item => item.Definition.TagId.Equals(tagId, StringComparison.OrdinalIgnoreCase)))
            {
                if (value.Sequence <= alarm.LastEvaluatedSequence)
                {
                    _staleUpdates++;
                    continue;
                }
                alarm.LastEvaluatedSequence = value.Sequence;
                alarm.LastSourceSequence = value.Sequence;
                alarm.LastSourceTimestampUtc = value.Timestamp;
                alarm.EvaluationQuality = value.Quality;

                var wasConditionActive = alarm.Pending || alarm.State is AlarmLifecycleState.ActiveUnacknowledged or AlarmLifecycleState.ActiveAcknowledged;
                var evaluation = AlarmEvaluator.Evaluate(alarm.Definition, value, wasConditionActive);
                alarm.IsEvaluationAvailable = evaluation.IsAvailable;
                if (!evaluation.IsAvailable)
                {
                    if (value.Quality == TagQuality.Good) _rejectedEvaluations++;
                    if (alarm.Pending) alarm.Pending = false;
                    continue;
                }
                alarm.ConditionActive = evaluation.ConditionActive;
                ApplyEvaluationLocked(alarm);
            }
            changed = _snapshot = BuildSnapshotLocked(CurrentOperationalStateLocked());
        }
        NotifySnapshotSubscribers(changed);
    }

    private void ApplyEvaluationLocked(MutableAlarm alarm)
    {
        var now = _timeProvider.GetUtcNow();
        if (alarm.State == AlarmLifecycleState.Normal)
        {
            if (!alarm.ConditionActive)
            {
                alarm.Pending = false;
                return;
            }
            if (alarm.Definition.ActivationDelay <= TimeSpan.Zero)
            {
                ActivateLocked(alarm, now);
                return;
            }
            if (!alarm.Pending)
            {
                alarm.Pending = true;
                alarm.DeadlineTimestamp = AddDuration(_timeProvider.GetTimestamp(), alarm.Definition.ActivationDelay);
                _deadlineSignals.Writer.TryWrite(true);
            }
            return;
        }

        if (alarm.State == AlarmLifecycleState.ReturnedUnacknowledged)
        {
            if (alarm.ConditionActive)
            {
                alarm.State = AlarmLifecycleState.ActiveUnacknowledged;
                alarm.TransitionTimestampUtc = now;
                AddEventLocked(alarm, AlarmEventType.Reactivated, now);
                _reactivated++;
            }
            return;
        }

        if (alarm.ConditionActive) return;
        alarm.TransitionTimestampUtc = now;
        _returned++;
        if (alarm.State == AlarmLifecycleState.ActiveUnacknowledged)
        {
            alarm.State = AlarmLifecycleState.ReturnedUnacknowledged;
            AddEventLocked(alarm, AlarmEventType.Returned, now);
        }
        else
        {
            alarm.State = AlarmLifecycleState.Normal;
            AddEventLocked(alarm, AlarmEventType.Returned, now);
            AddEventLocked(alarm, AlarmEventType.Closed, now);
            _closed++;
        }
    }

    private void ActivateLocked(MutableAlarm alarm, DateTimeOffset now)
    {
        alarm.InstanceId = Guid.NewGuid();
        alarm.ActivatedAtUtc = now;
        alarm.TransitionTimestampUtc = now;
        alarm.AcknowledgedAtUtc = null;
        alarm.AcknowledgedBy = null;
        alarm.State = alarm.Definition.AcknowledgementRequired
            ? AlarmLifecycleState.ActiveUnacknowledged
            : AlarmLifecycleState.ActiveAcknowledged;
        AddEventLocked(alarm, AlarmEventType.Activated, now);
        _activated++;
    }

    private void AddEventLocked(MutableAlarm alarm, AlarmEventType type, DateTimeOffset timestamp, string? acknowledgedBy = null)
    {
        if (alarm.InstanceId is not Guid instanceId) return;
        var alarmEvent = new AlarmEvent(
            ++_eventSequence, alarm.Definition.Id, instanceId, type, alarm.Definition.Severity, timestamp,
            alarm.Fingerprint, alarm.LastSourceSequence, alarm.LastSourceTimestampUtc, acknowledgedBy);
        if (!_options.PersistenceEnabled) return;
        if (_persistenceChannel is null)
        {
            _rejectedPersistence++;
            _persistenceGap = true;
            return;
        }
        var batch = new AlarmPersistenceBatch(_sessionId, [alarmEvent], CreateOpenInstanceRecordsLocked(), _eventSequence);
        if (_persistenceChannel.Writer.TryWrite(batch))
            _persistenceDepth++;
        else
        {
            _droppedPersistence++;
            _persistenceGap = true;
            _lastErrorCode = "ALARM_PERSISTENCE_QUEUE_FULL";
            _lastErrorMessage = "Alarm persistence queue is full.";
        }
    }

    private async Task RunPersistenceAsync(CancellationToken cancellationToken)
    {
        if (_persistenceChannel is null || _store is null) return;
        try
        {
            while (await ReadPersistenceBatchAsync(_persistenceChannel.Reader, cancellationToken).ConfigureAwait(false) is { } pending)
            {
                try
                {
                    await _store.PersistBatchAsync(pending.Batch, cancellationToken).ConfigureAwait(false);
                    lock (_sync)
                    {
                        _persistenceDepth -= pending.SourceItemCount;
                        _persistedEvents += pending.Batch.Events.Count;
                        _snapshot = BuildSnapshotLocked(CurrentOperationalStateLocked());
                    }
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(exception, "Alarm persistence write failed.");
                    lock (_sync)
                    {
                        _persistenceDepth -= pending.SourceItemCount;
                        _writeFailures++;
                        _persistenceGap = true;
                        _lastErrorCode = "ALARM_PERSISTENCE_WRITE_FAILED";
                        _lastErrorMessage = exception.Message;
                        _snapshot = BuildSnapshotLocked(AlarmRuntimeState.Degraded);
                    }
                }
                NotifySnapshotSubscribers();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task<PendingPersistenceBatch?> ReadPersistenceBatchAsync(
        ChannelReader<AlarmPersistenceBatch> reader,
        CancellationToken cancellationToken)
    {
        if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false) || !reader.TryRead(out var first))
            return null;

        var events = new List<AlarmEvent>(Math.Min(_options.BatchSize, 256));
        events.AddRange(first.Events);
        var latest = first;
        var sourceItemCount = 1;
        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var flushTask = Task.Delay(
            TimeSpan.FromMilliseconds(_options.FlushIntervalMilliseconds),
            _timeProvider,
            flushCts.Token);

        while (events.Count < _options.BatchSize)
        {
            while (events.Count < _options.BatchSize && reader.TryRead(out var next))
            {
                events.AddRange(next.Events);
                latest = next;
                sourceItemCount++;
            }
            if (events.Count >= _options.BatchSize || reader.Completion.IsCompleted) break;

            var availableTask = reader.WaitToReadAsync(flushCts.Token).AsTask();
            var completed = await Task.WhenAny(availableTask, flushTask).ConfigureAwait(false);
            if (completed == flushTask)
            {
                flushCts.Cancel();
                try { await availableTask.ConfigureAwait(false); }
                catch (OperationCanceledException) when (flushCts.IsCancellationRequested) { }
                break;
            }
            if (!await availableTask.ConfigureAwait(false)) break;
        }

        flushCts.Cancel();
        if (!flushTask.IsCompleted)
        {
            try { await flushTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (flushCts.IsCancellationRequested) { }
        }

        return new PendingPersistenceBatch(
            new AlarmPersistenceBatch(
                first.SessionId,
                events,
                latest.OpenInstances,
                latest.ContinuitySequence),
            sourceItemCount);
    }

    private async Task RunDeadlineCoordinatorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan? delay;
                lock (_sync)
                {
                    var pending = _alarms.Values.Where(alarm => alarm.Pending).Select(alarm => alarm.DeadlineTimestamp).ToArray();
                    delay = pending.Length == 0 ? null : RemainingDelay(pending.Min());
                }
                if (delay is null)
                {
                    await _deadlineSignals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var signalTask = _deadlineSignals.Reader.ReadAsync(iterationCts.Token).AsTask();
                var delayTask = Task.Delay(delay.Value, _timeProvider, iterationCts.Token);
                var completed = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
                iterationCts.Cancel();
                try { await (completed == signalTask ? delayTask : signalTask).ConfigureAwait(false); }
                catch (OperationCanceledException) when (iterationCts.IsCancellationRequested) { }
                if (completed == delayTask) ProcessDueDeadlines();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void ApplyRecoveryLocked(AlarmRecoveryResult recovery)
    {
        _eventSequence = Math.Max(_eventSequence, recovery.ContinuitySequence);
        _recoveredFromTrustedCheckpoint = recovery.RecoveryTrusted;
        _orphanedInstances = recovery.OrphanedInstanceCount;
        if (!recovery.RecoveryTrusted)
        {
            _recoveryUntrustedInstances = recovery.OpenInstances.Count;
            return;
        }
        foreach (var record in recovery.OpenInstances)
        {
            if (!_alarms.TryGetValue(record.AlarmId, out var alarm) || alarm.Fingerprint != record.DefinitionFingerprint)
            {
                _orphanedInstances++;
                continue;
            }
            alarm.InstanceId = record.InstanceId;
            alarm.State = record.State;
            alarm.ActivatedAtUtc = record.ActivatedAtUtc;
            alarm.AcknowledgedAtUtc = record.AcknowledgedAtUtc;
            alarm.AcknowledgedBy = record.AcknowledgedBy;
            alarm.LastSourceSequence = record.LastSourceSequence;
            alarm.LastSourceTimestampUtc = record.LastSourceTimestampUtc;
            alarm.EvaluationQuality = record.EvaluationQuality;
            alarm.IsEvaluationAvailable = record.EvaluationQuality == TagQuality.Good;
        }
    }

    private AlarmStoreCheckpoint CreateCheckpoint(bool recoveryTrusted)
    {
        lock (_sync)
            return new(_sessionId, recoveryTrusted, _eventSequence, _timeProvider.GetUtcNow(), CreateOpenInstanceRecordsLocked());
    }

    private IReadOnlyList<AlarmInstanceRecord> CreateOpenInstanceRecordsLocked() =>
        _alarms.Values.Where(alarm => alarm.InstanceId is not null && alarm.State != AlarmLifecycleState.Normal)
            .Select(alarm => new AlarmInstanceRecord(
                alarm.Definition.Id, alarm.InstanceId!.Value, alarm.State, alarm.Definition.Severity,
                alarm.Fingerprint, alarm.ActivatedAtUtc ?? _timeProvider.GetUtcNow(), alarm.AcknowledgedAtUtc,
                alarm.AcknowledgedBy, alarm.LastSourceSequence, alarm.LastSourceTimestampUtc ?? _timeProvider.GetUtcNow(),
                alarm.EvaluationQuality))
            .ToArray();

    private AlarmRuntimeSnapshot BuildSnapshotLocked(AlarmRuntimeState state) => new(
        state,
        _alarms.Values.OrderBy(alarm => alarm.Definition.Order).ThenBy(alarm => alarm.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .Select(alarm => new AlarmSnapshot(
                alarm.Definition.Id, alarm.Definition.Name, alarm.Definition.Message, alarm.InstanceId,
                alarm.State, alarm.Definition.Severity, alarm.Pending, alarm.IsEvaluationAvailable,
                alarm.EvaluationQuality, alarm.LastSourceSequence, alarm.LastSourceTimestampUtc,
                alarm.TransitionTimestampUtc, alarm.ActivatedAtUtc, alarm.AcknowledgedAtUtc, alarm.AcknowledgedBy))
            .ToArray(),
        _alarms.Count,
        _tagSubscriptions.Count,
        _alarms.Values.Count(alarm => alarm.Pending),
        _persistenceDepth,
        _activated, _acknowledged, _returned, _closed, _reactivated,
        _rejectedEvaluations, _staleUpdates, _subscriberExceptions, _persistedEvents,
        _rejectedPersistence, _droppedPersistence, _abandonedPersistence, _writeFailures,
        _recoveredFromTrustedCheckpoint, _recoveryUntrustedInstances, _orphanedInstances,
        _lastErrorCode, _lastErrorMessage);

    private void FailClosed(string code, string message)
    {
        lock (_sync)
        {
            _accepting = false;
            _lastErrorCode = code;
            _lastErrorMessage = message;
            _alarms.Clear();
            _snapshot = BuildSnapshotLocked(AlarmRuntimeState.Faulted);
        }
        NotifySnapshotSubscribers();
    }

    private void SetRuntimeState(AlarmRuntimeState state)
    {
        lock (_sync) _snapshot = BuildSnapshotLocked(state);
        NotifySnapshotSubscribers();
    }

    private AlarmRuntimeState CurrentOperationalStateLocked() =>
        _persistenceGap ? AlarmRuntimeState.Degraded : AlarmRuntimeState.Healthy;

    private long AddDuration(long timestamp, TimeSpan duration)
    {
        var delta = checked((long)Math.Ceiling(duration.TotalSeconds * _timeProvider.TimestampFrequency));
        return checked(timestamp + delta);
    }

    private TimeSpan RemainingDelay(long deadline)
    {
        var now = _timeProvider.GetTimestamp();
        return deadline <= now ? TimeSpan.Zero : _timeProvider.GetElapsedTime(now, deadline);
    }

    private void NotifySnapshotSubscribers(AlarmRuntimeSnapshot? snapshot = null)
    {
        Action<AlarmRuntimeSnapshot>[] subscribers;
        lock (_sync)
        {
            snapshot ??= _snapshot;
            subscribers = _snapshotSubscribers.ToArray();
        }
        foreach (var subscriber in subscribers)
        {
            try { subscriber(snapshot); }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _subscriberExceptions);
                _logger.LogWarning(exception, "Alarm runtime snapshot subscriber failed.");
            }
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private sealed class MutableAlarm(AlarmDefinition definition)
    {
        public AlarmDefinition Definition { get; } = definition;
        public string Fingerprint { get; } = AlarmDefinitionFingerprint.Create(definition);
        public Guid? InstanceId { get; set; }
        public AlarmLifecycleState State { get; set; }
        public bool Pending { get; set; }
        public long DeadlineTimestamp { get; set; }
        public bool ConditionActive { get; set; }
        public bool IsEvaluationAvailable { get; set; }
        public TagQuality EvaluationQuality { get; set; } = TagQuality.NotConfigured;
        public long LastSourceSequence { get; set; }
        public long LastEvaluatedSequence { get; set; }
        public DateTimeOffset? LastSourceTimestampUtc { get; set; }
        public DateTimeOffset? TransitionTimestampUtc { get; set; }
        public DateTimeOffset? ActivatedAtUtc { get; set; }
        public DateTimeOffset? AcknowledgedAtUtc { get; set; }
        public string? AcknowledgedBy { get; set; }
    }

    private sealed record PendingPersistenceBatch(AlarmPersistenceBatch Batch, int SourceItemCount);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose(); }
    }
}
