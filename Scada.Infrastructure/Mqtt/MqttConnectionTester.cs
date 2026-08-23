using Scada.Core.Mqtt;
namespace Scada.Infrastructure.Mqtt;
public sealed class MqttConnectionTester : IMqttConnectionTester
{
    public async Task<MqttConnectionResult> TestAsync(MqttOptions options, string runtimeId, CancellationToken cancellationToken)
    {
        await using IMqttTransport transport = new MqttNetTransport();
        string? password = null;
        if (!string.IsNullOrWhiteSpace(options.PasswordReference)) password = options.PasswordReference.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ? Environment.GetEnvironmentVariable(options.PasswordReference[4..]) : null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(options.ConnectionTimeoutMilliseconds);
        var id = string.IsNullOrWhiteSpace(options.ClientId) ? $"scada-{runtimeId}-test-{Guid.NewGuid():N}" : $"{options.ClientId}-test-{Guid.NewGuid():N}";
        var result = await transport.ConnectAsync(new MqttConnectRequest(options.Host, options.Port, options.ProtocolVersion, id, options.Username, password, options.UseTls, options.KeepAliveSeconds, TimeSpan.FromMilliseconds(options.ConnectionTimeoutMilliseconds)), timeout.Token).ConfigureAwait(false);
        if (result.IsAccepted) await transport.DisconnectAsync(timeout.Token).ConfigureAwait(false);
        return result;
    }
}
