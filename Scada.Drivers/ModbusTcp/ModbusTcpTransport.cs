using System.Net.Sockets;
using FluentModbus;

namespace Scada.Drivers.ModbusTcp;

internal interface IModbusTcpTransport : IAsyncDisposable
{
    Task ConnectAsync(ModbusTcpOptions options, CancellationToken cancellationToken);
    Task<byte[]> ReadAsync(ModbusArea area, byte unitId, ushort start, ushort count, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}

internal interface IModbusTcpTransportFactory
{
    IModbusTcpTransport Create();
}

internal sealed class FluentModbusTcpTransportFactory : IModbusTcpTransportFactory
{
    public IModbusTcpTransport Create() => new FluentModbusTcpTransport();
}

internal sealed class ModbusDataException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class FluentModbusTcpTransport : IModbusTcpTransport
{
    private ModbusTcpClient? _client;
    private TcpClient? _tcpClient;

    public async Task ConnectAsync(ModbusTcpOptions options, CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("Modbus TCP transport is already connected.");
        }

        var tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(options.Host, options.Port, cancellationToken);
            var client = new ModbusTcpClient();
            client.Initialize(tcpClient, ModbusEndianness.BigEndian);
            _tcpClient = tcpClient;
            _client = client;
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    public async Task<byte[]> ReadAsync(
        ModbusArea area,
        byte unitId,
        ushort start,
        ushort count,
        CancellationToken cancellationToken)
    {
        var client = _client ?? throw new InvalidOperationException("Modbus TCP transport is not connected.");
        try
        {
            var memory = area switch
            {
                ModbusArea.Coil => await client.ReadCoilsAsync(unitId, start, count, cancellationToken),
                ModbusArea.DiscreteInput => await client.ReadDiscreteInputsAsync(unitId, start, count, cancellationToken),
                ModbusArea.HoldingRegister => await client.ReadHoldingRegistersAsync(unitId, start, count, cancellationToken),
                ModbusArea.InputRegister => await client.ReadInputRegistersAsync(unitId, start, count, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported Modbus area '{area}'.")
            };
            return memory.ToArray();
        }
        catch (ModbusException exception) when (exception.ExceptionCode is
            ModbusExceptionCode.IllegalFunction or
            ModbusExceptionCode.IllegalDataAddress or
            ModbusExceptionCode.IllegalDataValue)
        {
            throw new ModbusDataException(exception.Message, exception);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _client?.Disconnect();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _tcpClient?.Dispose();
        _client = null;
        _tcpClient = null;
        return ValueTask.CompletedTask;
    }
}
