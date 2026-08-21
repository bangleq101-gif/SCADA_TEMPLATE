using Scada.Core.Configuration;

namespace Scada.App.ViewModels;

public sealed class OperationViewModel(RuntimeOptions options) : IWorkspaceLifecycle
{
    public bool IsActive { get; private set; }

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
            return $"{options.RuntimeId} • {driverSummary}";
        }
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}

public sealed class MachineSettingsViewModel : IWorkspaceLifecycle
{
    public bool IsActive { get; private set; }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}

public sealed class EngineeringViewModel : IWorkspaceLifecycle
{
    public bool IsActive { get; private set; }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
