using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.App.Hmi;

public enum HmiEquipmentKind { Motor, Pump, Valve, Tank, Pipe, Conveyor, Indicator }
public enum HmiTagRole { Run, Fault, Warning, Ready, Position, Level, Flow, Value }
public enum HmiVisualState { Unknown, Stopped, Running, Warning, Fault, BadQuality }

public interface IHmiDispatcher { void Post(Action action); }
public sealed class WpfHmiDispatcher : IHmiDispatcher
{
    public void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(DispatcherPriority.DataBind, action);
    }
}

public sealed class HmiEquipmentContext : INotifyPropertyChanged, IDisposable
{
    private readonly ITagCache _cache;
    private readonly object _lifecycleSync = new();
    private readonly IHmiDispatcher _dispatcher;
    private readonly Dictionary<HmiTagRole, string> _tags;
    private readonly Dictionary<HmiTagRole, TagValue> _values = [];
    private readonly List<IDisposable> _subscriptions = [];
    private long _generation;
    private bool _active;
    private bool _disposed;
    private HmiVisualState _state = HmiVisualState.Unknown;

    public HmiEquipmentContext(ITagCache cache, HmiEquipmentKind kind, string equipmentId, string displayName, IReadOnlyDictionary<HmiTagRole, string> tags, IHmiDispatcher? dispatcher = null)
    {
        _cache = cache; _dispatcher = dispatcher ?? new WpfHmiDispatcher(); Kind = kind; EquipmentId = equipmentId; DisplayName = displayName;
        _tags = tags.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value, EqualityComparer<HmiTagRole>.Default);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public HmiEquipmentKind Kind { get; }
    public string EquipmentId { get; }
    public string DisplayName { get; }
    public IReadOnlyDictionary<HmiTagRole, string> TagIds => _tags;
    public HmiVisualState State { get => _state; private set { if (_state != value) { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(StateText)); } } }
    public string StateText => State.ToString();
    public TagValue? GetValue(HmiTagRole role) => _values.TryGetValue(role, out var value) ? value : null;
    public object? DisplayValue => GetValue(Kind == HmiEquipmentKind.Tank ? HmiTagRole.Level : HmiTagRole.Value)?.Value;
    public TagQuality? DisplayQuality => GetValue(Kind == HmiEquipmentKind.Tank ? HmiTagRole.Level : HmiTagRole.Value)?.Quality;
    public DateTimeOffset? DisplayTimestamp => GetValue(Kind == HmiEquipmentKind.Tank ? HmiTagRole.Level : HmiTagRole.Value)?.Timestamp;
    public double TankFillFraction => Kind == HmiEquipmentKind.Tank && TryGetNumeric(GetValue(HmiTagRole.Level)?.Value, out var level)
        ? Math.Clamp(level / 100d, 0d, 1d)
        : 0d;

    public void Activate()
    {
        long generation;
        lock (_lifecycleSync) { if (_disposed || _active) return; _active = true; generation = ++_generation; }
        foreach (var tagId in _tags.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var subscription = _cache.Subscribe(tagId, value => Receive(value, generation));
            lock (_lifecycleSync)
            {
                if (_active && !_disposed && generation == _generation) _subscriptions.Add(subscription);
                else { subscription.Dispose(); return; }
            }
            if (_cache.TryGet(tagId, out var value) && value is not null) Receive(value, generation);
        }
        Reevaluate();
    }
    public void Deactivate()
    {
        IDisposable[] subscriptions;
        lock (_lifecycleSync) { _active = false; ++_generation; subscriptions = _subscriptions.ToArray(); _subscriptions.Clear(); }
        foreach (var subscription in subscriptions) subscription.Dispose();
    }
    public void Dispose() { lock (_lifecycleSync) { if (_disposed) return; _disposed = true; } Deactivate(); }

    private void Receive(TagValue value, long generation)
    {
        lock (_lifecycleSync) { if (!_active || _disposed || generation != _generation) return; }
        _dispatcher.Post(() =>
        {
            lock (_lifecycleSync) { if (!_active || _disposed || generation != _generation) return; }
            foreach (var role in _tags.Where(pair => string.Equals(pair.Value, value.TagId, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key)) _values[role] = value;
            Reevaluate();
        });
    }
    private void Reevaluate()
    {
        State = HmiStateEvaluator.Evaluate(Kind, _tags, _values);
        OnPropertyChanged(nameof(DisplayValue)); OnPropertyChanged(nameof(DisplayQuality)); OnPropertyChanged(nameof(DisplayTimestamp)); OnPropertyChanged(nameof(TankFillFraction));
    }
    private static bool TryGetNumeric(object? value, out double result)
    {
        if (value is byte or short or int or long or float or double or decimal)
        {
            result = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return double.IsFinite(result);
        }

        result = 0d;
        return false;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class HmiStateEvaluator
{
    public static HmiVisualState Evaluate(HmiEquipmentKind kind, IReadOnlyDictionary<HmiTagRole, string> tags, IReadOnlyDictionary<HmiTagRole, TagValue> values)
    {
        HmiTagRole[] required = kind switch
        {
            HmiEquipmentKind.Valve => [HmiTagRole.Position],
            HmiEquipmentKind.Tank => [HmiTagRole.Level],
            HmiEquipmentKind.Pipe => [HmiTagRole.Flow],
            HmiEquipmentKind.Indicator => [HmiTagRole.Value],
            _ => [HmiTagRole.Run]
        };
        if (required.Any(role => !tags.ContainsKey(role) || !values.TryGetValue(role, out var value) || !IsValid(role, value.Value))) return HmiVisualState.Unknown;
        if (required.Any(role => values[role].Quality != TagQuality.Good)) return HmiVisualState.BadQuality;
        if (IsTrue(HmiTagRole.Fault)) return HmiVisualState.Fault;
        if (IsTrue(HmiTagRole.Warning)) return HmiVisualState.Warning;
        return kind switch
        {
            HmiEquipmentKind.Valve => AsBool(values[HmiTagRole.Position].Value) ? HmiVisualState.Running : HmiVisualState.Stopped,
            HmiEquipmentKind.Tank => HmiVisualState.Running,
            HmiEquipmentKind.Indicator => values[HmiTagRole.Value].Value is bool indicator ? indicator ? HmiVisualState.Running : HmiVisualState.Stopped : HmiVisualState.Running,
            _ => AsBool(values[required[0]].Value) ? HmiVisualState.Running : HmiVisualState.Stopped
        };
        bool IsTrue(HmiTagRole role) => tags.ContainsKey(role) && values.TryGetValue(role, out var value) && value.Quality == TagQuality.Good && IsValid(role, value.Value) && AsBool(value.Value);
    }
    private static bool IsValid(HmiTagRole role, object? value) => role switch
    {
        HmiTagRole.Level => value is byte or short or int or long or float or double or decimal,
        HmiTagRole.Value => value is bool or byte or short or int or long or float or double or decimal,
        _ => value is bool
    };
    private static bool AsBool(object? value) => value is bool flag && flag;
}
