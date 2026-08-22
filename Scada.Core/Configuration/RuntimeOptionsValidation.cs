using Scada.Core.Tags;

namespace Scada.Core.Configuration;

public static class RuntimeOptionsValidation
{
    private static readonly HashSet<string> HistoryProfiles =
        new(StringComparer.OrdinalIgnoreCase) { "Digital", "Analog", "FastAnalog", "Custom" };

    private static readonly HashSet<string> MqttProfiles =
        new(StringComparer.OrdinalIgnoreCase) { "Default" };

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
        foreach (var tag in options.Tags ?? [])
        {
            ValidateTag(tag, deviceIds, scanGroups, tagIds, tagNames, issues);
        }

        return issues;
    }

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

        if (tag.HistoryEnabled && string.IsNullOrWhiteSpace(tag.HistoryProfile))
        {
            issues.Add(Error("HISTORY_PROFILE_REQUIRED", "Tag", tag.Id, nameof(tag.HistoryProfile),
                $"Tag '{tag.Id}' requires a history profile when history is enabled."));
        }
        else if (!string.IsNullOrWhiteSpace(tag.HistoryProfile) && !HistoryProfiles.Contains(tag.HistoryProfile))
        {
            issues.Add(Warning("HISTORY_PROFILE_UNKNOWN", "Tag", tag.Id, nameof(tag.HistoryProfile),
                $"History profile '{tag.HistoryProfile}' is not a built-in M4 profile and will be preserved."));
        }

        if (tag.MqttPublishEnabled && string.IsNullOrWhiteSpace(tag.MqttProfile))
        {
            issues.Add(Error("MQTT_PROFILE_REQUIRED", "Tag", tag.Id, nameof(tag.MqttProfile),
                $"Tag '{tag.Id}' requires an MQTT profile when publishing is enabled."));
        }
        else if (!string.IsNullOrWhiteSpace(tag.MqttProfile) && !MqttProfiles.Contains(tag.MqttProfile))
        {
            issues.Add(Warning("MQTT_PROFILE_UNKNOWN", "Tag", tag.Id, nameof(tag.MqttProfile),
                $"MQTT profile '{tag.MqttProfile}' is not a built-in M4 profile and will be preserved."));
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
