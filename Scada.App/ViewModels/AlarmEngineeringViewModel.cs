using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using Scada.App.Services;
using Scada.Core.Alarms;
using Scada.Core.Configuration;

namespace Scada.App.ViewModels;

public sealed class AlarmEngineeringViewModel : INotifyPropertyChanged, IWorkspaceLifecycle, IDisposable
{
    private readonly ProjectEditSession _session;
    private AlarmDefinitionEditorViewModel? _selectedDefinition;
    private string _searchText = string.Empty;
    private string _ruleFilter = "All rules";
    private string _severityFilter = "All severities";
    private string _enabledFilter = "All states";

    public AlarmEngineeringViewModel(ProjectEditSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Definitions = [];
        DefinitionsView = CollectionViewSource.GetDefaultView(Definitions);
        DefinitionsView.Filter = FilterDefinition;
        AddCommand = new RelayCommand(_ => Add());
        DuplicateCommand = new RelayCommand(_ => Duplicate(), _ => SelectedDefinition is not null);
        DeleteCommand = new RelayCommand(_ => Delete(), _ => SelectedDefinition is not null);
        SaveCommand = new RelayCommand(_ => Save(), _ => !_session.HasBlockingIssues && _session.CanonicalProjectPath is not null);
        RevertCommand = new RelayCommand(_ => _session.Revert());
        _session.PropertyChanged += OnSessionChanged;
        Rebuild();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<AlarmDefinitionEditorViewModel> Definitions { get; }
    public ICollectionView DefinitionsView { get; }
    public IReadOnlyList<AlarmRuleType> RuleTypes { get; } = Enum.GetValues<AlarmRuleType>();
    public IReadOnlyList<AlarmSeverity> Severities { get; } = Enum.GetValues<AlarmSeverity>();
    public IReadOnlyList<string> RuleFilters { get; } = ["All rules", .. Enum.GetNames<AlarmRuleType>()];
    public IReadOnlyList<string> SeverityFilters { get; } = ["All severities", .. Enum.GetNames<AlarmSeverity>()];
    public IReadOnlyList<string> EnabledFilters { get; } = ["All states", "Enabled", "Disabled"];
    public IReadOnlyList<ValidationIssue> ValidationIssues => _session.ValidationIssues;
    public bool HasBlockingIssues => _session.HasBlockingIssues;
    public bool IsDirty => _session.IsDirty;
    public bool RestartRequired => _session.RestartRequired;
    public string? LastErrorMessage => _session.LastErrorMessage;
    public IReadOnlyList<string> TagIds => _session.WorkingProject.Tags.Select(tag => tag.Id).ToArray();
    public ICommand AddCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }
    public bool IsActive { get; private set; }

