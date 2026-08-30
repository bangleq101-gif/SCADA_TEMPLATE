using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Core.Tags;
using Scada.Drivers.ModbusTcp;
using Xunit;

namespace Scada.Drivers.Tests;

public sealed class ModbusTcpTests
{
    [Theory]
    [InlineData("C:0", "Coil", "Boolean", TagDataType.Boolean, 1)]
    [InlineData("DI:65535", "DiscreteInput", "Boolean", TagDataType.Boolean, 1)]
    [InlineData("HR:10:I16", "HoldingRegister", "I16", TagDataType.Int32, 1)]
    [InlineData("HR:10:U32", "HoldingRegister", "U32", TagDataType.Int64, 2)]
    [InlineData("IR:10:F64", "InputRegister", "F64", TagDataType.Double, 4)]
    public void AddressParserAcceptsCanonicalZeroBasedGrammar(
        string text,
        string area,
        string encoding,
        TagDataType dataType,
        int registerCount)
    {
        Assert.True(ModbusAddress.TryParse(text, out var address, out var error), error);
        Assert.Equal(area, address!.Area.ToString());
        Assert.Equal(encoding, address.Encoding.ToString());
        Assert.Equal(dataType, address.DataType);
        Assert.Equal(registerCount, address.RegisterCount);
    }

    [Theory]
    [InlineData("40001")]
    [InlineData("HR:0")]
    [InlineData("C:0:I16")]
    [InlineData("HR:65535:F64")]
    [InlineData("HR:-1:I16")]
    [InlineData("HR:1:U64")]
    public void AddressParserRejectsAmbiguousOrUnsupportedGrammar(string text)
    {
        Assert.False(ModbusAddress.TryParse(text, out _, out _));
    }

    [Fact]
    public void PlannerCoalescesContiguousRequestsAndPreservesAreaBoundaries()
    {
        DriverReadRequest[] requests =
        [
            new("A", "HR:2:I16", TagDataType.Int32),
            new("B", "HR:0:I32", TagDataType.Int32),
            new("C", "IR:0:U16", TagDataType.Int32),
            new("D", "C:0", TagDataType.Boolean)
        ];

        var plan = ModbusReadPlanner.Create(requests);

        Assert.Empty(plan.InvalidRequestIndexes);
        var holding = Assert.Single(plan.Blocks, block => block.Area == ModbusArea.HoldingRegister);
        Assert.Equal((ushort)0, holding.Start);
        Assert.Equal((ushort)3, holding.Count);
        Assert.Equal([1, 0], holding.Requests.Select(item => item.Index));
        Assert.Equal(3, plan.Blocks.Count);
    }

    [Fact]
    public void PlannerSplitsRegisterRequestsAtProtocolLimit()
    {
        var requests = Enumerable.Range(0, 126)
            .Select(index => new DriverReadRequest($"T{index}", $"HR:{index}:U16", TagDataType.Int32))
            .ToArray();

        var blocks = ModbusReadPlanner.Create(requests).Blocks;

        Assert.Equal(2, blocks.Count);
        Assert.Equal((ushort)125, blocks[0].Count);
        Assert.Equal((ushort)1, blocks[1].Count);
    }

    [Fact]
    public void PlannerKeepsTenThousandTagsBoundedWithoutPerTagIo()
    {
        var requests = Enumerable.Range(0, 10_000)
            .Select(index => new DriverReadRequest($"T{index}", $"HR:{index}:U16", TagDataType.Int32))
            .ToArray();

        var plan = ModbusReadPlanner.Create(requests);

        Assert.Empty(plan.InvalidRequestIndexes);
        Assert.Equal(80, plan.Blocks.Count);
        Assert.All(plan.Blocks, block => Assert.InRange(block.Count, (ushort)1, (ushort)125));
        Assert.Equal(10_000, plan.Blocks.Sum(block => block.Requests.Count));
    }

    [Fact]
    public void DecoderAppliesConfiguredByteAndWordOrderDeterministically()
    {
        var bigHigh = new ModbusTcpOptions("localhost", 502, 1,
            ModbusRegisterByteOrder.BigEndian, ModbusRegisterWordOrder.HighToLow);
        var littleLow = new ModbusTcpOptions("localhost", 502, 1,
            ModbusRegisterByteOrder.LittleEndian, ModbusRegisterWordOrder.LowToHigh);
        var address = new ModbusAddress(ModbusArea.HoldingRegister, 0, ModbusEncoding.U32);

        Assert.Equal(0x11223344L, ModbusValueDecoder.Decode([0x11, 0x22, 0x33, 0x44], 0, address, bigHigh));
        Assert.Equal(0x11223344L, ModbusValueDecoder.Decode([0x44, 0x33, 0x22, 0x11], 0, address, littleLow));
    }

    [Fact]
    public async Task DriverBatchesReadsPreservesInputOrderAndUsesNoWriteOperation()
    {
        var transport = new FakeTransport
        {
            ReadHandler = (area, _, start, count, _) => Task.FromResult(area switch
            {
                ModbusArea.Coil => new byte[] { 0b0000_0001 },
                ModbusArea.HoldingRegister when start == 0 && count == 3 => new byte[] { 0, 42, 0, 0, 0, 7 },
                _ => throw new InvalidOperationException()
            })
        };
        var driver = new ModbusTcpPlcDriver(new FakeTransportFactory(transport), new FixedTimeProvider());
        var device = ValidDevice();
        await driver.ConnectAsync(device, CancellationToken.None);

        var results = await driver.ReadAsync(device,
        [
            new("B", "HR:1:U32", TagDataType.Int64),
            new("C", "C:0", TagDataType.Boolean),
            new("A", "HR:0:U16", TagDataType.Int32)
        ], CancellationToken.None);

        Assert.Equal(["B", "C", "A"], results.Select(result => result.TagId));
        Assert.Equal(7L, results[0].Value);
        Assert.Equal(true, results[1].Value);
        Assert.Equal(42, results[2].Value);
        Assert.Equal(2, transport.Reads.Count);
        Assert.All(results, result => Assert.Equal(TagQuality.Good, result.Quality));
    }

