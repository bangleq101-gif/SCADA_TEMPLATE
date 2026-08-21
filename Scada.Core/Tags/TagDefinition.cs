namespace Scada.Core.Tags;

public sealed class TagDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public TagDataType DataType { get; set; } = TagDataType.Double;
    public bool Enabled { get; set; } = true;
    public string ScanGroup { get; set; } = "Normal";
}
