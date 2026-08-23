namespace Scada.App.Services;

public interface IHistoryBufferConfirmation
{
    bool Confirm(string action, long count);
}