    [Fact]
    public async Task ProtocolDataErrorMarksOnlyAffectedBlockBadAndContinues()
    {
        var transport = new FakeTransport
        {
            ReadHandler = (area, _, _, _, _) => area == ModbusArea.HoldingRegister
                ? Task.FromException<byte[]>(new ModbusDataException("illegal address"))
                : Task.FromResult(new byte[] { 1 })
        };
        var driver = new ModbusTcpPlcDriver(new FakeTransportFactory(transport), new FixedTimeProvider());
        var device = ValidDevice();
        await driver.ConnectAsync(device, CancellationToken.None);

        var results = await driver.ReadAsync(device,
        [
            new("Bad", "HR:0:U16", TagDataType.Int32),
            new("Good", "C:0", TagDataType.Boolean)
        ], CancellationToken.None);

        Assert.Equal(TagQuality.Bad, results[0].Quality);
        Assert.Null(results[0].Value);
        Assert.Equal(TagQuality.Good, results[1].Quality);
        Assert.Equal(true, results[1].Value);
    }

    [Fact]
    public async Task TransportFailurePropagatesForExistingRuntimeReconnectPolicy()
    {
        var transport = new FakeTransport
        {
            ReadHandler = (_, _, _, _, _) => Task.FromException<byte[]>(new IOException("connection lost"))
        };
        var driver = new ModbusTcpPlcDriver(new FakeTransportFactory(transport), new FixedTimeProvider());
        var device = ValidDevice();
        await driver.ConnectAsync(device, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => driver.ReadAsync(
            device,
            [new("T", "C:0", TagDataType.Boolean)],
            CancellationToken.None));
    }

    [Fact]
    public async Task ConnectReadDisconnectAndDisposeHonorOwnershipAndCancellation()
    {
        var transport = new FakeTransport();
        var driver = new ModbusTcpPlcDriver(new FakeTransportFactory(transport), new FixedTimeProvider());
        var device = ValidDevice();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.ConnectAsync(device, cancelled.Token));
        await driver.ConnectAsync(device, CancellationToken.None);
        await driver.DisconnectAsync(device, CancellationToken.None);
        await driver.DisposeAsync();
        await driver.DisposeAsync();

        Assert.Equal(1, transport.ConnectCount);
        Assert.Equal(1, transport.DisconnectCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public void DeviceOptionsUsePortableDefaultsAndRejectOutOfRangeValues()
    {
        var device = new DeviceDefinition
        {
            Id = "PLC1",
            DriverType = "ModbusTcp",
            ConnectionOptions = new(StringComparer.OrdinalIgnoreCase) { ["Host"] = "plc.local" }
        };

        Assert.True(ModbusTcpOptions.TryParse(device, out var options, out var issues));
        Assert.Empty(issues);
        Assert.Equal(502, options.Port);
        Assert.Equal((byte)1, options.UnitId);
        Assert.Equal(ModbusRegisterByteOrder.BigEndian, options.ByteOrder);
        Assert.Equal(ModbusRegisterWordOrder.HighToLow, options.WordOrder);

        device.ConnectionOptions["Port"] = "0";
        device.ConnectionOptions["UnitId"] = "256";
        Assert.False(ModbusTcpOptions.TryParse(device, out _, out issues));
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public async Task EngineeringProviderValidatesOptionsAndTagSourceTypeWithoutIo()
    {
        var provider = new ModbusTcpEngineeringProvider();
        var invalidDevice = new DeviceDefinition { Id = "PLC1", DriverType = "ModbusTcp" };
        var device = ValidDevice();
        var tag = new TagDefinition
        {
            Id = "Pressure",
            Name = "Pressure",
            DeviceId = device.Id,
            Address = "HR:10:F32",
            SourceDataType = TagDataType.Int32,
            DataType = TagDataType.Double
        };

        Assert.Contains(provider.Validate(invalidDevice), issue => issue.PropertyName == ModbusTcpOptions.HostKey);
        Assert.Contains(provider.ValidateTag(device, tag), issue => issue.Code == "MODBUS_TCP_TAG_TYPE_MISMATCH");
        tag.SourceDataType = TagDataType.Double;
        Assert.Empty(provider.ValidateTag(device, tag));
        Assert.Empty(await provider.BrowseAddressesAsync(device, CancellationToken.None));
    }

    private static DeviceDefinition ValidDevice() => new()
    {
        Id = "PLC1",
        DriverType = "ModbusTcp",
        ConnectionOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = "127.0.0.1",
            ["Port"] = "502",
            ["UnitId"] = "1"
        }
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    }

    private sealed class FakeTransportFactory(FakeTransport transport) : IModbusTcpTransportFactory
    {
        public IModbusTcpTransport Create() => transport;
    }

    private sealed class FakeTransport : IModbusTcpTransport
    {
        public Func<ModbusArea, byte, ushort, ushort, CancellationToken, Task<byte[]>> ReadHandler { get; set; } =
            (_, _, _, _, _) => Task.FromResult(new byte[] { 0 });
        public List<(ModbusArea Area, byte UnitId, ushort Start, ushort Count)> Reads { get; } = [];
        public int ConnectCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Task ConnectAsync(ModbusTcpOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(ModbusArea area, byte unitId, ushort start, ushort count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add((area, unitId, start, count));
            return ReadHandler(area, unitId, start, count, cancellationToken);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
