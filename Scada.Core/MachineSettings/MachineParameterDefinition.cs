namespace Scada.Core.MachineSettings;

public sealed class MachineParameterDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public MachineParameterValueType ValueType { get; set; }
    public string Value { get; set; } = string.Empty;
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public bool IsVisible { get; set; } = true;
    public int Order { get; set; }
    public string LiveTagId { get; set; } = string.Empty;
}
