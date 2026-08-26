using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.Core.Drivers;

namespace Scada.App.ViewModels;

public sealed class DriverOptionEditorViewModel : INotifyPropertyChanged
{
    private readonly DeviceEditorRowViewModel _device;
    private readonly DriverOptionDefinition _definition;

    public DriverOptionEditorViewModel(DeviceEditorRowViewModel device, DriverOptionDefinition definition)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key => _definition.Key;
    public string DisplayName => _definition.DisplayName;
    public DriverOptionValueType ValueType => _definition.ValueType;
    public bool IsRequired => _definition.IsRequired;
    public bool IsAdvanced => _definition.IsAdvanced;
    public string? Description => _definition.Description;

    public string Value
    {
        get => _device.GetOption(Key, _definition.DefaultValue);
        set
        {
            if (string.Equals(Value, value, StringComparison.Ordinal))
            {
                return;
            }

            _device.SetOption(Key, value);
            OnPropertyChanged();
        }
    }

    public string DisplayValue => ValueType switch
    {
        DriverOptionValueType.Boolean when bool.TryParse(Value, out var parsed) => parsed ? "True" : "False",
        _ => Value
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
