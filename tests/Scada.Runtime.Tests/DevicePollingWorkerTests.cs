using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;
using Scada.Runtime.Drivers;
using Scada.Runtime.Polling;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class DevicePollingWorkerTests
{
    [Fact]
    public async Task SlowDeviceDoesNotBlockAnotherDevice()
    {
        var deviceAReadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceBRead = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new DelegateDriver(
            async (device, requests, cancellationToken) =>
            {
                if (device.Id == "PLC-A")
                {
                    deviceAReadStarted.TrySetResult(null);
                    await Task.Delay(200, cancellationToken);
                }
                else
                {
                    deviceBRead.TrySetResult(null);
                }

                return GoodResults(requests);
            });
        var options = CreateOptions(
            new DeviceDefinition { Id = "PLC-A", DriverType = "Test" },
            new DeviceDefinition { Id = "PLC-B", DriverType = "Test" });
        options.Tags =
        [
            new() { Id = "A1", DeviceId = "PLC-A", Address = "A1", DataType = TagDataType.Double },
            new() { Id = "B1", DeviceId = "PLC-B", Address = "B1", DataType = TagDataType.Double }
        ];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await deviceAReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await deviceBRead.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(deviceBRead.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task EachScanGroupIsReadAsItsOwnBatch()
    {
        var batches = new ConcurrentBag<IReadOnlyList<DriverReadRequest>>();
        var enoughBatches = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new DelegateDriver(
            (device, requests, _) =>
            {
                batches.Add(requests);
                if (batches.Count >= 2)
                {
                    enoughBatches.TrySetResult(null);
                }

                return Task.FromResult<IReadOnlyList<DriverReadResult>>(GoodResults(requests));
            });
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.ScanGroups =
        [
            new() { Name = "Fast", IntervalMilliseconds = 100 },
            new() { Name = "Normal", IntervalMilliseconds = 100 }
        ];
        options.Tags =
        [
            new() { Id = "FAST-1", DeviceId = "PLC-1", Address = "FAST-1", ScanGroup = "Fast" },
            new() { Id = "FAST-2", DeviceId = "PLC-1", Address = "FAST-2", ScanGroup = "Fast" },
            new() { Id = "NORMAL-1", DeviceId = "PLC-1", Address = "NORMAL-1", ScanGroup = "Normal" }
        ];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await enoughBatches.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Contains(batches, batch => batch.Count == 2 && batch.All(request => request.Address.StartsWith("FAST")));
        Assert.Contains(batches, batch => batch.Count == 1 && batch[0].Address == "NORMAL-1");
    }

    [Fact]
    public async Task FasterScanGroupRunsMoreOftenThanSlowerGroup()
    {
        var fastReads = 0;
        var normalReads = 0;
        var driver = new DelegateDriver(
            (device, requests, _) =>
            {
                if (requests[0].Address == "FAST")
                {
                    Interlocked.Increment(ref fastReads);
                }
                else
                {
                    Interlocked.Increment(ref normalReads);
                }

                return Task.FromResult<IReadOnlyList<DriverReadResult>>(GoodResults(requests));
            });
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.ScanGroups =
        [
            new() { Name = "Fast", IntervalMilliseconds = 25 },
            new() { Name = "Normal", IntervalMilliseconds = 100 }
        ];
        options.Tags =
        [
            new() { Id = "FAST", DeviceId = "PLC-1", Address = "FAST", ScanGroup = "Fast" },
            new() { Id = "NORMAL", DeviceId = "PLC-1", Address = "NORMAL", ScanGroup = "Normal" }
        ];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await Task.Delay(350);
        await runtime.Service.StopAsync(CancellationToken.None);

        Assert.True(fastReads >= 5, $"Expected at least five fast reads, got {fastReads}.");
        Assert.True(normalReads >= 2, $"Expected at least two normal reads, got {normalReads}.");
        Assert.True(fastReads > normalReads, $"Expected fast reads ({fastReads}) > normal reads ({normalReads}).");
    }

    [Fact]
    public async Task FailedConnectRetriesAndEventuallySucceeds()
    {
        var connectAttempts = 0;
        var firstRead = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new DelegateDriver(
            (device, requests, _) =>
            {
                firstRead.TrySetResult(null);
                return Task.FromResult<IReadOnlyList<DriverReadResult>>(GoodResults(requests));
            },
            connect: (_, _) =>
            {
                if (Interlocked.Increment(ref connectAttempts) == 1)
                {
                    throw new InvalidOperationException("initial connection failure");
                }

                return Task.CompletedTask;
            });
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Polling.InitialReconnectDelayMilliseconds = 10;
        options.Polling.MaxReconnectDelayMilliseconds = 20;
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var snapshot = Assert.Single(runtime.Service.DeviceStates.Values);
        Assert.True(connectAttempts >= 2);
        Assert.Equal(DeviceConnectionState.Connected, snapshot.ConnectionState);
    }

    [Fact]
    public async Task ReconnectReusesTheDriverInstanceAcquiredForTheDevice()
    {
        var created = new List<ReconnectDriver>();
        var readSucceeded = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new DriverResolver(
        [
            DriverRegistration.PerDevice("Test", _ =>
            {
                var driver = new ReconnectDriver(readSucceeded);
                created.Add(driver);
                return driver;
            })
        ]);
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        await using var runtime = await TestRuntime.StartAsync(options, resolver);
        await readSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var driver = Assert.Single(created);
        Assert.True(driver.ConnectCount >= 2);
        Assert.Single(created);
    }

    [Fact]
    public async Task ReadFailurePublishesDisconnectedQualityThroughTagCache()
    {
        var reconnectGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FailureThenHoldDriver(reconnectGate);
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Polling.InitialReconnectDelayMilliseconds = 10;
        options.Polling.MaxReconnectDelayMilliseconds = 10;
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        var before = DateTimeOffset.UtcNow;
        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await WaitUntilAsync(() =>
            runtime.Cache.TryGet("T1", out var value) && value?.Quality == TagQuality.Disconnected);
        var after = DateTimeOffset.UtcNow;

        Assert.True(runtime.Cache.TryGet("T1", out var disconnected));
        Assert.Null(disconnected!.Value);
        Assert.InRange(disconnected.Timestamp, before, after);

        reconnectGate.TrySetResult(null);
    }

    [Fact]
    public async Task ReadTimeoutRequestsCancellationAndReconnects()
    {
        var successfulRead = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var driver = new DelegateDriver(
            async (device, requests, cancellationToken) =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    await Task.Delay(250, cancellationToken);
                }

                successfulRead.TrySetResult(null);
                return GoodResults(requests);
            });
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Polling.ReadTimeoutMilliseconds = 20;
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await successfulRead.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(reads >= 2);
    }

    [Fact]
    public async Task CooperativeDisconnectCancellationClearsInFlightStateAndAllowsReconnect()
    {
        var driver = new CooperativeDisconnectDriver();
        var resolver = new DriverResolver(
        [
            DriverRegistration.PerDevice("Test", _ => driver)
        ]);
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Polling.DisconnectTimeoutMilliseconds = 25;
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        await using var runtime = await TestRuntime.StartAsync(options, resolver);
        await driver.ReconnectedRead.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await driver.FirstDisconnectCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await runtime.Service.StopAsync(CancellationToken.None);

        Assert.True(driver.ConnectCount >= 2);
        Assert.True(driver.DisconnectCount >= 2);
        Assert.Equal(1, driver.DisposeCount);
    }

    [Fact]
    public async Task LateConnectCompletionDoesNotPublishConnectedStateAfterShutdown()
    {
        var driver = new LateConnectDriver();
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Polling.ConnectTimeoutMilliseconds = 500;
        options.Polling.ShutdownTimeoutMilliseconds = 50;
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await driver.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await runtime.Service.StopAsync(CancellationToken.None);
        driver.ReleaseConnect.TrySetResult(null);
        await driver.ConnectReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(50);

        Assert.NotEqual(
            DeviceConnectionState.Connected,
            runtime.Service.DeviceStates["PLC-1"].ConnectionState);
        Assert.False(runtime.Cache.TryGet("T1", out _));
    }

    [Fact]
    public async Task LateReadCompletionDoesNotPublishTagValueAfterShutdown()
    {
        var driver = new LateReadDriver();
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Polling.ShutdownTimeoutMilliseconds = 50;
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await driver.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await runtime.Service.StopAsync(CancellationToken.None);
        driver.ReleaseRead.TrySetResult(null);
        await driver.ReadReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(50);

        Assert.False(runtime.Cache.TryGet("T1", out _));
        Assert.Null(runtime.Service.DeviceStates["PLC-1"].LastSuccessfulRead);
    }

    [Fact]
    public async Task StopAsyncIsBoundedWhenLeaseDisposeAsyncDoesNotComplete()
    {
        var driver = new HangingDisposeDriver();
        var resolver = new DriverResolver(
        [
            DriverRegistration.PerDevice("Test", _ => driver)
        ]);
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Polling.ShutdownTimeoutMilliseconds = 75;
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        await using var runtime = await TestRuntime.StartAsync(options, resolver);
        await WaitUntilAsync(() => driver.ConnectCount > 0);

        var stopwatch = Stopwatch.StartNew();
        var stopTask = runtime.Service.StopAsync(CancellationToken.None);
        await driver.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await stopTask;
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Shutdown took {stopwatch.Elapsed}.");

        driver.ReleaseDispose.TrySetResult(null);
        await driver.DisposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MultiDeviceShutdownDisconnectsEachConnectedDevice()
    {
        var driver = new DelegateDriver(
            (device, requests, _) => Task.FromResult<IReadOnlyList<DriverReadResult>>(GoodResults(requests)));
        var options = CreateOptions(
            new DeviceDefinition { Id = "PLC-A", DriverType = "Test" },
            new DeviceDefinition { Id = "PLC-B", DriverType = "Test" });
        options.Tags =
        [
            new() { Id = "A1", DeviceId = "PLC-A", Address = "A1" },
            new() { Id = "B1", DeviceId = "PLC-B", Address = "B1" }
        ];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await WaitUntilAsync(() => driver.ConnectCount >= 2);
        await runtime.Service.StopAsync(CancellationToken.None);

        Assert.Equal(2, driver.DisconnectCount);
    }

    [Fact]
    public async Task NonCooperativeDeviceDoesNotMakeShutdownWaitForever()
    {
        var readStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource<IReadOnlyList<DriverReadResult>>();
        var driver = new DelegateDriver(
            (device, requests, _) =>
            {
                readStarted.TrySetResult(null);
                return neverCompletes.Task;
            });
        var options = CreateOptions(new DeviceDefinition { Id = "PLC-1", DriverType = "Test" });
        options.Polling.ShutdownTimeoutMilliseconds = 100;
        options.Tags = [new() { Id = "T1", DeviceId = "PLC-1", Address = "T1" }];

        await using var runtime = await TestRuntime.StartAsync(options, driver);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var stopwatch = Stopwatch.StartNew();
        await runtime.Service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Shutdown took {stopwatch.Elapsed}.");
    }

    private static RuntimeOptions CreateOptions(params DeviceDefinition[] devices) => new()
    {
        Polling = new PollingOptions
        {
            ConnectTimeoutMilliseconds = 100,
            ReadTimeoutMilliseconds = 100,
            DisconnectTimeoutMilliseconds = 100,
            InitialReconnectDelayMilliseconds = 10,
            MaxReconnectDelayMilliseconds = 20,
            ShutdownTimeoutMilliseconds = 500
        },
        ScanGroups = [new ScanGroupDefinition { Name = "Normal", IntervalMilliseconds = 10 }],
        Devices = [.. devices]
    };

    private static IReadOnlyList<DriverReadResult> GoodResults(IReadOnlyList<DriverReadRequest> requests)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return requests
            .Select(request => new DriverReadResult(request.TagId, 1.0, TagQuality.Good, timestamp))
            .ToArray();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class DelegateDriver : IPlcDriver
    {
        private readonly Func<DeviceDefinition, IReadOnlyList<DriverReadRequest>, CancellationToken, Task<IReadOnlyList<DriverReadResult>>> _read;
        private readonly Func<DeviceDefinition, CancellationToken, Task> _connect;
        private int _connectCount;
        private int _disconnectCount;

        public DelegateDriver(
            Func<DeviceDefinition, IReadOnlyList<DriverReadRequest>, CancellationToken, Task<IReadOnlyList<DriverReadResult>>> read,
            Func<DeviceDefinition, CancellationToken, Task>? connect = null)
        {
            _read = read;
            _connect = connect ?? ((_, _) => Task.CompletedTask);
        }

        public string DriverType => "Test";
        public int ConnectCount => Volatile.Read(ref _connectCount);
        public int DisconnectCount => Volatile.Read(ref _disconnectCount);

        public async Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connectCount);
            await _connect(device, cancellationToken);
        }

        public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken) =>
            _read(device, requests, cancellationToken);

        public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _disconnectCount);
            return Task.CompletedTask;
        }
    }

    private sealed class ReconnectDriver(TaskCompletionSource<object?> successfulRead) : IPlcDriver
    {
        private int _connectCount;
        private int _readCount;

        public string DriverType => "Test";
        public int ConnectCount => Volatile.Read(ref _connectCount);

        public Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connectCount);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                throw new InvalidOperationException("first read failed");
            }

            successfulRead.TrySetResult(null);
            return Task.FromResult<IReadOnlyList<DriverReadResult>>(GoodResults(requests));
        }

        public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FailureThenHoldDriver(TaskCompletionSource<object?> reconnectGate) : IPlcDriver
    {
        private int _connectCount;

        public string DriverType => "Test";

        public async Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _connectCount) > 1)
            {
                await reconnectGate.Task.WaitAsync(cancellationToken);
            }
        }

        public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<DriverReadResult>>(
                new InvalidOperationException("read failed"));

        public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CooperativeDisconnectDriver : IPlcDriver, IAsyncDisposable
    {
        private int _connectCount;
        private int _readCount;
        private int _disconnectCount;
        private int _disposeCount;

        public string DriverType => "Test";
        public int ConnectCount => Volatile.Read(ref _connectCount);
        public int DisconnectCount => Volatile.Read(ref _disconnectCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public TaskCompletionSource<object?> ReconnectedRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> FirstDisconnectCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connectCount);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                return Task.FromException<IReadOnlyList<DriverReadResult>>(
                    new InvalidOperationException("first read failed"));
            }

            ReconnectedRead.TrySetResult(null);
            return Task.FromResult<IReadOnlyList<DriverReadResult>>(GoodResults(requests));
        }

        public async Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _disconnectCount) == 1)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    FirstDisconnectCancelled.TrySetResult(null);
                    throw;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LateConnectDriver : IPlcDriver
    {
        public string DriverType => "Test";
        public TaskCompletionSource<object?> ConnectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> ReleaseConnect { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> ConnectReturned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            ConnectStarted.TrySetResult(null);
            await ReleaseConnect.Task;
            ConnectReturned.TrySetResult(null);
        }

        public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DriverReadResult>>([]);

        public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class LateReadDriver : IPlcDriver
    {
        public string DriverType => "Test";
        public TaskCompletionSource<object?> ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> ReleaseRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> ReadReturned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public async Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult(null);
            await ReleaseRead.Task;
            ReadReturned.TrySetResult(null);
            return GoodResults(requests);
        }

        public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class HangingDisposeDriver : IPlcDriver, IAsyncDisposable
    {
        private int _connectCount;

        public string DriverType => "Test";
        public int ConnectCount => Volatile.Read(ref _connectCount);
        public TaskCompletionSource<object?> DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> ReleaseDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> DisposeCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _connectCount);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
            DeviceDefinition device,
            IReadOnlyList<DriverReadRequest> requests,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DriverReadResult>>(GoodResults(requests));

        public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult(null);
            await ReleaseDispose.Task;
            DisposeCompleted.TrySetResult(null);
        }
    }
}
