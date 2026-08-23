using System.Globalization;
using System.Text;
using Scada.Core.History;
using Scada.Core.Tags;

namespace Scada.Infrastructure.History.Influx;

public static class InfluxHistoryPointMapper
{
    public static string ToLineProtocol(InfluxOutboxRow row, string measurement)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(measurement);

        var sample = row.Sample;
        var builder = new StringBuilder();
        builder.Append(EscapeMeasurement(measurement));
        builder.Append(",runtime_id=");
        builder.Append(EscapeTag(sample.RuntimeId));
        builder.Append(",tag_id=");
        builder.Append(EscapeTag(sample.TagId));
        builder.Append(" data_type=\"");
        builder.Append(EscapeString(sample.DataType.ToString()));
        builder.Append("\",quality=\"");
        builder.Append(EscapeString(sample.Quality.ToString()));
        builder.Append("\",has_value=");
        builder.Append(sample.Value is null ? "false" : "true");
        builder.Append(",source_timestamp_utc_ticks=");
        builder.Append(sample.SourceTimestampUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
        builder.Append('i');
        builder.Append(",recorded_at_utc_ticks=");
        builder.Append(sample.RecordedAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
        builder.Append('i');
        builder.Append(",tag_sequence=");
        builder.Append(sample.TagSequence.ToString(CultureInfo.InvariantCulture));
        builder.Append('i');

        if (sample.Value is not null)
        {
            builder.Append(',');
            switch (sample.DataType)
            {
                case TagDataType.Boolean when sample.Value is bool boolean:
                    builder.Append("value_boolean=");
                    builder.Append(boolean ? "true" : "false");
                    break;
                case TagDataType.Int32 when sample.Value is int int32:
                    builder.Append("value_integer=");
                    builder.Append(int32.ToString(CultureInfo.InvariantCulture));
                    builder.Append('i');
                    break;
                case TagDataType.Int64 when sample.Value is long int64:
                    builder.Append("value_integer=");
                    builder.Append(int64.ToString(CultureInfo.InvariantCulture));
                    builder.Append('i');
                    break;
                case TagDataType.Double when sample.Value is double doubleValue && double.IsFinite(doubleValue):
                    builder.Append("value_real=");
                    builder.Append(doubleValue.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case TagDataType.String when sample.Value is string text:
                    builder.Append("value_text=\"");
                    builder.Append(EscapeString(text));
                    builder.Append('"');
                    break;
                default:
                    throw new HistoryStorePermanentException(
                        "INFLUX_VALUE_TYPE_INVALID",
                        $"History sample value does not match declared data type '{sample.DataType}'.");
            }
        }

        builder.Append(' ');
        builder.Append(row.RemoteTimestampNanoseconds.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static string EscapeMeasurement(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace(" ", "\\ ", StringComparison.Ordinal);

    private static string EscapeTag(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal)
            .Replace(" ", "\\ ", StringComparison.Ordinal);

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);
}
