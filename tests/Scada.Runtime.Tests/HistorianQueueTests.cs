using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Runtime.Historian;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class HistorianQueueTests
{
    [Fact]
    public async Task QueueIsBoundedAndReportsFailedTryWrite()
    {
        var queue = new HistorianQueue(1);
        var first = Sample(1);
        var second = Sample(2);

        Assert.True(queue.TryWrite(first));
        Assert.False(queue.TryWrite(second));
        Assert.Equal(1, queue.Depth);

        var batch = await queue.ReadBatchAsync(10, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(first, Assert.Single(batch!));
        Assert.Equal(0, queue.Depth);
    }

    [Fact]
    public async Task CompletedEmptyQueueReturnsNullAndDrainCountsAcceptedItems()
    {
        var queue = new HistorianQueue(4);
        Assert.True(queue.TryWrite(Sample(1)));
        Assert.True(queue.TryWrite(Sample(2)));
        queue.Complete();

        Assert.Equal(2, queue.Drain());
        Assert.Equal(0, queue.Depth);
        Assert.Null(await queue.ReadBatchAsync(2, TimeSpan.Zero, CancellationToken.None));
    }

    private static HistorySample Sample(long sequence) => new(
        "Runtime01",
        "T1",
        TagDataType.Double,
        sequence,
        TagQuality.Good,
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        DateTimeOffset.Parse("2026-01-01T00:00:01Z"),
        sequence);
}
