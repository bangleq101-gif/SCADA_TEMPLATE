using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class MonitoringWorkspaceLifecycleTests
{
    [Fact]
    public void ConstructorCreatesZeroSubscriptions()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache);

        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void ActivateCreatesOneSubscriptionPerRequiredTag()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache, "T1", "T2");

        monitoring.Activate();

        Assert.Equal(2, cache.ActiveSubscriptionCount);
        Assert.Equal(2, cache.TotalSubscriptionCount);
    }

    [Fact]
    public void SecondActivateDoesNotDuplicateSubscriptions()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache, "T1", "T2");

        monitoring.Activate();
        monitoring.Activate();

        Assert.Equal(2, cache.ActiveSubscriptionCount);
        Assert.Equal(2, cache.TotalSubscriptionCount);
    }

    [Fact]
    public void DeactivateDisposesAllSubscriptions()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache, "T1", "T2");
        monitoring.Activate();

        monitoring.Deactivate();

        Assert.Equal(0, cache.ActiveSubscriptionCount);
        Assert.Equal(2, cache.DisposedSubscriptionCount);
    }

    [Fact]
    public void SecondDeactivateIsSafe()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache, "T1");
        monitoring.Activate();

        monitoring.Deactivate();
        monitoring.Deactivate();

        Assert.Equal(0, cache.ActiveSubscriptionCount);
        Assert.Equal(1, cache.DisposedSubscriptionCount);
    }

    [Fact]
    public void ReactivationCreatesExactlyOneNewSet()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache, "T1", "T2");
        monitoring.Activate();
        monitoring.Deactivate();

        monitoring.Activate();

        Assert.Equal(2, cache.ActiveSubscriptionCount);
        Assert.Equal(4, cache.TotalSubscriptionCount);
        Assert.Equal(2, cache.DisposedSubscriptionCount);
    }

    [Fact]
    public void ActivateSeedsExistingCacheValues()
    {
        var cache = new TestTagCache();
        var timestamp = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
        cache.Seed(new TagValue("T1", 42, TagQuality.Good, timestamp, 7));
        var monitoring = CreateMonitoring(cache, "T1");

        monitoring.Activate();

        var row = Assert.Single(monitoring.Rows);
        Assert.Equal(42, row.Value);
        Assert.Equal(TagQuality.Good, row.Quality);
        Assert.Equal(timestamp, row.Timestamp);
    }

    [Fact]
    public void OldActivationCallbackCannotUpdateAfterDeactivate()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache, "T1");
        monitoring.Activate();
        monitoring.Deactivate();

        cache.InvokeSubscription(0, Value("T1", 10, 1));

        var row = Assert.Single(monitoring.Rows);
        Assert.Null(row.Value);
    }

    [Fact]
    public void OldActivationCallbackCannotUpdateLaterActivation()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache, "T1");
        monitoring.Activate();
        monitoring.Deactivate();
        monitoring.Activate();

        cache.InvokeSubscription(0, Value("T1", 10, 1));
        var row = Assert.Single(monitoring.Rows);
        Assert.Null(row.Value);

        cache.InvokeSubscription(1, Value("T1", 20, 2));
        Assert.Equal(20, row.Value);
    }

    [Fact]
    public void DisposeCleansSubscriptionsAndIsIdempotent()
    {
        var cache = new TestTagCache();
        var monitoring = CreateMonitoring(cache, "T1", "T2");
        monitoring.Activate();

        monitoring.Dispose();
        monitoring.Dispose();

        Assert.Equal(0, cache.ActiveSubscriptionCount);
        Assert.Equal(2, cache.DisposedSubscriptionCount);
    }

    [Fact]
    public void NavigationAwayAndBackControlsMonitoringSubscriptions()
    {
        var cache = new TestTagCache();
        var options = CreateOptions("T1", "T2");
        var operation = new OperationViewModel(options);
        var machineSettings = new MachineSettingsViewModel();
        var monitoring = new MonitoringViewModel(cache, options);
        var engineering = new EngineeringViewModel();
        var navigation = new NavigationService(operation, machineSettings, monitoring, engineering);

        navigation.Navigate(NavigationService.MonitoringOnlineTagsRoute);
        Assert.Equal(2, cache.ActiveSubscriptionCount);

        navigation.Navigate(NavigationService.OperationOverviewRoute);
        Assert.Equal(0, cache.ActiveSubscriptionCount);

        navigation.Navigate(NavigationService.MonitoringOnlineTagsRoute);
        Assert.Equal(2, cache.ActiveSubscriptionCount);
        Assert.Equal(4, cache.TotalSubscriptionCount);

        navigation.Navigate(NavigationService.MonitoringOnlineTagsRoute);
        Assert.Equal(4, cache.TotalSubscriptionCount);
    }

    private static MonitoringViewModel CreateMonitoring(TestTagCache cache, params string[] tagIds) =>
        new(cache, CreateOptions(tagIds));

    private static RuntimeOptions CreateOptions(params string[] tagIds) => new()
    {
        Tags = tagIds.Select(tagId => new TagDefinition
        {
            Id = tagId,
            Name = tagId,
            DeviceId = "TEST"
        }).ToList()
    };

    private static TagValue Value(string tagId, object value, long sequence) =>
        new(tagId, value, TagQuality.Good, DateTimeOffset.UtcNow, sequence);
}
