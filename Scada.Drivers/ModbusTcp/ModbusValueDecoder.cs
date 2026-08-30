using System.Buffers.Binary;

namespace Scada.Drivers.ModbusTcp;

internal static class ModbusValueDecoder
{
    public static object Decode(
        ReadOnlySpan<byte> block,
        int byteOffset,
        ModbusAddress address,
        ModbusTcpOptions options)
    {
        if (address.Encoding == ModbusEncoding.Boolean)
        {
            var bit = byteOffset;
            return (block[bit / 8] & (1 << (bit % 8))) != 0;
        }

        var size = address.RegisterCount * 2;
        Span<byte> normalized = stackalloc byte[8];
        block.Slice(byteOffset, size).CopyTo(normalized);
        if (options.ByteOrder == ModbusRegisterByteOrder.LittleEndian)
        {
            for (var index = 0; index < size; index += 2)
            {
                (normalized[index], normalized[index + 1]) = (normalized[index + 1], normalized[index]);
            }
        }

        if (options.WordOrder == ModbusRegisterWordOrder.LowToHigh && size > 2)
        {
            for (var left = 0; left < size / 2; left += 2)
            {
                var right = size - 2 - left;
                (normalized[left], normalized[right]) = (normalized[right], normalized[left]);
                (normalized[left + 1], normalized[right + 1]) = (normalized[right + 1], normalized[left + 1]);
            }
        }

        var value = normalized[..size];
        return address.Encoding switch
        {
            ModbusEncoding.I16 => (object)(int)BinaryPrimitives.ReadInt16BigEndian(value),
            ModbusEncoding.U16 => (object)(int)BinaryPrimitives.ReadUInt16BigEndian(value),
            ModbusEncoding.I32 => (object)BinaryPrimitives.ReadInt32BigEndian(value),
            ModbusEncoding.U32 => (object)(long)BinaryPrimitives.ReadUInt32BigEndian(value),
            ModbusEncoding.I64 => (object)BinaryPrimitives.ReadInt64BigEndian(value),
            ModbusEncoding.F32 => (object)(double)BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(value)),
            ModbusEncoding.F64 => (object)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(value)),
            _ => throw new InvalidOperationException($"Unsupported Modbus encoding '{address.Encoding}'.")
        };
    }
}
