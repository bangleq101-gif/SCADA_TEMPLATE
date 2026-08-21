using System.Text;
using Scada.Core.Tags;

namespace Scada.Drivers.Simulator;

public sealed class SimulatorValueGenerator
{
    public object Generate(string tagId, string address, TagDataType dataType, DateTimeOffset now)
    {
        var seed = (long)StableSeed(tagId, address);
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

    private static uint StableSeed(string tagId, string address)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var value in Encoding.UTF8.GetBytes(tagId))
        {
            hash = unchecked((hash ^ value) * prime);
        }

        hash = unchecked(hash * prime);

        foreach (var value in Encoding.UTF8.GetBytes(address))
        {
            hash = unchecked((hash ^ value) * prime);
        }

        return hash;
    }
}
