using System.ComponentModel;
using System.Globalization;
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
    private BulkEditValue<TagDataType> _sourceDataType = BulkEditValue<TagDataType>.Unchanged;
    private BulkEditValue<TagDataType> _dataType = BulkEditValue<TagDataType>.Unchanged;
    private BulkEditValue<double> _scale = BulkEditValue<double>.Unchanged;
    private BulkEditValue<double> _offset = BulkEditValue<double>.Unchanged;
    private BulkEditValue<string> _scanGroup = BulkEditValue<string>.Unchanged;
    private BulkEditValue<TagAccessMode> _accessMode = BulkEditValue<TagAccessMode>.Unchanged;
    private BulkEditValue<bool> _historyEnabled = BulkEditValue<bool>.Unchanged;
    private BulkEditValue<string> _historyProfile = BulkEditValue<string>.Unchanged;
    private BulkEditValue<bool> _mqttPublishEnabled = BulkEditValue<bool>.Unchanged;
    private BulkEditValue<string> _mqttProfile = BulkEditValue<string>.Unchanged;
    private string _scaleText = string.Empty;
    private string _offsetText = string.Empty;
    private string? _scaleInputError;
    private string? _offsetInputError;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BulkEditValue<bool> Enabled { get => _enabled; set => SetField(ref _enabled, value); }

    public BulkEditValue<string> DeviceId { get => _deviceId; set => SetField(ref _deviceId, value); }

    public BulkEditValue<TagDataType> SourceDataType { get => _sourceDataType; set => SetField(ref _sourceDataType, value); }

    public BulkEditValue<TagDataType> DataType { get => _dataType; set => SetField(ref _dataType, value); }

    public BulkEditValue<double> Scale
    {
        get => _scale;
        set => SetTransformValue(value, isScale: true);
    }

    public BulkEditValue<double> Offset
    {
        get => _offset;
        set => SetTransformValue(value, isScale: false);
    }

    public string ScaleText
    {
        get => _scaleText;
        set => SetTransformText(value, isScale: true);
    }

    public string OffsetText
    {
        get => _offsetText;
        set => SetTransformText(value, isScale: false);
    }

    public string? ScaleInputError => _scaleInputError;

    public string? OffsetInputError => _offsetInputError;

    public bool HasTransformInputErrors => _scaleInputError is not null || _offsetInputError is not null;

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
            SourceDataType = Uniform(values.Select(tag => tag.GetEffectiveSourceDataType())),
            DataType = Uniform(values.Select(tag => tag.DataType)),
            Scale = Uniform(values.Select(tag => tag.Scale)),
            Offset = Uniform(values.Select(tag => tag.Offset)),
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
        OnPropertyChanged(propertyName);
    }

    private void SetTransformValue(BulkEditValue<double> value, bool isScale)
    {
        if (isScale)
        {
            if (EqualityComparer<BulkEditValue<double>>.Default.Equals(_scale, value))
            {
                return;
            }

            _scale = value;
            _scaleText = FormatTransform(value);
            _scaleInputError = null;
            OnPropertyChanged(nameof(Scale));
            OnPropertyChanged(nameof(ScaleText));
            OnPropertyChanged(nameof(ScaleInputError));
        }
        else
        {
            if (EqualityComparer<BulkEditValue<double>>.Default.Equals(_offset, value))
            {
                return;
            }

            _offset = value;
            _offsetText = FormatTransform(value);
            _offsetInputError = null;
            OnPropertyChanged(nameof(Offset));
            OnPropertyChanged(nameof(OffsetText));
            OnPropertyChanged(nameof(OffsetInputError));
        }

        OnPropertyChanged(nameof(HasTransformInputErrors));
    }

    private void SetTransformText(string? value, bool isScale)
    {
        var text = value ?? string.Empty;
        if (string.Equals(isScale ? _scaleText : _offsetText, text, StringComparison.Ordinal))
        {
            return;
        }

        if (isScale)
        {
            _scaleText = text;
        }
        else
        {
            _offsetText = text;
        }

        BulkEditValue<double> nextValue;
        string? error;
        if (string.IsNullOrWhiteSpace(text))
        {
            nextValue = BulkEditValue<double>.Unchanged;
            error = null;
        }
        else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                 double.IsFinite(parsed))
        {
            nextValue = BulkEditValue<double>.Explicit(parsed);
            error = null;
        }
        else
        {
            nextValue = BulkEditValue<double>.Unchanged;
            error = "Use a finite invariant-culture number.";
        }

        if (isScale)
        {
            _scale = nextValue;
            _scaleInputError = error;
            OnPropertyChanged(nameof(ScaleText));
            OnPropertyChanged(nameof(Scale));
            OnPropertyChanged(nameof(ScaleInputError));
        }
        else
        {
            _offset = nextValue;
            _offsetInputError = error;
            OnPropertyChanged(nameof(OffsetText));
            OnPropertyChanged(nameof(Offset));
            OnPropertyChanged(nameof(OffsetInputError));
        }

        OnPropertyChanged(nameof(HasTransformInputErrors));
    }

    private static string FormatTransform(BulkEditValue<double> value) =>
        value.Kind == BulkEditValueKind.Explicit
            ? value.Value.ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
