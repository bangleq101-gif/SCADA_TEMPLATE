using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.App.Views;
using Xunit;

namespace Scada.App.Tests;

public sealed class RuntimeHealthWpfResourceTests
{
    [Fact]
    public void SystemAndDiagnosticsViewsRenderReadOnlyVisualSurfaces()
    {
        RunInSta(() =>
        {
            var system = new SystemServicesView();
            var diagnostics = new EngineeringDiagnosticsView();
            Render(system);
            Render(diagnostics);
            Assert.Single(FindDescendants<ItemsControl>(system));
            Assert.Single(FindDescendants<DataGrid>(diagnostics));
        });
    }

    [Fact]
    public void DiagnosticsDataGridDeclaresVirtualizedReadOnlyComposition()
    {
        RunInSta(() =>
        {
            var view = new EngineeringDiagnosticsView();
            Render(view);

            var dataGrid = FindDescendants<DataGrid>(view).Single();
            Assert.True(dataGrid.IsReadOnly);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(dataGrid));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(dataGrid));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(dataGrid)));
        });
    }

    private static ResourceDictionary Load(string path) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Scada.App;component/{path}", UriKind.Relative));

    private static void Render(FrameworkElement root)
    {
        if (root is Control control)
        {
            control.Foreground = Brushes.Black;
        }

        var window = new Window
        {
            Content = root,
            Width = 800,
            Height = 500,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };
        try
        {
            window.Show();
            PumpDispatcher();
            root.UpdateLayout();
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new Xunit.Sdk.XunitException("WPF test did not complete within 30 seconds.");
        }
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
