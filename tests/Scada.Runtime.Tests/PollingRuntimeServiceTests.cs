using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;
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
        var options = new RuntimeOptions
        {
            PollingIntervalMilliseconds = 10,
            Devices = [device],
            Tags =
            [
                new TagDefinition
                {
                    Id = "T1",
                    DeviceId = device.Id,
                    Address = "DB1.0",
                    DataType = TagDataType.Double
                }
            ]
        };
        var cache = new TagCache();
        var service = new PollingRuntimeService(
            driver,
            options,
            new TagEngine(cache),
            NullLogger<PollingRuntimeService>.Instance);

        await service.StartAsync(CancellationToken.None);
        for (var attempt = 0; attempt < 50 && driver.ConnectCount == 0; attempt++)
        {
            await Task.Delay(10);
        }

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, driver.ConnectCount);
        Assert.Equal(1, driver.DisconnectCount);
        Assert.False(driver.LastDisconnectToken.IsCancellationRequested);
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
