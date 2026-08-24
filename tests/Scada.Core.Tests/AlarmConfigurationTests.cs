using Scada.Core.Alarms;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Xunit;

namespace Scada.Core.Tests;

public sealed class AlarmConfigurationTests
{
    [Fact]
    public void DefaultsAreDisabledAndUseProjectRelativePersistence()
    {
        var options = new AlarmOptions();

        Assert.False(options.Enabled);
        Assert.True(options.PersistenceEnabled);
        Assert.Equal("Data/alarms.db", options.DatabasePath);
        Assert.Empty(options.Definitions);
    }

    [Fact]
    public void FingerprintChangesForMaterialFieldsButNotDisplayFields()
    {
        var definition = CreateNumericAlarm();
        var original = AlarmDefinitionFingerprint.Create(definition);

        definition.Name = "Renamed";
        definition.Message = "Changed display text";
        definition.Order = 99;
        Assert.Equal(original, AlarmDefinitionFingerprint.Create(definition));

        definition.Threshold = 81;
        Assert.NotEqual(original, AlarmDefinitionFingerprint.Create(definition));
    }

    [Fact]
    public void ValidationRequiresRuleCompatibleTagsAndFields()
    {
        var tags = new[]
        {
            new TagDefinition { Id = "BOOL", Name = "Boolean", DeviceId = "SIM", Address = "B", DataType = TagDataType.Boolean },
            new TagDefinition { Id = "REAL", Name = "Real", DeviceId = "SIM", Address = "R", DataType = TagDataType.Double },
            new TagDefinition { Id = "OFF", Name = "Disabled", DeviceId = "SIM", Address = "O", DataType = TagDataType.Int32, Enabled = false }
        };
        var options = new AlarmOptions
        {
            Enabled = true,
            Definitions =
            [
                new AlarmDefinition { Id = "D", Name = "Digital", TagId = "REAL", RuleType = AlarmRuleType.DigitalEquals, DigitalExpectedValue = true },
                new AlarmDefinition { Id = "N", Name = "Numeric", TagId = "BOOL", RuleType = AlarmRuleType.High, Threshold = 10 },
                new AlarmDefinition { Id = "M", Name = "Missing", TagId = "MISSING", RuleType = AlarmRuleType.High, Threshold = 10 },
                new AlarmDefinition { Id = "O", Name = "Disabled tag", TagId = "OFF", RuleType = AlarmRuleType.Low, Threshold = 1 },
                new AlarmDefinition { Id = "F", Name = "Nonfinite", TagId = "REAL", RuleType = AlarmRuleType.High, Threshold = double.NaN }
            ]
        };

        var issues = AlarmDefinitionValidation.CollectIssues(options, tags);

        Assert.Contains(issues, issue => issue.Code == "ALARM_TAG_TYPE_MISMATCH" && issue.ObjectId == "D");
        Assert.Contains(issues, issue => issue.Code == "ALARM_TAG_TYPE_MISMATCH" && issue.ObjectId == "N");
        Assert.Contains(issues, issue => issue.Code == "ALARM_TAG_MISSING" && issue.ObjectId == "M");
        Assert.Contains(issues, issue => issue.Code == "ALARM_TAG_DISABLED" && issue.ObjectId == "O");
        Assert.Contains(issues, issue => issue.Code == "ALARM_THRESHOLD_INVALID" && issue.ObjectId == "F");
    }

    [Fact]
    public void ValidationReportsDuplicateAndInvalidDisabledDefinitions()
    {
        var tags = new[]
        {
            new TagDefinition { Id = "REAL", Name = "Real", DeviceId = "SIM", Address = "R", DataType = TagDataType.Double }
        };
        var options = new AlarmOptions
        {
            Definitions =
            [
                CreateNumericAlarm(),
                new AlarmDefinition
                {
                    Id = "A1",
                    Name = string.Empty,
                    TagId = "REAL",
                    Enabled = false,
                    RuleType = AlarmRuleType.High,
                    Threshold = 10,
                    Deadband = -1,
                    ActivationDelay = TimeSpan.FromSeconds(-1)
                }
            ]
        };

        var issues = AlarmDefinitionValidation.CollectIssues(options, tags);

        Assert.Contains(issues, issue => issue.Code == "ALARM_ID_DUPLICATE");
        Assert.Contains(issues, issue => issue.Code == "ALARM_NAME_REQUIRED" && issue.ObjectId == "A1");
        Assert.Contains(issues, issue => issue.Code == "ALARM_DEADBAND_INVALID" && issue.ObjectId == "A1");
        Assert.Contains(issues, issue => issue.Code == "ALARM_ACTIVATION_DELAY_INVALID" && issue.ObjectId == "A1");
    }

    private static AlarmDefinition CreateNumericAlarm() => new()
    {
        Id = "A1",
        Name = "High temperature",
        Message = "Temperature is high",
        TagId = "REAL",
        RuleType = AlarmRuleType.High,
        Severity = AlarmSeverity.High,
        Threshold = 80,
        Deadband = 2,
        ActivationDelay = TimeSpan.FromSeconds(1),
        AcknowledgementRequired = true
    };
}
