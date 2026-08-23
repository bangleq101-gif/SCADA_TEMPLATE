using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Mqtt;
using Scada.Core.Tags;
using Scada.Runtime.Mqtt;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class MqttRuntimeServiceTests
{
    [Fact]
    public async Task PeriodicProfilePublishesWithoutSecondCallback()
    {
        var (service, cache, transport) = Create(maximumInterval: 20);
        cache.Upsert(new TagUpdate("T", 1d, TagQuality.Good, DateTimeOffset.UtcNow));
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => transport.Published >= 2);
        await service.StopAsync(CancellationToken.None);
        Assert.True(transport.Published >= 2);
    }

    [Fact]
    public async Task ConcurrentUpdatesDoNotThrowAndShutdownIsBounded()
    {
        var (service, cache, transport) = Create(maximumInterval: 0);
        await service.StartAsync(CancellationToken.None);
        await Task.WhenAll(Enumerable.Range(0, 200).Select(index => Task.Run(() => cache.Upsert(new TagUpdate("T", index, TagQuality.Good, DateTimeOffset.UtcNow)))));
        await WaitUntilAsync(() => transport.Published >= 1);
        await service.StopAsync(CancellationToken.None);
        Assert.True(transport.Disconnects >= 1);
    }

    [Fact]
    public async Task ReconnectForcesCurrentTagCacheSnapshot()
    {
        var (service, cache, transport) = Create(maximumInterval: 0, failPublishes: 1);
        cache.Upsert(new TagUpdate("T", 5d, TagQuality.Good, DateTimeOffset.UtcNow));
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => transport.Connects >= 2 && transport.Published >= 1);
        await service.StopAsync(CancellationToken.None);
        Assert.True(transport.Connects >= 2);
    }

    [Fact]
    public async Task RepeatedPublishFailuresUseBoundedExponentialBackoff()
    {
        var timeProvider = new ImmediateDelayTimeProvider();
        var (service, cache, transport) = Create(maximumInterval: 0, failPublishes: int.MaxValue, initialReconnectDelayMilliseconds: 15, maxReconnectDelayMilliseconds: 40, timeProvider: timeProvider);
        cache.Upsert(new TagUpdate("T", 5d, TagQuality.Good, DateTimeOffset.UtcNow));
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => transport.PublishFailures >= 4 && timeProvider.RequestedDelays.Count >= 4);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal([15d, 30d, 40d, 40d], timeProvider.RequestedDelays.Take(4).Select(delay => delay.TotalMilliseconds));
    }

    [Fact]
    public async Task PublishAcknowledgementDoesNotRemoveNewerPendingSequence()
    {
        var (service, cache, transport) = Create(maximumInterval: 0);
        transport.BlockNextPublish();
        cache.Upsert(new TagUpdate("T", 1d, TagQuality.Good, DateTimeOffset.UtcNow));
        await service.StartAsync(CancellationToken.None);
        await transport.WaitForPublishAsync();
        cache.Upsert(new TagUpdate("T", 2d, TagQuality.Good, DateTimeOffset.UtcNow));
        transport.ReleasePublish();
        await WaitUntilAsync(() => transport.SuccessfulPayloads.Count >= 2);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2d, ReadValue(transport.SuccessfulPayloads.Last()));
    }

    [Fact]
    public async Task SerializationRejectionDoesNotRemoveNewerPendingSequence()
    {
        var timeProvider = new BlockingTimeProvider();
        var (service, cache, transport) = Create(maximumInterval: 0, timeProvider: timeProvider);
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => transport.Connects >= 1);
        timeProvider.BlockNextUtcRead();
        cache.Upsert(new TagUpdate("T", double.NaN, TagQuality.Good, DateTimeOffset.UtcNow));
        await timeProvider.WaitForBlockAsync();
        cache.Upsert(new TagUpdate("T", 3d, TagQuality.Good, DateTimeOffset.UtcNow));
        timeProvider.ReleaseUtcRead();
        await WaitUntilAsync(() => transport.SuccessfulPayloads.Count >= 1);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(3d, ReadValue(transport.SuccessfulPayloads.Single()));
    }

    [Fact]
    public async Task IdleCoordinatorUsesNoPerTagTimersOrWaiters()
    {
        var (service, _, transport) = Create(maximumInterval: 0);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(350);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, transport.Connects);
        Assert.Empty(transport.SuccessfulPayloads);
    }

    private static (MqttRuntimeService Service, TagCache Cache, FakeTransport Transport) Create(int maximumInterval, int failPublishes = 0, int initialReconnectDelayMilliseconds = 10, int maxReconnectDelayMilliseconds = 20, TimeProvider? timeProvider = null)
    {
        var options = new RuntimeOptions { Mqtt = { Enabled = true, ReconnectInitialDelayMilliseconds = initialReconnectDelayMilliseconds, ReconnectMaxDelayMilliseconds = maxReconnectDelayMilliseconds, ConnectionTimeoutMilliseconds = 100, PublishTimeoutMilliseconds = 100, ShutdownTimeoutMilliseconds = 100 } };
        options.Devices.Add(new DeviceDefinition { Id = "D", DriverType = "Simulator" });
        options.Tags.Add(new TagDefinition { Id = "T", Name = "T", DeviceId = "D", Address = "T", MqttPublishEnabled = true });
        options.Mqtt.Profiles[0].MinimumIntervalMilliseconds = 0;
        options.Mqtt.Profiles[0].MaximumIntervalMilliseconds = maximumInterval;
        var cache = new TagCache(); var transport = new FakeTransport(failPublishes);
        return (new MqttRuntimeService(options, cache, transport, NullLogger<MqttRuntimeService>.Instance, timeProvider ?? TimeProvider.System), cache, transport);
    }
    private static async Task WaitUntilAsync(Func<bool> condition) { for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }
    private static double ReadValue(MqttPublishRequest request)
    {
        using var document = JsonDocument.Parse(request.Payload);
        return document.RootElement.GetProperty("value").GetDouble();
    }

    private sealed class FakeTransport : IMqttTransport
    {
        private int _remainingFailedPublishes;
        private readonly TaskCompletionSource _publishEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _publishRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blockNextPublish;
        public FakeTransport(int failPublishes) => _remainingFailedPublishes = failPublishes;
        public bool IsConnected { get; private set; }
        public int Published; public int Disconnects; public int Connects;
        public ConcurrentQueue<MqttPublishRequest> SuccessfulPayloads { get; } = [];
        public int PublishFailures;
        public Task<MqttConnectionResult> ConnectAsync(MqttConnectRequest request, CancellationToken cancellationToken) { IsConnected = true; Interlocked.Increment(ref Connects); return Task.FromResult(new MqttConnectionResult(true)); }
        public async Task PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _blockNextPublish, 0) == 1) { _publishEntered.TrySetResult(); await _publishRelease.Task.WaitAsync(cancellationToken); }
            if (Interlocked.Decrement(ref _remainingFailedPublishes) >= 0) { Interlocked.Increment(ref PublishFailures); throw new InvalidOperationException("planned publish failure"); }
            SuccessfulPayloads.Enqueue(request); Interlocked.Increment(ref Published);
        }
        public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; Interlocked.Increment(ref Disconnects); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void BlockNextPublish() => Interlocked.Exchange(ref _blockNextPublish, 1);
        public Task WaitForPublishAsync() => _publishEntered.Task;
        public void ReleasePublish() => _publishRelease.TrySetResult();
    }

    private sealed class BlockingTimeProvider : TimeProvider
    {
        private int _blockNextUtcRead;
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Exchange(ref _blockNextUtcRead, 0) == 1) { _entered.Set(); _release.Wait(); }
            return DateTimeOffset.UtcNow;
        }
        public void BlockNextUtcRead() => Interlocked.Exchange(ref _blockNextUtcRead, 1);
        public Task WaitForBlockAsync() => Task.Run(() => _entered.Wait(TimeSpan.FromSeconds(1)) ? Task.CompletedTask : throw new TimeoutException("MQTT payload serialization did not reach the controlled clock."));
        public void ReleaseUtcRead() => _release.Set();
    }

    private sealed class ImmediateDelayTimeProvider : TimeProvider
    {
        public ConcurrentQueue<TimeSpan> RequestedDelays { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            RequestedDelays.Enqueue(dueTime);
            return ImmediateTimer.CreateAndFire(callback, state);
        }

        private sealed class ImmediateTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref _disposed) == 0;
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            public void Fire() { if (Volatile.Read(ref _disposed) == 0) callback(state); }
            public static ImmediateTimer CreateAndFire(TimerCallback callback, object? state)
            {
                var timer = new ImmediateTimer(callback, state);
                ThreadPool.QueueUserWorkItem(_ => timer.Fire());
                return timer;
            }
        }
    }
}
