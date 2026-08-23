namespace Scada.Core.Mqtt;

public sealed class MqttOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public MqttProtocolVersion ProtocolVersion { get; set; } = MqttProtocolVersion.V311;
    public string ClientId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordReference { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string BaseTopic { get; set; } = "scada";
    public string TopicTemplate { get; set; } = "{BaseTopic}/{RuntimeId}/{DeviceId}/{TagId}";
    public int KeepAliveSeconds { get; set; } = 30;
    public int ConnectionTimeoutMilliseconds { get; set; } = 5_000;
    public int PublishTimeoutMilliseconds { get; set; } = 5_000;
    public int ReconnectInitialDelayMilliseconds { get; set; } = 1_000;
    public int ReconnectMaxDelayMilliseconds { get; set; } = 30_000;
    public int ShutdownTimeoutMilliseconds { get; set; } = 3_000;
    public List<MqttProfileDefinition> Profiles { get; set; } = MqttProfileDefaults.Create();
}

public enum MqttProtocolVersion { V311, V500 }
public enum MqttPublishMode { OnChange, Periodic, OnChangeAndPeriodic }
public enum MqttQualityOfService { AtMostOnce = 0, AtLeastOnce = 1 }

public sealed class MqttProfileDefinition
{
    public string Name { get; set; } = string.Empty;
    public MqttPublishMode Mode { get; set; } = MqttPublishMode.OnChangeAndPeriodic;
    public double Deadband { get; set; }
    public int MinimumIntervalMilliseconds { get; set; } = 1_000;
    public int MaximumIntervalMilliseconds { get; set; } = 60_000;
    public MqttQualityOfService QualityOfService { get; set; } = MqttQualityOfService.AtLeastOnce;
    public bool Retain { get; set; } = true;
}

public static class MqttProfileDefaults
{
    public static List<MqttProfileDefinition> Create() =>
    [
        new() { Name = "Default" },
        new() { Name = "WebDigital", Mode = MqttPublishMode.OnChange, MinimumIntervalMilliseconds = 0, MaximumIntervalMilliseconds = 0 },
        new() { Name = "WebAnalog", Mode = MqttPublishMode.OnChangeAndPeriodic }
    ];
}

public sealed class MqttProfileRegistry
{
    private readonly IReadOnlyDictionary<string, MqttProfileDefinition> _profiles;
    public MqttProfileRegistry(IEnumerable<MqttProfileDefinition> profiles) => _profiles = profiles
        .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
        .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    public bool TryGet(string? name, out MqttProfileDefinition? profile) => _profiles.TryGetValue(name ?? string.Empty, out profile);
    public IReadOnlyCollection<MqttProfileDefinition> All => _profiles.Values.ToArray();
}
