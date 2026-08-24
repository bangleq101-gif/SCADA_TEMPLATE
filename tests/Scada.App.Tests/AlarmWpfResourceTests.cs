using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Scada.App.ViewModels;
using Scada.Core.Alarms;
using Scada.Core.Tags;
using Scada.Runtime.Alarms;
using Xunit;

namespace Scada.App.Tests;

public sealed class AlarmWpfResourceTests
{
    [Theory]
    [InlineData("AlarmStateCellTemplate", "ActiveUnacknowledged", "!")]
    [InlineData("AlarmSeverityCellTemplate", "Critical", "◆")]
    [InlineData("AlarmQualityCellTemplate", "Bad", "!")]
    public void AlarmTemplatesRenderTextGlyphAndAccessibleSemanticName(string key, string expectedText, string expectedGlyph)
    {
        RunInSta(() =>
        {
            var resources = (ResourceDictionary)Application.LoadComponent(
                new Uri("/Scada.App;component/Resources/Alarms.xaml", UriKind.Relative));
            var template = Assert.IsType<DataTemplate>(resources[key]);
            var row = new AlarmRowViewModel(new AlarmSnapshot(
                "A1", "Alarm", "Message", Guid.NewGuid(), AlarmLifecycleState.ActiveUnacknowledged,
                AlarmSeverity.Critical, false, false, TagQuality.Bad, 1, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null));
            var presenter = new ContentPresenter { Content = row, ContentTemplate = template };

            presenter.ApplyTemplate();
            presenter.Measure(new Size(400, 80));
            presenter.Arrange(new Rect(0, 0, 400, 80));
            presenter.UpdateLayout();

            var texts = FindDescendants<TextBlock>(presenter).Select(item => item.Text).ToArray();
            Assert.Contains(expectedText, texts);
            Assert.Contains(expectedGlyph, texts);
            Assert.Contains(FindDescendants<FrameworkElement>(presenter),
                element => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)));
        });
    }

    [Fact]
    public void AlarmViewsDeclareVirtualizedListsAndReadOnlyOperationalActions()
    {
        var root = FindRepositoryRoot();
        var monitoring = File.ReadAllText(Path.Combine(root, "Scada.App", "Views", "AlarmMonitoringView.xaml"));
        var engineering = File.ReadAllText(Path.Combine(root, "Scada.App", "Views", "AlarmEngineeringView.xaml"));

        Assert.Contains("WorkspaceDataGridStyle", monitoring, StringComparison.Ordinal);
        Assert.Contains("Acknowledge selected", monitoring, StringComparison.Ordinal);
        Assert.Contains("Read-only Alarm event journal", monitoring, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", monitoring, StringComparison.Ordinal);
        Assert.DoesNotContain("PLC", monitoring, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TagManagerDataGridStyle", engineering, StringComparison.Ordinal);
        Assert.Contains("ValidationIssues", engineering, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", monitoring, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", engineering, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Scada.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
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
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
