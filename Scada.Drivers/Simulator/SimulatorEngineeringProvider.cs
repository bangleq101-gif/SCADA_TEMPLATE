using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;

namespace Scada.Drivers.Simulator;

public sealed class SimulatorEngineeringProvider : IDriverEngineeringProvider
{
    public string DriverType => "Simulator";

    public IReadOnlyList<DriverOptionDefinition> OptionDefinitions { get; } =
    [
        new(
            SimulatorFaultOptions.FaultModeKey,
            "Fault mode",
            DriverOptionValueType.String,
            nameof(SimulatorFaultMode.None),
            Description: "Deterministic simulator scenario used for commissioning and diagnostics."),
        new(
            SimulatorFaultOptions.FaultPeriodSecondsKey,
            "Fault period (s)",
            DriverOptionValueType.Integer,
            "10",
            IsAdvanced: true),
        new(
            SimulatorFaultOptions.FaultDurationSecondsKey,
            "Fault duration (s)",
            DriverOptionValueType.Integer,
            "1",
            IsAdvanced: true),
        new(
            SimulatorFaultOptions.FaultPhaseSecondsKey,
            "Fault phase (s)",
            DriverOptionValueType.Integer,
            "0",
            IsAdvanced: true)
    ];

    public IReadOnlyList<ValidationIssue> Validate(DeviceDefinition device)
    {
        ArgumentNullException.ThrowIfNull(device);
        SimulatorFaultOptions.TryParse(device, out _, out var issues);
        return issues;
    }

    public Task<IReadOnlyList<AddressBrowseCandidate>> BrowseAddressesAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AddressBrowseCandidate> candidates =
        [
            new("A1", TagDataType.Double, "Smooth analog value"),
            new("B1", TagDataType.Boolean, "Deterministic boolean value"),
            new("C1", TagDataType.Int32, "Deterministic counter"),
            new("S1", TagDataType.String, "Simulator text value")
        ];
        return Task.FromResult(candidates);
    }
}
