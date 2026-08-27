namespace Scada.Core.Tags;

/// <summary>
/// Converts one driver-owned raw value into the canonical engineering value used by TagCache consumers.
/// </summary>
public static class TagValueTransformer
{
    public static bool TryTransform(
        TagDefinition definition,
        object? rawValue,
        out object? engineeringValue,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var sourceDataType = definition.GetEffectiveSourceDataType();
        var dataType = definition.DataType;
        engineeringValue = null;
        failure = null;

        if (!Enum.IsDefined(sourceDataType) || !Enum.IsDefined(dataType))
        {
            failure = "The source or engineering data type is invalid.";
            return false;
        }

        var sourceIsNumeric = IsNumeric(sourceDataType);
        var targetIsNumeric = IsNumeric(dataType);
        if (!sourceIsNumeric || !targetIsNumeric)
        {
            if (sourceDataType != dataType)
            {
                failure = "Boolean and String tags require identical source and engineering data types.";
                return false;
            }

            if (definition.Scale != 1d || definition.Offset != 0d)
            {
                failure = "Boolean and String tags require an identity Scale and Offset.";
                return false;
            }

            return TryTransformNonNumeric(dataType, rawValue, out engineeringValue, out failure);
        }

        if (!double.IsFinite(definition.Scale) || definition.Scale == 0d)
        {
            failure = "Scale must be finite and nonzero.";
            return false;
        }

        if (!double.IsFinite(definition.Offset))
        {
            failure = "Offset must be finite.";
            return false;
        }

        // Do not round-trip an identity Int64 through Double. Values above 2^53
        // are legitimate PLC counters but cannot be represented exactly as Double.
        if (sourceDataType == dataType && definition.Scale == 1d && definition.Offset == 0d)
        {
            return TryCopyNumeric(sourceDataType, rawValue, out engineeringValue, out failure);
        }

        if (!TryReadNumeric(sourceDataType, rawValue, out var rawNumber, out failure))
        {
            return false;
        }

        var transformed = rawNumber * definition.Scale + definition.Offset;
        if (!double.IsFinite(transformed))
        {
            failure = "The Scale/Offset result is not finite.";
            return false;
        }

        return TryConvertNumeric(dataType, transformed, out engineeringValue, out failure);
    }

    public static bool IsNumeric(TagDataType dataType) => dataType is
        TagDataType.Int32 or TagDataType.Int64 or TagDataType.Double;

    private static bool TryTransformNonNumeric(
        TagDataType dataType,
        object? rawValue,
        out object? engineeringValue,
        out string? failure)
    {
        engineeringValue = null;
        failure = null;

        switch (dataType)
        {
            case TagDataType.Boolean when rawValue is bool boolean:
                engineeringValue = boolean;
                return true;
            case TagDataType.String when rawValue is string text:
                engineeringValue = text;
                return true;
            case TagDataType.Boolean:
                failure = "The driver value is not a Boolean.";
                return false;
            case TagDataType.String:
                failure = "The driver value is not a String.";
                return false;
            default:
                failure = "The data type is not supported.";
                return false;
        }
    }

    private static bool TryCopyNumeric(
        TagDataType dataType,
        object? rawValue,
        out object? engineeringValue,
        out string? failure)
    {
        engineeringValue = null;
        failure = null;

        switch (dataType)
        {
            case TagDataType.Int32 when rawValue is int int32:
                engineeringValue = int32;
                return true;
            case TagDataType.Int64 when rawValue is long int64:
                engineeringValue = int64;
                return true;
            case TagDataType.Double when rawValue is double @double && double.IsFinite(@double):
                engineeringValue = @double;
                return true;
            case TagDataType.Double:
                failure = "The driver Double value is not finite.";
                return false;
            default:
                failure = $"The driver value does not match source data type '{dataType}'.";
                return false;
        }
    }

    private static bool TryReadNumeric(
        TagDataType sourceDataType,
        object? rawValue,
        out double number,
        out string? failure)
    {
        number = default;
        failure = null;

        switch (sourceDataType)
        {
            case TagDataType.Int32 when rawValue is int int32:
                number = int32;
                return true;
            case TagDataType.Int64 when rawValue is long int64:
                number = int64;
                return true;
            case TagDataType.Double when rawValue is double @double && double.IsFinite(@double):
                number = @double;
                return true;
            case TagDataType.Double when rawValue is double:
                failure = "The driver Double value is not finite.";
                return false;
            default:
                failure = $"The driver value does not match source data type '{sourceDataType}'.";
                return false;
        }
    }

    private static bool TryConvertNumeric(
        TagDataType dataType,
        double number,
        out object? engineeringValue,
        out string? failure)
    {
        engineeringValue = null;
        failure = null;

        switch (dataType)
        {
            case TagDataType.Double:
                engineeringValue = number;
                return true;
            case TagDataType.Int32 when number >= int.MinValue &&
                                       number <= int.MaxValue &&
                                       Math.Truncate(number) == number:
                engineeringValue = (int)number;
                return true;
            case TagDataType.Int64 when number >= long.MinValue &&
                                       number < 9_223_372_036_854_775_808d &&
                                       Math.Truncate(number) == number:
                engineeringValue = (long)number;
                return true;
            case TagDataType.Int32:
                failure = "The engineering value cannot be represented as an Int32 without rounding or overflow.";
                return false;
            case TagDataType.Int64:
                failure = "The engineering value cannot be represented as an Int64 without rounding or overflow.";
                return false;
            default:
                failure = "The engineering data type is not numeric.";
                return false;
        }
    }
}
