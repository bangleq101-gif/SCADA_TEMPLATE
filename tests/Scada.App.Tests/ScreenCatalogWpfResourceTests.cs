using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Xunit;

namespace Scada.App.Tests;

public sealed class ScreenCatalogWpfResourceTests
{
    [Fact]
    public void NavigationResourceRendersCatalogLeavesWithAccessibleKeyboardNavigation()
    {
        RunInSta(() =>
        {
            var options = new RuntimeOptions();
            var operation = new OperationViewModel(options);
            var machineSettings = new MachineSettingsViewModel();
            var monitoring = new MonitoringViewModel(new TestTagCache(), options);
            var engineering = new EngineeringViewModel();
            var navigation = new NavigationService(operation, machineSettings, monitoring, engineering);
            using var shell = new ShellViewModel(navigation, options);
            var resources = Load("Resources/Controls.xaml");
            var tree = new TreeView
            {
                ItemsSource = shell.NavigationItems,
                ItemTemplate = Assert.IsType<HierarchicalDataTemplate>(resources["NavigationItemTemplate"]),
                ItemContainerStyle = Assert.IsType<Style>(resources["NavigationTreeViewItemStyle"]),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            var window = new Window
            {
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Width = 1000,
                Height = 700,
                DataContext = shell,
                Content = tree
            };
            window.Resources.MergedDictionaries.Add(Load("Resources/Colors.xaml"));
            window.Resources.MergedDictionaries.Add(resources);

            try
            {
                window.Show();
                PumpDispatcher();
                window.UpdateLayout();

                Assert.Equal(4, tree.Items.Count);

                var overviewButton = Assert.Single(
                    FindDescendants<Button>(tree),
                    button => button.Content as string == "Overview");
                var item = Assert.IsType<NavigationItem>(overviewButton.DataContext);

                Assert.Equal("operation.overview", item.RouteKey);
                Assert.Equal("operation.overview", item.ScreenId);
                Assert.Equal("operation", item.IconKey);
                Assert.Equal("operator", item.RequiredRole);
                Assert.Equal("Overview", AutomationProperties.GetName(overviewButton));
                Assert.Equal("Screen operation.overview", AutomationProperties.GetHelpText(overviewButton));
                Assert.True(overviewButton.Focusable);
                Assert.True(overviewButton.IsTabStop);
            }
            finally
            {
                window.Close();
                PumpDispatcher();
                operation.Dispose();
                machineSettings.Dispose();
                monitoring.Dispose();
            }
        });
    }

    private static ResourceDictionary Load(string componentPath) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Scada.App;component/{componentPath}", UriKind.Relative));

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
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
            throw new Xunit.Sdk.XunitException("M15 WPF screen catalog test timed out.");
        }

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
