namespace Scada.Core.Tags;

public sealed class ScanGroupDefinition
{
    public string Name { get; set; } = "Normal";
    public int IntervalMilliseconds { get; set; } = 500;
}
