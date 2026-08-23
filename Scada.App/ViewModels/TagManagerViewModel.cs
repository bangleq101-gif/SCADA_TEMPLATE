using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Scada.App.Services;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.App.ViewModels;

public sealed class TagManagerViewModel : INotifyPropertyChanged, IWorkspaceLifecycle, IDisposable
{
    private readonly ProjectEditSession _session;
    private readonly ITagCache _cache;
    private readonly IClipboardAdapter _clipboard;
    private readonly ITagImportDecisionService _importDecision;
    private readonly IDeleteConfirmation _deleteConfirmation;
    private readonly object _lifecycleSync = new();
    private readonly Dictionary<string, TagEditorRowViewModel> _rowsById = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _selectedSubscription;
    private long _selectionGeneration;
    private bool _active;
    private bool _disposed;
    private TagEditorRowViewModel? _selectedRow;
    private string _searchText = string.Empty;
    private string _enabledFilter = "All";
    private string _deviceFilter = "All";
    private string _dataTypeFilter = "All";
    private string _scanGroupFilter = "All";
    private bool _validationOnly;
    private string _statusText = "Ready";

    public TagManagerViewModel(
        ProjectEditSession session,
        ITagCache cache,
        IClipboardAdapter clipboard,
        ITagImportDecisionService importDecision,
        IDeleteConfirmation deleteConfirmation)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _importDecision = importDecision ?? throw new ArgumentNullException(nameof(importDecision));
        _deleteConfirmation = deleteConfirmation ?? throw new ArgumentNullException(nameof(deleteConfirmation));

        Rows = [];
        SelectedRows = [];
        ItemsView = CollectionViewSource.GetDefaultView(Rows);
        ItemsView.Filter = FilterRow;
        AddCommand = new RelayCommand(_ => AddTag());
        DuplicateCommand = new RelayCommand(_ => DuplicateSelected());
        DeleteCommand = new RelayCommand(_ => DeleteSelected());
        SaveCommand = new RelayCommand(_ => Save());
        RevertCommand = new RelayCommand(_ => Revert());
        CopyCommand = new RelayCommand(_ => CopySelected());
        PasteCommand = new RelayCommand(_ => PasteClipboard());
        RefreshQualityCommand = new RelayCommand(_ => RefreshQualitySnapshot());
        EnableSelectedCommand = new RelayCommand(_ => BulkSetEnabled(true));
        DisableSelectedCommand = new RelayCommand(_ => BulkSetEnabled(false));
        EnableHistoryCommand = new RelayCommand(_ => BulkSetHistoryEnabled(true));
        DisableHistoryCommand = new RelayCommand(_ => BulkSetHistoryEnabled(false));
        ApplyBulkEditCommand = new RelayCommand(_ => ApplyBulkEdit());

        _session.PropertyChanged += OnSessionPropertyChanged;
        BuildRows();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TagEditorRowViewModel> Rows { get; }

    public ObservableCollection<TagEditorRowViewModel> SelectedRows { get; }

    public ICollectionView ItemsView { get; }

    public IReadOnlyList<string> EnabledFilterOptions { get; } = ["All", "Enabled", "Disabled"];

    public ObservableCollection<string> DeviceFilterOptions { get; } = ["All"];

    public ObservableCollection<string> DeviceOptions { get; } = [];

    public IReadOnlyList<string> DataTypeFilterOptions { get; } = ["All", "Boolean", "Int32", "Int64", "Double", "String"];

    public IReadOnlyList<TagDataType> DataTypeOptions { get; } = Enum.GetValues<TagDataType>();

    public IReadOnlyList<TagAccessMode> AccessModeOptions { get; } = Enum.GetValues<TagAccessMode>();

    public ObservableCollection<string> ScanGroupFilterOptions { get; } = ["All"];

    public ObservableCollection<string> ScanGroupOptions { get; } = [];

    public TagBulkEditModel BulkEdit { get; private set; } = new();

