using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;

namespace Scada.Drivers.Simulator;

public sealed class SimulatorPlcDriver(SimulatorValueGenerator generator) : IPlcDriver
{
    public string DriverType => "Simulator";

    public Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
        DeviceDefinition device,
        IReadOnlyList<DriverReadRequest> requests,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<DriverReadResult> results = requests
            .Select(request => new DriverReadResult(
                request.TagId,
                generator.Generate(request.TagId, request.Address, request.DataType, now),
                TagQuality.Good,
                now))
            .ToArray();

        return Task.FromResult(results);
    }

    public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) => Task.CompletedTask;
}
