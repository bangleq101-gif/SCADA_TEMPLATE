using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.Core.Devices;

namespace Scada.App.ViewModels;

public sealed class DeviceEditorRowViewModel : INotifyPropertyChanged
{
    private readonly Action<DeviceEditorRowViewModel> _changed;

    public DeviceEditorRowViewModel(DeviceDefinition definition, Action<DeviceEditorRowViewModel> changed)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal DeviceDefinition Definition { get; }

    public string Id
    {
        get => Definition.Id;
        set => SetValue(Definition.Id, value, next => Definition.Id = next);
    }

    public string Name
    {
        get => Definition.Name;
        set => SetValue(Definition.Name, value, next => Definition.Name = next);
    }

    public bool Enabled
    {
        get => Definition.Enabled;
        set => SetValue(Definition.Enabled, value, next => Definition.Enabled = next);
    }

    public string DriverType
    {
        get => Definition.DriverType;
        set => SetValue(Definition.DriverType, value, next => Definition.DriverType = next);
    }

    public string ConnectionSummary =>
        Definition.ConnectionOptions.Count == 0
            ? "No options"
            : string.Join(", ", Definition.ConnectionOptions.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}"));

    public string SetOption(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        Definition.ConnectionOptions[key] = value;
        OnPropertyChanged(nameof(ConnectionSummary));
        _changed(this);
        return value;
    }

    public string GetOption(string key, string defaultValue = "") =>
        Definition.ConnectionOptions.TryGetValue(key, out var value) ? value : defaultValue;

    internal DeviceDefinition CopyDefinition() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        DriverType = DriverType,
        ConnectionOptions = new Dictionary<string, string>(
            Definition.ConnectionOptions,
            StringComparer.OrdinalIgnoreCase)
    };

    private void SetValue<T>(T oldValue, T newValue, Action<T> setter, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            return;
        }

        setter(newValue);
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(DriverType))
        {
            OnPropertyChanged(nameof(ConnectionSummary));
        }

        _changed(this);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
