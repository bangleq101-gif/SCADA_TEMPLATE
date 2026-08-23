using Scada.Core.Configuration;
using Xunit;

namespace Scada.Core.Tests;

public sealed class MqttConfigurationTests
{
    [Fact]
    public void DefaultMqttProfilesAndDisabledPublisherAreValid()
    {
        var options = new RuntimeOptions();
        var issues = RuntimeOptionsValidation.CollectIssues(options);
        Assert.False(options.Mqtt.Enabled);
        Assert.Contains(options.Mqtt.Profiles, profile => profile.Name == "Default");
        Assert.DoesNotContain(issues, issue => issue.Code.StartsWith("MQTT_", StringComparison.Ordinal) && issue.IsBlocking);
    }

    [Fact]
    public void TopicBuilderEscapesGeneratedSegmentsAndOverrideIsFinal()
    {
        var options = new RuntimeOptions();
        var tag = new Scada.Core.Tags.TagDefinition { Id = "SIM/01", Name = "A B", DeviceId = "D/1" };
        Assert.True(Scada.Core.Mqtt.MqttTopicBuilder.TryBuild("R 1", tag, options.Mqtt, out var generated));
        Assert.Equal("scada/R%201/D%2F1/SIM%2F01", generated);
        tag.MqttTopicOverride = "custom/topic";
        Assert.True(Scada.Core.Mqtt.MqttTopicBuilder.TryBuild("ignored", tag, options.Mqtt, out var overridden));
        Assert.Equal("custom/topic", overridden);
    }

    [Fact]
    public void EnabledMqttRejectsDuplicateResolvedTopics()
    {
        var options = new RuntimeOptions { Mqtt = { Enabled = true } };
        options.Devices.Add(new Scada.Core.Devices.DeviceDefinition { Id = "D", DriverType = "Simulator" });
        options.Tags.Add(new Scada.Core.Tags.TagDefinition { Id = "A", Name = "A", DeviceId = "D", Address = "A", MqttPublishEnabled = true, MqttTopicOverride = "same" });
        options.Tags.Add(new Scada.Core.Tags.TagDefinition { Id = "B", Name = "B", DeviceId = "D", Address = "B", MqttPublishEnabled = true, MqttTopicOverride = "same" });
        Assert.Contains(RuntimeOptionsValidation.CollectIssues(options), issue => issue.Code == "MQTT_TOPIC_DUPLICATE" && issue.IsBlocking);
    }

    [Fact]
    public void DuplicateMqttProfilesReturnIssueWithoutThrowing()
    {
        var options = new RuntimeOptions();
        options.Mqtt.Profiles.Add(new Scada.Core.Mqtt.MqttProfileDefinition { Name = "default" });
        var issues = RuntimeOptionsValidation.CollectIssues(options);
        Assert.Contains(issues, issue => issue.Code == "MQTT_PROFILE_NAME_INVALID" && issue.IsBlocking);
    }
}
