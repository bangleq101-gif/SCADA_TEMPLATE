namespace Scada.Core.History;

public sealed class HistoryStorePermanentException : Exception
{
    public HistoryStorePermanentException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
