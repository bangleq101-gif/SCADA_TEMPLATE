using System.Globalization;
using Scada.Core.Configuration;
using Scada.Core.Devices;

namespace Scada.Drivers.ModbusTcp;

internal enum ModbusRegisterByteOrder
{
    BigEndian,
    LittleEndian
}

internal enum ModbusRegisterWordOrder
{
    HighToLow,
    LowToHigh
}

internal sealed record ModbusTcpOptions(
    string Host,
    int Port,
    byte UnitId,
    ModbusRegisterByteOrder ByteOrder,
    ModbusRegisterWordOrder WordOrder)
{
    public const string HostKey = "Host";
    public const string PortKey = "Port";
    public const string UnitIdKey = "UnitId";
    public const string ByteOrderKey = "RegisterByteOrder";
    public const string WordOrderKey = "RegisterWordOrder";

    public static bool TryParse(
        DeviceDefinition device,
        out ModbusTcpOptions options,
        out IReadOnlyList<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(device);
        var found = new List<ValidationIssue>();
        var values = device.ConnectionOptions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        values.TryGetValue(HostKey, out var host);
        host = host?.Trim();
        if (string.IsNullOrWhiteSpace(host) || host.Any(char.IsControl))
        {
            found.Add(Error(device, HostKey, "Modbus TCP Host is required and cannot contain control characters."));
        }

        var port = ParseInteger(values, PortKey, 502, 1, 65_535, device, found);
        var unitId = ParseInteger(values, UnitIdKey, 1, byte.MinValue, byte.MaxValue, device, found);
        var byteOrder = ParseEnum(values, ByteOrderKey, ModbusRegisterByteOrder.BigEndian, device, found);
        var wordOrder = ParseEnum(values, WordOrderKey, ModbusRegisterWordOrder.HighToLow, device, found);

        options = new ModbusTcpOptions(host ?? string.Empty, port, (byte)unitId, byteOrder, wordOrder);
        issues = found;
        return found.Count == 0;
    }

    private static int ParseInteger(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue,
        int minimum,
        int maximum,
        DeviceDefinition device,
        ICollection<ValidationIssue> issues)
    {
        if (!values.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
            value >= minimum && value <= maximum)
        {
            return value;
        }

        issues.Add(Error(device, key, $"Modbus TCP {key} must be between {minimum} and {maximum}."));
        return defaultValue;
    }

    private static T ParseEnum<T>(
        IReadOnlyDictionary<string, string> values,
        string key,
        T defaultValue,
        DeviceDefinition device,
        ICollection<ValidationIssue> issues) where T : struct, Enum
    {
        if (!values.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value))
        {
            return value;
        }

        issues.Add(Error(device, key, $"Modbus TCP {key} is invalid."));
        return defaultValue;
    }

    private static ValidationIssue Error(DeviceDefinition device, string property, string message) =>
        new("MODBUS_TCP_OPTION_INVALID", ValidationSeverity.Error, "Device", device.Id, property, message);
}
