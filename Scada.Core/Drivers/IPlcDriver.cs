using Scada.Core.Devices;

namespace Scada.Core.Drivers;

public interface IPlcDriver
{
    string DriverType { get; }

    Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken);

    Task<IReadOnlyList<DriverReadResult>> ReadAsync(
        DeviceDefinition device,
        IReadOnlyList<DriverReadRequest> requests,
        CancellationToken cancellationToken);

    Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken);
}
