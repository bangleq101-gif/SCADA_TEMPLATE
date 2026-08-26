using Scada.Core.Configuration;
using Scada.Core.Devices;

namespace Scada.Core.Drivers;

/// <summary>
/// Provides driver-specific engineering metadata without becoming a PLC runtime contract.
/// Implementations may browse a configured source, but must not perform tag polling or writes.
/// </summary>
public interface IDriverEngineeringProvider
{
    string DriverType { get; }

    IReadOnlyList<DriverOptionDefinition> OptionDefinitions { get; }

    IReadOnlyList<ValidationIssue> Validate(DeviceDefinition device);

    Task<IReadOnlyList<AddressBrowseCandidate>> BrowseAddressesAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken);
}
