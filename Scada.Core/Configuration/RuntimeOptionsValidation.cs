using Scada.Core.Tags;
using Scada.Core.History;
using System.Globalization;
using System.Text.RegularExpressions;
using Scada.Core.Mqtt;

namespace Scada.Core.Configuration;

public static class RuntimeOptionsValidation
{
    private const int MaximumInfluxBufferedSamples = 10_000_000;
    private const int MaximumInfluxBatchSize = 10_000;
    private const int MaximumInfluxIntervalMilliseconds = 86_400_000;
    private const int MaximumInfluxTimeoutMilliseconds = 300_000;
    private const long MinimumInfluxRetentionSeconds = 3_600;
    private static readonly Regex EnvironmentVariableName =
        new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<ValidationIssue> CollectIssues(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(options.RuntimeId))
        {
            issues.Add(Error("RUNTIME_ID_REQUIRED", "Runtime", options.RuntimeId, nameof(options.RuntimeId),
                "RuntimeId is required."));
        }

        ValidatePolling(options, issues);
        var historian = options.Historian ?? new HistorianOptions();
        var mqtt = options.Mqtt ?? new MqttOptions();
        ValidateHistorianStorage(historian, issues);
        issues.AddRange(HistoryProfileValidation.CollectIssues(historian));
        ValidateMqtt(mqtt, issues);

        var historyRegistry = new HistoryProfileRegistry(historian.Profiles ?? []);

