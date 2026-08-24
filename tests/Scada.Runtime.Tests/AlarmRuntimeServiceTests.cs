using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Alarms;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Alarms;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class AlarmRuntimeServiceTests
{
    [Fact]
    public async Task ActivationDelayUsesMonotonicTimeAcrossWallClockJumps()
    {
        var clock = new ManualTimeProvider();
        var cache = new TrackingTagCache();
        await using var service = CreateService(cache, clock, delay: TimeSpan.FromSeconds(5));
        await service.StartAsync(CancellationToken.None);

        cache.Publish(Good(90, sequence: 1));
        clock.JumpUtc(TimeSpan.FromHours(12));
        service.ProcessDueDeadlines();
        Assert.Equal(AlarmLifecycleState.Normal, service.Snapshot.Alarms.Single().State);

        clock.JumpUtc(TimeSpan.FromHours(-24));
        clock.AdvanceMonotonic(TimeSpan.FromSeconds(4));
        service.ProcessDueDeadlines();
        Assert.Equal(AlarmLifecycleState.Normal, service.Snapshot.Alarms.Single().State);

        clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        service.ProcessDueDeadlines();
        Assert.Equal(AlarmLifecycleState.ActiveUnacknowledged, service.Snapshot.Alarms.Single().State);
        Assert.Single(service.Snapshot.Alarms, alarm => alarm.InstanceId is not null);

        service.ProcessDueDeadlines();
        Assert.Equal(1, service.Snapshot.ActivatedTransitions);
    }

    [Fact]
    public async Task ExactInstanceAcknowledgementIsStaleSafeIdempotentAndSupportsReturnedReactivation()
    {
        var cache = new TrackingTagCache();
        await using var service = CreateService(cache, delay: TimeSpan.Zero);
        await service.StartAsync(CancellationToken.None);

        cache.Publish(Good(90, 1));
        var first = Assert.IsType<Guid>(service.Snapshot.Alarms.Single().InstanceId);
        cache.Publish(Good(0, 2));
        Assert.Equal(AlarmLifecycleState.ReturnedUnacknowledged, service.Snapshot.Alarms.Single().State);

        cache.Publish(Good(90, 3));
        Assert.Equal(first, service.Snapshot.Alarms.Single().InstanceId);
        Assert.Equal(AlarmLifecycleState.ActiveUnacknowledged, service.Snapshot.Alarms.Single().State);

        var stale = service.Acknowledge(new AlarmAcknowledgementRequest(Guid.NewGuid(), "operator"));
        Assert.Equal(AlarmAcknowledgementStatus.StaleOrNotFound, stale.Status);

        var accepted = service.Acknowledge(new AlarmAcknowledgementRequest(first, "operator"));
        Assert.Equal(AlarmAcknowledgementStatus.Acknowledged, accepted.Status);
        Assert.Equal(AlarmLifecycleState.ActiveAcknowledged, service.Snapshot.Alarms.Single().State);

        var duplicate = service.Acknowledge(new AlarmAcknowledgementRequest(first, "operator"));
        Assert.Equal(AlarmAcknowledgementStatus.AlreadyAcknowledged, duplicate.Status);

        cache.Publish(Good(0, 4));
        Assert.Equal(AlarmLifecycleState.Normal, service.Snapshot.Alarms.Single().State);
        cache.Publish(Good(90, 5));
        Assert.NotEqual(first, service.Snapshot.Alarms.Single().InstanceId);
    }

    [Fact]
    public async Task DistinctTagSubscriptionSeedsAfterSubscribeAndUnavailableQualityHoldsLifecycle()
    {
        var cache = new TrackingTagCache { Seed = Good(90, 1) };
        var options = CreateOptions(delay: TimeSpan.Zero);
        options.Alarms.Definitions.Add(new AlarmDefinition
        {
            Id = "HIGH2", Name = "High 2", TagId = "T1", RuleType = AlarmRuleType.HighHigh,
            Threshold = 95, Deadband = 2
        });
        await using var service = CreateService(options, cache, new ManualTimeProvider());

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["subscribe:T1", "seed:T1"], cache.Operations);
        Assert.Equal(1, cache.ActiveSubscriptions);
        Assert.Equal(AlarmLifecycleState.ActiveUnacknowledged, service.Snapshot.Alarms.Single(a => a.AlarmId == "HIGH").State);

        cache.Publish(new TagValue("T1", 0d, TagQuality.Disconnected, DateTimeOffset.UtcNow, 2));
        var alarm = service.Snapshot.Alarms.Single(a => a.AlarmId == "HIGH");
        Assert.Equal(AlarmLifecycleState.ActiveUnacknowledged, alarm.State);
        Assert.False(alarm.IsEvaluationAvailable);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, cache.ActiveSubscriptions);
    }

    [Fact]
    public async Task PersistenceMarkerFailureFailsClosedBeforeAnyTagCacheSubscription()
    {
        var options = CreateOptions(TimeSpan.Zero);
        options.Alarms.PersistenceEnabled = true;
        var cache = new TrackingTagCache { Seed = Good(90, 1) };
        var store = new RecordingAlarmStore { FailBeginSession = true };
        await using var service = new AlarmRuntimeService(
            options, cache, store, NullLogger<AlarmRuntimeService>.Instance, new ManualTimeProvider());

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(AlarmRuntimeState.Faulted, service.Snapshot.State);
        Assert.Equal("ALARM_RECOVERY_MARKER_FAILED", service.Snapshot.LastErrorCode);
        Assert.Equal(0, cache.ActiveSubscriptions);
        Assert.Empty(service.Snapshot.Alarms);
        Assert.Equal(1, store.BeginSessionAttempts);
    }

    [Fact]
    public async Task AcknowledgeAllSnapshotsEligibleInstancesAndUsesPerInstancePath()
    {
        var cache = new TrackingTagCache();
        var options = CreateOptions(TimeSpan.Zero);
        options.Tags.Add(new TagDefinition { Id = "T2", Name = "T2", DeviceId = "SIM", Address = "T2", DataType = TagDataType.Boolean });
        options.Alarms.Definitions.Add(new AlarmDefinition
        {
            Id = "DIGITAL", Name = "Digital", TagId = "T2", RuleType = AlarmRuleType.DigitalEquals,
            DigitalExpectedValue = true, AcknowledgementRequired = true
        });
        await using var service = CreateService(options, cache, new ManualTimeProvider());
        await service.StartAsync(CancellationToken.None);
        cache.Publish(Good(90, 1));
        cache.Publish(new TagValue("T2", true, TagQuality.Good, DateTimeOffset.UtcNow, 1));

        var results = service.AcknowledgeAll("operator");

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal(AlarmAcknowledgementStatus.Acknowledged, result.Status));
        Assert.All(service.Snapshot.Alarms, alarm => Assert.Equal(AlarmLifecycleState.ActiveAcknowledged, alarm.State));
    }

    [Fact]
    public async Task GapFreePersistenceDrainsAndCommitsTrustedCheckpoint()
    {
        var options = CreateOptions(TimeSpan.Zero);
        options.Alarms.PersistenceEnabled = true;
        var cache = new TrackingTagCache();
        var store = new RecordingAlarmStore();
        await using var service = new AlarmRuntimeService(options, cache, store, NullLogger<AlarmRuntimeService>.Instance, new ManualTimeProvider());
        await service.StartAsync(CancellationToken.None);

        cache.Publish(Good(90, 1));
        await store.Persisted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(store.PersistedBatches);
        Assert.NotNull(store.TrustedCheckpoint);
        Assert.True(store.TrustedCheckpoint!.RecoveryTrusted);
        Assert.Equal(1, store.TrustedCheckpoint.ContinuitySequence);
    }

    [Fact]
    public async Task PersistenceWriteFailureDegradesAndPreventsTrustedCheckpoint()
    {
        var options = CreateOptions(TimeSpan.Zero);
        options.Alarms.PersistenceEnabled = true;
        var cache = new TrackingTagCache();
        var store = new RecordingAlarmStore { FailPersist = true };
        await using var service = new AlarmRuntimeService(options, cache, store, NullLogger<AlarmRuntimeService>.Instance, new ManualTimeProvider());
        var degraded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = service.Subscribe(snapshot =>
        {
            if (snapshot.State == AlarmRuntimeState.Degraded) degraded.TrySetResult(true);
        });
        await service.StartAsync(CancellationToken.None);

        cache.Publish(Good(90, 1));
        await degraded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, service.Snapshot.PersistenceWriteFailures);
        Assert.Null(store.TrustedCheckpoint);
    }

    [Theory]
    [InlineData(true, AlarmLifecycleState.ActiveAcknowledged)]
    [InlineData(false, AlarmLifecycleState.Normal)]
    public async Task RecoveryInjectsOnlyTrustedCompatibleCheckpoint(bool trusted, AlarmLifecycleState expected)
    {
        var options = CreateOptions(TimeSpan.Zero);
        options.Alarms.PersistenceEnabled = true;
        var definition = options.Alarms.Definitions.Single();
        var instance = new AlarmInstanceRecord(
            definition.Id, Guid.NewGuid(), AlarmLifecycleState.ActiveAcknowledged, definition.Severity,
            AlarmDefinitionFingerprint.Create(definition), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "operator", 5, DateTimeOffset.UtcNow, TagQuality.Good);
        var store = new RecordingAlarmStore { Recovery = new AlarmRecoveryResult(trusted, 5, [instance]) };
        await using var service = new AlarmRuntimeService(options, new TrackingTagCache(), store, NullLogger<AlarmRuntimeService>.Instance, new ManualTimeProvider());

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(expected, service.Snapshot.Alarms.Single().State);
        Assert.Equal(trusted, service.Snapshot.RecoveryTrusted);
    }

    [Fact]
    public async Task SnapshotSubscriberExceptionIsIsolatedFromTagCacheCallback()
    {
        var cache = new TrackingTagCache();
        await using var service = CreateService(cache, delay: TimeSpan.Zero);
        using var failing = service.Subscribe(_ => throw new InvalidOperationException("subscriber"));
        var delivered = 0;
        using var healthy = service.Subscribe(_ => delivered++);
        await service.StartAsync(CancellationToken.None);

        var exception = Record.Exception(() => cache.Publish(Good(90, 1)));

        Assert.Null(exception);
        Assert.True(delivered > 0);
        Assert.True(service.Snapshot.SubscriberExceptions > 0);
    }

    private static AlarmRuntimeService CreateService(
        TrackingTagCache cache,
        ManualTimeProvider? clock = null,
        TimeSpan? delay = null) =>
        CreateService(CreateOptions(delay ?? TimeSpan.Zero), cache, clock ?? new ManualTimeProvider());

    private static AlarmRuntimeService CreateService(
        RuntimeOptions options,
        TrackingTagCache cache,
        ManualTimeProvider clock) =>
        new(options, cache, null, NullLogger<AlarmRuntimeService>.Instance, clock);

    private static RuntimeOptions CreateOptions(TimeSpan delay)
    {
        return new RuntimeOptions
        {
            Tags =
            [
                new TagDefinition { Id = "T1", Name = "T1", DeviceId = "SIM", Address = "T1", DataType = TagDataType.Double }
            ],
            Alarms = new AlarmOptions
            {
                Enabled = true,
                PersistenceEnabled = false,
                Definitions =
                [
                    new AlarmDefinition
                    {
                        Id = "HIGH", Name = "High", TagId = "T1", RuleType = AlarmRuleType.High,
                        Severity = AlarmSeverity.High, Threshold = 80, Deadband = 5,
                        ActivationDelay = delay, AcknowledgementRequired = true
                    }
                ]
            }
        };
    }

    private static TagValue Good(double value, long sequence) =>
        new("T1", value, TagQuality.Good, DateTimeOffset.UtcNow, sequence);

    private sealed class TrackingTagCache : ITagCache
    {
        private readonly Dictionary<string, List<Action<TagValue>>> _callbacks = new(StringComparer.OrdinalIgnoreCase);
        public TagValue? Seed { get; init; }
        public List<string> Operations { get; } = [];
        public int ActiveSubscriptions => _callbacks.Values.Sum(callbacks => callbacks.Count);

        public bool TryGet(string tagId, out TagValue? value)
        {
            Operations.Add($"seed:{tagId}");
            value = Seed?.TagId.Equals(tagId, StringComparison.OrdinalIgnoreCase) == true ? Seed : null;
            return value is not null;
        }

        public IDisposable Subscribe(string tagId, Action<TagValue> callback)
        {
            Operations.Add($"subscribe:{tagId}");
            if (!_callbacks.TryGetValue(tagId, out var callbacks)) _callbacks[tagId] = callbacks = [];
            callbacks.Add(callback);
            return new CallbackDisposable(() => callbacks.Remove(callback));
        }

        public void Publish(TagValue value)
        {
            if (_callbacks.TryGetValue(value.TagId, out var callbacks))
                foreach (var callback in callbacks.ToArray()) callback(value);
        }
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose(); }
    }

    private sealed class RecordingAlarmStore : IAlarmEventStore
    {
        public bool FailBeginSession { get; init; }
        public bool FailPersist { get; init; }
        public int BeginSessionAttempts { get; private set; }
        public AlarmRecoveryResult Recovery { get; init; } = new(false, 0, []);
        public List<AlarmPersistenceBatch> PersistedBatches { get; } = [];
        public AlarmStoreCheckpoint? TrustedCheckpoint { get; private set; }
        public TaskCompletionSource<bool> Persisted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AlarmRecoveryResult> LoadRecoveryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Recovery);
        public Task BeginUntrustedSessionAsync(AlarmStoreSessionRequest request, CancellationToken cancellationToken)
        {
            BeginSessionAttempts++;
            return FailBeginSession ? Task.FromException(new IOException("marker failed")) : Task.CompletedTask;
        }
        public Task PersistBatchAsync(AlarmPersistenceBatch batch, CancellationToken cancellationToken)
        {
            PersistedBatches.Add(batch);
            Persisted.TrySetResult(true);
            return FailPersist ? Task.FromException(new IOException("write failed")) : Task.CompletedTask;
        }
        public Task CommitTrustedCheckpointAsync(AlarmStoreCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            TrustedCheckpoint = checkpoint;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmEventQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AlarmEvent>>([]);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => _utc;
        public void AdvanceMonotonic(TimeSpan amount) => Interlocked.Add(ref _timestamp, amount.Ticks);
        public void JumpUtc(TimeSpan amount) => _utc += amount;
    }
}
