namespace Scada.Drivers.Simulator;

public enum SimulatorFaultMode
{
    None,
    ConnectFailure,
    ReadFailure,
    Disconnected,
    BadQuality,
    IntermittentReadFailure
}
