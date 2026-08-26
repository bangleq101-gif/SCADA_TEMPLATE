using Scada.Core.Configuration;
using Scada.Core.Devices;

namespace Scada.Core.Drivers;

public static class DriverEngineeringValidation
{
    public static IReadOnlyList<ValidationIssue> Validate(
        DeviceDefinition device,
        IDriverEngineeringProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (provider is null)
        {
            return [new ValidationIssue(
                "DEVICE_DRIVER_UNSUPPORTED",
                ValidationSeverity.Error,
                "Device",
                device.Id,
                nameof(device.DriverType),
                $"No engineering provider is registered for driver type '{device.DriverType}'.")];
        }

        return provider.Validate(device);
    }
}
