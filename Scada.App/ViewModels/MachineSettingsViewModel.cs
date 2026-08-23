using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Scada.App.Hmi;
using Scada.App.Services;
using Scada.Core.MachineSettings;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.App.ViewModels;

public sealed class MachineSettingsViewModel : IWorkspaceLifecycle, IDisposable, INotifyPropertyChanged
{
    private readonly ProjectEditSession _session;
    private readonly ITagCache _cache;
    private readonly IMachineSettingsDispatcher _dispatcher;
    private MachineSettingsPageViewModel? _selectedPage;
    private bool _active;
    private bool _disposed;
    private bool _showHiddenConfiguration;
    private bool _discardDraftsOnWorkingProjectChange;

    public MachineSettingsViewModel(ProjectEditSession session, ITagCache cache, IMachineSettingsDispatcher? dispatcher = null)
    {
        _session = session; _cache = cache; _dispatcher = dispatcher ?? new WpfMachineSettingsDispatcher();
        Pages = []; ApplyPageCommand = new RelayCommand(_ => SelectedPage?.Apply()); RevertPageCommand = new RelayCommand(_ => SelectedPage?.RevertDrafts()); SaveProjectCommand = new RelayCommand(_ => _session.TrySave()); RevertProjectCommand = new RelayCommand(_ => RevertSavedProject());
        _session.PropertyChanged += OnSessionChanged; RebuildPages(preserveDrafts: false);
    }
    public MachineSettingsViewModel() : this(new ProjectEditSession(new RuntimeOptions(), null, null), new TagCache()) { }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<MachineSettingsPageViewModel> Pages { get; }
    public ObservableCollection<MachineSettingsPageGroupViewModel> PageGroups { get; } = [];
    public RelayCommand ApplyPageCommand { get; }
    public RelayCommand RevertPageCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand RevertProjectCommand { get; }
    public bool IsDirty => _session.IsDirty;
    public bool RestartRequired => _session.RestartRequired;
    public IReadOnlyList<ValidationIssue> ValidationIssues => _session.ValidationIssues;
    public bool HasBlockingIssues => _session.HasBlockingIssues;
    public string? LastErrorMessage => _session.LastErrorMessage;
    public bool ShowHiddenConfiguration { get => _showHiddenConfiguration; set { if (_showHiddenConfiguration == value) return; _showHiddenConfiguration = value; RebuildPages(preserveDrafts: true); OnPropertyChanged(); } }
    public MachineSettingsPageViewModel? SelectedPage { get => _selectedPage; set { if (ReferenceEquals(_selectedPage, value)) return; _selectedPage?.Deactivate(); _selectedPage = value; if (_active) _selectedPage?.Activate(); OnPropertyChanged(); } }
    public void Activate() { if (_disposed || _active) return; _active = true; SelectedPage?.Activate(); }
    public void Deactivate() { if (!_active) return; _active = false; SelectedPage?.Deactivate(); }
    public void Dispose() { if (_disposed) return; _disposed = true; Deactivate(); _session.PropertyChanged -= OnSessionChanged; foreach (var page in Pages) page.Dispose(); }
    private void RevertSavedProject()
    {
        _discardDraftsOnWorkingProjectChange = true;
        try { _session.Revert(); }
        finally { _discardDraftsOnWorkingProjectChange = false; }
    }
    private void RebuildPages(bool preserveDrafts)
    {
        var selectedId = SelectedPage?.Id;
        var drafts = preserveDrafts
            ? Pages.SelectMany(page => page.Editors.Select(editor => (Key: page.Id + "/" + editor.Id, editor.EditValueText))).ToDictionary(item => item.Key, item => item.EditValueText, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in Pages) page.Dispose(); Pages.Clear(); PageGroups.Clear();
        foreach (var page in _session.WorkingProject.MachineSettings.Pages.Where(page => page.IsVisible || ShowHiddenConfiguration).OrderBy(page => page.Group, StringComparer.Ordinal).ThenBy(page => page.Order).ThenBy(page => page.Id, StringComparer.Ordinal)) Pages.Add(new MachineSettingsPageViewModel(page, _session, _cache, _dispatcher, ShowHiddenConfiguration));
        foreach (var group in Pages.GroupBy(page => page.Group ?? string.Empty, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)) PageGroups.Add(new MachineSettingsPageGroupViewModel(group.Key, group));
        foreach (var page in Pages) foreach (var editor in page.Editors) if (drafts.TryGetValue(page.Id + "/" + editor.Id, out var draft)) editor.EditValueText = draft;
        SelectedPage = Pages.FirstOrDefault(page => string.Equals(page.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? Pages.FirstOrDefault();
    }
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(ProjectEditSession.WorkingProject)) RebuildPages(preserveDrafts: !_discardDraftsOnWorkingProjectChange); OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(RestartRequired)); OnPropertyChanged(nameof(ValidationIssues)); OnPropertyChanged(nameof(HasBlockingIssues)); OnPropertyChanged(nameof(LastErrorMessage)); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public interface IMachineSettingsDispatcher { void Post(Action action); }
public sealed class WpfMachineSettingsDispatcher : IMachineSettingsDispatcher { private readonly IHmiDispatcher _inner = new WpfHmiDispatcher(); public void Post(Action action) => _inner.Post(action); }

public sealed class MachineSettingsPageGroupViewModel(string name, IEnumerable<MachineSettingsPageViewModel> pages)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? "General" : name;
    public ReadOnlyCollection<MachineSettingsPageViewModel> Pages { get; } = new(pages.ToList());
}

