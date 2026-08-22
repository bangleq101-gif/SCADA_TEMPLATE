using Scada.Runtime.Historian;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class HistorianCoordinatorTests
{
    [Fact]
    public async Task EarlierScheduleWakesCoordinatorBeforeOlderDeadline()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new HistorianCoordinator(clock);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        coordinator.Schedule("A", clock.TimestampFrequency * 60);
        var waiting = coordinator.WaitForNextAsync(clock.GetTimestamp(), cancellation.Token);

        coordinator.Schedule("B", clock.TimestampFrequency * 5);
        await waiting;

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.TryTakeDue(clock.GetTimestamp(), out var tagId));
        Assert.Equal("B", tagId);
    }

    [Fact]
    public async Task ReschedulingSameTagEarlierWakesCoordinator()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new HistorianCoordinator(clock);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        coordinator.Schedule("A", clock.TimestampFrequency * 60);
        var waiting = coordinator.WaitForNextAsync(clock.GetTimestamp(), cancellation.Token);

        coordinator.Schedule("A", clock.TimestampFrequency * 5);
        await waiting;

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.TryTakeDue(clock.GetTimestamp(), out var tagId));
        Assert.Equal("A", tagId);
    }
}
