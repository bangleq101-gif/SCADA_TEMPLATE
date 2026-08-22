namespace Scada.Infrastructure.Persistence;

public sealed class ProjectPathResolver : IProjectPathResolver
{
    public ProjectPath Resolve(string explicitProjectFile) => new(explicitProjectFile);
}
