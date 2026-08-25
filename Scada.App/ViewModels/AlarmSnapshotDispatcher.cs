using System.Windows;
using System.Windows.Threading;

namespace Scada.App.ViewModels;

internal interface IAlarmSnapshotDispatcher
{
    void Post(Action action);
}

internal sealed class WpfAlarmSnapshotDispatcher : IAlarmSnapshotDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.DataBind, action);
    }
}
