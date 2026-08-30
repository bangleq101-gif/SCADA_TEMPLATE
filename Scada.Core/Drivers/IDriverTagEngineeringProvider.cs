using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;

namespace Scada.Core.Drivers;

/// <summary>
/// Optional engineering contract for validating driver-specific logical tag addresses.
/// It is not used by runtime polling and must not perform device I/O.
/// </summary>
public interface IDriverTagEngineeringProvider
{
    IReadOnlyList<ValidationIssue> ValidateTag(DeviceDefinition device, TagDefinition tag);
}
