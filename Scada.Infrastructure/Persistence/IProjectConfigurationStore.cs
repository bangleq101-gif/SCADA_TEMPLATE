namespace Scada.Infrastructure.Persistence;

public interface IProjectConfigurationStore
{
    ProjectDocument? Load();

    void Save(ProjectDocument document);
}
