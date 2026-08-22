using Scada.Core.Configuration;
using Scada.Core.Tags;

namespace Scada.Core.History;

public static class HistoryProfileValidation
{
    public static IReadOnlyList<ValidationIssue> CollectIssues(HistorianOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var issues = new List<ValidationIssue>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in options.Profiles ?? [])
        {
            if (profile is null)
            {
                issues.Add(Error("HISTORY_PROFILE_NAME_REQUIRED", "HistoryProfile", null, nameof(HistoryProfileDefinition.Name),
                    "History profile name is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                issues.Add(Error("HISTORY_PROFILE_NAME_REQUIRED", "HistoryProfile", null, nameof(profile.Name),
                    "History profile name is required."));
                continue;
            }

            if (!names.Add(profile.Name))
            {
                issues.Add(Error("HISTORY_PROFILE_DUPLICATE", "HistoryProfile", profile.Name, nameof(profile.Name),
                    $"History profile '{profile.Name}' is duplicated."));
            }

            if (!Enum.IsDefined(profile.Mode))
            {
                issues.Add(Error("HISTORY_PROFILE_MODE_INVALID", "HistoryProfile", profile.Name, nameof(profile.Mode),
                    $"History profile '{profile.Name}' has an invalid mode."));
            }

            if (!double.IsFinite(profile.Deadband) || profile.Deadband < 0)
            {
                issues.Add(Error("HISTORY_PROFILE_DEADBAND_INVALID", "HistoryProfile", profile.Name,
                    nameof(profile.Deadband),
                    $"History profile '{profile.Name}' deadband must be finite and non-negative."));
            }

            if (profile.MinimumIntervalMilliseconds < 0)
            {
                issues.Add(Error("HISTORY_PROFILE_MINIMUM_INTERVAL_INVALID", "HistoryProfile", profile.Name,
                    nameof(profile.MinimumIntervalMilliseconds),
                    $"History profile '{profile.Name}' minimum interval must be non-negative."));
            }

            if (profile.MaximumIntervalMilliseconds < 0 ||
                (profile.Mode is HistoryMode.Periodic or HistoryMode.OnChangeAndPeriodic &&
                 profile.MaximumIntervalMilliseconds <= 0))
            {
                issues.Add(Error("HISTORY_PROFILE_MAXIMUM_INTERVAL_INVALID", "HistoryProfile", profile.Name,
                    nameof(profile.MaximumIntervalMilliseconds),
                    $"History profile '{profile.Name}' maximum interval is invalid for its mode."));
            }
        }

        foreach (var requiredName in HistoryProfileDefaults.RequiredNames)
        {
            if (!names.Contains(requiredName))
            {
                issues.Add(Error("HISTORY_PROFILE_REQUIRED_BUILTIN", "HistoryProfile", requiredName, nameof(options.Profiles),
                    $"Required built-in history profile '{requiredName}' is missing."));
            }
        }

        if (options.QueueCapacity <= 0)
        {
            issues.Add(Error("HISTORIAN_QUEUE_CAPACITY_INVALID", "Historian", null, nameof(options.QueueCapacity),
                "Historian queue capacity must be greater than zero."));
        }

        if (options.BatchSize <= 0)
        {
            issues.Add(Error("HISTORIAN_BATCH_SIZE_INVALID", "Historian", null, nameof(options.BatchSize),
                "Historian batch size must be greater than zero."));
        }

        if (options.FlushIntervalMilliseconds <= 0)
        {
            issues.Add(Error("HISTORIAN_FLUSH_INTERVAL_INVALID", "Historian", null,
                nameof(options.FlushIntervalMilliseconds),
                "Historian flush interval must be greater than zero."));
        }

        if (options.ShutdownDrainTimeoutMilliseconds <= 0)
        {
            issues.Add(Error("HISTORIAN_SHUTDOWN_TIMEOUT_INVALID", "Historian", null,
                nameof(options.ShutdownDrainTimeoutMilliseconds),
                "Historian shutdown drain timeout must be greater than zero."));
        }

        if (options.Enabled && string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            issues.Add(Error("HISTORIAN_DATABASE_PATH_REQUIRED", "Historian", null, nameof(options.DatabasePath),
                "Enabled Historian requires a database path."));
        }

        return issues;
    }

    public static bool IsCompatible(TagDataType dataType, HistoryProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!Enum.IsDefined(dataType))
        {
            return false;
        }

        if (string.Equals(profile.Name, HistoryProfileDefaults.DigitalName, StringComparison.OrdinalIgnoreCase))
        {
            return dataType is TagDataType.Boolean or TagDataType.Int32 or TagDataType.Int64 or TagDataType.String;
        }

        if (string.Equals(profile.Name, HistoryProfileDefaults.AnalogName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.Name, HistoryProfileDefaults.FastAnalogName, StringComparison.OrdinalIgnoreCase))
        {
            return dataType is TagDataType.Int32 or TagDataType.Int64 or TagDataType.Double;
        }

        return profile.Deadband <= 0 || dataType is TagDataType.Int32 or TagDataType.Int64 or TagDataType.Double;
    }

    private static ValidationIssue Error(
        string code,
        string objectType,
        string? objectId,
        string? propertyName,
        string message) =>
        new(code, ValidationSeverity.Error, objectType, objectId, propertyName, message);
}
