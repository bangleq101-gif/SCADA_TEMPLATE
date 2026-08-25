using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Scada.Core.Alarms;
using Scada.Core.Tags;
using Scada.Runtime.Alarms;

namespace Scada.App.ViewModels;

public sealed class AlarmMonitoringViewModel : INotifyPropertyChanged, IWorkspaceLifecycle, IDisposable
{
    private readonly object _sync = new();
    private readonly AlarmRuntimeService _runtime;
    private readonly IAlarmEventStore? _eventStore;
    private readonly TimeProvider _timeProvider;
    private readonly IAlarmSnapshotDispatcher _dispatcher;
    private IDisposable? _subscription;
    private AlarmRuntimeSnapshot? _pendingSnapshot;
    private bool _dispatcherUpdatePending;
    private long _generation;
    private bool _active;
    private bool _disposed;
    private AlarmRowViewModel? _selectedAlarm;
    private AlarmRuntimeState _runtimeState;
    private string? _lastErrorCode;
    private string? _lastErrorMessage;
    private string? _historyErrorMessage;

    public AlarmMonitoringViewModel(
        AlarmRuntimeService runtime,
        IAlarmEventStore? eventStore = null,
        TimeProvider? timeProvider = null)
        : this(runtime, eventStore, timeProvider, null)
    {
    }

