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

    private static (MqttRuntimeService Service, TagCache Cache, FakeTransport Transport) Create(int maximumInterval)
    {
        var options = new RuntimeOptions { Mqtt = { Enabled = true, ReconnectInitialDelayMilliseconds = 10, ReconnectMaxDelayMilliseconds = 20, ConnectionTimeoutMilliseconds = 100, PublishTimeoutMilliseconds = 100, ShutdownTimeoutMilliseconds = 100 } };
        options.Devices.Add(new DeviceDefinition { Id = "D", DriverType = "Simulator" });
        options.Tags.Add(new TagDefinition { Id = "T", Name = "T", DeviceId = "D", Address = "T", MqttPublishEnabled = true });
        options.Mqtt.Profiles[0].MaximumIntervalMilliseconds = maximumInterval;
        var cache = new TagCache(); var transport = new FakeTransport();
        return (new MqttRuntimeService(options, cache, transport, NullLogger<MqttRuntimeService>.Instance, TimeProvider.System), cache, transport);
    }
    private static async Task WaitUntilAsync(Func<bool> condition) { for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }
    private sealed class FakeTransport : IMqttTransport
    {
        public bool IsConnected { get; private set; }
        public int Published; public int Disconnects;
        public Task<MqttConnectionResult> ConnectAsync(MqttConnectRequest request, CancellationToken cancellationToken) { IsConnected = true; return Task.FromResult(new MqttConnectionResult(true)); }
        public Task PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken) { Interlocked.Increment(ref Published); return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; Interlocked.Increment(ref Disconnects); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
