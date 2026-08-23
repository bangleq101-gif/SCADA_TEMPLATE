using Scada.Core.Tags;

namespace Scada.Core.Mqtt;

public sealed record MqttPublishRequest(string Topic, ReadOnlyMemory<byte> Payload, MqttQualityOfService QualityOfService, bool Retain);
public sealed record MqttConnectRequest(string Host, int Port, MqttProtocolVersion ProtocolVersion, string ClientId, string? Username, string? Password, bool UseTls, int KeepAliveSeconds, TimeSpan Timeout);
public sealed record MqttConnectionResult(bool IsAccepted, string? ErrorCode = null, string? ErrorMessage = null);
public interface IMqttTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    Task<MqttConnectionResult> ConnectAsync(MqttConnectRequest request, CancellationToken cancellationToken);
    Task PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}
public enum MqttRuntimeState { Disabled, Starting, Connecting, Online, Offline, ConfigurationRequired, Stopping, Faulted }
public sealed record MqttRuntimeSnapshot(MqttRuntimeState State, int ConfiguredTags, int PendingTags, long PublishedMessages, long CoalescedUpdates, long RejectedSamples, long PublishFailures, long ReconnectAttempts, DateTimeOffset? LastConnectedUtc, DateTimeOffset? LastPublishedUtc, string? LastErrorCode, string? LastErrorMessage);
public sealed record MqttPayload(int SchemaVersion, string RuntimeId, string DeviceId, string TagId, string TagName, TagDataType DataType, object? Value, TagQuality Quality, DateTimeOffset SourceTimestampUtc, DateTimeOffset PublishedAtUtc);
