namespace Scada.Runtime.Health;

public enum RuntimeHealthState
{
    Unknown,
    Starting,
    Healthy,
    Degraded,
    Faulted,
    Disabled,
    Stopping
}
