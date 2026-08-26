using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Scada.App.ViewModels;
using Scada.App.Views;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class MonitoringWpfResourceTests
{
    [Fact]
    public void MonitoringViewRendersAccessibleBoundedVirtualizedSurface()
    {
        RunInSta(() =>
        {
            var cache = new TestTagCache();
            using var viewModel = new MonitoringViewModel(cache, new RuntimeOptions
            {
                Tags = Enumerable.Range(0, 600).Select(index => new TagDefinition
                {
                    Id = $"T{index:D4}",
                    Name = $"Tag {index:D4}",
                    DeviceId = index % 2 == 0 ? "SIM01" : "SIM02",
                    Address = $"A{index}"
                }).ToList()
            });
            var window = new Window
            {
                Width = 900,
                Height = 600,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Background = Brushes.White,
                Foreground = Brushes.Black
            };
            window.Resources.MergedDictionaries.Add(Load("Resources/Colors.xaml"));
            window.Resources.MergedDictionaries.Add(Load("Resources/Controls.xaml"));
            var view = new MonitoringView { DataContext = viewModel };
            window.Content = view;

            try
            {
                window.Show();
                PumpDispatcher();
                view.UpdateLayout();

                var grid = Assert.Single(FindDescendants<DataGrid>(view));
                Assert.True(grid.IsReadOnly);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(grid));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(grid));
                Assert.Equal("Bounded online Tag monitor", AutomationProperties.GetName(grid));
                Assert.Contains(FindDescendants<TextBox>(view), text =>
                    AutomationProperties.GetName(text) == "Search monitored tags");
                Assert.Contains(FindDescendants<ComboBox>(view), combo =>
                    AutomationProperties.GetName(combo) == "Filter monitored tags by device");
                Assert.Contains(FindDescendants<Button>(view), button =>
                    AutomationProperties.GetName(button) == "Next monitoring page");
                Assert.Equal(MonitoringViewModel.DefaultPageSize, viewModel.Rows.Count);
            }
            finally
            {
                window.Close();
                PumpDispatcher();
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
            throw new Xunit.Sdk.XunitException("WPF monitoring test did not complete within 30 seconds.");
        }

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
