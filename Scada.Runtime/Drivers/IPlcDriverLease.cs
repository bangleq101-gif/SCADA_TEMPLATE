using Scada.Core.Drivers;

namespace Scada.Runtime.Drivers;

public interface IPlcDriverLease : IAsyncDisposable
{
    IPlcDriver Driver { get; }
}
