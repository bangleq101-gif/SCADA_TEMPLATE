using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Drivers.Simulator;
using Xunit;

namespace Scada.App.Tests;

public sealed class EngineeringDevicesWpfResourceTests
{
    [Fact]
    public void EngineeringDevicesViewRendersVirtualizedDeviceAndAddressSurfaces()
    {
        RunInSta(() =>
        {
            var provider = new SimulatorEngineeringProvider();
            var options = new RuntimeOptions
            {
                Devices = Enumerable.Range(0, 50)
                    .Select(index => new DeviceDefinition
                    {
                        Id = $"SIM{index:00}",
                        Name = $"Simulator {index:00}",
                        DriverType = "Simulator"
                    }).ToList()
            };
            using var viewModel = new EngineeringDevicesViewModel(
                new ProjectEditSession(options, null, null, [provider]),
                [provider]);
            var window = new Window { Width = 1000, Height = 700, Left = -32000, Top = -32000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Resources.MergedDictionaries.Add(Load("Resources/Colors.xaml"));
            window.Resources.MergedDictionaries.Add(Load("Resources/Controls.xaml"));
            var view = new Scada.App.Views.EngineeringDevicesView { DataContext = viewModel };
            window.Content = view;

            try
            {
                window.Show();
                PumpDispatcher();
                view.UpdateLayout();

                var grids = FindDescendants<DataGrid>(view).ToArray();
                Assert.True(grids.Length >= 2);
                Assert.Contains(grids, grid => VirtualizingPanel.GetIsVirtualizing(grid));
                Assert.Contains(FindDescendants<TextBlock>(view), text => text.Text == "Engineering Devices");
                Assert.Contains(FindDescendants<TextBlock>(view), text => text.Text == "Address Browser");
                Assert.Contains(FindDescendants<TextBox>(view), text => AutomationProperties.GetName(text) == "Device Id");
            }
            finally
            {
                window.Close();
                PumpDispatcher();
            }
        });
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
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new Xunit.Sdk.XunitException("Engineering Devices WPF test timed out.");
        }

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private static ResourceDictionary Load(string componentPath) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Scada.App;component/{componentPath}", UriKind.Relative));
}
