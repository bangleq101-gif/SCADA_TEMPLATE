using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.Core.Tags;

namespace Scada.App.ViewModels;

public enum BulkEditValueKind
{
    Unchanged,
    Mixed,
    Explicit
}

public readonly record struct BulkEditValue<T>(BulkEditValueKind Kind, T Value)
{
    public static BulkEditValue<T> Unchanged => new(BulkEditValueKind.Unchanged, default!);

    public static BulkEditValue<T> Mixed => new(BulkEditValueKind.Mixed, default!);

    public static BulkEditValue<T> Explicit(T value) => new(BulkEditValueKind.Explicit, value);

    public string DisplayName => Kind switch
    {
        BulkEditValueKind.Unchanged => "Unchanged",
        BulkEditValueKind.Mixed => "Mixed",
        _ => Value?.ToString() ?? "(empty)"
    };
}

public sealed class TagBulkEditModel : INotifyPropertyChanged
{
    private BulkEditValue<bool> _enabled = BulkEditValue<bool>.Unchanged;
    private BulkEditValue<string> _deviceId = BulkEditValue<string>.Unchanged;
    private BulkEditValue<TagDataType> _dataType = BulkEditValue<TagDataType>.Unchanged;
    private BulkEditValue<string> _scanGroup = BulkEditValue<string>.Unchanged;
    private BulkEditValue<TagAccessMode> _accessMode = BulkEditValue<TagAccessMode>.Unchanged;
    private BulkEditValue<bool> _historyEnabled = BulkEditValue<bool>.Unchanged;
    private BulkEditValue<string> _historyProfile = BulkEditValue<string>.Unchanged;
    private BulkEditValue<bool> _mqttPublishEnabled = BulkEditValue<bool>.Unchanged;
    private BulkEditValue<string> _mqttProfile = BulkEditValue<string>.Unchanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BulkEditValue<bool> Enabled { get => _enabled; set => SetField(ref _enabled, value); }

    public BulkEditValue<string> DeviceId { get => _deviceId; set => SetField(ref _deviceId, value); }

    public BulkEditValue<TagDataType> DataType { get => _dataType; set => SetField(ref _dataType, value); }

    public BulkEditValue<string> ScanGroup { get => _scanGroup; set => SetField(ref _scanGroup, value); }

    public BulkEditValue<TagAccessMode> AccessMode { get => _accessMode; set => SetField(ref _accessMode, value); }

    public BulkEditValue<bool> HistoryEnabled { get => _historyEnabled; set => SetField(ref _historyEnabled, value); }

    public BulkEditValue<string> HistoryProfile { get => _historyProfile; set => SetField(ref _historyProfile, value); }

    public BulkEditValue<bool> MqttPublishEnabled { get => _mqttPublishEnabled; set => SetField(ref _mqttPublishEnabled, value); }

    public BulkEditValue<string> MqttProfile { get => _mqttProfile; set => SetField(ref _mqttProfile, value); }

    public static TagBulkEditModel FromSelection(IEnumerable<TagDefinition> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var values = tags.ToArray();
        if (values.Length == 0)
        {
            return new TagBulkEditModel();
        }

        return new TagBulkEditModel
        {
            Enabled = Uniform(values.Select(tag => tag.Enabled)),
            DeviceId = Uniform(values.Select(tag => tag.DeviceId)),
            DataType = Uniform(values.Select(tag => tag.DataType)),
            ScanGroup = Uniform(values.Select(tag => tag.ScanGroup)),
            AccessMode = Uniform(values.Select(tag => tag.AccessMode)),
            HistoryEnabled = Uniform(values.Select(tag => tag.HistoryEnabled)),
            HistoryProfile = Uniform(values.Select(tag => tag.HistoryProfile)),
            MqttPublishEnabled = Uniform(values.Select(tag => tag.MqttPublishEnabled)),
            MqttProfile = Uniform(values.Select(tag => tag.MqttProfile))
        };
    }

    private static BulkEditValue<T> Uniform<T>(IEnumerable<T> values)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return BulkEditValue<T>.Unchanged;
        }

        var first = enumerator.Current;
        while (enumerator.MoveNext())
        {
            if (!EqualityComparer<T>.Default.Equals(first, enumerator.Current))
            {
                return BulkEditValue<T>.Mixed;
            }
        }

        return BulkEditValue<T>.Unchanged;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
