using Scada.Core.Configuration;

namespace Scada.App.ViewModels;

public sealed class OperationViewModel(RuntimeOptions options)
{
    public string RuntimeSummary
    {
        get
        {
            var driverTypes = options.Devices
                .Select(device => device.DriverType)
                .Where(driverType => !string.IsNullOrWhiteSpace(driverType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var driverSummary = driverTypes.Length == 0 ? "No drivers configured" : string.Join(", ", driverTypes);
            return $"{options.RuntimeId} • {driverSummary} • TagCache active";
        }
    }
}

public sealed class MachineSettingsViewModel
{
}

public sealed class EngineeringViewModel
{
}
