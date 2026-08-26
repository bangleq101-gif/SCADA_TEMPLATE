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
        var fault = ParseFault(device);
        if (fault.Mode == SimulatorFaultMode.ConnectFailure ||
            fault.Mode == SimulatorFaultMode.Disconnected ||
            (fault.Mode == SimulatorFaultMode.IntermittentReadFailure &&
             fault.IsFaultActive(device, DateTimeOffset.UtcNow)))
        {
            throw new InvalidOperationException($"Simulator connection fault for device '{device.Id}'.");
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DriverReadResult>> ReadAsync(
        DeviceDefinition device,
        IReadOnlyList<DriverReadRequest> requests,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var fault = ParseFault(device);
        if (fault.Mode == SimulatorFaultMode.ReadFailure ||
            (fault.Mode == SimulatorFaultMode.IntermittentReadFailure && fault.IsFaultActive(device, now)))
        {
            throw new InvalidOperationException($"Simulator read fault for device '{device.Id}'.");
        }

        if (fault.Mode == SimulatorFaultMode.Disconnected)
        {
            IReadOnlyList<DriverReadResult> disconnected = requests
                .Select(request => new DriverReadResult(request.TagId, null, TagQuality.Disconnected, now))
                .ToArray();
            return Task.FromResult(disconnected);
        }

        var quality = fault.Mode == SimulatorFaultMode.BadQuality
            ? TagQuality.Bad
            : TagQuality.Good;
        IReadOnlyList<DriverReadResult> results = requests
            .Select(request => new DriverReadResult(
                request.TagId,
                generator.Generate(request.TagId, request.Address, request.DataType, now),
                quality,
                now))
            .ToArray();

        return Task.FromResult(results);
    }

    public Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken) => Task.CompletedTask;

    private static SimulatorFaultOptions ParseFault(DeviceDefinition device)
    {
        if (SimulatorFaultOptions.TryParse(device, out var options, out var issues))
        {
            return options;
        }

        throw new InvalidOperationException(string.Join(
            Environment.NewLine,
            issues.Select(issue => issue.Message)));
    }
}