    public IReadOnlyList<BulkEditValue<bool>> BulkEnabledOptions { get; } =
    [
        BulkEditValue<bool>.Unchanged,
        BulkEditValue<bool>.Mixed,
        BulkEditValue<bool>.Explicit(true),
        BulkEditValue<bool>.Explicit(false)
    ];

    public IReadOnlyList<BulkEditValue<bool>> BulkHistoryEnabledOptions { get; } =
    [
        BulkEditValue<bool>.Unchanged,
        BulkEditValue<bool>.Mixed,
        BulkEditValue<bool>.Explicit(true),
        BulkEditValue<bool>.Explicit(false)
    ];

    public IReadOnlyList<BulkEditValue<bool>> BulkMqttPublishEnabledOptions { get; } =
    [
        BulkEditValue<bool>.Unchanged,
        BulkEditValue<bool>.Mixed,
        BulkEditValue<bool>.Explicit(true),
        BulkEditValue<bool>.Explicit(false)
    ];

    public IReadOnlyList<BulkEditValue<string>> BulkDeviceOptions =>
        [BulkEditValue<string>.Unchanged, BulkEditValue<string>.Mixed, .. DeviceOptions.Select(value => BulkEditValue<string>.Explicit(value))];

    public IReadOnlyList<BulkEditValue<string>> BulkScanGroupOptions =>
        [BulkEditValue<string>.Unchanged, BulkEditValue<string>.Mixed, .. ScanGroupOptions.Select(value => BulkEditValue<string>.Explicit(value))];

    public IReadOnlyList<BulkEditValue<TagDataType>> BulkDataTypeOptions =>
        [BulkEditValue<TagDataType>.Unchanged, BulkEditValue<TagDataType>.Mixed, .. DataTypeOptions.Select(value => BulkEditValue<TagDataType>.Explicit(value))];

    public IReadOnlyList<BulkEditValue<TagAccessMode>> BulkAccessModeOptions =>
        [BulkEditValue<TagAccessMode>.Unchanged, BulkEditValue<TagAccessMode>.Mixed, .. AccessModeOptions.Select(value => BulkEditValue<TagAccessMode>.Explicit(value))];

    public IReadOnlyList<BulkEditValue<string>> BulkHistoryProfileOptions { get; } =
    [
        BulkEditValue<string>.Unchanged,
        BulkEditValue<string>.Mixed,
        BulkEditValue<string>.Explicit("Digital"),
        BulkEditValue<string>.Explicit("Analog"),
        BulkEditValue<string>.Explicit("FastAnalog"),
        BulkEditValue<string>.Explicit("Custom")
    ];

    public IReadOnlyList<BulkEditValue<string>> BulkMqttProfileOptions =>
        [BulkEditValue<string>.Unchanged, BulkEditValue<string>.Mixed, .. _session.WorkingProject.Mqtt.Profiles.Select(profile => BulkEditValue<string>.Explicit(profile.Name))];

    public RelayCommand AddCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand RefreshQualityCommand { get; }
    public RelayCommand EnableSelectedCommand { get; }
    public RelayCommand DisableSelectedCommand { get; }
    public RelayCommand EnableHistoryCommand { get; }
    public RelayCommand DisableHistoryCommand { get; }
    public RelayCommand ApplyBulkEditCommand { get; }

    public TagImportPreparation? LastImportPreparation { get; private set; }

    public bool IsActive
    {
        get
        {
            lock (_lifecycleSync)
            {
                return _active && !_disposed;
            }
        }
    }

