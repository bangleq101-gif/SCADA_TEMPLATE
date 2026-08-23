using System.Security.Cryptography;
using System.Text;
using Scada.Core.History;

namespace Scada.Infrastructure.History.Influx;

public static class InfluxSampleKey
{
    public static string Create(string destinationFingerprint, HistorySample sample)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFingerprint);
        ArgumentNullException.ThrowIfNull(sample);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteString(writer, destinationFingerprint);
            WriteString(writer, sample.RuntimeId);
            WriteString(writer, sample.TagId);
            writer.Write((int)sample.DataType);
            writer.Write((int)sample.Quality);
            writer.Write(sample.SourceTimestampUtc.UtcDateTime.Ticks);
            writer.Write(sample.RecordedAtUtc.UtcDateTime.Ticks);
            writer.Write(sample.TagSequence);

            if (sample.Value is null)
            {
                writer.Write((byte)0);
            }
            else
            {
                writer.Write((byte)1);
                switch (sample.DataType)
                {
                    case Scada.Core.Tags.TagDataType.Boolean when sample.Value is bool boolean:
                        writer.Write((byte)1);
                        writer.Write(boolean);
                        break;
                    case Scada.Core.Tags.TagDataType.Int32 when sample.Value is int int32:
                        writer.Write((byte)2);
                        writer.Write(int32);
                        break;
                    case Scada.Core.Tags.TagDataType.Int64 when sample.Value is long int64:
                        writer.Write((byte)3);
                        writer.Write(int64);
                        break;
                    case Scada.Core.Tags.TagDataType.Double when sample.Value is double doubleValue && double.IsFinite(doubleValue):
                        writer.Write((byte)4);
                        writer.Write(BitConverter.DoubleToInt64Bits(doubleValue));
                        break;
                    case Scada.Core.Tags.TagDataType.String when sample.Value is string text:
                        writer.Write((byte)5);
                        WriteString(writer, text);
                        break;
                    default:
                        throw new ArgumentException("The HistorySample value does not match its declared data type.", nameof(sample));
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
