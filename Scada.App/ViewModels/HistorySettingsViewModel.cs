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
    private bool _isActive;
    private string _statusText = "Not started";
    private string _lastErrorText = string.Empty;
    private string _lastWriteText = "Never";
    private string _saveStatusText = string.Empty;
    private HistoryProfileEditor? _selectedProfile;

    public HistorySettingsViewModel(ProjectEditSession session, HistorianRuntimeService historian)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _historian = historian ?? throw new ArgumentNullException(nameof(historian));
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
        OnPropertyChanged(nameof(RuntimeStateText));
        OnPropertyChanged(nameof(QueueDepth));
        OnPropertyChanged(nameof(EnqueuedSamples));
        OnPropertyChanged(nameof(WrittenSamples));
        OnPropertyChanged(nameof(RejectedSamples));
        OnPropertyChanged(nameof(DroppedSamples));
        OnPropertyChanged(nameof(AbandonedSamples));
        OnPropertyChanged(nameof(WriteFailures));
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
        OnPropertyChanged(nameof(DatabasePath));
        OnPropertyChanged(nameof(QueueCapacity));
        OnPropertyChanged(nameof(BatchSize));
        OnPropertyChanged(nameof(FlushIntervalMilliseconds));
        OnPropertyChanged(nameof(ShutdownDrainTimeoutMilliseconds));
        OnPropertyChanged(nameof(HasBlockingIssues));
        OnPropertyChanged(nameof(ValidationSummaryText));
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
