using Scada.Core.Configuration;
using Scada.Core.Tags;

namespace Scada.Core.MachineSettings;

public static class MachineSettingsValidation
{
    public static IReadOnlyList<ValidationIssue> CollectIssues(MachineSettingsOptions? options, IEnumerable<TagDefinition> tags)
    {
        var issues = new List<ValidationIssue>();
        var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tagCatalog = (tags ?? []).Where(tag => tag.Enabled).Select(tag => tag.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var page in options?.Pages ?? [])
        {
            if (string.IsNullOrWhiteSpace(page.Id)) { issues.Add(Error("MACHINE_PAGE_ID_REQUIRED", null, nameof(page.Id), "Page id is required.")); continue; }
            if (!pageIds.Add(page.Id)) issues.Add(Error("MACHINE_PAGE_ID_DUPLICATE", page.Id, nameof(page.Id), "Page id is duplicated."));
            if (string.IsNullOrWhiteSpace(page.Title)) issues.Add(Error("MACHINE_PAGE_TITLE_REQUIRED", page.Id, nameof(page.Title), "Page title is required."));
            if (page.Order < 0) issues.Add(Error("MACHINE_PAGE_ORDER_INVALID", page.Id, nameof(page.Order), "Page order cannot be negative."));
            var parameterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in page.Parameters ?? [])
            {
                var identity = $"{page.Id}/{parameter.Id}";
                if (string.IsNullOrWhiteSpace(parameter.Id)) { issues.Add(Error("MACHINE_PARAMETER_ID_REQUIRED", identity, nameof(parameter.Id), "Parameter id is required.")); continue; }
                if (!parameterIds.Add(parameter.Id)) issues.Add(Error("MACHINE_PARAMETER_ID_DUPLICATE", identity, nameof(parameter.Id), "Parameter id is duplicated within the page."));
                if (string.IsNullOrWhiteSpace(parameter.Name)) issues.Add(Error("MACHINE_PARAMETER_NAME_REQUIRED", identity, nameof(parameter.Name), "Parameter name is required."));
                if (!Enum.IsDefined(parameter.ValueType)) issues.Add(Error("MACHINE_PARAMETER_TYPE_INVALID", identity, nameof(parameter.ValueType), "Parameter value type is invalid."));
                if (parameter.Order < 0) issues.Add(Error("MACHINE_PARAMETER_ORDER_INVALID", identity, nameof(parameter.Order), "Parameter order cannot be negative."));
                if (!MachineParameterValueCodec.TryNormalizePersisted(parameter.ValueType, parameter.Value, out var normalized)) issues.Add(Error("MACHINE_PARAMETER_VALUE_INVALID", identity, nameof(parameter.Value), "Persisted parameter value is invalid."));
                if ((parameter.Min.HasValue || parameter.Max.HasValue) && parameter.ValueType is not (MachineParameterValueType.Integer or MachineParameterValueType.Decimal)) issues.Add(Error("MACHINE_PARAMETER_BOUNDS_TYPE_INVALID", identity, nameof(parameter.Min), "Bounds require an Integer or Decimal parameter."));
                if (parameter.Min > parameter.Max) issues.Add(Error("MACHINE_PARAMETER_BOUNDS_RANGE_INVALID", identity, nameof(parameter.Min), "Minimum cannot exceed maximum."));
                if (MachineParameterValueCodec.TryGetNumeric(parameter.ValueType, normalized, out var number) && ((parameter.Min.HasValue && number < parameter.Min) || (parameter.Max.HasValue && number > parameter.Max))) issues.Add(Error("MACHINE_PARAMETER_VALUE_RANGE_INVALID", identity, nameof(parameter.Value), "Persisted parameter value is outside bounds."));
                if (!string.IsNullOrWhiteSpace(parameter.LiveTagId) && !tagCatalog.Contains(parameter.LiveTagId)) issues.Add(new ValidationIssue("MACHINE_PARAMETER_LIVE_TAG_UNRESOLVED", ValidationSeverity.Warning, "MachineParameter", identity, nameof(parameter.LiveTagId), "LiveTagId is missing or disabled and will not be subscribed."));
            }
        }
        return issues;
    }

    private static ValidationIssue Error(string code, string? id, string property, string message) => new(code, ValidationSeverity.Error, "MachineParameter", id, property, message);
}
