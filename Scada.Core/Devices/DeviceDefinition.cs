namespace Scada.Core.Devices;

public sealed class DeviceDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string DriverType { get; set; } = string.Empty;
    public Dictionary<string, string> ConnectionOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
