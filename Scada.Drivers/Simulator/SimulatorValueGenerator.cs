using Scada.Core.Tags;

namespace Scada.Drivers.Simulator;

public sealed class SimulatorValueGenerator
{
    public object Generate(string tagId, string address, TagDataType dataType, DateTimeOffset now)
    {
        var seed = Math.Abs(HashCode.Combine(tagId, address));
        var seconds = now.ToUnixTimeMilliseconds() / 1000d;

        return dataType switch
        {
            TagDataType.Boolean => ((long)(seconds / 2) + seed) % 2 == 0,
            TagDataType.Int32 => (int)((seconds + seed) % 1000),
            TagDataType.Int64 => (long)((seconds + seed) % 100000),
            TagDataType.String => $"SIM-{tagId}",
            _ => 50d + Math.Sin((seconds + seed) / 5d) * 25d
        };
    }
}
