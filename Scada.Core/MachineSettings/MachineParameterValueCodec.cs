using System.Globalization;

namespace Scada.Core.MachineSettings;

public static class MachineParameterValueCodec
{
    public static bool TryNormalizePersisted(MachineParameterValueType type, string? value, out string normalized) =>
        TryNormalize(type, value, CultureInfo.InvariantCulture, out normalized);

    public static bool TryNormalizeEditor(MachineParameterValueType type, string? value, CultureInfo culture, out string normalized)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return TryNormalize(type, value, culture, out normalized);
    }

    public static string FormatForEditor(MachineParameterValueType type, string value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (!TryNormalizePersisted(type, value, out var canonical)) return value;
        return type switch
        {
            MachineParameterValueType.Decimal => decimal.Parse(canonical, CultureInfo.InvariantCulture).ToString("G29", culture),
            MachineParameterValueType.Integer => long.Parse(canonical, CultureInfo.InvariantCulture).ToString(culture),
            _ => canonical
        };
    }

    public static bool TryGetNumeric(MachineParameterValueType type, string canonical, out decimal number)
    {
        if (type is MachineParameterValueType.Integer or MachineParameterValueType.Decimal &&
            decimal.TryParse(canonical, NumberStyles.Number, CultureInfo.InvariantCulture, out number)) return true;
        number = default;
        return false;
    }

    private static bool TryNormalize(MachineParameterValueType type, string? value, CultureInfo culture, out string normalized)
    {
        value ??= string.Empty;
        switch (type)
        {
            case MachineParameterValueType.String: normalized = value; return true;
            case MachineParameterValueType.Boolean:
                if (value is "true" or "false") { normalized = value; return true; }
                break;
            case MachineParameterValueType.Integer:
                if (long.TryParse(value, NumberStyles.Integer, culture, out var integer)) { normalized = integer.ToString(CultureInfo.InvariantCulture); return true; }
                break;
            case MachineParameterValueType.Decimal:
                var separator = culture.NumberFormat.NumberDecimalSeparator;
                if (separator != "." && value.Contains('.', StringComparison.Ordinal)) break;
                if (decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, culture, out var decimalValue)) { normalized = decimalValue.ToString("G29", CultureInfo.InvariantCulture); return true; }
                break;
        }
        normalized = string.Empty;
        return false;
    }
}
