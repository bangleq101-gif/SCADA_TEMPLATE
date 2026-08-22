namespace Scada.Infrastructure.Persistence;

public sealed class ProjectDocumentException : InvalidOperationException
{
    public ProjectDocumentException(string message)
        : base(message)
    {
    }

    public ProjectDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
