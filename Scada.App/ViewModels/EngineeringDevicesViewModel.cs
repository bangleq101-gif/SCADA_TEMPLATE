using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.App.Services;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;

namespace Scada.App.ViewModels;

public sealed class EngineeringDevicesViewModel : IWorkspaceLifecycle, IDisposable
{
    private readonly ProjectEditSession _session;
    private readonly IReadOnlyDictionary<string, IDriverEngineeringProvider> _providers;
    private readonly object _sync = new();
    private DeviceEditorRowViewModel? _selectedDevice;
    private DriverOptionEditorViewModel? _selectedOption;
    private AddressBrowseCandidate? _selectedCandidate;
    private bool _active;
    private bool _disposed;
    private string _searchText = string.Empty;
    private string _statusText = "Ready";

    public EngineeringDevicesViewModel(
        ProjectEditSession session,
        IEnumerable<IDriverEngineeringProvider> providers)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers
            .GroupBy(provider => provider.DriverType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Devices = [];
        DriverOptionEditors = [];
        AddressCandidates = [];
        DriverTypes = _providers.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        AddCommand = new RelayCommand(_ => AddDevice());
        DuplicateCommand = new RelayCommand(_ => DuplicateSelected());
        DeleteCommand = new RelayCommand(_ => DeleteSelected());
        SaveCommand = new RelayCommand(_ => Save());
        RevertCommand = new RelayCommand(_ => Revert());
        BrowseAddressesCommand = new AsyncRelayCommand(BrowseAddressesAsync);
        _session.PropertyChanged += OnSessionPropertyChanged;
        RebuildDevices();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeviceEditorRowViewModel> Devices { get; }
    public ObservableCollection<DriverOptionEditorViewModel> DriverOptionEditors { get; }
    public ObservableCollection<AddressBrowseCandidate> AddressCandidates { get; }
    public IReadOnlyList<string> DriverTypes { get; }

    public RelayCommand AddCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RevertCommand { get; }
    public AsyncRelayCommand BrowseAddressesCommand { get; }

    public bool IsActive
    {
        get { lock (_sync) return _active && !_disposed; }
        private set => SetField(ref _active, value);
    }

    public DeviceEditorRowViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            RebuildOptionEditors();
            AddressCandidates.Clear();
            SelectedCandidate = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDeviceIssues));
            BrowseAddressesCommand.Refresh();
            DeleteCommand.Refresh();
            DuplicateCommand.Refresh();
        }
    }

    public DriverOptionEditorViewModel? SelectedOption
    {
        get => _selectedOption;
        set => SetField(ref _selectedOption, value);
    }

    public AddressBrowseCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (ReferenceEquals(_selectedCandidate, value)) return;
            _selectedCandidate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAddress));
        }
    }

    public string SelectedAddress => SelectedCandidate?.Address ?? string.Empty;

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsDirty => _session.IsDirty;
    public bool RestartRequired => _session.RestartRequired;
    public bool HasBlockingIssues => _session.HasBlockingIssues;
    public string ValidationSummary =>
        _session.ValidationIssues.Count == 0
            ? "No validation issues"
            : string.Join(Environment.NewLine, _session.ValidationIssues.Select(issue => $"{issue.Severity}: {issue.ObjectType} {issue.ObjectId} {issue.Message}"));

    public IReadOnlyList<ValidationIssue> SelectedDeviceIssues =>
        SelectedDevice is null
            ? []
            : _session.ValidationIssues
                .Where(issue => string.Equals(issue.ObjectType, "Device", StringComparison.OrdinalIgnoreCase))
                .Where(issue => string.Equals(issue.ObjectId, SelectedDevice.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
        }

        _session.PropertyChanged -= OnSessionPropertyChanged;
        BrowseAddressesCommand.Dispose();
    }

    private void AddDevice()
    {
        var id = NextDeviceId();
        var driverType = DriverTypes.FirstOrDefault() ?? string.Empty;
        _session.WorkingProject.Devices.Add(new DeviceDefinition
        {
            Id = id,
            Name = id,
            Enabled = true,
            DriverType = driverType
        });
        _session.MarkChanged();
        RebuildDevices(id);
        StatusText = $"Added device {id}.";
    }

    private void DuplicateSelected()
    {
        if (SelectedDevice is null) return;
        var copy = SelectedDevice.CopyDefinition();
        copy.Id = NextDeviceId(SelectedDevice.Id);
        copy.Name = $"{SelectedDevice.Name} copy";
        _session.WorkingProject.Devices.Add(copy);
        _session.MarkChanged();
        RebuildDevices(copy.Id);
        StatusText = $"Duplicated device as {copy.Id}.";
    }

    private void DeleteSelected()
    {
        if (SelectedDevice is null) return;
        var id = SelectedDevice.Id;
        _session.WorkingProject.Devices.Remove(SelectedDevice.Definition);
        _session.MarkChanged();
        RebuildDevices();
        StatusText = $"Deleted device {id}.";
    }

    private void Save()
    {
        if (_session.TrySave())
        {
            StatusText = "Project saved.";
        }
        else
        {
            StatusText = _session.LastErrorMessage ?? $"Save blocked: {_session.ValidationIssues.Count} validation issue(s).";
        }

        RefreshState();
    }

    private void Revert()
    {
        _session.Revert();
        RebuildDevices();
        StatusText = "Reverted to the saved project.";
    }

    private async Task BrowseAddressesAsync(CancellationToken cancellationToken)
    {
        if (SelectedDevice is null || !_providers.TryGetValue(SelectedDevice.DriverType, out var provider))
        {
            AddressCandidates.Clear();
            StatusText = "Address browsing is unavailable for the selected driver.";
            return;
        }

        try
        {
            var candidates = await provider.BrowseAddressesAsync(SelectedDevice.Definition, cancellationToken);
            AddressCandidates.Clear();
            foreach (var candidate in candidates.OrderBy(candidate => candidate.Address, StringComparer.OrdinalIgnoreCase))
            {
                AddressCandidates.Add(candidate);
            }

            StatusText = $"Loaded {AddressCandidates.Count} read-only address candidates.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Address browsing cancelled.";
        }
        catch (Exception exception)
        {
            AddressCandidates.Clear();
            StatusText = $"Address browsing failed: {exception.Message}";
        }
    }

    private void RebuildDevices(string? selectedId = null)
    {
        var id = selectedId ?? SelectedDevice?.Id;
        Devices.Clear();
        foreach (var device in _session.WorkingProject.Devices)
        {
            Devices.Add(new DeviceEditorRowViewModel(device, OnDeviceChanged));
        }

        SelectedDevice = Devices.FirstOrDefault(device => string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? Devices.FirstOrDefault();
        RefreshState();
    }

    private void RebuildOptionEditors()
    {
        DriverOptionEditors.Clear();
        if (SelectedDevice is null || !_providers.TryGetValue(SelectedDevice.DriverType, out var provider))
        {
            return;
        }

        foreach (var definition in provider.OptionDefinitions)
        {
            DriverOptionEditors.Add(new DriverOptionEditorViewModel(SelectedDevice, definition));
        }
    }

    private void OnDeviceChanged(DeviceEditorRowViewModel device)
    {
        _session.MarkChanged();
        if (ReferenceEquals(device, SelectedDevice))
        {
            RebuildOptionEditors();
        }

        RefreshState();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProjectEditSession.WorkingProject))
        {
            RebuildDevices();
        }

        RefreshState();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(RestartRequired));
        OnPropertyChanged(nameof(HasBlockingIssues));
        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(SelectedDeviceIssues));
    }

    private string NextDeviceId(string? seed = null)
    {
        var prefix = string.IsNullOrWhiteSpace(seed) ? "DEVICE" : $"{seed}_COPY";
        var index = 1;
        var existing = _session.WorkingProject.Devices
            .Select(device => device.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = prefix;
        while (existing.Contains(candidate))
        {
            candidate = $"{prefix}{index++}";
        }

        return candidate;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
