namespace Scada.Core.History;

public sealed class HistoryStoreTransientException : Exception
{
    public HistoryStoreTransientException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
