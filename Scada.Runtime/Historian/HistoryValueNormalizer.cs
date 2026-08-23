using Scada.Core.Tags;

namespace Scada.Runtime.Historian;

public static class HistoryValueNormalizer
{
    public static bool TryNormalize(
        TagDataType dataType,
        object? value,
        out object? normalized,
        out string? error)
    {
        normalized = value;
        error = null;

        if (value is null)
        {
            return true;
        }

        var valid = dataType switch
        {
            TagDataType.Boolean => value is bool,
            TagDataType.Int32 => value is int,
            TagDataType.Int64 => value is long,
            TagDataType.Double => value is double doubleValue && double.IsFinite(doubleValue),
            TagDataType.String => value is string,
            _ => false
        };

        if (!valid)
        {
            normalized = null;
            error = value is double doubleValue && !double.IsFinite(doubleValue)
                ? $"Non-finite Double value '{doubleValue}' is not supported."
                : $"Value type '{value.GetType().FullName}' is not compatible with '{dataType}'.";
        }

        return valid;
    }
}
