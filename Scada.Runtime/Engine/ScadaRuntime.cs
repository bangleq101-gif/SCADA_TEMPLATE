using Scada.Core.Common;
using Scada.Core.Configuration;

namespace Scada.Runtime.Engine;

public sealed class ScadaRuntime(RuntimeOptions options)
{
    public RuntimeId RuntimeId => new(options.RuntimeId);
}
