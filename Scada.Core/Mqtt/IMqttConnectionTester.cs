namespace Scada.Core.Mqtt;
public interface IMqttConnectionTester { Task<MqttConnectionResult> TestAsync(MqttOptions options, string runtimeId, CancellationToken cancellationToken); }
