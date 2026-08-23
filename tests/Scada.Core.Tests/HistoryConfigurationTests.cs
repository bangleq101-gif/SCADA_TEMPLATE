using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.History;
using Scada.Core.Tags;
using Xunit;

namespace Scada.Core.Tests;

public sealed class HistoryConfigurationTests
{
    [Fact]
    public void DefaultProfilesHaveTheApprovedCatalog()
    {
        var options = new RuntimeOptions();

        Assert.Collection(
            options.Historian.Profiles,
            digital =>
            {
                Assert.Equal("Digital", digital.Name);
                Assert.Equal(HistoryMode.OnChange, digital.Mode);
                Assert.Equal(0, digital.Deadband);
                Assert.Equal(0, digital.MinimumIntervalMilliseconds);
                Assert.Equal(0, digital.MaximumIntervalMilliseconds);
            },
            analog =>
            {
                Assert.Equal("Analog", analog.Name);
                Assert.Equal(HistoryMode.OnChangeAndPeriodic, analog.Mode);
                Assert.Equal(0.1, analog.Deadband);
                Assert.Equal(1_000, analog.MinimumIntervalMilliseconds);
                Assert.Equal(60_000, analog.MaximumIntervalMilliseconds);
            },
            fastAnalog =>
            {
                Assert.Equal("FastAnalog", fastAnalog.Name);
                Assert.Equal(HistoryMode.OnChangeAndPeriodic, fastAnalog.Mode);
                Assert.Equal(0.01, fastAnalog.Deadband);
                Assert.Equal(100, fastAnalog.MinimumIntervalMilliseconds);
                Assert.Equal(5_000, fastAnalog.MaximumIntervalMilliseconds);
            },
            custom =>
            {
                Assert.Equal("Custom", custom.Name);
                Assert.Equal(HistoryMode.OnChangeAndPeriodic, custom.Mode);
                Assert.Equal(0, custom.Deadband);
                Assert.Equal(1_000, custom.MinimumIntervalMilliseconds);
                Assert.Equal(10_000, custom.MaximumIntervalMilliseconds);
            });
    }

    [Fact]
    public void ProfileValidationRequiresEachBuiltinExactlyOnce()
    {
        var options = new RuntimeOptions();
        options.Historian.Profiles =
        [
            new() { Name = "Digital", Mode = HistoryMode.OnChange },
            new() { Name = "digital", Mode = HistoryMode.OnChange },
            new() { Name = "Analog", Mode = HistoryMode.OnChangeAndPeriodic, MaximumIntervalMilliseconds = 1 },
            new() { Name = "Custom", Mode = HistoryMode.OnChangeAndPeriodic, MaximumIntervalMilliseconds = 1 }
        ];

        var issues = RuntimeOptionsValidation.CollectIssues(options);

        Assert.Contains(issues, issue => issue.Code == "HISTORY_PROFILE_DUPLICATE");
        Assert.Contains(issues, issue => issue.Code == "HISTORY_PROFILE_REQUIRED_BUILTIN" && issue.ObjectId == "FastAnalog");
    }

    [Fact]
    public void UnknownHistoryProfileIsWarningAndIncompatibleTypeIsWarning()
    {
        var options = CreateOptions();
        options.Tags[0].HistoryEnabled = true;
        options.Tags[0].HistoryProfile = "FutureProfile";
        options.Tags.Add(new TagDefinition
        {
            Id = "T2",
            Name = "Boolean Analog",
            DeviceId = "SIM01",
            Address = "A2",
            DataType = TagDataType.Boolean,
            HistoryEnabled = true,
            HistoryProfile = "Analog"
        });

        var issues = RuntimeOptionsValidation.CollectIssues(options);

        Assert.Contains(issues, issue => issue.Code == "HISTORY_PROFILE_UNKNOWN" && !issue.IsBlocking);
        Assert.Contains(issues, issue => issue.Code == "HISTORY_PROFILE_TYPE_INCOMPATIBLE" && !issue.IsBlocking);
    }

    [Fact]
    public void QueueCapacityBelowValidHistoryTagsIsAWarning()
    {
        var options = CreateOptions();
        options.Historian.QueueCapacity = 1;
        options.Tags[0].Enabled = true;
        options.Tags[0].HistoryEnabled = true;
        options.Tags[0].HistoryProfile = "Analog";
        options.Tags.Add(new TagDefinition
        {
            Id = "T2",
            Name = "Second Double",
            DeviceId = "SIM01",
            Address = "A2",
            DataType = TagDataType.Double,
            Enabled = true,
            HistoryEnabled = true,
            HistoryProfile = "Analog"
        });

        var issues = RuntimeOptionsValidation.CollectIssues(options);

        var warning = Assert.Single(issues, issue =>
            issue.Code == "HISTORIAN_QUEUE_CAPACITY_BELOW_HISTORY_TAGS");
        Assert.False(warning.IsBlocking);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void HistoryQueryRejectsUnboundedOrReversedRanges()
    {
        var from = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var to = from.AddMinutes(1);

        Assert.Throws<ArgumentException>(() => new HistoryQuery("Runtime01", "T1", to, from, 10).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistoryQuery("Runtime01", "T1", from, to, 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistoryQuery("Runtime01", "T1", from, to, HistoryQuery.MaximumLimit + 1).Validate());
    }

    private static RuntimeOptions CreateOptions() => new()
    {
        Devices = [new DeviceDefinition { Id = "SIM01", DriverType = "Simulator" }],
        Tags =
        [
            new TagDefinition
            {
                Id = "T1",
                Name = "Double",
                DeviceId = "SIM01",
                Address = "A1",
                DataType = TagDataType.Double,
                HistoryProfile = "Analog"
            }
        ]
    };
}