    public TagEditorRowViewModel? SelectedRow
    {
        get => _selectedRow;
        private set
        {
            if (ReferenceEquals(_selectedRow, value))
            {
                return;
            }

            InvalidateSelectionGeneration();
            DisposeSelectedSubscription();
            _selectedRow = value;
            OnPropertyChanged();
            SubscribeSelectedRow();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    public string EnabledFilter
    {
        get => _enabledFilter;
        set
        {
            if (SetField(ref _enabledFilter, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    public string DeviceFilter
    {
        get => _deviceFilter;
        set
        {
            if (SetField(ref _deviceFilter, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    public string DataTypeFilter
    {
        get => _dataTypeFilter;
        set
        {
            if (SetField(ref _dataTypeFilter, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    public string ScanGroupFilter
    {
        get => _scanGroupFilter;
        set
        {
            if (SetField(ref _scanGroupFilter, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    public bool ValidationOnly
    {
        get => _validationOnly;
        set
        {
            if (SetField(ref _validationOnly, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    public bool IsDirty => _session.IsDirty;
    public bool RestartRequired => _session.RestartRequired;
    public int ValidationErrorCount => _session.ValidationIssues.Count(issue => issue.IsBlocking);
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string? CanonicalProjectPath => _session.CanonicalProjectPath?.FullPath;

    public void Activate()
    {
        lock (_lifecycleSync)
        {
            if (_disposed || _active)
            {
                return;
            }

            _active = true;
            _selectionGeneration++;
        }

        SubscribeSelectedRow();
        OnPropertyChanged(nameof(IsActive));
    }

    public void Deactivate()
    {
        lock (_lifecycleSync)
        {
            _active = false;
            _selectionGeneration++;
        }

        DisposeSelectedSubscription();
        OnPropertyChanged(nameof(IsActive));
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Deactivate();
        _session.PropertyChanged -= OnSessionPropertyChanged;
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    public void SetSelection(IEnumerable<object> selectedItems)
    {
        var selected = selectedItems
            .OfType<TagEditorRowViewModel>()
            .Where(row => Rows.Contains(row))
            .Distinct()
            .ToArray();
        SelectedRows.Clear();
        foreach (var row in selected)
        {
            SelectedRows.Add(row);
        }

        SelectedRow = selected.FirstOrDefault();
        RefreshBulkEditModel();
    }

    public void ImportCsv(string path)
    {
        var text = File.ReadAllText(path);
        ApplyImportedTags(CsvCodec.Import(text), "CSV import");
    }

    public void ExportCsv(string path, IEnumerable<TagEditorRowViewModel>? source = null)
    {
        var tags = (source ?? ItemsView.Cast<TagEditorRowViewModel>()).Select(row => row.Definition);
        File.WriteAllText(path, CsvCodec.Export(tags));
        StatusText = "CSV exported.";
    }

    public void BulkSetEnabled(bool enabled)
    {
        BulkEdit.Enabled = BulkEditValue<bool>.Explicit(enabled);
        ApplyBulkEdit(enabled ? "Enabled" : "Disabled");
    }

    public void BulkSetHistoryEnabled(bool enabled)
    {
        BulkEdit.HistoryEnabled = BulkEditValue<bool>.Explicit(enabled);
        ApplyBulkEdit(enabled ? "History enabled" : "History disabled");
    }

    public void ApplyBulkEdit()
    {
        ApplyBulkEdit("Bulk edit");
    }

    private void AddTag()
    {
        var tag = new TagDefinition
        {
            Id = CreateUniqueId(),
            Name = CreateUniqueName("New Tag"),
            DeviceId = _session.WorkingProject.Devices.FirstOrDefault()?.Id ?? string.Empty,
            ScanGroup = _session.WorkingProject.ScanGroups.FirstOrDefault(group =>
                string.Equals(group.Name, "Normal", StringComparison.OrdinalIgnoreCase))?.Name
                ?? _session.WorkingProject.ScanGroups.FirstOrDefault()?.Name
                ?? "Normal"
        };
        _session.WorkingProject.Tags.Add(tag);
        BuildRows();
        SelectSingleById(tag.Id);
        _session.MarkChanged();
        RefreshValidation();
        StatusText = "New tag added; complete required fields before Save.";
    }

    private void DuplicateSelected()
    {
        var selected = SelectedRows.Count > 0 ? SelectedRows.ToArray() : SelectedRow is null ? [] : [SelectedRow];
        if (selected.Length == 0)
        {
            return;
        }

        var duplicates = selected.Select(row =>
        {
            var duplicate = CloneTag(row.Definition);
            duplicate.Id = CreateUniqueId();
            duplicate.Name = CreateUniqueName($"{row.Name} Copy");
            return duplicate;
        }).ToArray();
        _session.WorkingProject.Tags.AddRange(duplicates);
        BuildRows();
        SetSelection(duplicates.Select(duplicate => (object)_rowsById[duplicate.Id]));
        _session.MarkChanged();
        RefreshValidation();
        StatusText = $"Duplicated {duplicates.Length} tag(s).";
    }

    private void DeleteSelected()
    {
        var selected = SelectedRows.Count > 0 ? SelectedRows.ToArray() : SelectedRow is null ? [] : [SelectedRow];
        if (selected.Length == 0)
        {
            return;
        }

        if (!_deleteConfirmation.ConfirmDelete(selected.Length))
        {
            StatusText = "Delete cancelled; no project changes were made.";
            return;
        }

        var ids = selected.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _session.WorkingProject.Tags.RemoveAll(tag => ids.Contains(tag.Id));
        BuildRows();
        _session.MarkChanged();
        RefreshValidation();
        StatusText = $"Deleted {selected.Length} tag(s).";
    }

    private void Save()
    {
        if (_session.TrySave())
        {
            RefreshValidation();
            StatusText = _session.RestartRequired
                ? "Project saved. Restart required before Runtime configuration changes become active."
                : "Project saved.";
        }
        else
        {
            RefreshValidation();
            StatusText = _session.LastErrorMessage
                ?? $"Save blocked: {ValidationErrorCount} validation issue(s).";
        }
    }

    private void Revert()
    {
        _session.Revert();
        RefreshValidation();
        StatusText = "Unsaved changes reverted.";
    }

    private void CopySelected()
    {
        var selected = SelectedRows.Count > 0 ? SelectedRows : SelectedRow is null ? [] : [SelectedRow];
        if (selected.Count == 0)
        {
            return;
        }

        _clipboard.SetText(TagClipboardCodec.Export(selected.Select(row => row.Definition)));
        StatusText = $"Copied {selected.Count} tag(s) as TSV.";
    }

    private void PasteClipboard()
    {
        var text = _clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "Clipboard does not contain tag TSV data.";
            return;
        }

        try
        {
            ApplyImportedTags(TagClipboardCodec.Import(text), "Clipboard paste");
        }
        catch (FormatException exception)
        {
            StatusText = $"Paste blocked: {exception.Message}";
        }
    }

    private void RefreshQualitySnapshot()
    {
        if (SelectedRow is null)
        {
            return;
        }

        if (_cache.TryGet(SelectedRow.Id, out var value) && value is not null)
        {
            SelectedRow.ApplyRuntimeValue(value, RuntimeStatusFor(SelectedRow));
            StatusText = "Selected runtime quality refreshed.";
        }
        else
        {
            SelectedRow.ApplyRuntimeUnavailable(RuntimeStatusFor(SelectedRow));
            StatusText = "Selected tag is not present in the running Runtime.";
        }
    }

    private void ApplyBulkEdit(string description)
    {
        var selected = SelectedRows.Count > 0 ? SelectedRows.ToArray() : SelectedRow is null ? [] : [SelectedRow];
        if (selected.Length == 0)
        {
            return;
        }

        var candidate = ProjectSnapshotCloner.Clone(_session.WorkingProject);
        var ids = selected.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in candidate.Tags.Where(tag => ids.Contains(tag.Id)))
        {
            ApplyExplicitBulkValues(tag);
        }

        var blockingIssues = RuntimeOptionsValidation.CollectIssues(candidate)
            .Where(issue => issue.IsBlocking)
            .ToArray();
        if (blockingIssues.Length > 0)
        {
            var firstIssue = blockingIssues[0];
            StatusText = $"{description} blocked: {firstIssue.Code} — {firstIssue.Message}";
            return;
        }

        _session.ReplaceWorkingProject(candidate);
        SetSelection(selected.Select(row => (object)_rowsById[row.Id]));
        RefreshValidation();
        StatusText = $"{description} applied to {selected.Length} tag(s); Save when ready.";
    }

    private void ApplyExplicitBulkValues(TagDefinition tag)
    {
        if (BulkEdit.Enabled.Kind == BulkEditValueKind.Explicit)
        {
            tag.Enabled = BulkEdit.Enabled.Value;
        }

        if (BulkEdit.DeviceId.Kind == BulkEditValueKind.Explicit)
        {
            tag.DeviceId = BulkEdit.DeviceId.Value;
        }

        if (BulkEdit.DataType.Kind == BulkEditValueKind.Explicit)
        {
            tag.DataType = BulkEdit.DataType.Value;
        }

        if (BulkEdit.ScanGroup.Kind == BulkEditValueKind.Explicit)
        {
            tag.ScanGroup = BulkEdit.ScanGroup.Value;
        }

        if (BulkEdit.AccessMode.Kind == BulkEditValueKind.Explicit)
        {
            tag.AccessMode = BulkEdit.AccessMode.Value;
        }

        if (BulkEdit.HistoryEnabled.Kind == BulkEditValueKind.Explicit)
        {
            tag.HistoryEnabled = BulkEdit.HistoryEnabled.Value;
        }

        if (BulkEdit.HistoryProfile.Kind == BulkEditValueKind.Explicit)
        {
            tag.HistoryProfile = BulkEdit.HistoryProfile.Value;
        }

        if (BulkEdit.MqttPublishEnabled.Kind == BulkEditValueKind.Explicit)
        {
            tag.MqttPublishEnabled = BulkEdit.MqttPublishEnabled.Value;
        }

        if (BulkEdit.MqttProfile.Kind == BulkEditValueKind.Explicit)
        {
            tag.MqttProfile = BulkEdit.MqttProfile.Value;
        }
    }

    private void ApplyImportedTags(IReadOnlyList<TagDefinition> imported, string operation)
    {
        var preparation = TagImportPreparer.Prepare(imported, _session.WorkingProject.Tags);
        LastImportPreparation = preparation;
        OnPropertyChanged(nameof(LastImportPreparation));
        if (preparation.Candidates.Count == 0)
        {
            StatusText = $"{operation} contained no tag rows.";
            return;
        }

        var decision = _importDecision.Decide(preparation, operation);
        if (decision == TagImportDecision.Cancel)
        {
            StatusText = $"{operation} cancelled; no project changes were made.";
            return;
        }

        if (preparation.HasConflicts && decision == TagImportDecision.ApplyAll)
        {
            StatusText = $"{operation} blocked: resolve {preparation.Conflicts.Count} conflict(s) before applying.";
            return;
        }

        var toApply = decision == TagImportDecision.AppendNonConflicting
            ? preparation.NonConflictingCandidates
            : preparation.Candidates;
        if (toApply.Count == 0)
        {
            StatusText = $"{operation} produced no non-conflicting tags; no project changes were made.";
            return;
        }

        var candidate = ProjectSnapshotCloner.Clone(_session.WorkingProject);
        foreach (var prepared in toApply)
        {
            candidate.Tags.Add(CloneTag(prepared.Definition));
        }

        _session.ReplaceWorkingProject(candidate);
        RefreshValidation();
        var generatedCount = toApply.Count(candidateTag => candidateTag.IdGenerated);
        StatusText = $"{operation} added {toApply.Count} tag(s)" +
            (generatedCount == 0 ? string.Empty : $" ({generatedCount} Id(s) generated)") +
            "; Save when ready.";
    }

    private void BuildRows()
    {
        InvalidateSelectionGeneration();
        DisposeSelectedSubscription();
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Rows.Clear();
        _rowsById.Clear();
        foreach (var tag in _session.WorkingProject.Tags)
        {
            var row = new TagEditorRowViewModel(tag);
            if (_cache.TryGet(tag.Id, out var currentValue) && currentValue is not null)
            {
                row.ApplyRuntimeValue(currentValue, RuntimeStatusFor(row));
            }
            else
            {
                row.ApplyRuntimeUnavailable(RuntimeStatusFor(row));
            }

            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
            _rowsById[tag.Id] = row;
        }

        SelectedRows.Clear();
        _selectedRow = null;
        OnPropertyChanged(nameof(SelectedRow));
        BulkEdit = new TagBulkEditModel();
        OnPropertyChanged(nameof(BulkEdit));
        RefreshOptionCollections();
        ItemsView.Refresh();
        RefreshValidation();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TagEditorRowViewModel.Value)
            or nameof(TagEditorRowViewModel.Quality)
            or nameof(TagEditorRowViewModel.Timestamp)
            or nameof(TagEditorRowViewModel.RuntimeStatus)
            or nameof(TagEditorRowViewModel.HasErrors)
            or nameof(TagEditorRowViewModel.HasWarnings)
            or nameof(TagEditorRowViewModel.Warnings)
            or nameof(TagEditorRowViewModel.WarningSummary)
            or nameof(TagEditorRowViewModel.HasRuntimeConfigurationWarning))
        {
            return;
        }

        _session.MarkChanged();
        if (sender is TagEditorRowViewModel changedRow && ReferenceEquals(changedRow, SelectedRow))
        {
            changedRow.SetRuntimeStatus(RuntimeStatusFor(changedRow));
        }
        RefreshBulkEditModel();
        ItemsView.Refresh();
        RefreshValidation();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProjectEditSession.WorkingProject))
        {
            BuildRows();
        }

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(RestartRequired));
        OnPropertyChanged(nameof(ValidationErrorCount));
        OnPropertyChanged(nameof(CanonicalProjectPath));
    }

    private void RefreshValidation()
    {
        var issuesByTag = _session.ValidationIssues
            .Where(issue => string.Equals(issue.ObjectType, "Tag", StringComparison.OrdinalIgnoreCase))
            .GroupBy(issue => issue.ObjectId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            row.SetValidationIssues(issuesByTag.TryGetValue(row.Id, out var issues) ? issues : []);
        }

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(RestartRequired));
        OnPropertyChanged(nameof(ValidationErrorCount));
        ItemsView.Refresh();
    }

    private void RefreshOptionCollections()
    {
        var devices = _session.WorkingProject.Devices
            .Select(device => device.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var scanGroups = _session.WorkingProject.ScanGroups
            .Select(group => group.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceOptions(DeviceFilterOptions, ["All", .. devices]);
        ReplaceOptions(DeviceOptions, devices);
        ReplaceOptions(ScanGroupFilterOptions, ["All", .. scanGroups]);
        ReplaceOptions(ScanGroupOptions, scanGroups);
        OnPropertyChanged(nameof(BulkDeviceOptions));
        OnPropertyChanged(nameof(BulkScanGroupOptions));
    }

    private bool FilterRow(object item)
    {
        if (item is not TagEditorRowViewModel row)
        {
            return false;
        }

        if (EnabledFilter == "Enabled" && !row.Enabled || EnabledFilter == "Disabled" && row.Enabled)
        {
            return false;
        }

        if (DeviceFilter != "All" && !string.Equals(DeviceFilter, row.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (DataTypeFilter != "All" && !string.Equals(DataTypeFilter, row.DataType.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ScanGroupFilter != "All" && !string.Equals(ScanGroupFilter, row.ScanGroup, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ValidationOnly && !row.HasErrors)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return row.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.DeviceId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Address.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Unit.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectSingleById(string id)
    {
        if (_rowsById.TryGetValue(id, out var row))
        {
            SetSelection([row]);
        }
    }

    private void SubscribeSelectedRow()
    {
        DisposeSelectedSubscription();
        if (!IsActive || SelectedRow is null)
        {
            return;
        }

        var row = SelectedRow;
        var id = row.Id;
        long generation;
        lock (_lifecycleSync)
        {
            generation = _selectionGeneration;
        }

        if (_cache.TryGet(id, out var currentValue) && currentValue is not null)
        {
            row.ApplyRuntimeValue(currentValue, RuntimeStatusFor(row));
        }
        else
        {
            row.ApplyRuntimeUnavailable(RuntimeStatusFor(row));
        }

        if (!_session.StartupProject.Tags.Any(tag =>
            string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var subscription = _cache.Subscribe(id, value => OnSelectedValue(id, value, generation));
        lock (_lifecycleSync)
        {
            if (_active && !_disposed && _selectionGeneration == generation && ReferenceEquals(SelectedRow, row))
            {
                _selectedSubscription = subscription;
                return;
            }
        }

        subscription.Dispose();
    }

    private void OnSelectedValue(string id, TagValue value, long generation)
    {
        if (!IsCurrentSelection(id, generation))
        {
            return;
        }

        void Update()
        {
            if (IsCurrentSelection(id, generation) && SelectedRow is { } row)
            {
                row.ApplyRuntimeValue(value, RuntimeStatusFor(row));
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Update();
        }
        else
        {
            dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(Update));
        }
    }

    private bool IsCurrentSelection(string id, long generation)
    {
        lock (_lifecycleSync)
        {
            return _active && !_disposed && _selectionGeneration == generation
                && SelectedRow is not null
                && string.Equals(SelectedRow.Id, id, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void DisposeSelectedSubscription()
    {
        var subscription = Interlocked.Exchange(ref _selectedSubscription, null);
        subscription?.Dispose();
    }

    private void RefreshBulkEditModel()
    {
        BulkEdit = TagBulkEditModel.FromSelection(SelectedRows.Select(row => row.Definition));
        OnPropertyChanged(nameof(BulkEdit));
    }

    private void InvalidateSelectionGeneration()
    {
        lock (_lifecycleSync)
        {
            _selectionGeneration++;
        }
    }

    private string RuntimeStatusFor(TagEditorRowViewModel row)
    {
        var runtimeTag = _session.StartupProject.Tags.FirstOrDefault(tag =>
            string.Equals(tag.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        if (runtimeTag is null)
        {
            return "Not Loaded";
        }

        return RuntimeDefinitionMatches(runtimeTag, row.Definition)
            ? "Current runtime"
            : "Current runtime / restart required";
    }

    private static bool RuntimeDefinitionMatches(TagDefinition left, TagDefinition right) =>
        string.Equals(left.DeviceId, right.DeviceId, StringComparison.Ordinal) &&
        string.Equals(left.Address, right.Address, StringComparison.Ordinal) &&
        left.DataType == right.DataType &&
        left.Enabled == right.Enabled &&
        string.Equals(left.ScanGroup, right.ScanGroup, StringComparison.Ordinal);

    private string CreateUniqueId() => CreateUniqueId(_session.WorkingProject.Tags);

    private static string CreateUniqueId(IEnumerable<TagDefinition> tags)
    {
        var existing = tags.Select(tag => tag.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string id;
        do
        {
            id = $"TAG_{Guid.NewGuid():N}";
        } while (!existing.Add(id));

        return id;
    }

    private string CreateUniqueName(string baseName) => CreateUniqueName(baseName, _session.WorkingProject.Tags);

    private static string CreateUniqueName(string baseName, IEnumerable<TagDefinition> tags)
    {
        var existing = tags.Select(tag => tag.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = string.IsNullOrWhiteSpace(baseName) ? "New Tag" : baseName.Trim();
        var suffix = 2;
        while (!existing.Add(candidate))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }

    private static TagDefinition CloneTag(TagDefinition source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Description = source.Description,
        DeviceId = source.DeviceId,
        Address = source.Address,
        DataType = source.DataType,
        Enabled = source.Enabled,
        ScanGroup = source.ScanGroup,
        AccessMode = source.AccessMode,
        Min = source.Min,
        Max = source.Max,
        Unit = source.Unit,
        HistoryEnabled = source.HistoryEnabled,
        HistoryProfile = source.HistoryProfile,
        MqttPublishEnabled = source.MqttPublishEnabled,
        MqttProfile = source.MqttProfile,
        MqttTopicOverride = source.MqttTopicOverride
    };

    private static void ReplaceOptions(ObservableCollection<string> target, IEnumerable<string> values)
    {
        var next = values.ToArray();
        if (target.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        target.Clear();
        foreach (var value in next)
        {
            target.Add(value);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