    internal AlarmMonitoringViewModel(
        AlarmRuntimeService runtime,
        IAlarmEventStore? eventStore,
        TimeProvider? timeProvider,
        IAlarmSnapshotDispatcher? dispatcher)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _eventStore = eventStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dispatcher = dispatcher ?? new WpfAlarmSnapshotDispatcher();
        Rows = [];
        History = [];
        AcknowledgeSelectedCommand = new RelayCommand(_ => AcknowledgeSelected(), _ => SelectedAlarm?.CanAcknowledge == true);
        AcknowledgeAllCommand = new RelayCommand(_ => _runtime.AcknowledgeAll());
        LoadHistoryCommand = new AsyncRelayCommand(LoadHistoryAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<AlarmRowViewModel> Rows { get; }
    public ObservableCollection<AlarmEvent> History { get; }
    public ICommand AcknowledgeSelectedCommand { get; }
    public ICommand AcknowledgeAllCommand { get; }
    public AsyncRelayCommand LoadHistoryCommand { get; }
    public AlarmRuntimeState RuntimeState { get => _runtimeState; private set { _runtimeState = value; OnPropertyChanged(); OnPropertyChanged(nameof(HealthMessage)); } }
    public string? LastErrorCode { get => _lastErrorCode; private set { _lastErrorCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(HealthMessage)); } }
    public string? LastErrorMessage { get => _lastErrorMessage; private set { _lastErrorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HealthMessage)); } }
    public string HealthMessage => LastErrorCode is null ? RuntimeState.ToString() : $"{RuntimeState}: {LastErrorCode} — {LastErrorMessage}";
    public string? HistoryErrorMessage { get => _historyErrorMessage; private set { _historyErrorMessage = value; OnPropertyChanged(); } }
    public int OwnedSubscriptionCount { get { lock (_sync) return _subscription is null ? 0 : 1; } }
    internal int PendingDispatcherUpdateCount { get { lock (_sync) return _dispatcherUpdatePending ? 1 : 0; } }

    public AlarmRowViewModel? SelectedAlarm
    {
        get => _selectedAlarm;
        set
        {
            if (ReferenceEquals(_selectedAlarm, value)) return;
            _selectedAlarm = value;
            OnPropertyChanged();
            (AcknowledgeSelectedCommand as RelayCommand)?.Refresh();
        }
    }

    public void Activate()
    {
        long generation;
        lock (_sync)
        {
            if (_disposed || _active) return;
            _active = true;
            generation = ++_generation;
        }
        var subscription = _runtime.Subscribe(snapshot => ApplySnapshot(snapshot, generation));
        lock (_sync)
        {
            if (!_active || _disposed || generation != _generation)
            {
                subscription.Dispose();
                return;
            }
            _subscription = subscription;
        }
        ApplySnapshot(_runtime.Snapshot, generation);
    }

    public void Deactivate()
    {
        IDisposable? subscription;
        lock (_sync)
        {
            _active = false;
            _generation++;
            subscription = _subscription;
            _subscription = null;
            _pendingSnapshot = null;
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
        LoadHistoryCommand.Dispose();
    }

    private void AcknowledgeSelected()
    {
        if (SelectedAlarm?.InstanceId is Guid instanceId)
            _runtime.Acknowledge(new AlarmAcknowledgementRequest(instanceId));
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        long generation;
        lock (_sync)
        {
            if (!_active || _disposed) return;
            generation = _generation;
        }
        HistoryErrorMessage = null;
        if (_eventStore is null)
        {
            HistoryErrorMessage = "Alarm history store is unavailable.";
            return;
        }

        try
        {
            var now = _timeProvider.GetUtcNow();
            var events = await _eventStore.QueryAsync(
                new AlarmEventQuery(now.AddDays(-1), now, Limit: 1_000), cancellationToken);
            if (!IsCurrent(generation)) return;
            void Update()
            {
                if (!IsCurrent(generation)) return;
                History.Clear();
                foreach (var alarmEvent in events.OrderByDescending(item => item.Sequence))
                    History.Add(alarmEvent);
            }
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) Update();
            else await dispatcher.InvokeAsync(Update, DispatcherPriority.DataBind, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(generation)) HistoryErrorMessage = exception.Message;
        }
    }

    private void ApplySnapshot(AlarmRuntimeSnapshot snapshot, long generation)
    {
        if (!IsCurrent(generation)) return;
        lock (_sync)
        {
            if (!IsCurrentLocked(generation)) return;
            _pendingSnapshot = snapshot;
            if (_dispatcherUpdatePending) return;
            _dispatcherUpdatePending = true;
        }
        _dispatcher.Post(() => DrainSnapshot(generation));
    }

    private void DrainSnapshot(long generation)
    {
        AlarmRuntimeSnapshot? snapshot;
        lock (_sync)
        {
            // A queued callback from a previous activation must not touch the new generation's state.
            if (!IsCurrentLocked(generation)) return;
            snapshot = _pendingSnapshot;
            _pendingSnapshot = null;
            if (snapshot is null)
            {
                _dispatcherUpdatePending = false;
                return;
            }
        }

        if (IsCurrent(generation))
        {
            var selectedId = SelectedAlarm?.InstanceId;
            Rows.Clear();
            foreach (var alarm in snapshot.Alarms.Where(item => item.State != AlarmLifecycleState.Normal))
                Rows.Add(new AlarmRowViewModel(alarm));
            SelectedAlarm = selectedId is null ? null : Rows.FirstOrDefault(row => row.InstanceId == selectedId);
            RuntimeState = snapshot.State;
            LastErrorCode = snapshot.LastErrorCode;
            LastErrorMessage = snapshot.LastErrorMessage;
        }

        lock (_sync)
        {
            if (!IsCurrentLocked(generation)) return;
            if (_pendingSnapshot is null)
            {
                _dispatcherUpdatePending = false;
                return;
            }
        }

        // At most one new dispatcher item is created for a newer snapshot.
        _dispatcher.Post(() => DrainSnapshot(generation));
    }

    private bool IsCurrent(long generation)
    {
        lock (_sync) return _active && !_disposed && generation == _generation;
    }

    private bool IsCurrentLocked(long generation) => _active && !_disposed && generation == _generation;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class AlarmRowViewModel(AlarmSnapshot snapshot)
{
    public string AlarmId => snapshot.AlarmId;
    public string Name => snapshot.Name;
    public string Message => snapshot.Message;
    public Guid? InstanceId => snapshot.InstanceId;
    public AlarmLifecycleState State => snapshot.State;
    public AlarmSeverity Severity => snapshot.Severity;
    public TagQuality Quality => snapshot.EvaluationQuality;
    public bool IsEvaluationAvailable => snapshot.IsEvaluationAvailable;
    public long LastSourceSequence => snapshot.LastSourceSequence;
    public DateTimeOffset? SourceTimestampUtc => snapshot.LastSourceTimestampUtc;
    public DateTimeOffset? TransitionTimestampUtc => snapshot.TransitionTimestampUtc;
    public DateTimeOffset? AcknowledgedAtUtc => snapshot.AcknowledgedAtUtc;
    public string? AcknowledgedBy => snapshot.AcknowledgedBy;
    public bool CanAcknowledge => InstanceId is not null && State is AlarmLifecycleState.ActiveUnacknowledged or AlarmLifecycleState.ReturnedUnacknowledged;
}
