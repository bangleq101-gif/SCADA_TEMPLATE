using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Scada.Core.Configuration;
using Scada.Core.Alarms;
using Scada.Core.Devices;
using Scada.Core.History;
using Scada.Core.Mqtt;
using Scada.Core.MachineSettings;
using Scada.Core.Tags;
using Scada.Core.Drivers;
using Scada.Infrastructure.Persistence;

namespace Scada.App.Services;

public sealed class ProjectEditSession : INotifyPropertyChanged
{
    private readonly IProjectConfigurationStore? _store;
    private readonly ProjectPath? _projectPath;
    private readonly IReadOnlyList<IDriverEngineeringProvider> _driverProviders;
    private RuntimeOptions _workingProject;
    private RuntimeOptions _savedProject;
    private bool _isDirty;
    private bool _restartRequired;
    private IReadOnlyList<ValidationIssue> _validationIssues = [];
    private string? _lastErrorMessage;

    public ProjectEditSession(
        RuntimeOptions startupProject,
        ProjectPath? projectPath,
        IProjectConfigurationStore? store,
        IEnumerable<IDriverEngineeringProvider>? driverProviders = null)
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
        _driverProviders = (driverProviders ?? [])
            .Where(provider => provider is not null)
            .ToArray();
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
        ValidationIssues = Scada.Infrastructure.Configuration.ConfigurationValidator
            .CollectIssues(_workingProject, _driverProviders);
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
                StorageProvider = source.Historian.StorageProvider,
                DatabasePath = source.Historian.DatabasePath,
                Influx = CloneInfluxOptions(source.Historian.Influx),
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
            Mqtt = CloneMqttOptions(source.Mqtt),
            Alarms = CloneAlarmOptions(source.Alarms),
            MachineSettings = new MachineSettingsOptions
            {
                Pages = source.MachineSettings.Pages.Select(page => new MachineSettingsPageDefinition
                {
                    Id = page.Id, Title = page.Title, Description = page.Description, Group = page.Group,
                    Order = page.Order, IsVisible = page.IsVisible,
                    Parameters = page.Parameters.Select(parameter => new MachineParameterDefinition
                    {
                        Id = parameter.Id, Name = parameter.Name, Description = parameter.Description, Group = parameter.Group,
                        ValueType = parameter.ValueType, Value = parameter.Value, Min = parameter.Min, Max = parameter.Max,
                        Unit = parameter.Unit, IsReadOnly = parameter.IsReadOnly, IsVisible = parameter.IsVisible,
                        Order = parameter.Order, LiveTagId = parameter.LiveTagId
                    }).ToList()
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

    private static InfluxDbOptions CloneInfluxOptions(InfluxDbOptions source) => new()
    {
        Url = source.Url,
        Organization = source.Organization,
        Bucket = source.Bucket,
        Measurement = source.Measurement,
        TokenReference = source.TokenReference,
        BufferPath = source.BufferPath,
        MaxBufferedSamples = source.MaxBufferedSamples,
        SyncBatchSize = source.SyncBatchSize,
        SyncIntervalMilliseconds = source.SyncIntervalMilliseconds,
        HealthProbeIntervalMilliseconds = source.HealthProbeIntervalMilliseconds,
        ConnectionTimeoutMilliseconds = source.ConnectionTimeoutMilliseconds,
        WriteTimeoutMilliseconds = source.WriteTimeoutMilliseconds,
        QueryTimeoutMilliseconds = source.QueryTimeoutMilliseconds,
        ReconnectInitialDelayMilliseconds = source.ReconnectInitialDelayMilliseconds,
        ReconnectMaxDelayMilliseconds = source.ReconnectMaxDelayMilliseconds,
        RetentionSeconds = source.RetentionSeconds
    };

    private static MqttOptions CloneMqttOptions(MqttOptions source) => new()
    {
        Enabled = source.Enabled, Host = source.Host, Port = source.Port, ProtocolVersion = source.ProtocolVersion,
        ClientId = source.ClientId, Username = source.Username, PasswordReference = source.PasswordReference,
        UseTls = source.UseTls, BaseTopic = source.BaseTopic, TopicTemplate = source.TopicTemplate,
        KeepAliveSeconds = source.KeepAliveSeconds, ConnectionTimeoutMilliseconds = source.ConnectionTimeoutMilliseconds,
        PublishTimeoutMilliseconds = source.PublishTimeoutMilliseconds,
        ReconnectInitialDelayMilliseconds = source.ReconnectInitialDelayMilliseconds,
        ReconnectMaxDelayMilliseconds = source.ReconnectMaxDelayMilliseconds,
        ShutdownTimeoutMilliseconds = source.ShutdownTimeoutMilliseconds,
        Profiles = source.Profiles.Select(profile => new MqttProfileDefinition
        {
            Name = profile.Name, Mode = profile.Mode, Deadband = profile.Deadband,
            MinimumIntervalMilliseconds = profile.MinimumIntervalMilliseconds,
            MaximumIntervalMilliseconds = profile.MaximumIntervalMilliseconds,
            QualityOfService = profile.QualityOfService, Retain = profile.Retain
        }).ToList()
    };

    private static AlarmOptions CloneAlarmOptions(AlarmOptions source) => new()
    {
        Enabled = source.Enabled,
        PersistenceEnabled = source.PersistenceEnabled,
        DatabasePath = source.DatabasePath,
        QueueCapacity = source.QueueCapacity,
        BatchSize = source.BatchSize,
        FlushIntervalMilliseconds = source.FlushIntervalMilliseconds,
        StartupTimeoutMilliseconds = source.StartupTimeoutMilliseconds,
        ShutdownDrainTimeoutMilliseconds = source.ShutdownDrainTimeoutMilliseconds,
        Definitions = source.Definitions.Select(definition => new AlarmDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Message = definition.Message,
            TagId = definition.TagId,
            Enabled = definition.Enabled,
            Order = definition.Order,
            RuleType = definition.RuleType,
            Severity = definition.Severity,
            DigitalExpectedValue = definition.DigitalExpectedValue,
            Threshold = definition.Threshold,
            Deadband = definition.Deadband,
            ActivationDelay = definition.ActivationDelay,
            AcknowledgementRequired = definition.AcknowledgementRequired
        }).ToList()
    };
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
            left.Historian.StorageProvider != right.Historian.StorageProvider ||
            !string.Equals(left.Historian.DatabasePath, right.Historian.DatabasePath, StringComparison.Ordinal) ||
            !InfluxOptionsEqual(left.Historian.Influx, right.Historian.Influx) ||
            !MqttOptionsEqual(left.Mqtt, right.Mqtt) ||
            !AlarmOptionsEqual(left.Alarms, right.Alarms) ||
            left.Historian.QueueCapacity != right.Historian.QueueCapacity ||
            left.Historian.BatchSize != right.Historian.BatchSize ||
            left.Historian.FlushIntervalMilliseconds != right.Historian.FlushIntervalMilliseconds ||
            left.Historian.ShutdownDrainTimeoutMilliseconds != right.Historian.ShutdownDrainTimeoutMilliseconds ||
            left.Historian.Profiles.Count != right.Historian.Profiles.Count ||
            !MachineSettingsEqual(left.MachineSettings, right.MachineSettings) ||
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

    private static bool InfluxOptionsEqual(InfluxDbOptions? left, InfluxDbOptions? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.Url, right.Url, StringComparison.Ordinal) &&
               string.Equals(left.Organization, right.Organization, StringComparison.Ordinal) &&
               string.Equals(left.Bucket, right.Bucket, StringComparison.Ordinal) &&
               string.Equals(left.Measurement, right.Measurement, StringComparison.Ordinal) &&
               string.Equals(left.TokenReference, right.TokenReference, StringComparison.Ordinal) &&
               string.Equals(left.BufferPath, right.BufferPath, StringComparison.Ordinal) &&
               left.MaxBufferedSamples == right.MaxBufferedSamples &&
               left.SyncBatchSize == right.SyncBatchSize &&
               left.SyncIntervalMilliseconds == right.SyncIntervalMilliseconds &&
               left.HealthProbeIntervalMilliseconds == right.HealthProbeIntervalMilliseconds &&
               left.ConnectionTimeoutMilliseconds == right.ConnectionTimeoutMilliseconds &&
               left.WriteTimeoutMilliseconds == right.WriteTimeoutMilliseconds &&
               left.QueryTimeoutMilliseconds == right.QueryTimeoutMilliseconds &&
               left.ReconnectInitialDelayMilliseconds == right.ReconnectInitialDelayMilliseconds &&
               left.ReconnectMaxDelayMilliseconds == right.ReconnectMaxDelayMilliseconds &&
               left.RetentionSeconds == right.RetentionSeconds;
    }

    private static bool MqttOptionsEqual(MqttOptions left, MqttOptions right) =>
        left.Enabled == right.Enabled && left.Host == right.Host && left.Port == right.Port &&
        left.ProtocolVersion == right.ProtocolVersion && left.ClientId == right.ClientId && left.Username == right.Username &&
        left.PasswordReference == right.PasswordReference && left.UseTls == right.UseTls && left.BaseTopic == right.BaseTopic &&
        left.TopicTemplate == right.TopicTemplate && left.KeepAliveSeconds == right.KeepAliveSeconds &&
        left.ConnectionTimeoutMilliseconds == right.ConnectionTimeoutMilliseconds && left.PublishTimeoutMilliseconds == right.PublishTimeoutMilliseconds &&
        left.ReconnectInitialDelayMilliseconds == right.ReconnectInitialDelayMilliseconds && left.ReconnectMaxDelayMilliseconds == right.ReconnectMaxDelayMilliseconds &&
        left.ShutdownTimeoutMilliseconds == right.ShutdownTimeoutMilliseconds && left.Profiles.Count == right.Profiles.Count &&
        left.Profiles.Zip(right.Profiles).All(pair => pair.First.Name == pair.Second.Name && pair.First.Mode == pair.Second.Mode && pair.First.Deadband == pair.Second.Deadband && pair.First.MinimumIntervalMilliseconds == pair.Second.MinimumIntervalMilliseconds && pair.First.MaximumIntervalMilliseconds == pair.Second.MaximumIntervalMilliseconds && pair.First.QualityOfService == pair.Second.QualityOfService && pair.First.Retain == pair.Second.Retain);

    private static bool AlarmOptionsEqual(AlarmOptions left, AlarmOptions right) =>
        left.Enabled == right.Enabled && left.PersistenceEnabled == right.PersistenceEnabled &&
        left.DatabasePath == right.DatabasePath && left.QueueCapacity == right.QueueCapacity &&
        left.BatchSize == right.BatchSize && left.FlushIntervalMilliseconds == right.FlushIntervalMilliseconds &&
        left.StartupTimeoutMilliseconds == right.StartupTimeoutMilliseconds &&
        left.ShutdownDrainTimeoutMilliseconds == right.ShutdownDrainTimeoutMilliseconds &&
        left.Definitions.Count == right.Definitions.Count &&
        left.Definitions.Zip(right.Definitions).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name && pair.First.Message == pair.Second.Message &&
            pair.First.TagId == pair.Second.TagId && pair.First.Enabled == pair.Second.Enabled && pair.First.Order == pair.Second.Order &&
            pair.First.RuleType == pair.Second.RuleType && pair.First.Severity == pair.Second.Severity &&
            pair.First.DigitalExpectedValue == pair.Second.DigitalExpectedValue && pair.First.Threshold == pair.Second.Threshold &&
            pair.First.Deadband == pair.Second.Deadband && pair.First.ActivationDelay == pair.Second.ActivationDelay &&
            pair.First.AcknowledgementRequired == pair.Second.AcknowledgementRequired);

    private static bool MachineSettingsEqual(MachineSettingsOptions left, MachineSettingsOptions right) =>
        left.Pages.Count == right.Pages.Count && left.Pages.Zip(right.Pages).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Title == pair.Second.Title && pair.First.Description == pair.Second.Description &&
            pair.First.Group == pair.Second.Group && pair.First.Order == pair.Second.Order && pair.First.IsVisible == pair.Second.IsVisible &&
            pair.First.Parameters.Count == pair.Second.Parameters.Count && pair.First.Parameters.Zip(pair.Second.Parameters).All(items =>
                items.First.Id == items.Second.Id && items.First.Name == items.Second.Name && items.First.Description == items.Second.Description && items.First.Group == items.Second.Group &&
                items.First.ValueType == items.Second.ValueType && items.First.Value == items.Second.Value && items.First.Min == items.Second.Min && items.First.Max == items.Second.Max &&
                items.First.Unit == items.Second.Unit && items.First.IsReadOnly == items.Second.IsReadOnly && items.First.IsVisible == items.Second.IsVisible &&
                items.First.Order == items.Second.Order && items.First.LiveTagId == items.Second.LiveTagId));

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
