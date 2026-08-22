namespace Scada.Core.Tags;

public sealed class TagDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public TagDataType DataType { get; set; } = TagDataType.Double;
    public bool Enabled { get; set; } = true;
    public string ScanGroup { get; set; } = "Normal";
    public TagAccessMode AccessMode { get; set; } = TagAccessMode.ReadOnly;
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool HistoryEnabled { get; set; }
    public string HistoryProfile { get; set; } = "Analog";
    public bool MqttPublishEnabled { get; set; }
    public string MqttProfile { get; set; } = "Default";
    public string MqttTopicOverride { get; set; } = string.Empty;
}
