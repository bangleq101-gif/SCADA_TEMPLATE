using Scada.Core.Drivers;

namespace Scada.Drivers.ModbusTcp;

internal sealed record ModbusPlannedRequest(int Index, DriverReadRequest Request, ModbusAddress Address);

internal sealed record ModbusReadBlock(
    ModbusArea Area,
    ushort Start,
    ushort Count,
    IReadOnlyList<ModbusPlannedRequest> Requests);

internal sealed record ModbusReadPlan(
    IReadOnlyList<ModbusReadBlock> Blocks,
    IReadOnlyList<int> InvalidRequestIndexes);

internal static class ModbusReadPlanner
{
    public static ModbusReadPlan Create(IReadOnlyList<DriverReadRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var valid = new List<ModbusPlannedRequest>();
        var invalid = new List<int>();
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (!ModbusAddress.TryParse(request.Address, out var address, out _) ||
                address!.DataType != request.DataType)
            {
                invalid.Add(index);
                continue;
            }

            valid.Add(new ModbusPlannedRequest(index, request, address));
        }

        var blocks = new List<ModbusReadBlock>();
        foreach (var areaGroup in valid.GroupBy(item => item.Address.Area))
        {
            var maximum = areaGroup.Key is ModbusArea.Coil or ModbusArea.DiscreteInput ? 2_000 : 125;
            var ordered = areaGroup.OrderBy(item => item.Address.Offset).ThenBy(item => item.Index).ToArray();
            var members = new List<ModbusPlannedRequest>();
            var start = 0;
            var end = 0;
            foreach (var item in ordered)
            {
                var itemStart = item.Address.Offset;
                var itemEnd = itemStart + item.Address.RegisterCount;
                if (members.Count == 0)
                {
                    start = itemStart;
                    end = itemEnd;
                    members.Add(item);
                    continue;
                }

                var unionEnd = Math.Max(end, itemEnd);
                if (itemStart <= end && unionEnd - start <= maximum)
                {
                    end = unionEnd;
                    members.Add(item);
                    continue;
                }

                blocks.Add(CreateBlock(areaGroup.Key, start, end, members));
                members = [item];
                start = itemStart;
                end = itemEnd;
            }

            if (members.Count > 0)
            {
                blocks.Add(CreateBlock(areaGroup.Key, start, end, members));
            }
        }

        return new ModbusReadPlan(blocks, invalid);
    }

    private static ModbusReadBlock CreateBlock(
        ModbusArea area,
        int start,
        int end,
        IReadOnlyList<ModbusPlannedRequest> requests) =>
        new(area, checked((ushort)start), checked((ushort)(end - start)), requests.ToArray());
}
