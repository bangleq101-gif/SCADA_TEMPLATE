using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.Core.Configuration;
using Scada.Core.Tags;

namespace Scada.App.ViewModels;

public sealed class TagEditorRowViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private readonly Dictionary<string, string[]> _errors = new(StringComparer.OrdinalIgnoreCase);
    private TagQuality _quality = TagQuality.NotConfigured;
    private object? _value;
    private DateTimeOffset? _timestamp;
    private string _runtimeStatus = "Not Loaded";

    public TagEditorRowViewModel(TagDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public TagDefinition Definition { get; }

    public string Id => Definition.Id;
    public string Name { get => Definition.Name; set => SetValue(Definition.Name, value, newValue => Definition.Name = newValue, nameof(Name)); }
    public string Description { get => Definition.Description; set => SetValue(Definition.Description, value, newValue => Definition.Description = newValue, nameof(Description)); }
    public string DeviceId { get => Definition.DeviceId; set => SetValue(Definition.DeviceId, value, newValue => Definition.DeviceId = newValue, nameof(DeviceId)); }
    public string Address { get => Definition.Address; set => SetValue(Definition.Address, value, newValue => Definition.Address = newValue, nameof(Address)); }
    public TagDataType DataType { get => Definition.DataType; set => SetValue(Definition.DataType, value, newValue => Definition.DataType = newValue, nameof(DataType)); }
    public bool Enabled { get => Definition.Enabled; set => SetValue(Definition.Enabled, value, newValue => Definition.Enabled = newValue, nameof(Enabled)); }
    public string ScanGroup { get => Definition.ScanGroup; set => SetValue(Definition.ScanGroup, value, newValue => Definition.ScanGroup = newValue, nameof(ScanGroup)); }
    public TagAccessMode AccessMode { get => Definition.AccessMode; set => SetValue(Definition.AccessMode, value, newValue => Definition.AccessMode = newValue, nameof(AccessMode)); }
    public double? Min { get => Definition.Min; set => SetValue(Definition.Min, value, newValue => Definition.Min = newValue, nameof(Min)); }
    public double? Max { get => Definition.Max; set => SetValue(Definition.Max, value, newValue => Definition.Max = newValue, nameof(Max)); }
    public string Unit { get => Definition.Unit; set => SetValue(Definition.Unit, value, newValue => Definition.Unit = newValue, nameof(Unit)); }
    public bool HistoryEnabled { get => Definition.HistoryEnabled; set => SetValue(Definition.HistoryEnabled, value, newValue => Definition.HistoryEnabled = newValue, nameof(HistoryEnabled)); }
    public string HistoryProfile { get => Definition.HistoryProfile; set => SetValue(Definition.HistoryProfile, value, newValue => Definition.HistoryProfile = newValue, nameof(HistoryProfile)); }
    public bool MqttPublishEnabled { get => Definition.MqttPublishEnabled; set => SetValue(Definition.MqttPublishEnabled, value, newValue => Definition.MqttPublishEnabled = newValue, nameof(MqttPublishEnabled)); }
    public string MqttProfile { get => Definition.MqttProfile; set => SetValue(Definition.MqttProfile, value, newValue => Definition.MqttProfile = newValue, nameof(MqttProfile)); }
    public string MqttTopicOverride { get => Definition.MqttTopicOverride; set => SetValue(Definition.MqttTopicOverride, value, newValue => Definition.MqttTopicOverride = newValue, nameof(MqttTopicOverride)); }

    public object? Value
    {
        get => _value;
        private set => SetField(ref _value, value);
    }

    public TagQuality Quality
    {
        get => _quality;
        private set => SetField(ref _quality, value);
    }

    public DateTimeOffset? Timestamp
    {
        get => _timestamp;
        private set => SetField(ref _timestamp, value);
    }

    public string RuntimeStatus
    {
        get => _runtimeStatus;
        private set => SetField(ref _runtimeStatus, value);
    }

    public bool HasErrors => _errors.Count > 0;

    public bool HasWarnings => _errors.Values.SelectMany(messages => messages).Any(message => message.StartsWith("Warning:", StringComparison.Ordinal));

    public bool HasRuntimeConfigurationWarning { get; private set; }

    public bool HasErrorsChanged { get; private set; }

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is null)
        {
            return _errors.Values.SelectMany(messages => messages);
        }

        return _errors.TryGetValue(propertyName, out var messages) ? messages : [];
    }

    public void ApplyRuntimeValue(TagValue value, string status)
    {
        Value = value.Value;
        Quality = value.Quality;
        Timestamp = value.Timestamp;
        RuntimeStatus = status;
    }

    public void ApplyRuntimeUnavailable(string status = "Not Loaded")
    {
        Value = null;
        Quality = TagQuality.NotConfigured;
        Timestamp = null;
        RuntimeStatus = status;
    }

    public void SetRuntimeStatus(string status) => RuntimeStatus = status;

    public void SetValidationIssues(IEnumerable<ValidationIssue> issues)
    {
        var next = issues
            .GroupBy(issue => issue.PropertyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(issue =>
                    issue.IsBlocking ? issue.Message : $"Warning: {issue.Message}").ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var changedProperties = _errors.Keys.Union(next.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        _errors.Clear();
        foreach (var pair in next)
        {
            _errors[pair.Key] = pair.Value;
        }

        HasRuntimeConfigurationWarning = issues.Any(issue => issue.Code == "RUNTIME_RESTART_REQUIRED");
        foreach (var property in changedProperties)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(property));
        }

        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasRuntimeConfigurationWarning));
    }

    private void SetValue<T>(T oldValue, T newValue, Action<T> setter, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            return;
        }

        setter(newValue);
        OnPropertyChanged(propertyName);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
