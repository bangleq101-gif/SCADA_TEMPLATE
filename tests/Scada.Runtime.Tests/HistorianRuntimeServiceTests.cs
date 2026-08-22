using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Configuration;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Runtime.Historian;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class HistorianRuntimeServiceTests
{
    [Fact]
    public async Task SubscribeBeforeSeedDoesNotDuplicateTheSameSequence()
    {
        var tag = CreateHistoryTag();
        var value = new TagValue(tag.Id, 12.5d, TagQuality.Good, DateTimeOffset.UtcNow, 42);
        var cache = new TestHistorianTagCache(value);
        var store = new TestHistoryStore();
        await using var service = CreateService(CreateOptions(tag), cache, store);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => store.WrittenSamples.Count == 1);
        await service.StopAsync(CancellationToken.None);

        Assert.Single(store.WrittenSamples);
        Assert.Equal(42, store.WrittenSamples[0].TagSequence);
    }

    [Fact]
    public async Task StopCompletesQueueAndDrainsAcceptedSampleBeforeCancellingWriter()
    {
        var tag = CreateHistoryTag();
        var cache = new TestHistorianTagCache();
        var store = new TestHistoryStore();
        var options = CreateOptions(tag);
        options.Historian.BatchSize = 10;
        options.Historian.FlushIntervalMilliseconds = 5_000;
        await using var service = CreateService(options, cache, store);

        await service.StartAsync(CancellationToken.None);
        await store.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cache.Publish(new TagValue(tag.Id, 5d, TagQuality.Good, DateTimeOffset.UtcNow, 1));

        await service.StopAsync(CancellationToken.None);

        Assert.Single(store.WrittenSamples);
        Assert.Equal(1, service.Snapshot.WrittenSamples);
        Assert.Equal(0, service.Snapshot.AbandonedSamples);
    }

    [Fact]
    public async Task DisabledHistorianDoesNotInitializeStoreOrSubscribe()
    {
        var tag = CreateHistoryTag();
        var cache = new TestHistorianTagCache();
        var store = new TestHistoryStore();
        var options = CreateOptions(tag);
        options.Historian.Enabled = false;
        await using var service = CreateService(options, cache, store);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(HistorianRuntimeState.Disabled, service.Snapshot.State);
        Assert.Equal(0, store.InitializeCount);
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public async Task FullQueueDropsValidSampleWithoutBlockingCallback()
    {
        var tag = CreateHistoryTag();
        var cache = new TestHistorianTagCache();
        var store = new TestHistoryStore(blockInitialization: true);
        var options = CreateOptions(tag);
        options.Historian.QueueCapacity = 1;
        options.Historian.BatchSize = 10;
        options.Historian.Profiles.Single(profile => profile.Name == "Analog").Deadband = 0;
        options.Historian.Profiles.Single(profile => profile.Name == "Analog").MinimumIntervalMilliseconds = 0;
        await using var service = CreateService(options, cache, store);

        await service.StartAsync(CancellationToken.None);
        cache.Publish(new TagValue(tag.Id, 1d, TagQuality.Good, DateTimeOffset.UtcNow, 1));
        cache.Publish(new TagValue(tag.Id, 2d, TagQuality.Good, DateTimeOffset.UtcNow, 2));

        Assert.Equal(1, service.Snapshot.DroppedSamples);
        store.ReleaseInitialization();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DroppedAcceptedSampleKeepsItsPeriodicSchedule()
    {
        var tag = CreateHistoryTag();
        var cache = new TestHistorianTagCache();
        var store = new TestHistoryStore(blockInitialization: true);
        var options = CreateOptions(tag);
        options.Historian.QueueCapacity = 1;
        options.Historian.BatchSize = 1;
        options.Historian.FlushIntervalMilliseconds = 10;
        var profile = options.Historian.Profiles.Single(profile => profile.Name == "Analog");
        profile.Deadband = 0;
        profile.MinimumIntervalMilliseconds = 0;
        profile.MaximumIntervalMilliseconds = 50;
        await using var service = CreateService(options, cache, store);

        await service.StartAsync(CancellationToken.None);
        cache.Publish(new TagValue(tag.Id, 1d, TagQuality.Good, DateTimeOffset.UtcNow, 1));
        cache.Publish(new TagValue(tag.Id, 2d, TagQuality.Good, DateTimeOffset.UtcNow, 2));

        Assert.Equal(1, service.Snapshot.DroppedSamples);
        store.ReleaseInitialization();
        await WaitUntilAsync(() => store.WrittenSamples.Any(sample => sample.TagSequence == 2));
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(store.WrittenSamples, sample => sample.TagSequence == 2);
    }

    [Fact]
    public async Task DifferentTagCallbacksDoNotShareOneGlobalEvaluationLock()
    {
        var clock = new BlockingFrequencyTimeProvider();
        var tagA = CreateHistoryTag("T1");
        var tagB = CreateHistoryTag("T2");
        var options = CreateOptions(tagA);
        options.Tags.Add(tagB);
        var cache = new TestHistorianTagCache();
        var store = new TestHistoryStore(blockInitialization: true);
        await using var service = CreateService(options, cache, store, clock);

        await service.StartAsync(CancellationToken.None);
        clock.BlockNextFrequency();
        var firstTask = Task.Run(() => cache.Publish(
            new TagValue(tagA.Id, 1d, TagQuality.Good, clock.GetUtcNow(), 1)));
        clock.WaitUntilBlocked();

        Task? secondTask = null;
        try
        {
            secondTask = Task.Run(() => cache.Publish(
                new TagValue(tagB.Id, 1d, TagQuality.Good, clock.GetUtcNow(), 1)));
            var completed = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(secondTask, completed);
            await secondTask;
        }
        finally
        {
            clock.ReleaseFrequency();
            await firstTask;
            if (secondTask is not null)
            {
                await secondTask;
            }

            store.ReleaseInitialization();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SlowPreflightIsBoundedAndBackgroundInitializationCanStart()
    {
        var tag = CreateHistoryTag();
        var cache = new TestHistorianTagCache();
        var store = new BlockingPreflightHistoryStore();
        await using var service = CreateService(CreateOptions(tag), cache, store);

        var startTask = service.StartAsync(CancellationToken.None);
        await store.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await startTask.WaitAsync(TimeSpan.FromSeconds(2));
        await store.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, store.InitializeCount);
        store.ReleasePreflight();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InitializationRetryBackoffStopsWhenServiceStops()
    {
        var tag = CreateHistoryTag();
        var cache = new TestHistorianTagCache();
        var store = new TestHistoryStore(failInitializationAttempts: int.MaxValue);
        var options = CreateOptions(tag);
        options.Historian.ShutdownDrainTimeoutMilliseconds = 100;
        await using var service = CreateService(options, cache, store);

        await service.StartAsync(CancellationToken.None);
        await store.InitializationAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(store.InitializeCount >= 1);
    }

    [Fact]
    public async Task ExhaustedRecoverableBatchIsAbandonedAndLaterBatchContinues()
    {
        var tag = CreateHistoryTag();
        var cache = new TestHistorianTagCache();
        var store = new TestHistoryStore(failFirstBatchAttempts: 4);
        var options = CreateOptions(tag);
        options.Historian.Profiles.Single(profile => profile.Name == "Analog").Deadband = 0;
        options.Historian.Profiles.Single(profile => profile.Name == "Analog").MinimumIntervalMilliseconds = 0;
        await using var service = CreateService(options, cache, store);

        await service.StartAsync(CancellationToken.None);
        await store.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cache.Publish(new TagValue(tag.Id, 1d, TagQuality.Good, DateTimeOffset.UtcNow, 1));
        cache.Publish(new TagValue(tag.Id, 2d, TagQuality.Good, DateTimeOffset.UtcNow, 2));

        await WaitUntilAsync(() => store.WrittenSamples.Any(sample => sample.TagSequence == 2));
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(store.WrittenSamples, sample => sample.TagSequence == 2);
        Assert.Equal(1, service.Snapshot.AbandonedSamples);
        Assert.Equal(4, service.Snapshot.WriteFailures);
    }

    private static HistorianRuntimeService CreateService(
        RuntimeOptions options,
        TestHistorianTagCache cache,
        IHistoryStore store,
        TimeProvider? timeProvider = null) =>
        new(options, cache, store, NullLogger<HistorianRuntimeService>.Instance, timeProvider ?? TimeProvider.System);

    private static RuntimeOptions CreateOptions(TagDefinition tag) => new()
    {
        RuntimeId = "Runtime01",
        Historian = new HistorianOptions
        {
            Enabled = true,
            QueueCapacity = 16,
            BatchSize = 1,
            FlushIntervalMilliseconds = 25,
            ShutdownDrainTimeoutMilliseconds = 2_000
        },
        Tags = [tag]
    };

    private static TagDefinition CreateHistoryTag(string id = "T1") => new()
    {
        Id = id,
        Name = id,
        DeviceId = "SIM01",
        Address = id,
        DataType = TagDataType.Double,
        HistoryEnabled = true,
        HistoryProfile = "Analog"
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class TestHistorianTagCache(TagValue? seed = null) : ITagCache
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, TagValue> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string TagId, Action<TagValue> Callback, bool Disposed)> _subscriptions = [];

        public TestHistorianTagCache() : this(null)
        {
        }

        public int ActiveSubscriptionCount
        {
            get
            {
                lock (_sync)
                {
                    return _subscriptions.Count(subscription => !subscription.Disposed);
                }
            }
        }

        public bool TryGet(string tagId, out TagValue? value)
        {
            lock (_sync)
            {
                return _values.TryGetValue(tagId, out value);
            }
        }

        public IDisposable Subscribe(string tagId, Action<TagValue> callback)
        {
            var entry = new SubscriptionEntry(tagId, callback, false);
            lock (_sync)
            {
                _subscriptions.Add((entry.TagId, entry.Callback, entry.Disposed));
                if (seed is not null)
                {
                    _values[seed.TagId] = seed;
                }
            }

            // Deliberately publish before Subscribe returns to exercise the
            // subscribe-before-seed race contract.
            if (seed is not null)
            {
                callback(seed);
            }

            return new DelegateSubscription(() => Dispose(entry));
        }

        public void Publish(TagValue value)
        {
            Action<TagValue>[] callbacks;
            lock (_sync)
            {
                _values[value.TagId] = value;
                callbacks = _subscriptions
                    .Where(subscription => !subscription.Disposed &&
                        string.Equals(subscription.TagId, value.TagId, StringComparison.OrdinalIgnoreCase))
                    .Select(subscription => subscription.Callback)
                    .ToArray();
            }

            foreach (var callback in callbacks)
            {
                callback(value);
            }
        }

        private void Dispose(SubscriptionEntry entry)
        {
            lock (_sync)
            {
                for (var index = 0; index < _subscriptions.Count; index++)
                {
                    var current = _subscriptions[index];
                    if (ReferenceEquals(current.Callback, entry.Callback))
                    {
                        _subscriptions[index] = (current.TagId, current.Callback, true);
                    }
                }
            }
        }

        private sealed record SubscriptionEntry(string TagId, Action<TagValue> Callback, bool Disposed);

        private sealed class DelegateSubscription(Action dispose) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    dispose();
                }
            }
        }
    }

    private sealed class TestHistoryStore(
        bool blockInitialization = false,
        int failFirstBatchAttempts = 0,
        int failInitializationAttempts = 0) : IHistoryStore
    {
        private readonly bool _blockInitialization = blockInitialization;
        private int _failFirstBatchAttempts = failFirstBatchAttempts;
        private int _failInitializationAttempts = failInitializationAttempts;
        private readonly TaskCompletionSource<bool> _releaseInitialization =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _sync = new();

        public TaskCompletionSource<bool> Initialized { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> InitializationAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<HistorySample> WrittenSamples { get; } = [];

        public int InitializeCount { get; private set; }

        public Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready));

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            InitializeCount++;
            InitializationAttempted.TrySetResult(true);
            if (_failInitializationAttempts > 0)
            {
                _failInitializationAttempts--;
                throw new IOException("transient initialization failure");
            }

            if (_blockInitialization)
            {
                await _releaseInitialization.Task.WaitAsync(cancellationToken);
            }

            Initialized.TrySetResult(true);
        }

        public Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_failFirstBatchAttempts > 0 && samples.Any(sample => sample.TagSequence == 1))
            {
                _failFirstBatchAttempts--;
                throw new IOException("transient test failure");
            }

            lock (_sync)
            {
                WrittenSamples.AddRange(samples);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistorySample>>([]);

        public void ReleaseInitialization() => _releaseInitialization.TrySetResult(true);
    }

    private sealed class BlockingPreflightHistoryStore : IHistoryStore
    {
        private readonly TaskCompletionSource<HistoryStorePreflightResult> _preflight =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> PreflightStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Initialized { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int InitializeCount { get; private set; }

        public Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken)
        {
            PreflightStarted.TrySetResult(true);
            return _preflight.Task;
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            InitializeCount++;
            Initialized.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistorySample>>([]);

        public void ReleasePreflight() =>
            _preflight.TrySetResult(new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready));
    }

    private sealed class BlockingFrequencyTimeProvider : TimeProvider
    {
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private int _blockNext;

        public override long TimestampFrequency
        {
            get
            {
                if (Interlocked.Exchange(ref _blockNext, 0) == 1)
                {
                    _entered.Set();
                    _release.Wait();
                }

                return TimeSpan.TicksPerSecond;
            }
        }

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

        public override long GetTimestamp() => 0;

        public void BlockNextFrequency() => Volatile.Write(ref _blockNext, 1);

        public void WaitUntilBlocked() => Assert.True(_entered.Wait(TimeSpan.FromSeconds(2)));

        public void ReleaseFrequency() => _release.Set();
    }
}
