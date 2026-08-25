using System.Windows;
using System.Windows.Threading;

namespace Scada.App.ViewModels;

public interface IRuntimeHealthDispatcher
{
    void Post(Action action);
}

public sealed class WpfRuntimeHealthDispatcher : IRuntimeHealthDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.DataBind, action);
    }
}
