using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scada.Core.Configuration;
using Scada.Core.Drivers;
using Scada.Runtime.Devices;
using Scada.Runtime.Engine;

namespace Scada.Runtime.Polling;

public sealed class PollingRuntimeService(
    IPlcDriver driver,
    RuntimeOptions options,
    TagEngine tagEngine,
    ILogger<PollingRuntimeService> logger) : BackgroundService
{
    private readonly object _connectionSync = new();
    private readonly Dictionary<string, DeviceRuntimeState> _deviceStates =
        options.Devices.ToDictionary(device => device.Id, device => new DeviceRuntimeState(device.Id), StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _connectedDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, DeviceRuntimeState> DeviceStates => _deviceStates;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabledDevices = options.Devices.Where(device => device.Enabled).ToArray();

        foreach (var device in enabledDevices)
        {
            try
            {
                await driver.ConnectAsync(device, stoppingToken);
                _deviceStates[device.Id].MarkConnected();
                lock (_connectionSync)
                {
                    _connectedDeviceIds.Add(device.Id);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _deviceStates[device.Id].MarkFailure(exception);
                logger.LogError(exception, "Unable to connect device {DeviceId}.", device.Id);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var device in enabledDevices)
            {
                await PollDeviceAsync(device, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(options.PollingIntervalMilliseconds), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            await DisconnectConnectedDevicesAsync(cancellationToken);
        }
    }

    private async Task DisconnectConnectedDevicesAsync(CancellationToken cancellationToken)
    {
        string[] connectedDeviceIds;
        lock (_connectionSync)
        {
            connectedDeviceIds = _connectedDeviceIds.ToArray();
            _connectedDeviceIds.Clear();
        }

        foreach (var device in options.Devices.Where(device => connectedDeviceIds.Contains(device.Id, StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                await driver.DisconnectAsync(device, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Disconnect cancelled for device {DeviceId} during runtime shutdown.", device.Id);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Unable to disconnect device {DeviceId} during runtime shutdown.", device.Id);
            }
        }
    }

    private async Task PollDeviceAsync(Scada.Core.Devices.DeviceDefinition device, CancellationToken cancellationToken)
    {
        var requests = options.Tags
            .Where(tag => tag.Enabled && string.Equals(tag.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            .Select(tag => new DriverReadRequest(tag.Id, tag.Address, tag.DataType))
            .ToArray();

        if (requests.Length == 0)
        {
            return;
        }

        try
        {
            var results = await driver.ReadAsync(device, requests, cancellationToken);
            tagEngine.Apply(results);
            _deviceStates[device.Id].MarkSuccess(DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _deviceStates[device.Id].MarkFailure(exception);
            logger.LogError(exception, "Polling failed for device {DeviceId}.", device.Id);
        }
    }
}
