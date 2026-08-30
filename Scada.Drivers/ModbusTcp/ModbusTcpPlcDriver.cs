using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;

namespace Scada.Drivers.ModbusTcp;

public sealed class ModbusTcpPlcDriver : IPlcDriver, IAsyncDisposable
{
    private readonly IModbusTcpTransportFactory _transportFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private IModbusTcpTransport? _transport;
    private ModbusTcpOptions? _options;
    private int _disposeStarted;

    public ModbusTcpPlcDriver() : this(new FluentModbusTcpTransportFactory(), TimeProvider.System)
    {
    }

    internal ModbusTcpPlcDriver(IModbusTcpTransportFactory transportFactory, TimeProvider timeProvider)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public string DriverType => "ModbusTcp";

    public async Task ConnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!ModbusTcpOptions.TryParse(device, out var options, out var issues))
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, issues.Select(issue => issue.Message)));
        }

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
            await DisposeTransportAsync();
            var transport = _transportFactory.Create();
            try
            {
                await transport.ConnectAsync(options, cancellationToken);
                _options = options;
                _transport = transport;
            }
            catch
            {
                await transport.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<DriverReadResult>> ReadAsync(
        DeviceDefinition device,
        IReadOnlyList<DriverReadRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(requests);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
            var transport = _transport ?? throw new InvalidOperationException("Modbus TCP driver is not connected.");
            var options = _options ?? throw new InvalidOperationException("Modbus TCP options are unavailable.");
            var timestamp = _timeProvider.GetUtcNow();
            var results = new DriverReadResult[requests.Count];
            var plan = ModbusReadPlanner.Create(requests);
            foreach (var index in plan.InvalidRequestIndexes)
            {
                results[index] = new DriverReadResult(requests[index].TagId, null, TagQuality.Bad, timestamp);
            }

            foreach (var block in plan.Blocks)
            {
                try
                {
                    var bytes = await transport.ReadAsync(
                        block.Area,
                        options.UnitId,
                        block.Start,
                        block.Count,
                        cancellationToken);
                    foreach (var item in block.Requests)
                    {
                        var relative = item.Address.Offset - block.Start;
                        var offset = item.Address.Encoding == ModbusEncoding.Boolean ? relative : relative * 2;
                        var value = ModbusValueDecoder.Decode(bytes, offset, item.Address, options);
                        results[item.Index] = new DriverReadResult(item.Request.TagId, value, TagQuality.Good, timestamp);
                    }
                }
                catch (ModbusDataException)
                {
                    foreach (var item in block.Requests)
                    {
                        results[item.Index] = new DriverReadResult(item.Request.TagId, null, TagQuality.Bad, timestamp);
                    }
                }
            }

            return results;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                if (_transport is not null)
                {
                    await _transport.DisconnectAsync(cancellationToken);
                }
            }
            finally
            {
                await DisposeTransportAsync();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        await _operationGate.WaitAsync();
        try
        {
            await DisposeTransportAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask DisposeTransportAsync()
    {
        var transport = _transport;
        _transport = null;
        _options = null;
        if (transport is not null)
        {
            await transport.DisposeAsync();
        }
    }
}
