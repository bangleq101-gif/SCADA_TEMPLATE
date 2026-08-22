using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Infrastructure.Persistence;

namespace Scada.App.Services;

public sealed class ProjectEditSession : INotifyPropertyChanged
{
    private readonly IProjectConfigurationStore? _store;
    private readonly ProjectPath? _projectPath;
    private RuntimeOptions _workingProject;
    private RuntimeOptions _savedProject;
    private bool _isDirty;
    private bool _restartRequired;
    private IReadOnlyList<ValidationIssue> _validationIssues = [];
    private string? _lastErrorMessage;

    public ProjectEditSession(
        RuntimeOptions startupProject,
        ProjectPath? projectPath,
        IProjectConfigurationStore? store)
    {
        ArgumentNullException.ThrowIfNull(startupProject);
        if (projectPath is not null && store is null)
        {
            throw new ArgumentException("A project store is required when a canonical path is configured.", nameof(store));
        }

        StartupProject = ProjectSnapshotCloner.Clone(startupProject);
        _savedProject = ProjectSnapshotCloner.Clone(startupProject);
        _workingProject = ProjectSnapshotCloner.Clone(startupProject);
        _projectPath = projectPath;
        _store = store;
        RefreshState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RuntimeOptions StartupProject { get; }

    public RuntimeOptions SavedProject => _savedProject;

    public RuntimeOptions WorkingProject => _workingProject;

    public ProjectPath? CanonicalProjectPath => _projectPath;

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetField(ref _isDirty, value);
    }

    public bool RestartRequired
    {
        get => _restartRequired;
        private set => SetField(ref _restartRequired, value);
    }

    public IReadOnlyList<ValidationIssue> ValidationIssues
    {
        get => _validationIssues;
        private set => SetField(ref _validationIssues, value);
    }

    public bool HasBlockingIssues => ValidationIssues.Any(issue => issue.IsBlocking);

    public string? LastErrorMessage
    {
        get => _lastErrorMessage;
        private set => SetField(ref _lastErrorMessage, value);
    }

    public void MarkChanged()
    {
        RefreshState();
    }

    public void ReplaceWorkingProject(RuntimeOptions project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _workingProject = ProjectSnapshotCloner.Clone(project);
        OnPropertyChanged(nameof(WorkingProject));
        RefreshState();
    }

    public bool TrySave()
    {
        LastErrorMessage = null;
        RefreshState();

        if (HasBlockingIssues)
        {
            return false;
        }

        if (_projectPath is null || _store is null)
        {
            AddBlockingIssue(new ValidationIssue(
                "PROJECT_PATH_REQUIRED",
                ValidationSeverity.Error,
                "Project",
                null,
                null,
                "No canonical project Save destination is configured."));
            return false;
        }

        try
        {
            _store.Save(new ProjectDocument
            {
                SchemaVersion = ProjectDocumentSchema.CurrentVersion,
                Scada = ProjectSnapshotCloner.Clone(_workingProject)
            });

            _savedProject = ProjectSnapshotCloner.Clone(_workingProject);
            OnPropertyChanged(nameof(SavedProject));
            IsDirty = false;
            RestartRequired = !ProjectSnapshotComparer.AreEquivalent(_savedProject, StartupProject);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastErrorMessage = exception.Message;
            return false;
        }
    }

    public void Revert()
    {
        _workingProject = ProjectSnapshotCloner.Clone(_savedProject);
        OnPropertyChanged(nameof(WorkingProject));
        RefreshState();
    }

    private void RefreshState()
    {
        IsDirty = !ProjectSnapshotComparer.AreEquivalent(_workingProject, _savedProject);
        RestartRequired = !ProjectSnapshotComparer.AreEquivalent(_workingProject, StartupProject);
        ValidationIssues = RuntimeOptionsValidation.CollectIssues(_workingProject);
        OnPropertyChanged(nameof(HasBlockingIssues));
    }

