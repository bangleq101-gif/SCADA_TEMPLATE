using Scada.Core.Tags;

namespace Scada.Core.Mqtt;

public static class MqttTopicBuilder
{
    public static bool TryBuild(string runtimeId, TagDefinition tag, MqttOptions options, out string? topic)
    {
        topic = string.IsNullOrWhiteSpace(tag.MqttTopicOverride)
            ? options.TopicTemplate.Replace("{BaseTopic}", options.BaseTopic, StringComparison.Ordinal)
                .Replace("{RuntimeId}", Escape(runtimeId), StringComparison.Ordinal)
                .Replace("{DeviceId}", Escape(tag.DeviceId), StringComparison.Ordinal)
                .Replace("{TagId}", Escape(tag.Id), StringComparison.Ordinal)
                .Replace("{TagName}", Escape(tag.Name), StringComparison.Ordinal)
            : tag.MqttTopicOverride;
        return !string.IsNullOrWhiteSpace(topic) && !topic.Contains('+') && !topic.Contains('#') &&
               !topic.Any(char.IsControl) && !topic.Contains('{') && !topic.Contains('}');
    }
    private static string Escape(string value) => Uri.EscapeDataString(value);
}
