using Scada.Core.Configuration;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.Core.Alarms;
using Scada.Runtime.Alarms;

namespace Scada.App.ViewModels;

public sealed class OperationViewModel : IWorkspaceLifecycle, INotifyPropertyChanged, IDisposable
{
    private readonly object _sync = new();
    private readonly RuntimeOptions _options;
    private readonly AlarmRuntimeService? _alarmRuntime;
    private readonly IAlarmSnapshotDispatcher _dispatcher;
    private IDisposable? _alarmSubscription;
    private AlarmRuntimeSnapshot? _pendingAlarmSnapshot;
    private bool _dispatcherUpdatePending;
    private long _activationGeneration;
    private int _activeAlarmCount;
    private int _unacknowledgedAlarmCount;
    private AlarmRuntimeState _alarmRuntimeState;
    private bool _disposed;

    public OperationViewModel(RuntimeOptions options, AlarmRuntimeService? alarmRuntime = null)
        : this(options, alarmRuntime, null)
    {
    }

    internal OperationViewModel(RuntimeOptions options, AlarmRuntimeService? alarmRuntime, IAlarmSnapshotDispatcher? dispatcher)
    {
        _options = options;
        _alarmRuntime = alarmRuntime;
        _dispatcher = dispatcher ?? new WpfAlarmSnapshotDispatcher();
        _alarmRuntimeState = alarmRuntime?.Snapshot.State ?? AlarmRuntimeState.Disabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsActive { get; private set; }
    public int ActiveAlarmCount { get => _activeAlarmCount; private set { _activeAlarmCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(AlarmSummary)); } }
    public int UnacknowledgedAlarmCount { get => _unacknowledgedAlarmCount; private set { _unacknowledgedAlarmCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(AlarmSummary)); } }
    public AlarmRuntimeState AlarmRuntimeState { get => _alarmRuntimeState; private set { _alarmRuntimeState = value; OnPropertyChanged(); OnPropertyChanged(nameof(AlarmSummary)); } }
    public string AlarmSummary => $"{ActiveAlarmCount} active • {UnacknowledgedAlarmCount} unacknowledged • {AlarmRuntimeState}";
    public int OwnedAlarmSubscriptionCount { get { lock (_sync) return _alarmSubscription is null ? 0 : 1; } }
    internal int PendingAlarmDispatcherUpdateCount { get { lock (_sync) return _dispatcherUpdatePending ? 1 : 0; } }

    public string RuntimeSummary
    {
        get
        {
            var driverTypes = _options.Devices
                .Select(device => device.DriverType)
                .Where(driverType => !string.IsNullOrWhiteSpace(driverType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var driverSummary = driverTypes.Length == 0 ? "No drivers configured" : string.Join(", ", driverTypes);
            return $"{_options.RuntimeId} • {driverSummary}";
        }
    }

    public void Activate()
    {
        long generation;
        lock (_sync)
        {
            if (_disposed || IsActive) return;
            IsActive = true;
            generation = ++_activationGeneration;
        }
        if (_alarmRuntime is null) return;
        var subscription = _alarmRuntime.Subscribe(snapshot => ApplyAlarmSnapshot(snapshot, generation));
        lock (_sync)
        {
            if (_disposed || !IsActive || generation != _activationGeneration)
            {
                subscription.Dispose();
                return;
            }
            _alarmSubscription = subscription;
        }
        ApplyAlarmSnapshot(_alarmRuntime.Snapshot, generation);
    }

    public void Deactivate()
    {
        IDisposable? subscription;
        lock (_sync)
        {
            IsActive = false;
            _activationGeneration++;
            subscription = _alarmSubscription;
            _alarmSubscription = null;
            _pendingAlarmSnapshot = null;
            _dispatcherUpdatePending = false;
        }
        subscription?.Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Deactivate();
    }

    private void ApplyAlarmSnapshot(AlarmRuntimeSnapshot snapshot, long generation)
    {
        if (!IsCurrent(generation)) return;
        lock (_sync)
        {
            if (!IsCurrentLocked(generation)) return;
            _pendingAlarmSnapshot = snapshot;
            if (_dispatcherUpdatePending) return;
            _dispatcherUpdatePending = true;
        }
        _dispatcher.Post(() => DrainAlarmSnapshot(generation));
    }

    private void DrainAlarmSnapshot(long generation)
    {
        AlarmRuntimeSnapshot? snapshot;
        lock (_sync)
        {
            if (!IsCurrentLocked(generation)) return;
            snapshot = _pendingAlarmSnapshot;
            _pendingAlarmSnapshot = null;
            if (snapshot is null)
            {
                _dispatcherUpdatePending = false;
                return;
            }
        }

        if (IsCurrent(generation))
        {
            ActiveAlarmCount = snapshot.Alarms.Count(alarm => alarm.State != AlarmLifecycleState.Normal);
            UnacknowledgedAlarmCount = snapshot.Alarms.Count(alarm => alarm.State is AlarmLifecycleState.ActiveUnacknowledged or AlarmLifecycleState.ReturnedUnacknowledged);
            AlarmRuntimeState = snapshot.State;
        }

        lock (_sync)
        {
            if (!IsCurrentLocked(generation)) return;
            if (_pendingAlarmSnapshot is null)
            {
                _dispatcherUpdatePending = false;
                return;
            }
        }

        _dispatcher.Post(() => DrainAlarmSnapshot(generation));
    }

    private bool IsCurrent(long generation)
    {
        lock (_sync) return IsActive && !_disposed && generation == _activationGeneration;
    }

    private bool IsCurrentLocked(long generation) => IsActive && !_disposed && generation == _activationGeneration;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
