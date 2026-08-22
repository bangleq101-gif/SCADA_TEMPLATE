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
        TestHistoryStore store) =>
        new(options, cache, store, NullLogger<HistorianRuntimeService>.Instance, TimeProvider.System);

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

    private static TagDefinition CreateHistoryTag() => new()
    {
        Id = "T1",
        Name = "Temperature",
        DeviceId = "SIM01",
        Address = "A1",
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

    private sealed class TestHistoryStore(bool blockInitialization = false, int failFirstBatchAttempts = 0) : IHistoryStore
    {
        private readonly bool _blockInitialization = blockInitialization;
        private int _failFirstBatchAttempts = failFirstBatchAttempts;
        private readonly TaskCompletionSource<bool> _releaseInitialization =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _sync = new();

        public TaskCompletionSource<bool> Initialized { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<HistorySample> WrittenSamples { get; } = [];

        public int InitializeCount { get; private set; }

        public HistoryStorePreflightResult Preflight() =>
            new(HistoryStorePreflightStatus.Ready);

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            InitializeCount++;
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
}