    public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value ?? string.Empty; OnPropertyChanged(); DefinitionsView.Refresh(); } }
    public string RuleFilter { get => _ruleFilter; set { if (_ruleFilter == value) return; _ruleFilter = value; OnPropertyChanged(); DefinitionsView.Refresh(); } }
    public string SeverityFilter { get => _severityFilter; set { if (_severityFilter == value) return; _severityFilter = value; OnPropertyChanged(); DefinitionsView.Refresh(); } }
    public string EnabledFilter { get => _enabledFilter; set { if (_enabledFilter == value) return; _enabledFilter = value; OnPropertyChanged(); DefinitionsView.Refresh(); } }

    public AlarmDefinitionEditorViewModel? SelectedDefinition
    {
        get => _selectedDefinition;
        set
        {
            if (ReferenceEquals(_selectedDefinition, value)) return;
            _selectedDefinition = value;
            OnPropertyChanged();
            (DuplicateCommand as RelayCommand)?.Refresh();
            (DeleteCommand as RelayCommand)?.Refresh();
        }
    }

    public bool Enabled
    {
        get => _session.WorkingProject.Alarms.Enabled;
        set { if (_session.WorkingProject.Alarms.Enabled == value) return; _session.WorkingProject.Alarms.Enabled = value; MarkChanged(); }
    }

    public bool PersistenceEnabled
    {
        get => _session.WorkingProject.Alarms.PersistenceEnabled;
        set { if (_session.WorkingProject.Alarms.PersistenceEnabled == value) return; _session.WorkingProject.Alarms.PersistenceEnabled = value; MarkChanged(); }
    }

    public string DatabasePath
    {
        get => _session.WorkingProject.Alarms.DatabasePath;
        set { if (_session.WorkingProject.Alarms.DatabasePath == value) return; _session.WorkingProject.Alarms.DatabasePath = value; MarkChanged(); }
    }

    public void Activate() { IsActive = true; OnPropertyChanged(nameof(IsActive)); }
    public void Deactivate() { IsActive = false; OnPropertyChanged(nameof(IsActive)); }

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionChanged;
    }

    private void Add()
    {
        var id = NextId();
        var definition = new AlarmDefinition { Id = id, Name = id, TagId = TagIds.FirstOrDefault() ?? string.Empty, RuleType = AlarmRuleType.High, Threshold = 0 };
        _session.WorkingProject.Alarms.Definitions.Add(definition);
        var editor = new AlarmDefinitionEditorViewModel(definition, MarkChanged);
        Definitions.Add(editor);
        DefinitionsView.Refresh();
        SelectedDefinition = editor;
        MarkChanged();
    }

    private void Duplicate()
    {
        if (SelectedDefinition is null) return;
        var source = SelectedDefinition.Definition;
        var copy = new AlarmDefinition
        {
            Id = NextId(), Name = source.Name + " Copy", Message = source.Message, TagId = source.TagId,
            Enabled = source.Enabled, Order = source.Order, RuleType = source.RuleType, Severity = source.Severity,
            DigitalExpectedValue = source.DigitalExpectedValue, Threshold = source.Threshold, Deadband = source.Deadband,
            ActivationDelay = source.ActivationDelay, AcknowledgementRequired = source.AcknowledgementRequired
        };
        _session.WorkingProject.Alarms.Definitions.Add(copy);
        var editor = new AlarmDefinitionEditorViewModel(copy, MarkChanged);
        Definitions.Add(editor);
        DefinitionsView.Refresh();
        SelectedDefinition = editor;
        MarkChanged();
    }

    private void Delete()
    {
        if (SelectedDefinition is null) return;
        _session.WorkingProject.Alarms.Definitions.Remove(SelectedDefinition.Definition);
        Definitions.Remove(SelectedDefinition);
        DefinitionsView.Refresh();
        SelectedDefinition = Definitions.FirstOrDefault();
        DefinitionsView.Refresh();
        MarkChanged();
    }

    private void Save()
    {
        _session.TrySave();
        RaiseSessionProperties();
    }

    private void Rebuild()
    {
        Definitions.Clear();
        foreach (var definition in _session.WorkingProject.Alarms.Definitions)
            Definitions.Add(new AlarmDefinitionEditorViewModel(definition, MarkChanged));
        SelectedDefinition = Definitions.FirstOrDefault();
        OnPropertyChanged(nameof(TagIds));
        RaiseSessionProperties();
    }

    private string NextId()
    {
        var ids = _session.WorkingProject.Alarms.Definitions.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"Alarm{index}";
            if (!ids.Contains(candidate)) return candidate;
        }
    }

    private bool FilterDefinition(object item)
    {
        if (item is not AlarmDefinitionEditorViewModel definition) return false;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !definition.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
            !definition.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
            !definition.TagId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) return false;
        if (Enum.TryParse<AlarmRuleType>(RuleFilter, out var rule) && definition.RuleType != rule) return false;
        if (Enum.TryParse<AlarmSeverity>(SeverityFilter, out var severity) && definition.Severity != severity) return false;
        if (EnabledFilter == "Enabled" && !definition.Enabled) return false;
        if (EnabledFilter == "Disabled" && definition.Enabled) return false;
        return true;
    }

    private void MarkChanged()
    {
        _session.MarkChanged();
        RaiseSessionProperties();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProjectEditSession.WorkingProject)) Rebuild();
        else RaiseSessionProperties();
    }

    private void RaiseSessionProperties()
    {
        OnPropertyChanged(nameof(ValidationIssues));
        OnPropertyChanged(nameof(HasBlockingIssues));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(RestartRequired));
        OnPropertyChanged(nameof(LastErrorMessage));
        (SaveCommand as RelayCommand)?.Refresh();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class AlarmDefinitionEditorViewModel(AlarmDefinition definition, Action changed) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    internal AlarmDefinition Definition => definition;
    public string Id { get => definition.Id; set => Set(definition.Id, value, item => definition.Id = item); }
    public string Name { get => definition.Name; set => Set(definition.Name, value, item => definition.Name = item); }
    public string Message { get => definition.Message; set => Set(definition.Message, value, item => definition.Message = item); }
    public string TagId { get => definition.TagId; set => Set(definition.TagId, value, item => definition.TagId = item); }
    public bool Enabled { get => definition.Enabled; set => Set(definition.Enabled, value, item => definition.Enabled = item); }
    public int Order { get => definition.Order; set => Set(definition.Order, value, item => definition.Order = item); }
    public AlarmRuleType RuleType { get => definition.RuleType; set => Set(definition.RuleType, value, item => definition.RuleType = item); }
    public AlarmSeverity Severity { get => definition.Severity; set => Set(definition.Severity, value, item => definition.Severity = item); }
    public bool? DigitalExpectedValue { get => definition.DigitalExpectedValue; set => Set(definition.DigitalExpectedValue, value, item => definition.DigitalExpectedValue = item); }
    public double? Threshold { get => definition.Threshold; set => Set(definition.Threshold, value, item => definition.Threshold = item); }
    public double Deadband { get => definition.Deadband; set => Set(definition.Deadband, value, item => definition.Deadband = item); }
    public TimeSpan ActivationDelay { get => definition.ActivationDelay; set => Set(definition.ActivationDelay, value, item => definition.ActivationDelay = item); }
    public bool AcknowledgementRequired { get => definition.AcknowledgementRequired; set => Set(definition.AcknowledgementRequired, value, item => definition.AcknowledgementRequired = item); }

    private void Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        changed();
    }
}
