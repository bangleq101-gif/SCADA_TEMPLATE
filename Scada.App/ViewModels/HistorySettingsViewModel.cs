using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.App.Services;
using Scada.Core.Configuration;
using Scada.Core.History;
using Scada.Runtime.Historian;

namespace Scada.App.ViewModels;

public sealed class HistorySettingsViewModel : INotifyPropertyChanged, IWorkspaceLifecycle
{
    private readonly ProjectEditSession _session;
    private readonly HistorianRuntimeService _historian;
    private readonly IHistoryStoreDiagnostics? _storeDiagnostics;
    private readonly IHistoryStoreMaintenance? _storeMaintenance;
    private readonly IHistoryBufferConfirmation? _bufferConfirmation;
    private readonly IHistoryConnectionTester? _connectionTester;
    private bool _isActive;
    private string _statusText = "Not started";
    private string _lastErrorText = string.Empty;
    private string _lastWriteText = "Never";
    private string _saveStatusText = string.Empty;
    private HistoryProfileEditor? _selectedProfile;

    public HistorySettingsViewModel(
        ProjectEditSession session,
        HistorianRuntimeService historian,
        IHistoryStoreDiagnostics? storeDiagnostics = null,
        IHistoryStoreMaintenance? storeMaintenance = null,
        IHistoryBufferConfirmation? bufferConfirmation = null,
        IHistoryConnectionTester? connectionTester = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _historian = historian ?? throw new ArgumentNullException(nameof(historian));
        _storeDiagnostics = storeDiagnostics;
        _storeMaintenance = storeMaintenance;
        _bufferConfirmation = bufferConfirmation;
        _connectionTester = connectionTester;
        Profiles = [];
        SaveCommand = new RelayCommand(_ => Save());
        RevertCommand = new RelayCommand(_ => Revert());
        AddProfileCommand = new RelayCommand(_ => AddProfile());
        DeleteProfileCommand = new RelayCommand(parameter =>
        {
            if (parameter is HistoryProfileEditor profile)
            {
                DeleteProfile(profile);
            }
        });
        RefreshStatusCommand = new RelayCommand(_ => RefreshStatus());
        TestConnectionCommand = new RelayCommand(_ => _ = TestConnectionAsync());
        ApplyRetentionCommand = new RelayCommand(_ => _ = ApplyRetentionAsync());
        ClearCurrentBufferCommand = new RelayCommand(_ => _ = ClearCurrentBufferAsync());
        ClearPreviousBufferCommand = new RelayCommand(_ => _ = ClearPreviousBufferAsync());
        _session.PropertyChanged += OnSessionPropertyChanged;
        RebuildProfiles();
        RefreshStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<HistoryProfileEditor> Profiles { get; }

    public HistoryProfileEditor? SelectedProfile
    {
        get => _selectedProfile;
        set => SetField(ref _selectedProfile, value);
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand AddProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand RefreshStatusCommand { get; }
    public RelayCommand TestConnectionCommand { get; }
    public RelayCommand ApplyRetentionCommand { get; }
    public RelayCommand ClearCurrentBufferCommand { get; }
    public RelayCommand ClearPreviousBufferCommand { get; }

    public Array StorageProviders { get; } = Enum.GetValues<HistoryStorageProvider>();

    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    public bool Enabled
    {
        get => _session.WorkingProject.Historian.Enabled;
        set
        {
            if (Enabled == value)
            {
                return;
            }

            _session.WorkingProject.Historian.Enabled = value;
            _session.MarkChanged();
            OnPropertyChanged();
        }
    }

    public HistoryStorageProvider StorageProvider
    {
        get => _session.WorkingProject.Historian.StorageProvider;
        set => SetHistorianValue(
            value,
            current => current.StorageProvider,
            (current, proposed) => current.StorageProvider = proposed,
            nameof(StorageProvider));
    }

    public bool IsInfluxSelected => StorageProvider == HistoryStorageProvider.InfluxDb2;

    public string InfluxUrl
    {
        get => _session.WorkingProject.Historian.Influx.Url;
        set => SetInfluxValue(value, influx => influx.Url, (influx, proposed) => influx.Url = proposed, nameof(InfluxUrl));
    }

    public string InfluxOrganization
    {
        get => _session.WorkingProject.Historian.Influx.Organization;
        set => SetInfluxValue(value, influx => influx.Organization, (influx, proposed) => influx.Organization = proposed, nameof(InfluxOrganization));
    }

    public string InfluxBucket
    {
        get => _session.WorkingProject.Historian.Influx.Bucket;
        set => SetInfluxValue(value, influx => influx.Bucket, (influx, proposed) => influx.Bucket = proposed, nameof(InfluxBucket));
    }

    public string InfluxMeasurement
    {
        get => _session.WorkingProject.Historian.Influx.Measurement;
        set => SetInfluxValue(value, influx => influx.Measurement, (influx, proposed) => influx.Measurement = proposed, nameof(InfluxMeasurement));
    }

    public string InfluxTokenReference
    {
        get => _session.WorkingProject.Historian.Influx.TokenReference;
        set
        {
            SetInfluxValue(value, influx => influx.TokenReference, (influx, proposed) => influx.TokenReference = proposed, nameof(InfluxTokenReference));
            OnPropertyChanged(nameof(TokenStatusText));
        }
    }

    public string InfluxBufferPath
    {
        get => _session.WorkingProject.Historian.Influx.BufferPath;
        set => SetInfluxValue(value, influx => influx.BufferPath, (influx, proposed) => influx.BufferPath = proposed, nameof(InfluxBufferPath));
    }

    public int MaxBufferedSamples
    {
        get => _session.WorkingProject.Historian.Influx.MaxBufferedSamples;
        set => SetInfluxValue(value, influx => influx.MaxBufferedSamples, (influx, proposed) => influx.MaxBufferedSamples = proposed, nameof(MaxBufferedSamples));
    }

    public int SyncBatchSize
    {
        get => _session.WorkingProject.Historian.Influx.SyncBatchSize;
        set => SetInfluxValue(value, influx => influx.SyncBatchSize, (influx, proposed) => influx.SyncBatchSize = proposed, nameof(SyncBatchSize));
    }

    public int SyncIntervalMilliseconds
    {
        get => _session.WorkingProject.Historian.Influx.SyncIntervalMilliseconds;
        set => SetInfluxValue(value, influx => influx.SyncIntervalMilliseconds, (influx, proposed) => influx.SyncIntervalMilliseconds = proposed, nameof(SyncIntervalMilliseconds));
    }

    public int HealthProbeIntervalMilliseconds
    {
        get => _session.WorkingProject.Historian.Influx.HealthProbeIntervalMilliseconds;
        set => SetInfluxValue(value, influx => influx.HealthProbeIntervalMilliseconds, (influx, proposed) => influx.HealthProbeIntervalMilliseconds = proposed, nameof(HealthProbeIntervalMilliseconds));
    }

    public int ConnectionTimeoutMilliseconds
    {
        get => _session.WorkingProject.Historian.Influx.ConnectionTimeoutMilliseconds;
        set => SetInfluxValue(value, influx => influx.ConnectionTimeoutMilliseconds, (influx, proposed) => influx.ConnectionTimeoutMilliseconds = proposed, nameof(ConnectionTimeoutMilliseconds));
    }

    public int WriteTimeoutMilliseconds
    {
        get => _session.WorkingProject.Historian.Influx.WriteTimeoutMilliseconds;
        set => SetInfluxValue(value, influx => influx.WriteTimeoutMilliseconds, (influx, proposed) => influx.WriteTimeoutMilliseconds = proposed, nameof(WriteTimeoutMilliseconds));
    }

    public int QueryTimeoutMilliseconds
    {
        get => _session.WorkingProject.Historian.Influx.QueryTimeoutMilliseconds;
        set => SetInfluxValue(value, influx => influx.QueryTimeoutMilliseconds, (influx, proposed) => influx.QueryTimeoutMilliseconds = proposed, nameof(QueryTimeoutMilliseconds));
    }

    public int ReconnectInitialDelayMilliseconds
    {
        get => _session.WorkingProject.Historian.Influx.ReconnectInitialDelayMilliseconds;
        set => SetInfluxValue(value, influx => influx.ReconnectInitialDelayMilliseconds, (influx, proposed) => influx.ReconnectInitialDelayMilliseconds = proposed, nameof(ReconnectInitialDelayMilliseconds));
    }

    public int ReconnectMaxDelayMilliseconds
    {
        get => _session.WorkingProject.Historian.Influx.ReconnectMaxDelayMilliseconds;
        set => SetInfluxValue(value, influx => influx.ReconnectMaxDelayMilliseconds, (influx, proposed) => influx.ReconnectMaxDelayMilliseconds = proposed, nameof(ReconnectMaxDelayMilliseconds));
    }

    public long RetentionSeconds
    {
        get => _session.WorkingProject.Historian.Influx.RetentionSeconds;
        set => SetInfluxValue(value, influx => influx.RetentionSeconds, (influx, proposed) => influx.RetentionSeconds = proposed, nameof(RetentionSeconds));
    }

    public string PreviousDestinationFingerprint { get; set; } = string.Empty;
    public string TokenStatusText { get; private set; } = "Not checked";
    public string StoreStateText { get; private set; } = "Disabled";
    public long PendingSamples { get; private set; }
    public long OrphanedDestinationSamples { get; private set; }
    public long RemoteRejectedSamples { get; private set; }
    public long ExpiredSamples { get; private set; }
    public long BufferFullRejections { get; private set; }
    public long SyncFailures { get; private set; }
    public string LastRemoteSuccessText { get; private set; } = "Never";

    public string DatabasePath
    {
        get => _session.WorkingProject.Historian.DatabasePath;
        set
        {
            if (string.Equals(DatabasePath, value, StringComparison.Ordinal))
            {
                return;
            }

            _session.WorkingProject.Historian.DatabasePath = value;
            _session.MarkChanged();
            OnPropertyChanged();
        }
    }

    public int QueueCapacity
    {
        get => _session.WorkingProject.Historian.QueueCapacity;
        set
        {
            if (QueueCapacity == value)
            {
                return;
            }

            _session.WorkingProject.Historian.QueueCapacity = value;
            _session.MarkChanged();
            OnPropertyChanged();
        }
    }

    public int BatchSize
    {
        get => _session.WorkingProject.Historian.BatchSize;
        set
        {
            if (BatchSize == value)
            {
                return;
            }

            _session.WorkingProject.Historian.BatchSize = value;
            _session.MarkChanged();
            OnPropertyChanged();
        }
    }

    public int FlushIntervalMilliseconds
    {
        get => _session.WorkingProject.Historian.FlushIntervalMilliseconds;
        set
        {
            if (FlushIntervalMilliseconds == value)
            {
                return;
            }

            _session.WorkingProject.Historian.FlushIntervalMilliseconds = value;
            _session.MarkChanged();
            OnPropertyChanged();
        }
    }

    public int ShutdownDrainTimeoutMilliseconds
    {
        get => _session.WorkingProject.Historian.ShutdownDrainTimeoutMilliseconds;
        set
        {
            if (ShutdownDrainTimeoutMilliseconds == value)
            {
                return;
            }

            _session.WorkingProject.Historian.ShutdownDrainTimeoutMilliseconds = value;
            _session.MarkChanged();
            OnPropertyChanged();
        }
    }

    public bool IsDirty => _session.IsDirty;
    public bool RestartRequired => _session.RestartRequired;
    public string RuntimeStateText { get; private set; } = "Disabled";
    public string LastErrorText { get => _lastErrorText; private set => SetField(ref _lastErrorText, value); }
    public string LastWriteText { get => _lastWriteText; private set => SetField(ref _lastWriteText, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string SaveStatusText { get => _saveStatusText; private set => SetField(ref _saveStatusText, value); }
    public bool HasBlockingIssues => _session.HasBlockingIssues;
    public string ValidationSummaryText
    {
        get
        {
            var blockingCount = _session.ValidationIssues.Count(issue => issue.IsBlocking);
            var warningCount = _session.ValidationIssues.Count(issue => issue.Severity == ValidationSeverity.Warning);
            return blockingCount == 0 && warningCount == 0
                ? "Configuration valid."
                : $"Validation: {blockingCount} blocking issue(s), {warningCount} warning(s).";
        }
    }
    public int QueueDepth { get; private set; }
    public long EnqueuedSamples { get; private set; }
    public long WrittenSamples { get; private set; }
    public long RejectedSamples { get; private set; }
    public long DroppedSamples { get; private set; }
    public long AbandonedSamples { get; private set; }
    public long WriteFailures { get; private set; }

    public void Activate()
    {
        IsActive = true;
        RefreshStatus();
    }

    public void Deactivate() => IsActive = false;

    public void RefreshStatus()
    {
        var snapshot = _historian.Snapshot;
        RuntimeStateText = snapshot.State.ToString();
        LastErrorText = string.IsNullOrWhiteSpace(snapshot.LastErrorMessage)
            ? string.Empty
            : $"{snapshot.LastErrorCode}: {snapshot.LastErrorMessage}";
        LastWriteText = snapshot.LastWriteUtc?.ToLocalTime().ToString("G") ?? "Never";
        StatusText = snapshot.State.ToString();
        QueueDepth = snapshot.QueueDepth;
        EnqueuedSamples = snapshot.EnqueuedSamples;
        WrittenSamples = snapshot.WrittenSamples;
        RejectedSamples = snapshot.RejectedSamples;
        DroppedSamples = snapshot.DroppedSamples;
        AbandonedSamples = snapshot.AbandonedSamples;
        WriteFailures = snapshot.WriteFailures;
        TokenStatusText = GetTokenStatusText();
        if (_storeDiagnostics is not null)
        {
            var storeSnapshot = _storeDiagnostics.Snapshot;
            StoreStateText = storeSnapshot.State.ToString();
            PendingSamples = storeSnapshot.PendingSamples;
            OrphanedDestinationSamples = storeSnapshot.OrphanedDestinationSamples;
            RemoteRejectedSamples = storeSnapshot.RemoteRejectedSamples;
            ExpiredSamples = storeSnapshot.ExpiredSamples;
            BufferFullRejections = storeSnapshot.BufferFullRejections;
            SyncFailures = storeSnapshot.SyncFailures;
            LastRemoteSuccessText = storeSnapshot.LastRemoteSuccessUtc?.ToLocalTime().ToString("G") ?? "Never";
        }
        else
        {
            StoreStateText = "SQLite";
        }
        OnPropertyChanged(nameof(RuntimeStateText));
        OnPropertyChanged(nameof(QueueDepth));
        OnPropertyChanged(nameof(EnqueuedSamples));
        OnPropertyChanged(nameof(WrittenSamples));
        OnPropertyChanged(nameof(RejectedSamples));
        OnPropertyChanged(nameof(DroppedSamples));
        OnPropertyChanged(nameof(AbandonedSamples));
        OnPropertyChanged(nameof(WriteFailures));
        OnPropertyChanged(nameof(TokenStatusText));
        OnPropertyChanged(nameof(StoreStateText));
        OnPropertyChanged(nameof(PendingSamples));
        OnPropertyChanged(nameof(OrphanedDestinationSamples));
        OnPropertyChanged(nameof(RemoteRejectedSamples));
        OnPropertyChanged(nameof(ExpiredSamples));
        OnPropertyChanged(nameof(BufferFullRejections));
        OnPropertyChanged(nameof(SyncFailures));
        OnPropertyChanged(nameof(LastRemoteSuccessText));
    }

    public void AddProfile(string? requestedName = null)
    {
        var name = string.IsNullOrWhiteSpace(requestedName) ? "CustomProfile" : requestedName.Trim();
        var suffix = 1;
        while (_session.WorkingProject.Historian.Profiles.Any(profile =>
                   string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{requestedName ?? "CustomProfile"}{suffix++}";
        }

        _session.WorkingProject.Historian.Profiles.Add(new HistoryProfileDefinition
        {
            Name = name,
            Mode = HistoryMode.OnChangeAndPeriodic,
            Deadband = 0,
            MinimumIntervalMilliseconds = 1_000,
            MaximumIntervalMilliseconds = 10_000
        });
        _session.MarkChanged();
        RebuildProfiles();
    }

    public void DeleteProfile(HistoryProfileEditor profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.IsBuiltIn)
        {
            return;
        }

        var definition = _session.WorkingProject.Historian.Profiles.FirstOrDefault(candidate =>
            ReferenceEquals(candidate, profile.Definition));
        if (definition is not null)
        {
            _session.WorkingProject.Historian.Profiles.Remove(definition);
            _session.MarkChanged();
            RebuildProfiles();
        }
    }

    private void Save()
    {
        var saved = _session.TrySave();
        SaveStatusText = saved
            ? "Saved successfully."
            : !string.IsNullOrWhiteSpace(_session.LastErrorMessage)
                ? $"Save failed: {_session.LastErrorMessage}"
                : _session.HasBlockingIssues
                    ? "Save blocked by validation errors."
                    : "Save failed.";
        RebuildProfiles();
        NotifySessionState();
    }

    private void Revert()
    {
        _session.Revert();
        SaveStatusText = "Changes reverted.";
        RebuildProfiles();
        NotifySessionState();
    }

    private void RebuildProfiles()
    {
        Profiles.Clear();
        foreach (var definition in _session.WorkingProject.Historian.Profiles)
        {
            Profiles.Add(new HistoryProfileEditor(definition, MarkChanged,
                proposedName => ValidateProfileRename(definition, proposedName)));
        }
    }

    private string? ValidateProfileRename(HistoryProfileDefinition definition, string proposedName)
    {
        if (string.IsNullOrWhiteSpace(proposedName))
        {
            return "Profile name is required.";
        }

        if (HistoryProfileDefaults.RequiredNames.Contains(proposedName, StringComparer.OrdinalIgnoreCase))
        {
            return "Built-in profile names are reserved for built-in profiles.";
        }

        if (_session.WorkingProject.Historian.Profiles.Any(profile =>
                !ReferenceEquals(profile, definition) &&
                string.Equals(profile.Name, proposedName, StringComparison.OrdinalIgnoreCase)))
        {
            return "Another configured profile already uses this name.";
        }

        return null;
    }

    private void MarkChanged()
    {
        _session.MarkChanged();
        NotifySessionState();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProjectEditSession.WorkingProject) or nameof(ProjectEditSession.SavedProject))
        {
            RebuildProfiles();
        }

        NotifySessionState();
    }

    private void NotifySessionState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(RestartRequired));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(StorageProvider));
        OnPropertyChanged(nameof(IsInfluxSelected));
        OnPropertyChanged(nameof(DatabasePath));
        OnPropertyChanged(nameof(InfluxUrl));
        OnPropertyChanged(nameof(InfluxOrganization));
        OnPropertyChanged(nameof(InfluxBucket));
        OnPropertyChanged(nameof(InfluxMeasurement));
        OnPropertyChanged(nameof(InfluxTokenReference));
        OnPropertyChanged(nameof(InfluxBufferPath));
        OnPropertyChanged(nameof(MaxBufferedSamples));
        OnPropertyChanged(nameof(SyncBatchSize));
        OnPropertyChanged(nameof(SyncIntervalMilliseconds));
        OnPropertyChanged(nameof(HealthProbeIntervalMilliseconds));
        OnPropertyChanged(nameof(ConnectionTimeoutMilliseconds));
        OnPropertyChanged(nameof(WriteTimeoutMilliseconds));
        OnPropertyChanged(nameof(QueryTimeoutMilliseconds));
        OnPropertyChanged(nameof(ReconnectInitialDelayMilliseconds));
        OnPropertyChanged(nameof(ReconnectMaxDelayMilliseconds));
        OnPropertyChanged(nameof(RetentionSeconds));
        OnPropertyChanged(nameof(TokenStatusText));
        OnPropertyChanged(nameof(QueueCapacity));
        OnPropertyChanged(nameof(BatchSize));
        OnPropertyChanged(nameof(FlushIntervalMilliseconds));
        OnPropertyChanged(nameof(ShutdownDrainTimeoutMilliseconds));
        OnPropertyChanged(nameof(HasBlockingIssues));
        OnPropertyChanged(nameof(ValidationSummaryText));
    }

    private void SetHistorianValue<T>(
        T value,
        Func<HistorianOptions, T> get,
        Action<HistorianOptions, T> set,
        string propertyName)
    {
        var historian = _session.WorkingProject.Historian;
        if (EqualityComparer<T>.Default.Equals(get(historian), value))
        {
            return;
        }

        set(historian, value);
        _session.MarkChanged();
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(StorageProvider))
        {
            OnPropertyChanged(nameof(IsInfluxSelected));
        }
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(RestartRequired));
        OnPropertyChanged(nameof(HasBlockingIssues));
        OnPropertyChanged(nameof(ValidationSummaryText));
    }

    private void SetInfluxValue<T>(
        T value,
        Func<InfluxDbOptions, T> get,
        Action<InfluxDbOptions, T> set,
        string propertyName)
    {
        var influx = _session.WorkingProject.Historian.Influx;
        if (EqualityComparer<T>.Default.Equals(get(influx), value))
        {
            return;
        }

        set(influx, value);
        _session.MarkChanged();
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(RestartRequired));
        OnPropertyChanged(nameof(HasBlockingIssues));
        OnPropertyChanged(nameof(ValidationSummaryText));
    }

    private async Task TestConnectionAsync()
    {
        if (!IsInfluxSelected)
        {
            SaveStatusText = "Select InfluxDB 2.x before testing the connection.";
            OnPropertyChanged(nameof(SaveStatusText));
            return;
        }

        var result = _connectionTester is not null
            ? await _connectionTester.TestAsync(
                    ProjectSnapshotCloner.Clone(_session.WorkingProject).Historian.Influx,
                    CancellationToken.None)
                .ConfigureAwait(false)
            : _storeDiagnostics is not null
                ? await _storeDiagnostics.ProbeAsync(CancellationToken.None).ConfigureAwait(false)
                : new HistoryStoreOperationResult(false, "INFLUX_TEST_UNAVAILABLE", "No connection tester is registered.");
        SaveStatusText = result.Succeeded
            ? "Connection and bucket access verified. Write permission will be exercised by normal historian operation."
            : $"Connection test failed: {result.ErrorCode}.";
        RefreshStatus();
        OnPropertyChanged(nameof(SaveStatusText));
    }

    private async Task ApplyRetentionAsync()
    {
        if (_storeMaintenance is null)
        {
            SaveStatusText = "Retention management is available when InfluxDB is the active provider.";
        }
        else
        {
            var result = await _storeMaintenance.ApplyRetentionAsync(CancellationToken.None).ConfigureAwait(false);
            SaveStatusText = result.Succeeded
                ? "Retention applied."
                : $"Retention apply failed: {result.ErrorCode}.";
        }

        RefreshStatus();
        OnPropertyChanged(nameof(SaveStatusText));
    }

    private async Task ClearCurrentBufferAsync()
    {
        if (_storeMaintenance is null || _bufferConfirmation is null)
        {
            return;
        }

        if (!_bufferConfirmation.Confirm("Clear current destination buffer", PendingSamples - OrphanedDestinationSamples))
        {
            return;
        }

        var result = await _storeMaintenance.ClearCurrentBufferAsync(CancellationToken.None).ConfigureAwait(false);
        SaveStatusText = result.Succeeded ? "Current destination buffer cleared." : $"Clear failed: {result.ErrorCode}.";
        RefreshStatus();
        OnPropertyChanged(nameof(SaveStatusText));
    }

    private async Task ClearPreviousBufferAsync()
    {
        if (_storeMaintenance is null || _bufferConfirmation is null || string.IsNullOrWhiteSpace(PreviousDestinationFingerprint))
        {
            SaveStatusText = "Enter a previous destination fingerprint first.";
            OnPropertyChanged(nameof(SaveStatusText));
            return;
        }

        if (!_bufferConfirmation.Confirm("Clear previous destination buffer", OrphanedDestinationSamples))
        {
            return;
        }

        var result = await _storeMaintenance.ClearPreviousDestinationBufferAsync(
                PreviousDestinationFingerprint,
                CancellationToken.None)
            .ConfigureAwait(false);
        SaveStatusText = result.Succeeded ? "Previous destination buffer cleared." : $"Clear failed: {result.ErrorCode}.";
        RefreshStatus();
        OnPropertyChanged(nameof(SaveStatusText));
    }

    private string GetTokenStatusText()
    {
        var reference = InfluxTokenReference;
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("env:", StringComparison.Ordinal))
        {
            return "Invalid token reference";
        }

        var variable = reference[4..];
        if (string.IsNullOrWhiteSpace(variable))
        {
            return "Missing token";
        }

        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))
            ? "Missing token"
            : "Token configured";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class HistoryProfileEditor : INotifyPropertyChanged
{
    private readonly Action _changed;
    private readonly Func<string, string?> _validateRename;
    private string? _renameValidationMessage;

    public HistoryProfileEditor(
        HistoryProfileDefinition definition,
        Action changed,
        Func<string, string?>? validateRename = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _validateRename = validateRename ?? (_ => null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HistoryProfileDefinition Definition { get; }

    public bool IsBuiltIn => HistoryProfileDefaults.RequiredNames.Contains(Definition.Name, StringComparer.OrdinalIgnoreCase);

    public string? RenameValidationMessage
    {
        get => _renameValidationMessage;
        private set
        {
            if (string.Equals(_renameValidationMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _renameValidationMessage = value;
            OnPropertyChanged();
        }
    }

    public string Name
    {
        get => Definition.Name;
        set
        {
            var proposedName = value?.Trim() ?? string.Empty;
            if (string.Equals(Name, proposedName, StringComparison.Ordinal))
            {
                RenameValidationMessage = null;
                return;
            }

            if (IsBuiltIn)
            {
                RenameValidationMessage = "Built-in profiles cannot be renamed.";
                return;
            }

            var validationMessage = _validateRename(proposedName);
            if (validationMessage is not null)
            {
                RenameValidationMessage = validationMessage;
                return;
            }

            Definition.Name = proposedName;
            RenameValidationMessage = null;
            _changed();
            OnPropertyChanged();
        }
    }

    public HistoryMode Mode
    {
        get => Definition.Mode;
        set
        {
            if (Definition.Mode == value)
            {
                return;
            }

            Definition.Mode = value;
            _changed();
            OnPropertyChanged();
        }
    }

    public double Deadband
    {
        get => Definition.Deadband;
        set
        {
            if (Definition.Deadband.Equals(value))
            {
                return;
            }

            Definition.Deadband = value;
            _changed();
            OnPropertyChanged();
        }
    }

    public int MinimumIntervalMilliseconds
    {
        get => Definition.MinimumIntervalMilliseconds;
        set
        {
            if (Definition.MinimumIntervalMilliseconds == value)
            {
                return;
            }

            Definition.MinimumIntervalMilliseconds = value;
            _changed();
            OnPropertyChanged();
        }
    }

    public int MaximumIntervalMilliseconds
    {
        get => Definition.MaximumIntervalMilliseconds;
        set
        {
            if (Definition.MaximumIntervalMilliseconds == value)
            {
                return;
            }

            Definition.MaximumIntervalMilliseconds = value;
            _changed();
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
