using System.ComponentModel;

namespace Scada.App.ViewModels;

public sealed class NavigationService(
    OperationViewModel operation,
    MachineSettingsViewModel machineSettings,
    MonitoringViewModel monitoring,
    EngineeringViewModel engineering) : INotifyPropertyChanged
{
    private readonly Dictionary<string, object> _pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["operation"] = operation,
        ["machine-settings"] = machineSettings,
        ["monitoring"] = monitoring,
        ["engineering"] = engineering
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public object CurrentViewModel { get; private set; } = operation;

    public void Navigate(string key)
    {
        if (_pages.TryGetValue(key, out var page))
        {
            CurrentViewModel = page;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentViewModel)));
        }
    }
}
