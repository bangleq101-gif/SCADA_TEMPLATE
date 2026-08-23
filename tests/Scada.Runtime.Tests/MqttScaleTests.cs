using Scada.Core.Mqtt;
using Scada.Core.Tags;
using Scada.Runtime.Mqtt;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class MqttScaleTests
{
    [Fact]
    public void TenThousandTagsUseEvaluatorStateOnly()
    {
        var evaluator = new MqttProfileEvaluator(TimeProvider.System);
        var profile = new MqttProfileDefinition { Name = "Default" };
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 10_000; index++)
        {
            var id = $"T{index}";
            Assert.True(evaluator.ShouldPublish(new TagDefinition { Id = id }, profile, new TagValue(id, index, TagQuality.Good, now, 1)));
        }
    }
}