public sealed class MachineSettingsPageViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MachineSettingsPageDefinition _definition; private readonly ProjectEditSession _session; private readonly ITagCache _cache; private readonly IMachineSettingsDispatcher _dispatcher;
    private readonly object _sync = new(); private readonly List<IDisposable> _subscriptions = []; private bool _active; private bool _disposed; private long _generation; private readonly Dictionary<string,long> _sequences = new(StringComparer.OrdinalIgnoreCase);
    public MachineSettingsPageViewModel(MachineSettingsPageDefinition definition, ProjectEditSession session, ITagCache cache, IMachineSettingsDispatcher dispatcher, bool showHidden = false)
    {
        _definition = definition; _session = session; _cache = cache; _dispatcher = dispatcher;
        Editors = new ObservableCollection<ParameterEditorViewModel>((definition.Parameters ?? []).OrderBy(p => p.Order).ThenBy(p => p.Id, StringComparer.Ordinal).Select(p => new ParameterEditorViewModel(p, showHidden)));
        Groups = new ObservableCollection<ParameterGroupViewModel>(Editors.Where(e => e.IsVisible).GroupBy(e => e.Definition.Group ?? string.Empty, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new ParameterGroupViewModel(g.Key, g)));
        PresentationRows = new ReadOnlyCollection<object>(Groups.SelectMany(group => new object[] { group }.Concat(group.Editors)).ToList());
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id => _definition.Id; public string Title => _definition.Title; public string Description => _definition.Description; public string Group => _definition.Group;
    public ObservableCollection<ParameterEditorViewModel> Editors { get; }
    public ObservableCollection<ParameterGroupViewModel> Groups { get; }
    public ReadOnlyCollection<object> PresentationRows { get; }
    public string ApplyStatus { get; private set; } = string.Empty;
    public void Activate()
    {
        long generation; lock (_sync) { if (_disposed || _active) return; _active = true; generation = ++_generation; }
        var enabledTags = _definition.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.LiveTagId) && _session.WorkingProject.Tags.Any(t => t.Enabled && string.Equals(t.Id, p.LiveTagId, StringComparison.OrdinalIgnoreCase))).Select(p => p.LiveTagId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var tagId in enabledTags) { var subscription = _cache.Subscribe(tagId, value => Receive(value, generation)); lock (_sync) { if (_active && !_disposed && _generation == generation) _subscriptions.Add(subscription); else { subscription.Dispose(); return; } } if (_cache.TryGet(tagId, out var value) && value is not null) Receive(value, generation); }
    }
    public void Deactivate() { IDisposable[] subscriptions; lock (_sync) { _active = false; ++_generation; subscriptions = _subscriptions.ToArray(); _subscriptions.Clear(); } foreach (var subscription in subscriptions) subscription.Dispose(); }
    public bool Apply()
    {
        var candidates = new List<(MachineParameterDefinition Definition,string Value)>();
        foreach (var editor in Editors.Where(editor => editor.IsVisible && !editor.IsReadOnly)) { if (!editor.TryGetNormalized(out var normalized)) { ApplyStatus = "Correct parameter errors before applying."; OnPropertyChanged(nameof(ApplyStatus)); return false; } candidates.Add((editor.Definition, normalized)); }
        foreach (var candidate in candidates) candidate.Definition.Value = candidate.Value;
        if (candidates.Count > 0) _session.MarkChanged();
        ApplyStatus = candidates.Count == 0 ? "No editable parameters." : "Page draft applied. Save project when ready."; OnPropertyChanged(nameof(ApplyStatus)); return true;
    }
    public void RevertDrafts() { foreach (var editor in Editors) editor.ResetDraft(); ApplyStatus = "Page drafts reverted."; OnPropertyChanged(nameof(ApplyStatus)); }
    public void Dispose() { lock (_sync) { if (_disposed) return; _disposed = true; } Deactivate(); }
    private void Receive(TagValue value, long generation)
    { lock (_sync) { if (!_active || _disposed || generation != _generation || (_sequences.TryGetValue(value.TagId, out var current) && value.Sequence < current)) return; _sequences[value.TagId] = value.Sequence; }
      _dispatcher.Post(() => { lock (_sync) { if (!_active || _disposed || generation != _generation) return; } foreach (var editor in Editors.Where(e => string.Equals(e.LiveTagId, value.TagId, StringComparison.OrdinalIgnoreCase))) editor.SetLiveValue(value); }); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ParameterEditorViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private string _editValueText; private readonly Dictionary<string,List<string>> _errors = []; private TagValue? _liveValue;
    public ParameterEditorViewModel(MachineParameterDefinition definition, bool showHidden = false) { Definition = definition; IsVisible = definition.IsVisible || showHidden; _editValueText = MachineParameterValueCodec.FormatForEditor(definition.ValueType, definition.Value, CultureInfo.CurrentCulture); Validate(); }
    public event PropertyChangedEventHandler? PropertyChanged; public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public MachineParameterDefinition Definition { get; } public string Id => Definition.Id; public string Name => Definition.Name; public string Description => Definition.Description; public string Unit => Definition.Unit; public string LiveTagId => Definition.LiveTagId; public bool IsReadOnly => Definition.IsReadOnly; public bool IsVisible { get; } public MachineParameterValueType ValueType => Definition.ValueType;
    public string EditValueText { get => _editValueText; set { if (_editValueText == value) return; _editValueText = value; Validate(); OnPropertyChanged(); } }
    public bool IsBoolean => ValueType == MachineParameterValueType.Boolean;
    public bool IsInteger => ValueType == MachineParameterValueType.Integer;
    public bool IsDecimal => ValueType == MachineParameterValueType.Decimal;
    public bool IsString => ValueType == MachineParameterValueType.String;
    public bool IsNonBoolean => !IsBoolean;
    public bool IsEditable => !IsReadOnly;
    public bool BooleanValue { get => string.Equals(EditValueText, "true", StringComparison.Ordinal); set { if (!IsBoolean) return; EditValueText = value ? "true" : "false"; OnPropertyChanged(); } }
    public object? LiveValue => _liveValue?.Value; public TagQuality? LiveQuality => _liveValue?.Quality; public DateTimeOffset? LiveTimestamp => _liveValue?.Timestamp; public bool HasErrors => _errors.Count > 0;
    public string ErrorMessage => _errors.TryGetValue(nameof(EditValueText), out var errors) ? string.Join(" ", errors) : string.Empty;
    public IEnumerable GetErrors(string? propertyName) => _errors.TryGetValue(propertyName ?? nameof(EditValueText), out var errors) ? errors : [];
    public bool TryGetNormalized(out string normalized) { Validate(); if (HasErrors) { normalized = string.Empty; return false; } return MachineParameterValueCodec.TryNormalizeEditor(ValueType, EditValueText, CultureInfo.CurrentCulture, out normalized); }
    public void ResetDraft() { _editValueText = MachineParameterValueCodec.FormatForEditor(ValueType, Definition.Value, CultureInfo.CurrentCulture); Validate(); OnPropertyChanged(nameof(EditValueText)); OnPropertyChanged(nameof(BooleanValue)); }
    public void SetLiveValue(TagValue value) { _liveValue = value; OnPropertyChanged(nameof(LiveValue)); OnPropertyChanged(nameof(LiveQuality)); OnPropertyChanged(nameof(LiveTimestamp)); }
    private void Validate() { _errors.Clear(); if (!MachineParameterValueCodec.TryNormalizeEditor(ValueType, EditValueText, CultureInfo.CurrentCulture, out var normalized)) Add("Enter a valid value."); else if (MachineParameterValueCodec.TryGetNumeric(ValueType, normalized, out var numeric) && ((Definition.Min.HasValue && numeric < Definition.Min) || (Definition.Max.HasValue && numeric > Definition.Max))) Add("Value is outside configured bounds."); ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(EditValueText))); OnPropertyChanged(nameof(HasErrors)); OnPropertyChanged(nameof(ErrorMessage)); }
    private void Add(string message) => _errors[nameof(EditValueText)] = [message]; private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ParameterGroupViewModel(string name, IEnumerable<ParameterEditorViewModel> editors)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? "General" : name;
    public ReadOnlyCollection<ParameterEditorViewModel> Editors { get; } = new(editors.ToList());
}
