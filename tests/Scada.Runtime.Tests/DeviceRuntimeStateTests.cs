using Scada.Core.Devices;
using Scada.Runtime.Devices;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class DeviceRuntimeStateTests
{
    [Fact]
    public void SnapshotContainsStateCountersAndTiming()
    {
        var state = new DeviceRuntimeState("PLC-1");
        var sample = DateTimeOffset.Parse("2026-01-01T00:00:01Z");
        var completed = DateTimeOffset.Parse("2026-01-01T00:00:01.100Z");
        var failure = DateTimeOffset.Parse("2026-01-01T00:00:02Z");

        state.MarkConnecting();
        state.MarkConnected();
        state.MarkScanStarted(sample);
        state.MarkSuccess(sample, completed, TimeSpan.FromMilliseconds(100));
        state.AddMissedCycles(2);
        state.MarkFailure(new InvalidOperationException("read failed"), failure);

        var snapshot = state.Snapshot();

        Assert.Equal("PLC-1", snapshot.DeviceId);
        Assert.Equal(DeviceConnectionState.Faulted, snapshot.ConnectionState);
        Assert.Equal(sample, snapshot.LastSuccessfulRead);
        Assert.Equal(failure, snapshot.LastFailureAt);
        Assert.Equal(1, snapshot.ReadCount);
        Assert.Equal(1, snapshot.FailureCount);
        Assert.Equal(TimeSpan.FromMilliseconds(100), snapshot.LastScanDuration);
        Assert.Equal(2, snapshot.MissedCycleCount);
    }
}
