using Scada.Core.Devices;

namespace Scada.Runtime.Drivers;

public interface IPlcDriverResolver
{
    IPlcDriverLease Acquire(DeviceDefinition device);
}
