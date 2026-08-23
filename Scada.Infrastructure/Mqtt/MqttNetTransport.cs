using MQTTnet;
using MQTTnet.Protocol;
using Scada.Core.Mqtt;

namespace Scada.Infrastructure.Mqtt;

public sealed class MqttNetTransport : IMqttTransport
{
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    public bool IsConnected => _client.IsConnected;

    public async Task<MqttConnectionResult> ConnectAsync(MqttConnectRequest request, CancellationToken cancellationToken)
    {
        var builder = new MqttClientOptionsBuilder().WithTcpServer(request.Host, request.Port).WithClientId(request.ClientId).WithKeepAlivePeriod(TimeSpan.FromSeconds(request.KeepAliveSeconds));
        builder.WithProtocolVersion(request.ProtocolVersion == Scada.Core.Mqtt.MqttProtocolVersion.V500 ? MQTTnet.Formatter.MqttProtocolVersion.V500 : MQTTnet.Formatter.MqttProtocolVersion.V311);
        if (!string.IsNullOrWhiteSpace(request.Username)) builder.WithCredentials(request.Username, request.Password);
        if (request.UseTls) builder.WithTlsOptions(options => options.UseTls(true));
        var result = await _client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
        return new MqttConnectionResult(result.ResultCode == MqttClientConnectResultCode.Success, result.ResultCode.ToString(), result.ReasonString);
    }
    public async Task PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder().WithTopic(request.Topic).WithPayload(request.Payload.ToArray()).WithQualityOfServiceLevel(request.QualityOfService == MqttQualityOfService.AtLeastOnce ? MqttQualityOfServiceLevel.AtLeastOnce : MqttQualityOfServiceLevel.AtMostOnce).WithRetainFlag(request.Retain).Build();
        await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }
    public Task DisconnectAsync(CancellationToken cancellationToken) => _client.IsConnected ? _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken) : Task.CompletedTask;
    public async ValueTask DisposeAsync() { if (_client.IsConnected) await _client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None).ConfigureAwait(false); _client.Dispose(); }
}
