using Scada.Core.Devices;
using Scada.Core.Tags;
using Scada.Runtime.Polling;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class DevicePollingPlanTests
{
    [Fact]
    public void PlanGroupsEnabledTagsByConfiguredScanGroupForOneDevice()
    {
        var device = new DeviceDefinition { Id = "PLC-1", DriverType = "Test" };
        var plan = DevicePollingPlan.Create(
            device,
            [
                new() { Id = "F1", DeviceId = "PLC-1", Address = "F1", ScanGroup = "Fast" },
                new() { Id = "F2", DeviceId = "PLC-1", Address = "F2", ScanGroup = "Fast" },
                new() { Id = "N1", DeviceId = "PLC-1", Address = "N1", ScanGroup = "Normal" },
                new() { Id = "OTHER", DeviceId = "PLC-2", Address = "OTHER", ScanGroup = "Fast" },
                new() { Id = "DISABLED", DeviceId = "PLC-1", Address = "DISABLED", Enabled = false, ScanGroup = "Fast" }
            ],
            [
                new() { Name = "Fast", IntervalMilliseconds = 100 },
                new() { Name = "Normal", IntervalMilliseconds = 500 }
            ]);

        Assert.Collection(
            plan.Groups,
            fast =>
            {
                Assert.Equal("Fast", fast.Name);
                Assert.Equal(2, fast.Requests.Count);
            },
            normal =>
            {
                Assert.Equal("Normal", normal.Name);
                Assert.Single(normal.Requests);
                Assert.Equal("N1", normal.Requests[0].TagId);
            });
        Assert.Collection(plan.Tags, _ => { }, _ => { }, _ => { });
    }
}