    private void AddBlockingIssue(ValidationIssue issue)
    {
        ValidationIssues = ValidationIssues
            .Where(existing => existing.Code != issue.Code)
            .Append(issue)
            .ToArray();
        OnPropertyChanged(nameof(HasBlockingIssues));
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

internal static class ProjectSnapshotCloner
{
    public static RuntimeOptions Clone(RuntimeOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RuntimeOptions
        {
            RuntimeId = source.RuntimeId,
            Polling = new PollingOptions
            {
                ConnectTimeoutMilliseconds = source.Polling.ConnectTimeoutMilliseconds,
                ReadTimeoutMilliseconds = source.Polling.ReadTimeoutMilliseconds,
                DisconnectTimeoutMilliseconds = source.Polling.DisconnectTimeoutMilliseconds,
                InitialReconnectDelayMilliseconds = source.Polling.InitialReconnectDelayMilliseconds,
                MaxReconnectDelayMilliseconds = source.Polling.MaxReconnectDelayMilliseconds,
                ShutdownTimeoutMilliseconds = source.Polling.ShutdownTimeoutMilliseconds
            },
            Historian = new HistorianOptions
            {
                Enabled = source.Historian.Enabled,
                DatabasePath = source.Historian.DatabasePath,
                QueueCapacity = source.Historian.QueueCapacity,
                BatchSize = source.Historian.BatchSize,
                FlushIntervalMilliseconds = source.Historian.FlushIntervalMilliseconds,
                ShutdownDrainTimeoutMilliseconds = source.Historian.ShutdownDrainTimeoutMilliseconds,
                Profiles = source.Historian.Profiles.Select(profile => new HistoryProfileDefinition
                {
                    Name = profile.Name,
                    Mode = profile.Mode,
                    Deadband = profile.Deadband,
                    MinimumIntervalMilliseconds = profile.MinimumIntervalMilliseconds,
                    MaximumIntervalMilliseconds = profile.MaximumIntervalMilliseconds
                }).ToList()
            },
            ScanGroups = source.ScanGroups.Select(group => new ScanGroupDefinition
            {
                Name = group.Name,
                IntervalMilliseconds = group.IntervalMilliseconds
            }).ToList(),
            Devices = source.Devices.Select(device => new DeviceDefinition
            {
                Id = device.Id,
                Name = device.Name,
                Enabled = device.Enabled,
                DriverType = device.DriverType,
                ConnectionOptions = new Dictionary<string, string>(
                    device.ConnectionOptions,
                    StringComparer.OrdinalIgnoreCase)
            }).ToList(),
            Tags = source.Tags.Select(tag => new TagDefinition
            {
                Id = tag.Id,
                Name = tag.Name,
                Description = tag.Description,
                DeviceId = tag.DeviceId,
                Address = tag.Address,
                DataType = tag.DataType,
                Enabled = tag.Enabled,
                ScanGroup = tag.ScanGroup,
                AccessMode = tag.AccessMode,
                Min = tag.Min,
                Max = tag.Max,
                Unit = tag.Unit,
                HistoryEnabled = tag.HistoryEnabled,
                HistoryProfile = tag.HistoryProfile,
                MqttPublishEnabled = tag.MqttPublishEnabled,
                MqttProfile = tag.MqttProfile,
                MqttTopicOverride = tag.MqttTopicOverride
            }).ToList()
        };
    }
}

internal static class ProjectSnapshotComparer
{
    public static bool AreEquivalent(RuntimeOptions left, RuntimeOptions right)
    {
        if (!string.Equals(left.RuntimeId, right.RuntimeId, StringComparison.Ordinal) ||
            left.Polling.ConnectTimeoutMilliseconds != right.Polling.ConnectTimeoutMilliseconds ||
            left.Polling.ReadTimeoutMilliseconds != right.Polling.ReadTimeoutMilliseconds ||
            left.Polling.DisconnectTimeoutMilliseconds != right.Polling.DisconnectTimeoutMilliseconds ||
            left.Polling.InitialReconnectDelayMilliseconds != right.Polling.InitialReconnectDelayMilliseconds ||
            left.Polling.MaxReconnectDelayMilliseconds != right.Polling.MaxReconnectDelayMilliseconds ||
            left.Polling.ShutdownTimeoutMilliseconds != right.Polling.ShutdownTimeoutMilliseconds ||
            left.Historian.Enabled != right.Historian.Enabled ||
            !string.Equals(left.Historian.DatabasePath, right.Historian.DatabasePath, StringComparison.Ordinal) ||
            left.Historian.QueueCapacity != right.Historian.QueueCapacity ||
            left.Historian.BatchSize != right.Historian.BatchSize ||
            left.Historian.FlushIntervalMilliseconds != right.Historian.FlushIntervalMilliseconds ||
            left.Historian.ShutdownDrainTimeoutMilliseconds != right.Historian.ShutdownDrainTimeoutMilliseconds ||
            left.Historian.Profiles.Count != right.Historian.Profiles.Count ||
            left.ScanGroups.Count != right.ScanGroups.Count ||
            left.Devices.Count != right.Devices.Count ||
            left.Tags.Count != right.Tags.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Historian.Profiles.Count; index++)
        {
            var leftProfile = left.Historian.Profiles[index];
            var rightProfile = right.Historian.Profiles[index];
            if (!string.Equals(leftProfile.Name, rightProfile.Name, StringComparison.Ordinal) ||
                leftProfile.Mode != rightProfile.Mode ||
                leftProfile.Deadband != rightProfile.Deadband ||
                leftProfile.MinimumIntervalMilliseconds != rightProfile.MinimumIntervalMilliseconds ||
                leftProfile.MaximumIntervalMilliseconds != rightProfile.MaximumIntervalMilliseconds)
            {
                return false;
            }
        }

        for (var index = 0; index < left.ScanGroups.Count; index++)
        {
            var leftGroup = left.ScanGroups[index];
            var rightGroup = right.ScanGroups[index];
            if (!string.Equals(leftGroup.Name, rightGroup.Name, StringComparison.Ordinal) ||
                leftGroup.IntervalMilliseconds != rightGroup.IntervalMilliseconds)
            {
                return false;
            }
        }

        for (var index = 0; index < left.Devices.Count; index++)
        {
            var leftDevice = left.Devices[index];
            var rightDevice = right.Devices[index];
            if (!string.Equals(leftDevice.Id, rightDevice.Id, StringComparison.Ordinal) ||
                !string.Equals(leftDevice.Name, rightDevice.Name, StringComparison.Ordinal) ||
                leftDevice.Enabled != rightDevice.Enabled ||
                !string.Equals(leftDevice.DriverType, rightDevice.DriverType, StringComparison.Ordinal) ||
                !DictionaryEquals(leftDevice.ConnectionOptions, rightDevice.ConnectionOptions))
            {
                return false;
            }
        }

        for (var index = 0; index < left.Tags.Count; index++)
        {
            var leftTag = left.Tags[index];
            var rightTag = right.Tags[index];
            if (!string.Equals(leftTag.Id, rightTag.Id, StringComparison.Ordinal) ||
                !string.Equals(leftTag.Name, rightTag.Name, StringComparison.Ordinal) ||
                !string.Equals(leftTag.Description, rightTag.Description, StringComparison.Ordinal) ||
                !string.Equals(leftTag.DeviceId, rightTag.DeviceId, StringComparison.Ordinal) ||
                !string.Equals(leftTag.Address, rightTag.Address, StringComparison.Ordinal) ||
                leftTag.DataType != rightTag.DataType ||
                leftTag.Enabled != rightTag.Enabled ||
                !string.Equals(leftTag.ScanGroup, rightTag.ScanGroup, StringComparison.Ordinal) ||
                leftTag.AccessMode != rightTag.AccessMode ||
                leftTag.Min != rightTag.Min ||
                leftTag.Max != rightTag.Max ||
                !string.Equals(leftTag.Unit, rightTag.Unit, StringComparison.Ordinal) ||
                leftTag.HistoryEnabled != rightTag.HistoryEnabled ||
                !string.Equals(leftTag.HistoryProfile, rightTag.HistoryProfile, StringComparison.Ordinal) ||
                leftTag.MqttPublishEnabled != rightTag.MqttPublishEnabled ||
                !string.Equals(leftTag.MqttProfile, rightTag.MqttProfile, StringComparison.Ordinal) ||
                !string.Equals(leftTag.MqttTopicOverride, rightTag.MqttTopicOverride, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
