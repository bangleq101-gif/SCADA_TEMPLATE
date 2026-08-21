using Microsoft.Extensions.Hosting;
using Scada.Runtime.Devices;

namespace Scada.Runtime.Polling;

public sealed class PollingRuntimeService(DeviceManager deviceManager) : IHostedService
{
    public IReadOnlyDictionary<string, DeviceRuntimeSnapshot> DeviceStates => deviceManager.DeviceSnapshots;

    public Task StartAsync(CancellationToken cancellationToken) =>
        deviceManager.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        deviceManager.StopAsync(cancellationToken);
}
