using System.Reflection;
using System.IO;
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

    [Fact]
    public void MainWindowStatusBarExposesVisibleGlyphTextAndAccessibleHealthIndicators()
    {
        RunInSta(() =>
        {
            var root = new StackPanel
            {
                DataContext = new StatusBarProbe(),
                Orientation = Orientation.Horizontal
            };
            AddIndicator(root, nameof(StatusBarProbe.PlcHealthIndicatorText), nameof(StatusBarProbe.PlcHealthAutomationName));
            AddIndicator(root, nameof(StatusBarProbe.HistoryHealthIndicatorText), nameof(StatusBarProbe.HistoryHealthAutomationName));
            AddIndicator(root, nameof(StatusBarProbe.MqttHealthIndicatorText), nameof(StatusBarProbe.MqttHealthAutomationName));
            AddIndicator(root, nameof(StatusBarProbe.RuntimeHealthIndicatorText), nameof(StatusBarProbe.RuntimeHealthAutomationName));
            Render(root);

            var indicators = FindDescendants<TextBlock>(root).ToArray();
            Assert.Equal(4, indicators.Length);
            Assert.Contains(indicators, item => item.Text.Contains("●", StringComparison.Ordinal));
            Assert.All(indicators, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Text));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)));
            });

            var mainWindowXaml = File.ReadAllText(
                Path.Combine(FindRepositoryRoot(), "Scada.App", "MainWindow.xaml"));
            Assert.Contains("PlcHealthIndicatorText", mainWindowXaml, StringComparison.Ordinal);
            Assert.Contains("HistoryHealthIndicatorText", mainWindowXaml, StringComparison.Ordinal);
            Assert.Contains("MqttHealthIndicatorText", mainWindowXaml, StringComparison.Ordinal);
            Assert.Contains("RuntimeHealthIndicatorText", mainWindowXaml, StringComparison.Ordinal);
            Assert.Contains("AutomationProperties.Name", mainWindowXaml, StringComparison.Ordinal);
        });
    }

    private static void AddIndicator(StackPanel root, string textProperty, string automationProperty)
    {
        var text = new TextBlock();
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(textProperty));
        text.SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding(automationProperty));
        root.Children.Add(text);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Scada.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
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

    private sealed class StatusBarProbe
    {
        public string RuntimeStatusText => "Runtime01  •  Operation";
        public string CurrentWorkspaceTitle => "Operation";
        public string PlcHealthIndicatorText => "● PLC: Healthy";
        public string HistoryHealthIndicatorText => "● History: Healthy";
        public string MqttHealthIndicatorText => "— MQTT: Disabled";
        public string RuntimeHealthIndicatorText => "● Runtime: Healthy";
        public string PlcHealthAutomationName => "PLC health: Healthy";
        public string HistoryHealthAutomationName => "History health: Healthy";
        public string MqttHealthAutomationName => "MQTT health: Disabled";
        public string RuntimeHealthAutomationName => "Runtime health: Healthy";
        public IReadOnlyList<object> NavigationItems { get; } = [];
        public object? CurrentViewModel => null;
    }
}
