namespace Scada.Core.Drivers;

public sealed record DriverOptionDefinition(
    string Key,
    string DisplayName,
    DriverOptionValueType ValueType,
    string DefaultValue,
    bool IsRequired = false,
    bool IsAdvanced = false,
    string? Description = null);
