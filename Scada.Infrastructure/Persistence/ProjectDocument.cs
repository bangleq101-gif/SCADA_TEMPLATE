using Scada.Core.Configuration;

namespace Scada.Infrastructure.Persistence;

public sealed class ProjectDocument
{
    public int SchemaVersion { get; set; }

    public RuntimeOptions? Scada { get; set; }
}
