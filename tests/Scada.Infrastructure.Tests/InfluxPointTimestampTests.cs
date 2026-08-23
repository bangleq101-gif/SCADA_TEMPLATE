using Scada.Infrastructure.History.Influx;
using Xunit;

namespace Scada.Infrastructure.Tests;

public sealed class InfluxPointTimestampTests
{
    [Fact]
    public void ExactInfluxNanosecondBoundsAreAcceptedAndAdjacentTicksAreRejected()
    {
        var minimum = DateTimeOffset.UnixEpoch.AddTicks(-92_233_720_368_547_758L);
        var maximum = DateTimeOffset.UnixEpoch.AddTicks(92_233_720_368_547_758L);

        Assert.True(InfluxPointTimestamp.TryGetBaseNanoseconds(minimum, out var minimumNanoseconds));
        Assert.True(InfluxPointTimestamp.TryGetBaseNanoseconds(maximum, out var maximumNanoseconds));
        Assert.Equal(InfluxPointTimestamp.MinNanoseconds + 6, minimumNanoseconds);
        Assert.Equal(InfluxPointTimestamp.MaxNanoseconds - 6, maximumNanoseconds);
        Assert.False(InfluxPointTimestamp.TryGetBaseNanoseconds(minimum.AddTicks(-1), out _));
        Assert.False(InfluxPointTimestamp.TryGetBaseNanoseconds(maximum.AddTicks(1), out _));
    }
}
