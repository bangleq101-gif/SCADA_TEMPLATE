namespace Scada.Core.MachineSettings;

public sealed class MachineSettingsPageDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
    public List<MachineParameterDefinition> Parameters { get; set; } = [];
}
