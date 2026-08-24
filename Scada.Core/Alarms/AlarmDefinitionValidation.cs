using Scada.Core.Configuration;
using Scada.Core.Tags;

namespace Scada.Core.Alarms;

public static class AlarmDefinitionValidation
{
    public static IReadOnlyList<ValidationIssue> CollectIssues(
        AlarmOptions? options,
        IReadOnlyCollection<TagDefinition> tags)
    {
        options ??= new AlarmOptions();
        ArgumentNullException.ThrowIfNull(tags);

        var issues = new List<ValidationIssue>();
        ValidateOptions(options, issues);
        var tagMap = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Id))
            .GroupBy(tag => tag.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in options.Definitions ?? [])
        {
            ValidateDefinition(definition, tagMap, ids, issues);
        }

        return issues;
    }

    private static void ValidateOptions(AlarmOptions options, ICollection<ValidationIssue> issues)
    {
        if (options.QueueCapacity <= 0 || options.BatchSize <= 0 ||
            options.BatchSize > options.QueueCapacity || options.FlushIntervalMilliseconds <= 0 ||
            options.ShutdownDrainTimeoutMilliseconds <= 0)
        {
            issues.Add(Error("ALARM_OPTIONS_INVALID", null, null,
                "Alarm queue, batch, flush and shutdown settings must be positive and batch size cannot exceed queue capacity."));
        }

        if (options.PersistenceEnabled && IsInvalidRelativePath(options.DatabasePath))
        {
            issues.Add(Error("ALARM_DATABASE_PATH_INVALID", null, nameof(options.DatabasePath),
                "Alarm database path must be non-empty, project-relative and must not contain traversal."));
        }
    }

    private static void ValidateDefinition(
        AlarmDefinition definition,
        IReadOnlyDictionary<string, TagDefinition> tagMap,
        ISet<string> ids,
        ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            issues.Add(Error("ALARM_ID_REQUIRED", definition.Id, nameof(definition.Id), "Alarm id is required."));
        else if (!ids.Add(definition.Id))
            issues.Add(Error("ALARM_ID_DUPLICATE", definition.Id, nameof(definition.Id), $"Alarm id '{definition.Id}' is duplicated."));

        if (string.IsNullOrWhiteSpace(definition.Name))
            issues.Add(Error("ALARM_NAME_REQUIRED", definition.Id, nameof(definition.Name), "Alarm name is required."));
        if (!Enum.IsDefined(definition.RuleType))
            issues.Add(Error("ALARM_RULE_INVALID", definition.Id, nameof(definition.RuleType), "Alarm rule type is invalid."));
        if (!Enum.IsDefined(definition.Severity))
            issues.Add(Error("ALARM_SEVERITY_INVALID", definition.Id, nameof(definition.Severity), "Alarm severity is invalid."));
        if (!double.IsFinite(definition.Deadband) || definition.Deadband < 0)
            issues.Add(Error("ALARM_DEADBAND_INVALID", definition.Id, nameof(definition.Deadband), "Alarm deadband must be finite and non-negative."));
        if (definition.ActivationDelay < TimeSpan.Zero)
            issues.Add(Error("ALARM_ACTIVATION_DELAY_INVALID", definition.Id, nameof(definition.ActivationDelay), "Alarm activation delay cannot be negative."));

        if (string.IsNullOrWhiteSpace(definition.TagId) || !tagMap.TryGetValue(definition.TagId, out var tag))
        {
            issues.Add(Error("ALARM_TAG_MISSING", definition.Id, nameof(definition.TagId), $"Alarm '{definition.Id}' references an unknown TagId."));
            ValidateRuleFields(definition, null, issues);
            return;
        }

        if (!tag.Enabled)
            issues.Add(Error("ALARM_TAG_DISABLED", definition.Id, nameof(definition.TagId), $"Alarm '{definition.Id}' references disabled tag '{tag.Id}'."));

        ValidateRuleFields(definition, tag.DataType, issues);
    }

    private static void ValidateRuleFields(
        AlarmDefinition definition,
        TagDataType? dataType,
        ICollection<ValidationIssue> issues)
    {
        if (definition.RuleType == AlarmRuleType.DigitalEquals)
        {
            if (definition.DigitalExpectedValue is null || definition.Threshold is not null)
                issues.Add(Error("ALARM_DIGITAL_FIELDS_INVALID", definition.Id, null, "DigitalEquals requires DigitalExpectedValue and forbids Threshold."));
            if (dataType is not null && dataType != TagDataType.Boolean)
                issues.Add(Error("ALARM_TAG_TYPE_MISMATCH", definition.Id, nameof(definition.TagId), "DigitalEquals requires a Boolean tag."));
            return;
        }

        if (definition.Threshold is null || !double.IsFinite(definition.Threshold.Value))
            issues.Add(Error("ALARM_THRESHOLD_INVALID", definition.Id, nameof(definition.Threshold), "Numeric alarm threshold must be finite."));
        if (definition.DigitalExpectedValue is not null)
            issues.Add(Error("ALARM_NUMERIC_FIELDS_INVALID", definition.Id, nameof(definition.DigitalExpectedValue), "Numeric alarms forbid DigitalExpectedValue."));
        if (dataType is not null && dataType is not (TagDataType.Int32 or TagDataType.Int64 or TagDataType.Double))
            issues.Add(Error("ALARM_TAG_TYPE_MISMATCH", definition.Id, nameof(definition.TagId), "Numeric alarms require Int32, Int64 or Double tags."));
    }

    private static bool IsInvalidRelativePath(string? path) =>
        string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
        path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");

    private static ValidationIssue Error(string code, string? objectId, string? propertyName, string message) =>
        new(code, ValidationSeverity.Error, "Alarm", objectId, propertyName, message);
}
