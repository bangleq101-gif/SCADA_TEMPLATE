namespace Scada.Core.Alarms;

public enum AlarmLifecycleState
{
    Normal,
    ActiveUnacknowledged,
    ActiveAcknowledged,
    ReturnedUnacknowledged
}