        var scanGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scanGroup in options.ScanGroups ?? [])
        {
            if (string.IsNullOrWhiteSpace(scanGroup.Name))
            {
                issues.Add(Error("SCAN_GROUP_NAME_REQUIRED", "ScanGroup", null, nameof(scanGroup.Name),
                    "Scan group name is required."));
            }
            else if (!scanGroups.Add(scanGroup.Name))
            {
                issues.Add(Error("SCAN_GROUP_DUPLICATE", "ScanGroup", scanGroup.Name, nameof(scanGroup.Name),
                    $"Scan group '{scanGroup.Name}' is duplicated."));
            }

            if (scanGroup.IntervalMilliseconds <= 0)
            {
                issues.Add(Error("SCAN_GROUP_INTERVAL_INVALID", "ScanGroup", scanGroup.Name,
                    nameof(scanGroup.IntervalMilliseconds),
                    $"Scan group '{scanGroup.Name}' interval must be greater than zero."));
            }
        }

        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in options.Devices ?? [])
        {
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                issues.Add(Error("DEVICE_ID_REQUIRED", "Device", null, nameof(device.Id),
                    "Device id is required."));
            }
            else if (!deviceIds.Add(device.Id))
            {
                issues.Add(Error("DEVICE_ID_DUPLICATE", "Device", device.Id, nameof(device.Id),
                    $"Device id '{device.Id}' is duplicated."));
            }

            if (device.Enabled && string.IsNullOrWhiteSpace(device.DriverType))
            {
                issues.Add(Error("DEVICE_DRIVER_REQUIRED", "Device", device.Id, nameof(device.DriverType),
                    $"Enabled device '{device.Id}' requires a driver type."));
            }
        }

        var tagIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validHistoryTagCount = 0;
        var mqttTopics = new HashSet<string>(StringComparer.Ordinal);
        var mqttRegistry = new MqttProfileRegistry(mqtt.Profiles ?? []);
        foreach (var tag in options.Tags ?? [])
        {
            ValidateTag(tag, deviceIds, scanGroups, tagIds, tagNames, historyRegistry, mqttRegistry, mqtt, issues);
            if (mqtt.Enabled && tag.Enabled && tag.MqttPublishEnabled && mqttRegistry.TryGet(tag.MqttProfile, out _) && MqttTopicBuilder.TryBuild(options.RuntimeId, tag, mqtt, out var topic) && !mqttTopics.Add(topic!))
                issues.Add(Error("MQTT_TOPIC_DUPLICATE", "Tag", tag.Id, nameof(tag.MqttTopicOverride), $"MQTT topic '{topic}' is used by more than one publishing tag."));
            else if (mqtt.Enabled && tag.Enabled && tag.MqttPublishEnabled && mqttRegistry.TryGet(tag.MqttProfile, out _) && !MqttTopicBuilder.TryBuild(options.RuntimeId, tag, mqtt, out _))
                issues.Add(Error("MQTT_TOPIC_INVALID", "Tag", tag.Id, nameof(tag.MqttTopicOverride), "MQTT publish topic is invalid."));
            if (IsValidHistorySubscription(tag, historyRegistry))
            {
                validHistoryTagCount++;
            }
        }

        if (historian.QueueCapacity < validHistoryTagCount)
        {
            issues.Add(Warning(
                "HISTORIAN_QUEUE_CAPACITY_BELOW_HISTORY_TAGS",
                "Historian",
                null,
                nameof(historian.QueueCapacity),
                $"Historian queue capacity {historian.QueueCapacity} is below the {validHistoryTagCount} valid history-enabled tags; samples may be dropped under load."));
        }

        return issues;
    }

    private static void ValidateMqtt(MqttOptions mqtt, ICollection<ValidationIssue> issues)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in mqtt.Profiles ?? [])
        {
            if (string.IsNullOrWhiteSpace(profile.Name) || !names.Add(profile.Name))
                issues.Add(Error("MQTT_PROFILE_NAME_INVALID", "Mqtt", profile.Name, nameof(profile.Name), "MQTT profile names must be nonempty and unique."));
            if (!Enum.IsDefined(profile.Mode) || !Enum.IsDefined(profile.QualityOfService) || profile.Deadband < 0 || profile.MinimumIntervalMilliseconds < 0 || profile.MaximumIntervalMilliseconds < 0 || (profile.MaximumIntervalMilliseconds > 0 && profile.MinimumIntervalMilliseconds > profile.MaximumIntervalMilliseconds))
                issues.Add(Error("MQTT_PROFILE_INVALID", "Mqtt", profile.Name, null, "MQTT profile settings are invalid."));
        }
        if (!mqtt.Enabled) return;
        if (string.IsNullOrWhiteSpace(mqtt.Host) || mqtt.Host.Any(char.IsControl) || mqtt.Host.Contains('@')) issues.Add(Error("MQTT_HOST_INVALID", "Mqtt", null, nameof(mqtt.Host), "MQTT host is invalid."));
        if (mqtt.Port is < 1 or > 65535) issues.Add(Error("MQTT_PORT_INVALID", "Mqtt", null, nameof(mqtt.Port), "MQTT port must be 1 through 65535."));
        if (!Enum.IsDefined(mqtt.ProtocolVersion)) issues.Add(Error("MQTT_PROTOCOL_INVALID", "Mqtt", null, nameof(mqtt.ProtocolVersion), "MQTT protocol is invalid."));
        if (!string.IsNullOrWhiteSpace(mqtt.PasswordReference) && !TryParseEnvironmentReference(mqtt.PasswordReference, out _)) issues.Add(Error("MQTT_PASSWORD_REFERENCE_INVALID", "Mqtt", null, nameof(mqtt.PasswordReference), "MQTT PasswordReference must use env:<VARIABLE_NAME>."));
        if (mqtt.ConnectionTimeoutMilliseconds <= 0 || mqtt.PublishTimeoutMilliseconds <= 0 || mqtt.ReconnectInitialDelayMilliseconds <= 0 || mqtt.ReconnectMaxDelayMilliseconds < mqtt.ReconnectInitialDelayMilliseconds || mqtt.ShutdownTimeoutMilliseconds <= 0) issues.Add(Error("MQTT_OPTIONS_INVALID", "Mqtt", null, null, "MQTT timeout and reconnect settings are invalid."));
    }

    public static IReadOnlyList<ValidationIssue> CollectInfluxIssues(InfluxDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var issues = new List<ValidationIssue>();
        ValidateHistorianStorage(
            new HistorianOptions
            {
                Enabled = true,
                StorageProvider = HistoryStorageProvider.InfluxDb2,
                Influx = options
            },
            issues);
        return issues;
    }

    private static void ValidateHistorianStorage(
        HistorianOptions historian,
        ICollection<ValidationIssue> issues)
    {
        if (!Enum.IsDefined(historian.StorageProvider))
        {
            issues.Add(Error(
                "HISTORIAN_STORAGE_PROVIDER_INVALID",
                "Historian",
                null,
                nameof(historian.StorageProvider),
                "Historian storage provider is invalid."));
            return;
        }

        var influx = historian.Influx ?? new InfluxDbOptions();
        if (historian.StorageProvider != HistoryStorageProvider.InfluxDb2)
        {
            return;
        }

        if (!Uri.TryCreate(influx.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            issues.Add(Error(
                "INFLUX_URL_INVALID",
                "Historian",
                null,
                nameof(influx.Url),
                "InfluxDB URL must be an absolute HTTP or HTTPS URL without embedded credentials."));
        }

        ValidateRequiredInfluxName(influx.Organization, "INFLUX_ORGANIZATION_REQUIRED", nameof(influx.Organization), "Organization", issues);
        ValidateRequiredInfluxName(influx.Bucket, "INFLUX_BUCKET_REQUIRED", nameof(influx.Bucket), "Bucket", issues);
        ValidateRequiredInfluxName(influx.Measurement, "INFLUX_MEASUREMENT_REQUIRED", nameof(influx.Measurement), "Measurement", issues);

        if (!TryParseEnvironmentReference(influx.TokenReference, out _))
        {
            issues.Add(Error(
                "INFLUX_TOKEN_REFERENCE_INVALID",
                "Historian",
                null,
                nameof(influx.TokenReference),
                "InfluxDB TokenReference must use the env:<VARIABLE_NAME> format."));
        }

        if (IsInvalidProjectRelativePath(influx.BufferPath))
        {
            issues.Add(Error(
                "INFLUX_BUFFER_PATH_INVALID",
                "Historian",
                null,
                nameof(influx.BufferPath),
                "InfluxDB buffer path must be a project-relative path without traversal."));
        }

        if (influx.MaxBufferedSamples <= 0 || influx.MaxBufferedSamples > MaximumInfluxBufferedSamples)
        {
            issues.Add(Error(
                "INFLUX_BUFFER_CAPACITY_INVALID",
                "Historian",
                null,
                nameof(influx.MaxBufferedSamples),
                $"InfluxDB buffer capacity must be between 1 and {MaximumInfluxBufferedSamples.ToString(CultureInfo.InvariantCulture)} samples."));
        }

        if (influx.SyncBatchSize <= 0 ||
            influx.SyncBatchSize > MaximumInfluxBatchSize ||
            (influx.MaxBufferedSamples > 0 && influx.SyncBatchSize > influx.MaxBufferedSamples))
        {
            issues.Add(Error(
                "INFLUX_SYNC_BATCH_INVALID",
                "Historian",
                null,
                nameof(influx.SyncBatchSize),
                $"InfluxDB sync batch size must be between 1 and {MaximumInfluxBatchSize.ToString(CultureInfo.InvariantCulture)} and not exceed buffer capacity."));
        }

        ValidatePositiveBounded(influx.SyncIntervalMilliseconds, MaximumInfluxIntervalMilliseconds,
            nameof(influx.SyncIntervalMilliseconds), "INFLUX_SYNC_INTERVAL_INVALID", issues);
        ValidatePositiveBounded(influx.HealthProbeIntervalMilliseconds, MaximumInfluxIntervalMilliseconds,
            nameof(influx.HealthProbeIntervalMilliseconds), "INFLUX_HEALTH_INTERVAL_INVALID", issues);
        ValidatePositiveBounded(influx.ConnectionTimeoutMilliseconds, MaximumInfluxTimeoutMilliseconds,
            nameof(influx.ConnectionTimeoutMilliseconds), "INFLUX_CONNECTION_TIMEOUT_INVALID", issues);
        ValidatePositiveBounded(influx.WriteTimeoutMilliseconds, MaximumInfluxTimeoutMilliseconds,
            nameof(influx.WriteTimeoutMilliseconds), "INFLUX_WRITE_TIMEOUT_INVALID", issues);
        ValidatePositiveBounded(influx.QueryTimeoutMilliseconds, MaximumInfluxTimeoutMilliseconds,
            nameof(influx.QueryTimeoutMilliseconds), "INFLUX_QUERY_TIMEOUT_INVALID", issues);
        ValidatePositiveBounded(influx.ReconnectInitialDelayMilliseconds, MaximumInfluxIntervalMilliseconds,
            nameof(influx.ReconnectInitialDelayMilliseconds), "INFLUX_RECONNECT_INITIAL_INVALID", issues);
        ValidatePositiveBounded(influx.ReconnectMaxDelayMilliseconds, MaximumInfluxIntervalMilliseconds,
            nameof(influx.ReconnectMaxDelayMilliseconds), "INFLUX_RECONNECT_MAX_INVALID", issues);

        if (influx.ReconnectInitialDelayMilliseconds > influx.ReconnectMaxDelayMilliseconds)
        {
            issues.Add(Error(
                "INFLUX_RECONNECT_RANGE_INVALID",
                "Historian",
                null,
                nameof(influx.ReconnectInitialDelayMilliseconds),
                "InfluxDB reconnect initial delay cannot exceed the maximum delay."));
        }

        if (influx.RetentionSeconds is > 0 and < MinimumInfluxRetentionSeconds)
        {
            issues.Add(Error(
                "INFLUX_RETENTION_INVALID",
                "Historian",
                null,
                nameof(influx.RetentionSeconds),
                $"Finite InfluxDB retention must be zero or at least {MinimumInfluxRetentionSeconds.ToString(CultureInfo.InvariantCulture)} seconds."));
        }
        else if (influx.RetentionSeconds < 0)
        {
            issues.Add(Error(
                "INFLUX_RETENTION_INVALID",
                "Historian",
                null,
                nameof(influx.RetentionSeconds),
                "InfluxDB retention cannot be negative."));
        }
    }

    private static void ValidateRequiredInfluxName(
        string? value,
        string code,
        string propertyName,
        string displayName,
        ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            issues.Add(Error(code, "Historian", null, propertyName,
                $"InfluxDB {displayName} is required and cannot contain control characters."));
        }
    }

    private static void ValidatePositiveBounded(
        int value,
        int maximum,
        string propertyName,
        string code,
        ICollection<ValidationIssue> issues)
    {
        if (value <= 0 || value > maximum)
        {
            issues.Add(Error(code, "Historian", null, propertyName,
                $"InfluxDB {propertyName} must be between 1 and {maximum.ToString(CultureInfo.InvariantCulture)}."));
        }
    }

    private static bool TryParseEnvironmentReference(string? value, out string variableName)
    {
        variableName = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("env:", StringComparison.Ordinal))
        {
            return false;
        }

        variableName = value[4..];
        return EnvironmentVariableName.IsMatch(variableName);
    }

    private static bool IsInvalidProjectRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return true;
        }

        return path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static bool IsValidHistorySubscription(
        TagDefinition tag,
        HistoryProfileRegistry historyRegistry) =>
        tag.Enabled &&
        tag.HistoryEnabled &&
        historyRegistry.TryGet(tag.HistoryProfile, out var profile) &&
        profile is not null &&
        HistoryProfileValidation.IsCompatible(tag.DataType, profile);

    private static void ValidatePolling(RuntimeOptions options, ICollection<ValidationIssue> issues)
    {
        if (options.Polling is null ||
            options.Polling.ConnectTimeoutMilliseconds <= 0 ||
            options.Polling.ReadTimeoutMilliseconds <= 0 ||
            options.Polling.DisconnectTimeoutMilliseconds <= 0 ||
            options.Polling.InitialReconnectDelayMilliseconds <= 0 ||
            options.Polling.MaxReconnectDelayMilliseconds <= 0 ||
            options.Polling.InitialReconnectDelayMilliseconds > options.Polling.MaxReconnectDelayMilliseconds ||
            options.Polling.ShutdownTimeoutMilliseconds <= 0)
        {
            issues.Add(Error("POLLING_OPTIONS_INVALID", "Runtime", options.RuntimeId, nameof(options.Polling),
                "Polling timeout, reconnect and shutdown settings are invalid."));
        }
    }

    private static void ValidateTag(
        TagDefinition tag,
        ISet<string> deviceIds,
        ISet<string> scanGroups,
        ISet<string> tagIds,
        ISet<string> tagNames,
        HistoryProfileRegistry historyRegistry,
        MqttProfileRegistry mqttRegistry,
        MqttOptions mqtt,
        ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(tag.Id))
        {
            issues.Add(Error("TAG_ID_REQUIRED", "Tag", null, nameof(tag.Id), "Tag id is required."));
        }
        else if (!tagIds.Add(tag.Id))
        {
            issues.Add(Error("TAG_ID_DUPLICATE", "Tag", tag.Id, nameof(tag.Id),
                $"Tag id '{tag.Id}' is duplicated."));
        }

        if (string.IsNullOrWhiteSpace(tag.Name))
        {
            issues.Add(Error("TAG_NAME_REQUIRED", "Tag", tag.Id, nameof(tag.Name), "Tag name is required."));
        }
        else if (!tagNames.Add(tag.Name))
        {
            issues.Add(Error("TAG_NAME_DUPLICATE", "Tag", tag.Id, nameof(tag.Name),
                $"Tag name '{tag.Name}' is duplicated."));
        }

        if (!deviceIds.Contains(tag.DeviceId))
        {
            issues.Add(Error("TAG_DEVICE_MISSING", "Tag", tag.Id, nameof(tag.DeviceId),
                $"Tag '{tag.Id}' references missing device '{tag.DeviceId}'."));
        }

        if (string.IsNullOrWhiteSpace(tag.Address))
        {
            issues.Add(Error("TAG_ADDRESS_REQUIRED", "Tag", tag.Id, nameof(tag.Address),
                $"Tag '{tag.Id}' requires an address."));
        }

        if (tag.Enabled && !scanGroups.Contains(tag.ScanGroup))
        {
            issues.Add(Error("TAG_SCAN_GROUP_MISSING", "Tag", tag.Id, nameof(tag.ScanGroup),
                $"Tag '{tag.Id}' references missing scan group '{tag.ScanGroup}'."));
        }

        if (!Enum.IsDefined(tag.DataType))
        {
            issues.Add(Error("TAG_DATA_TYPE_INVALID", "Tag", tag.Id, nameof(tag.DataType),
                $"Tag '{tag.Id}' has an invalid data type."));
        }

        if (!Enum.IsDefined(tag.AccessMode))
        {
            issues.Add(Error("TAG_ACCESS_MODE_INVALID", "Tag", tag.Id, nameof(tag.AccessMode),
                $"Tag '{tag.Id}' has an invalid access mode."));
        }

        if (tag.Min.HasValue && tag.Max.HasValue && tag.Min.Value > tag.Max.Value)
        {
            issues.Add(Error("TAG_RANGE_INVALID", "Tag", tag.Id, nameof(tag.Min),
                $"Tag '{tag.Id}' minimum cannot be greater than maximum."));
        }

        HistoryProfileDefinition? historyProfile = null;
        if (tag.HistoryEnabled && string.IsNullOrWhiteSpace(tag.HistoryProfile))
        {
            issues.Add(Error("HISTORY_PROFILE_REQUIRED", "Tag", tag.Id, nameof(tag.HistoryProfile),
                $"Tag '{tag.Id}' requires a history profile when history is enabled."));
        }
        else if (!string.IsNullOrWhiteSpace(tag.HistoryProfile) && !historyRegistry.TryGet(tag.HistoryProfile, out historyProfile))
        {
            issues.Add(Warning("HISTORY_PROFILE_UNKNOWN", "Tag", tag.Id, nameof(tag.HistoryProfile),
                $"History profile '{tag.HistoryProfile}' is not configured and will be preserved."));
        }
        else if (tag.HistoryEnabled && historyProfile is not null &&
                 !HistoryProfileValidation.IsCompatible(tag.DataType, historyProfile))
        {
            issues.Add(Warning("HISTORY_PROFILE_TYPE_INCOMPATIBLE", "Tag", tag.Id, nameof(tag.DataType),
                $"Tag '{tag.Id}' data type '{tag.DataType}' is incompatible with history profile '{historyProfile.Name}'; Historian will skip it."));
        }

        if (tag.MqttPublishEnabled && string.IsNullOrWhiteSpace(tag.MqttProfile))
        {
            issues.Add(Error("MQTT_PROFILE_REQUIRED", "Tag", tag.Id, nameof(tag.MqttProfile),
                $"Tag '{tag.Id}' requires an MQTT profile when publishing is enabled."));
        }
        else if (!string.IsNullOrWhiteSpace(tag.MqttProfile) && !mqttRegistry.TryGet(tag.MqttProfile, out _))
        {
            issues.Add(Warning("MQTT_PROFILE_UNKNOWN", "Tag", tag.Id, nameof(tag.MqttProfile),
                $"MQTT profile '{tag.MqttProfile}' is not configured and will be preserved."));
        }
    }

    private static ValidationIssue Error(
        string code,
        string objectType,
        string? objectId,
        string? propertyName,
        string message) =>
        new(code, ValidationSeverity.Error, objectType, objectId, propertyName, message);

    private static ValidationIssue Warning(
        string code,
        string objectType,
        string? objectId,
        string? propertyName,
        string message) =>
        new(code, ValidationSeverity.Warning, objectType, objectId, propertyName, message);
}
