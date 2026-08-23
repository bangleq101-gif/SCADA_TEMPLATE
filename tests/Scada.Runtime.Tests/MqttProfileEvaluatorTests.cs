using Scada.Core.Mqtt;
using Scada.Core.Tags;
using Scada.Runtime.Mqtt;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class MqttProfileEvaluatorTests
{
    [Fact]
    public void QualityTransitionBypassesMinimumInterval()
    {
        var evaluator = new MqttProfileEvaluator(TimeProvider.System);
        var tag = new TagDefinition { Id = "T" };
        var profile = new MqttProfileDefinition { Mode = MqttPublishMode.OnChange, MinimumIntervalMilliseconds = 60_000 };
        var now = DateTimeOffset.UtcNow;
        Assert.True(evaluator.ShouldPublish(tag, profile, new TagValue("T", 1d, TagQuality.Good, now, 1)));
        Assert.True(evaluator.ShouldPublish(tag, profile, new TagValue("T", 1d, TagQuality.Disconnected, now, 2)));
    }
}
