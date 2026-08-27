using System.Globalization;
using System.Text;
using Scada.Core.Tags;

namespace Scada.App.Services;

internal static class TagTableCodec
{
    public static readonly string[] Headers =
    [
        "Id", "Name", "Description", "DeviceId", "Address", "SourceDataType", "DataType", "Scale", "Offset", "Enabled", "ScanGroup",
        "AccessMode", "Min", "Max", "Unit", "HistoryEnabled", "HistoryProfile", "MqttPublishEnabled",
        "MqttProfile", "MqttTopicOverride"
    ];

    public static string Export(IEnumerable<TagDefinition> tags, char delimiter)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(delimiter, Headers.Select(header => DelimitedTextParser.Escape(header, delimiter))));
        foreach (var tag in tags)
        {
            var fields = new[]
            {
                tag.Id,
                tag.Name,
                tag.Description,
                tag.DeviceId,
                tag.Address,
                tag.GetEffectiveSourceDataType().ToString(),
                tag.DataType.ToString(),
                FormatNumber(tag.Scale),
                FormatNumber(tag.Offset),
                tag.Enabled.ToString(CultureInfo.InvariantCulture),
                tag.ScanGroup,
                tag.AccessMode.ToString(),
                FormatNumber(tag.Min),
                FormatNumber(tag.Max),
                tag.Unit,
                tag.HistoryEnabled.ToString(CultureInfo.InvariantCulture),
                tag.HistoryProfile,
                tag.MqttPublishEnabled.ToString(CultureInfo.InvariantCulture),
                tag.MqttProfile,
                tag.MqttTopicOverride
            };
            builder.AppendLine(string.Join(delimiter, fields.Select(field => DelimitedTextParser.Escape(field, delimiter))));
        }

        return builder.ToString();
    }

    public static IReadOnlyList<TagDefinition> Import(string text, char delimiter)
    {
        var rows = DelimitedTextParser.Parse(text, delimiter);
        if (rows.Count == 0)
        {
            throw new FormatException("The tag table does not contain a header row.");
        }

        var headerIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows[0].Count; index++)
        {
            var header = rows[0][index].Trim();
            if (string.IsNullOrWhiteSpace(header))
            {
                throw new FormatException("The tag table contains an empty header.");
            }

            if (!headerIndexes.TryAdd(header, index))
            {
                throw new FormatException($"The tag table contains duplicate header '{header}'.");
            }
        }

        RequireHeader(headerIndexes, "Name");
        var tags = new List<TagDefinition>();
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0]))
            {
                continue;
            }

            var tag = new TagDefinition
            {
                Id = Get(row, headerIndexes, "Id"),
                Name = Get(row, headerIndexes, "Name"),
                Description = Get(row, headerIndexes, "Description"),
                DeviceId = Get(row, headerIndexes, "DeviceId"),
                Address = Get(row, headerIndexes, "Address"),
                ScanGroup = GetOrDefault(row, headerIndexes, "ScanGroup", "Normal"),
                Unit = Get(row, headerIndexes, "Unit"),
                HistoryProfile = GetOrDefault(row, headerIndexes, "HistoryProfile", "Analog"),
                MqttProfile = GetOrDefault(row, headerIndexes, "MqttProfile", "Default"),
                MqttTopicOverride = Get(row, headerIndexes, "MqttTopicOverride")
            };

            tag.DataType = ParseEnum(row, headerIndexes, "DataType", TagDataType.Double);
            tag.SourceDataType = ParseEnum(row, headerIndexes, "SourceDataType", tag.DataType);
            tag.Scale = ParseDoubleOrDefault(row, headerIndexes, "Scale", 1d);
            tag.Offset = ParseDoubleOrDefault(row, headerIndexes, "Offset", 0d);
            tag.AccessMode = ParseEnum(row, headerIndexes, "AccessMode", TagAccessMode.ReadOnly);
            tag.Enabled = ParseBoolean(row, headerIndexes, "Enabled", true);
            tag.Min = ParseNullableDouble(row, headerIndexes, "Min");
            tag.Max = ParseNullableDouble(row, headerIndexes, "Max");
            tag.HistoryEnabled = ParseBoolean(row, headerIndexes, "HistoryEnabled", false);
            tag.MqttPublishEnabled = ParseBoolean(row, headerIndexes, "MqttPublishEnabled", false);
            tags.Add(tag);
        }

        return tags;
    }

    private static string FormatNumber(double? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatNumber(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static void RequireHeader(IReadOnlyDictionary<string, int> indexes, string name)
    {
        if (!indexes.ContainsKey(name))
        {
            throw new FormatException($"The tag table is missing required header '{name}'.");
        }
    }

    private static string Get(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> indexes, string name)
    {
        return indexes.TryGetValue(name, out var index) && index < row.Count ? row[index] : string.Empty;
    }

    private static string GetOrDefault(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> indexes,
        string name,
        string defaultValue) =>
        string.IsNullOrEmpty(Get(row, indexes, name)) ? defaultValue : Get(row, indexes, name);

    private static TEnum ParseEnum<TEnum>(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> indexes,
        string name,
        TEnum defaultValue)
        where TEnum : struct, Enum
    {
        var value = Get(row, indexes, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) && Enum.IsDefined(result))
        {
            return result;
        }

        throw new FormatException($"Value '{value}' is not valid for {name}.");
    }

    private static bool ParseBoolean(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> indexes,
        string name,
        bool defaultValue)
    {
        var value = Get(row, indexes, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        return value.Trim() switch
        {
            "1" or "yes" or "Yes" or "YES" => true,
            "0" or "no" or "No" or "NO" => false,
            _ => throw new FormatException($"Value '{value}' is not a valid boolean for {name}.")
        };
    }

    private static double? ParseNullableDouble(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> indexes,
        string name)
    {
        var value = Get(row, indexes, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        throw new FormatException($"Value '{value}' is not a valid number for {name}.");
    }

    private static double ParseDoubleOrDefault(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> indexes,
        string name,
        double defaultValue)
    {
        var value = Get(row, indexes, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) &&
            double.IsFinite(result))
        {
            return result;
        }

        throw new FormatException($"Value '{value}' is not a valid finite number for {name}.");
    }
}
