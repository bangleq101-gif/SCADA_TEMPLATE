using System.Text.Json;
using Scada.Core.Tags;

namespace Scada.Core.Mqtt;

public static class MqttPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static bool TrySerialize(MqttPayload payload, out byte[] bytes)
    {
        if (payload.Value is double number && !double.IsFinite(number)) { bytes = []; return false; }
        bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Options); return true;
    }
}
