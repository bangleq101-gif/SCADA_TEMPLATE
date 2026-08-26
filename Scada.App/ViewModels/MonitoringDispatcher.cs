using System.Windows;
using System.Windows.Threading;

namespace Scada.App.ViewModels;

/// <summary>
/// Marshals coalesced monitoring updates to the WPF UI thread.
/// </summary>
public interface IMonitoringDispatcher
{
    bool CheckAccess();

    void Enqueue(Action callback);
}

public sealed class WpfMonitoringDispatcher : IMonitoringDispatcher
{
    public bool CheckAccess()
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess();
    }

    public void Enqueue(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            callback();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.DataBind, callback);
    }
}
