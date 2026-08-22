namespace Scada.Infrastructure.Persistence;

public interface IProjectPathResolver
{
    ProjectPath Resolve(string explicitProjectFile);
}
