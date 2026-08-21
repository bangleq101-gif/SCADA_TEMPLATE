using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;
using Scada.Runtime.Drivers;
using Scada.Runtime.Engine;
using Scada.Runtime.Polling;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class PollingRuntimeServiceTests
{
    [Fact]
    public async Task StopAsyncDisconnectsSuccessfullyConnectedDevices()
    {
        var driver = new TrackingDriver();
        var device = new DeviceDefinition
        {
            Id = "PLC-1",
            Name = "Test PLC",
            DriverType = "Test"
        };
        var options = CreateOptions(device);
        options.Tags =
        [
            new TagDefinition
            {
                Id = "T1",
                DeviceId = device.Id,
                Address = "DB1.0",
                DataType = TagDataType.Double
            }
        ];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await WaitUntilAsync(() => driver.ConnectCount > 0);

        await runtime.Service.StopAsync(CancellationToken.None);

        Assert.Equal(1, driver.ConnectCount);
        Assert.Equal(1, driver.DisconnectCount);
        Assert.False(driver.LastDisconnectToken.IsCancellationRequested);
    }

    private static RuntimeOptions CreateOptions(params DeviceDefinition[] devices) => new()
    {
        Polling = new PollingOptions
        {
            ConnectTimeoutMilliseconds = 250,
            ReadTimeoutMilliseconds = 250,
            DisconnectTimeoutMilliseconds = 250,
            InitialReconnectDelayMilliseconds = 10,
            MaxReconnectDelayMilliseconds = 20,
            ShutdownTimeoutMilliseconds = 500
        },
        ScanGroups = [new ScanGroupDefinition { Name = "Normal", IntervalMilliseconds = 10 }],
        Devices = [.. devices]
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class TrackingDriver : IPlcDriver
    {
        public string DriverType => "Test";
        public int ConnectCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public CancellationToken LastDisconnectToken { get; private set; }

        public Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DriverReadResult>>(Array.Empty<DriverReadResult>());
        }

        public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectCount++;
            LastDisconnectToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}

internal sealed class TestRuntime : IAsyncDisposable
{
    private TestRuntime(PollingRuntimeService service, TagCache cache)
    {
        Service = service;
        Cache = cache;
    }

    public PollingRuntimeService Service { get; }
    public TagCache Cache { get; }

    public static async Task<TestRuntime> StartAsync(RuntimeOptions options, IPlcDriver driver)
    {
        return await StartAsync(
            options,
            new DriverResolver([DriverRegistration.Shared(driver.DriverType, driver)]));
    }

    public static async Task<TestRuntime> StartAsync(RuntimeOptions options, IPlcDriverResolver resolver)
    {
        var cache = new TagCache();
        var tagEngine = new TagEngine(cache);
        var manager = new DeviceManager(
            options,
            resolver,
            tagEngine,
            NullLogger<DeviceManager>.Instance,
            NullLogger<DevicePollingWorker>.Instance,
            TimeProvider.System);
        var service = new PollingRuntimeService(manager);
        await service.StartAsync(CancellationToken.None);
        return new TestRuntime(service, cache);
    }

    public ValueTask DisposeAsync() => new(Service.StopAsync(CancellationToken.None));
}
