using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.History;
using Scada.Core.Mqtt;
using Scada.Core.Tags;
using Scada.Runtime.Alarms;
using Scada.Runtime.Drivers;
using Scada.Runtime.Engine;
using Scada.Runtime.Health;
using Scada.Runtime.Historian;
using Scada.Runtime.Mqtt;
using Scada.Runtime.Polling;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class RuntimeHealthServiceTests
{
    [Fact]
    public async Task StartAndStopOwnExactlyOneSamplerAndTimer()
    {
        using var fixture = HealthFixture.Create();

        await fixture.Service.StartAsync(CancellationToken.None);
        Assert.Equal(1, fixture.Service.SamplerTaskCount);
        Assert.Equal(1, fixture.Service.TimerCount);

        await fixture.Service.StopAsync(CancellationToken.None);

        Assert.Equal(0, fixture.Service.SamplerTaskCount);
        Assert.Equal(0, fixture.Service.TimerCount);
    }

    [Fact]
    public void OneExplicitSampleProducesOnePublicationAndDoesNotNeedTagCallback()
    {
        using var fixture = HealthFixture.Create();
        var received = 0;
        using var good = fixture.Service.Subscribe(_ => received++);
        using var bad = fixture.Service.Subscribe(_ => throw new InvalidOperationException("subscriber failure"));

        var before = fixture.Service.MaterializationCount;
        fixture.Service.SampleOnceForTests();

        Assert.Equal(before + 1, fixture.Service.MaterializationCount);
        Assert.True(received >= 2); // initial snapshot plus explicit sample
        Assert.Equal(0, fixture.Cache.Snapshot.SubscriptionCount);
    }

    [Fact]
    public void RawTagCacheUpdatesDoNotPublishHealthDirectly()
    {
        using var fixture = HealthFixture.Create();
        var publications = 0;
        using var subscription = fixture.Service.Subscribe(_ => publications++);
        fixture.Cache.Upsert(new TagUpdate("T1", 1d, TagQuality.Good, DateTimeOffset.UtcNow));
        fixture.Cache.Upsert(new TagUpdate("T1", 2d, TagQuality.Good, DateTimeOffset.UtcNow));

        Assert.Equal(1, publications);
        Assert.Equal(0, fixture.Service.MaterializationCount);
    }

    [Fact]
    public void ProcessTelemetryFirstSampleIsUnknownThenUsesDelta()
    {
        var clock = new ManualTimeProvider();
        using var fixture = HealthFixture.Create(clock, new SequenceProcessSource(
            new ProcessTelemetryReading(TimeSpan.FromSeconds(1), 10),
            new ProcessTelemetryReading(TimeSpan.FromSeconds(2), 20)));

        var first = fixture.Service.SampleOnceForTests();
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = fixture.Service.SampleOnceForTests();

        Assert.Null(first.Process.CpuPercent);
        Assert.NotNull(second.Process.CpuPercent);
        Assert.Equal(20, second.Process.WorkingSetBytes);
    }

    [Fact]
    public async Task PeriodicSamplerPublishesOnlyAtBoundedCadence()
    {
        var clock = new ManualTimeProvider();
        using var fixture = HealthFixture.Create(clock);
        var publications = 0;
        using var subscription = fixture.Service.Subscribe(_ => publications++);

        await fixture.Service.StartAsync(CancellationToken.None);
        Assert.Equal(0, fixture.Service.MaterializationCount);

        clock.Advance(TimeSpan.FromMilliseconds(999));
        await DrainAsync();
        Assert.Equal(0, fixture.Service.MaterializationCount);
        Assert.Equal(1, publications);

        fixture.Cache.Upsert(new TagUpdate("T1", 1d, TagQuality.Good, clock.GetUtcNow()));
        await DrainAsync();
        Assert.Equal(0, fixture.Service.MaterializationCount);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await DrainAsync();
        Assert.Equal(1, fixture.Service.MaterializationCount);
        Assert.Equal(2, publications);

        clock.Advance(TimeSpan.FromSeconds(5));
        await DrainAsync();
        Assert.InRange(fixture.Service.MaterializationCount, 2, 6);

        await fixture.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UptimeUsesMonotonicTimeWhenUtcMoves()
    {
        var clock = new ManualTimeProvider();
        using var fixture = HealthFixture.Create(clock);

        await fixture.Service.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(10));
        var beforeUtcJump = fixture.Service.SampleOnceForTests();

        clock.SetUtcNow(beforeUtcJump.CapturedAtUtc.AddDays(-2));
        var afterBackwardJump = fixture.Service.SampleOnceForTests();
        clock.SetUtcNow(beforeUtcJump.CapturedAtUtc.AddDays(4));
        var afterForwardJump = fixture.Service.SampleOnceForTests();

        Assert.Equal(TimeSpan.FromSeconds(10), beforeUtcJump.Uptime);
        Assert.Equal(TimeSpan.FromSeconds(10), afterBackwardJump.Uptime);
        Assert.Equal(TimeSpan.FromSeconds(10), afterForwardJump.Uptime);
    }

    [Fact]
    public async Task LargeConfiguredTopologyUsesOneHealthCoordinatorWithoutTagSubscriptions()
    {
        var clock = new ManualTimeProvider();
        var options = new RuntimeOptions
        {
            Devices = Enumerable.Range(1, 50)
                .Select(index => new DeviceDefinition { Id = $"PLC-{index:00}", Enabled = true })
                .ToList(),
            Tags = Enumerable.Range(1, 10_000)
                .Select(index => new TagDefinition
                {
                    Id = $"T-{index:00000}",
                    Name = $"Tag {index}",
                    DeviceId = $"PLC-{((index - 1) % 50) + 1:00}",
                    Address = $"DB1.DBD{index}"
                })
                .ToList()
        };
        using var fixture = HealthFixture.Create(clock, options: options);

        foreach (var tag in options.Tags)
        {
            fixture.Cache.Upsert(new TagUpdate(tag.Id, 1d, TagQuality.Good, clock.GetUtcNow()));
        }

        await fixture.Service.StartAsync(CancellationToken.None);
        foreach (var tag in options.Tags.Take(100))
        {
            fixture.Cache.Upsert(new TagUpdate(tag.Id, 2d, TagQuality.Good, clock.GetUtcNow()));
        }

        Assert.Equal(0, fixture.Service.MaterializationCount);
        Assert.Equal(0, fixture.Cache.Snapshot.SubscriptionCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        await DrainAsync();
        var snapshot = fixture.Service.Snapshot;

        Assert.Equal(1, fixture.Service.SamplerTaskCount);
        Assert.Equal(1, fixture.Service.TimerCount);
        Assert.Equal(10_000, snapshot.TagCache.ValueCount);
        Assert.Equal(50, snapshot.Plc.EnabledDeviceCount);
        Assert.InRange(snapshot.Devices.Count, 0, 50);
        Assert.Equal(0, fixture.Cache.Snapshot.SubscriptionCount);
        Assert.InRange(fixture.Service.MaterializationCount, 1, 1);

        await fixture.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CancelledShutdownKeepsSamplerOwnershipUntilInFlightSampleCompletes()
    {
        var clock = new ManualTimeProvider();
        var process = new BlockingProcessSource();
        using var fixture = HealthFixture.Create(clock, process);

        await fixture.Service.StartAsync(CancellationToken.None);
        var advance = Task.Run(() => clock.Advance(TimeSpan.FromSeconds(1)));
        await process.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await fixture.Service.StopAsync(cancelled.Token);

        Assert.Equal(1, fixture.Service.SamplerTaskCount);
        Assert.Equal(1, fixture.Service.TimerCount);

        process.Release.Set();
        await advance;
        for (var index = 0; index < 20 && (fixture.Service.SamplerTaskCount != 0 || fixture.Service.TimerCount != 0); index++)
        {
            await Task.Yield();
        }

        Assert.Equal(0, fixture.Service.SamplerTaskCount);
        Assert.Equal(0, fixture.Service.TimerCount);
    }

    private static async Task DrainAsync()
    {
        for (var index = 0; index < 8; index++)
        {
            await Task.Yield();
        }
    }

    private sealed class HealthFixture : IDisposable
    {
        private HealthFixture(
            RuntimeHealthService service,
            TagCache cache,
            HistorianRuntimeService historian,
            MqttRuntimeService mqtt,
            AlarmRuntimeService alarm,
            DeviceManager deviceManager)
        {
            Service = service;
            Cache = cache;
            Historian = historian;
            Mqtt = mqtt;
            Alarm = alarm;
            DeviceManager = deviceManager;
        }

        public RuntimeHealthService Service { get; }
        public TagCache Cache { get; }
        public HistorianRuntimeService Historian { get; }
        public MqttRuntimeService Mqtt { get; }
        public AlarmRuntimeService Alarm { get; }
        public DeviceManager DeviceManager { get; }

        public static HealthFixture Create(
            TimeProvider? clock = null,
            IProcessTelemetrySource? process = null,
            RuntimeOptions? options = null)
        {
            clock ??= TimeProvider.System;
            options ??= new RuntimeOptions
            {
                Tags = [new TagDefinition { Id = "T1", Name = "T1", DeviceId = "D1", Address = "T1" }]
            };
            var cache = new TagCache();
            var deviceManager = new DeviceManager(
                options,
                new DriverResolver([]),
                new TagEngine(cache),
                NullLogger<DeviceManager>.Instance,
                NullLogger<DevicePollingWorker>.Instance,
                clock);
            var historian = new HistorianRuntimeService(
                options,
                cache,
                new NoOpHistoryStore(),
                NullLogger<HistorianRuntimeService>.Instance,
                clock);
            var mqtt = new MqttRuntimeService(
                options,
                cache,
                new NoOpMqttTransport(),
                NullLogger<MqttRuntimeService>.Instance,
                clock);
            var alarm = new AlarmRuntimeService(
                options,
                cache,
                null,
                NullLogger<AlarmRuntimeService>.Instance,
                clock);
            var service = new RuntimeHealthService(
                options,
                deviceManager,
                cache,
                historian,
                mqtt,
                alarm,
                NullLogger<RuntimeHealthService>.Instance,
                clock,
                samplingInterval: TimeSpan.FromSeconds(1),
                processTelemetry: process);
            return new HealthFixture(service, cache, historian, mqtt, alarm, deviceManager);
        }

        public void Dispose()
        {
            Service.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Historian.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Mqtt.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Alarm.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class SequenceProcessSource(params ProcessTelemetryReading[] readings) : IProcessTelemetrySource
    {
        private int _index;
        public ProcessTelemetryReading Read() => readings[Math.Min(_index++, readings.Length - 1)];
    }

    private sealed class BlockingProcessSource : IProcessTelemetrySource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);

        public ProcessTelemetryReading Read()
        {
            Entered.TrySetResult();
            Release.Wait();
            return new ProcessTelemetryReading(TimeSpan.FromSeconds(1), 1_024);
        }
    }

    private sealed class NoOpHistoryStore : IHistoryStore
    {
        public Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready));
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistorySample>>([]);
    }

    private sealed class NoOpMqttTransport : IMqttTransport
    {
        public bool IsConnected => false;
        public Task<MqttConnectionResult> ConnectAsync(MqttConnectRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MqttConnectionResult(false));
        public Task PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
