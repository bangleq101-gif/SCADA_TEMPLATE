namespace Scada.Infrastructure.Alarms;

public sealed class AlarmStoreException : Exception
{
    public AlarmStoreException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
