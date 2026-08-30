using System.Globalization;
using Scada.Core.Tags;

namespace Scada.Drivers.ModbusTcp;

internal enum ModbusArea
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister
}

internal enum ModbusEncoding
{
    Boolean,
    I16,
    U16,
    I32,
    U32,
    I64,
    F32,
    F64
}

internal sealed record ModbusAddress(ModbusArea Area, ushort Offset, ModbusEncoding Encoding)
{
    public int RegisterCount => Encoding switch
    {
        ModbusEncoding.Boolean or ModbusEncoding.I16 or ModbusEncoding.U16 => 1,
        ModbusEncoding.I32 or ModbusEncoding.U32 or ModbusEncoding.F32 => 2,
        ModbusEncoding.I64 or ModbusEncoding.F64 => 4,
        _ => throw new InvalidOperationException($"Unsupported Modbus encoding '{Encoding}'.")
    };

    public TagDataType DataType => Encoding switch
    {
        ModbusEncoding.Boolean => TagDataType.Boolean,
        ModbusEncoding.I16 or ModbusEncoding.U16 or ModbusEncoding.I32 => TagDataType.Int32,
        ModbusEncoding.U32 or ModbusEncoding.I64 => TagDataType.Int64,
        ModbusEncoding.F32 or ModbusEncoding.F64 => TagDataType.Double,
        _ => throw new InvalidOperationException($"Unsupported Modbus encoding '{Encoding}'.")
    };

    public static bool TryParse(string? text, out ModbusAddress? address, out string error)
    {
        address = null;
        error = string.Empty;
        var parts = text?.Split(':', StringSplitOptions.TrimEntries) ?? [];
        if (parts.Length is < 2 or > 3 ||
            !ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            error = "Use C:<offset>, DI:<offset>, HR:<offset>:<encoding>, or IR:<offset>:<encoding>.";
            return false;
        }

        ModbusArea area;
        ModbusEncoding encoding;
        switch (parts[0].ToUpperInvariant())
        {
            case "C" when parts.Length == 2:
                area = ModbusArea.Coil;
                encoding = ModbusEncoding.Boolean;
                break;
            case "DI" when parts.Length == 2:
                area = ModbusArea.DiscreteInput;
                encoding = ModbusEncoding.Boolean;
                break;
            case "HR" when parts.Length == 3 && TryParseEncoding(parts[2], out encoding):
                area = ModbusArea.HoldingRegister;
                break;
            case "IR" when parts.Length == 3 && TryParseEncoding(parts[2], out encoding):
                area = ModbusArea.InputRegister;
                break;
            default:
                error = "The Modbus area or encoding is unsupported.";
                return false;
        }

        var parsed = new ModbusAddress(area, offset, encoding);
        if ((int)offset + parsed.RegisterCount > ushort.MaxValue + 1)
        {
            error = "The value extends beyond the Modbus address range.";
            return false;
        }

        address = parsed;
        return true;
    }

    private static bool TryParseEncoding(string text, out ModbusEncoding encoding) =>
        Enum.TryParse(text, ignoreCase: true, out encoding) &&
        Enum.IsDefined(encoding) &&
        encoding != ModbusEncoding.Boolean;
}
