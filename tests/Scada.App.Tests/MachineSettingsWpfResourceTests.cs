using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.MachineSettings;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class MachineSettingsWpfResourceTests
{
    [Theory]
    [InlineData(MachineParameterValueType.Boolean, typeof(CheckBox))]
    [InlineData(MachineParameterValueType.Integer, typeof(TextBox))]
    [InlineData(MachineParameterValueType.Decimal, typeof(TextBox))]
    [InlineData(MachineParameterValueType.String, typeof(TextBox))]
    public void ParameterEditorRendersTheTypedAccessibleInput(MachineParameterValueType type, Type expectedInput)
    {
        RunInSta(() =>
        {
            var editor = Editor(type, isReadOnly: false, isVisible: true);
            var presenter = Presenter(editor, "ParameterEditorTemplate");

            Render(presenter, new Size(720, 240), () =>
            {
                var input = Assert.Single(FindDescendants<Control>(presenter), expectedInput.IsInstanceOfType);
                Assert.Equal("Speed", AutomationProperties.GetName(input));
                Assert.Equal("Configured speed", AutomationProperties.GetHelpText(input));
                Assert.True(input.IsTabStop);
                Assert.Equal(type == MachineParameterValueType.Boolean, input is CheckBox);
            });
        });
    }

    [Fact]
    public void ParameterEditorRendersMetadataReadOnlyErrorAndLiveSignalUpdates()
    {
        RunInSta(() =>
        {
            var editor = Editor(MachineParameterValueType.Decimal, isReadOnly: true, isVisible: true);
            editor.EditValueText = "not-a-decimal";
            var presenter = Presenter(editor, "ParameterEditorTemplate");

            Render(presenter, new Size(720, 240), () =>
            {
                var input = Assert.Single(FindDescendants<TextBox>(presenter));
                Assert.True(input.IsReadOnly);
                var texts = FindDescendants<TextBlock>(presenter).ToArray();
                Assert.Contains(texts, text => text.Text == "Speed");
                Assert.Contains(texts, text => text.Text == "Configured speed");
                Assert.Contains(texts, text => text.Text == "bar");
                Assert.Contains(texts, text => text.Text == "Min: 0");
                Assert.Contains(texts, text => text.Text == "Max: 100");
                var error = Assert.Single(texts, text => AutomationProperties.GetName(text) == "Validation error");
                Assert.Equal(Visibility.Visible, error.Visibility);
                Assert.Contains("Validation error:", error.Text, StringComparison.Ordinal);
                Assert.NotEqual(Brushes.Transparent, error.Foreground);

                editor.SetLiveValue(new TagValue("live", 42.5d, TagQuality.Good, DateTimeOffset.UnixEpoch, 1));
                PumpDispatcher();

                Assert.Contains(texts, text => text.Text == "Value: 42.5");
                Assert.Contains(texts, text => text.Text == "Quality: Good");
                Assert.Contains(texts, text => text.Text?.StartsWith("Timestamp: 1970-01-01", StringComparison.Ordinal) == true);
            });
        });
    }

    [Fact]
    public void HiddenParameterTemplateCollapsesItsRenderedRoot()
    {
        RunInSta(() =>
        {
            var presenter = Presenter(Editor(MachineParameterValueType.String, isReadOnly: false, isVisible: false), "ParameterEditorTemplate");

            Render(presenter, new Size(720, 240), () =>
            {
                var root = Assert.Single(FindDescendants<Border>(presenter), border => AutomationProperties.GetName(border.Child) == "Speed");
                Assert.Equal(Visibility.Collapsed, root.Visibility);
            });
        });
    }

    [Fact]
    public void NavigationTreeRendersGroupAndLeafTitlesAndSelectsTheLeaf()
    {
        RunInSta(() =>
        {
            var options = Options(2);
            options.MachineSettings.Pages[0].Group = "Drive";
            options.MachineSettings.Pages[0].Title = "Drive setup";
            options.MachineSettings.Pages[1].Group = "Safety";
            options.MachineSettings.Pages[1].Title = "Safety setup";
            var viewModel = new MachineSettingsViewModel(new ProjectEditSession(options, null, null), new TestTagCache());
            var resources = Resources();
            var tree = new TreeView
            {
                ItemsSource = viewModel.PageGroups,
                ItemTemplate = Assert.IsType<HierarchicalDataTemplate>(resources["MachineSettingsPageGroupTemplate"])
            };
            tree.Resources.MergedDictionaries.Add(resources);
            tree.SelectedItemChanged += (_, args) =>
            {
                if (args.NewValue is MachineSettingsPageViewModel page) viewModel.SelectedPage = page;
            };

            Render(tree, new Size(300, 300), () =>
            {
                var driveGroup = Assert.IsType<TreeViewItem>(tree.ItemContainerGenerator.ContainerFromIndex(0));
                driveGroup.IsExpanded = true;
                PumpDispatcher();
                var drivePage = viewModel.PageGroups[0].Pages[0];
                var leaf = Assert.IsType<TreeViewItem>(driveGroup.ItemContainerGenerator.ContainerFromItem(drivePage));
                leaf.IsSelected = true;
                PumpDispatcher();

                Assert.Contains(FindDescendants<TextBlock>(tree), text => text.Text == "Drive");
                Assert.Contains(FindDescendants<TextBlock>(tree), text => text.Text == "Drive setup");
                Assert.Same(drivePage, tree.SelectedItem);
                Assert.Same(drivePage, viewModel.SelectedPage);
            });
        });
    }

    [Fact]
    public void LargeGroupedPageUsesOneVirtualizationOwnerAndBoundsRealizedEditors()
    {
        RunInSta(() =>
        {
            var options = Options(1, parameterCount: 500, groupCount: 50);
            var viewModel = new MachineSettingsViewModel(new ProjectEditSession(options, null, null), new TestTagCache());
            var list = new ListBox
            {
                ItemsSource = viewModel.SelectedPage!.PresentationRows,
                BorderThickness = new Thickness(0)
            };
            list.SetValue(ScrollViewer.CanContentScrollProperty, true);
            list.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
            list.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            list.Resources.MergedDictionaries.Add(Resources());

            Render(list, new Size(720, 280), () =>
            {
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                Assert.Equal(550, list.Items.Count);
                Assert.Contains(FindDescendants<TextBlock>(list), text => text.Text == "Group 00");
                var realizedEditors = FindDescendants<TextBox>(list).Count() + FindDescendants<CheckBox>(list).Count();
                Assert.InRange(realizedEditors, 1, 100);
                Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1));
                Assert.Single(FindDescendants<ListBox>(list).Prepend(list).Distinct());
            });

            AssertNoPerParameterSchedulingFields(typeof(ParameterEditorViewModel));
            AssertNoPerParameterSchedulingFields(typeof(ParameterGroupViewModel));
        });
    }

    private static ContentPresenter Presenter(object content, string templateKey)
    {
        var resources = Resources();
        var presenter = new ContentPresenter
        {
            Content = content,
            ContentTemplate = Assert.IsType<DataTemplate>(resources[templateKey])
        };
        presenter.Resources.MergedDictionaries.Add(resources);
        return presenter;
    }

    private static void Render(FrameworkElement root, Size size, Action assertion)
    {
        var window = new Window
        {
            Content = root,
            Width = size.Width,
            Height = size.Height,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };

        try
        {
            window.Show();
            PumpDispatcher();
            root.UpdateLayout();
            assertion();
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void AssertNoPerParameterSchedulingFields(Type type)
    {
        var fieldTypes = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(field => field.FieldType).ToArray();
        Assert.DoesNotContain(fieldTypes, fieldType => typeof(Task).IsAssignableFrom(fieldType));
        Assert.DoesNotContain(fieldTypes, fieldType => typeof(Timer).IsAssignableFrom(fieldType));
        Assert.DoesNotContain(fieldTypes, fieldType => typeof(DispatcherTimer).IsAssignableFrom(fieldType));
    }

    private static RuntimeOptions Options(int pageCount, int parameterCount = 1, int groupCount = 1) => new()
    {
        MachineSettings = new MachineSettingsOptions
        {
            Pages = Enumerable.Range(0, pageCount).Select(pageIndex => new MachineSettingsPageDefinition
            {
                Id = $"page-{pageIndex}",
                Title = $"Page {pageIndex}",
                Parameters = Enumerable.Range(0, parameterCount).Select(parameterIndex => new MachineParameterDefinition
                {
                    Id = $"parameter-{parameterIndex}",
                    Name = $"Parameter {parameterIndex}",
                    Description = "Configured speed",
                    Group = $"Group {parameterIndex % groupCount:00}",
                    Unit = "bar",
                    ValueType = MachineParameterValueType.Integer,
                    Value = "10",
                    Min = 0,
                    Max = 100
                }).ToList()
            }).ToList()
        }
    };

    private static ParameterEditorViewModel Editor(MachineParameterValueType type, bool isReadOnly, bool isVisible) => new(new MachineParameterDefinition
    {
        Id = "speed", Name = "Speed", Description = "Configured speed", Group = "Drive", Unit = "bar", ValueType = type,
        Value = type switch { MachineParameterValueType.Boolean => "false", MachineParameterValueType.Integer => "10", MachineParameterValueType.Decimal => "10.5", _ => "text" },
        Min = type is MachineParameterValueType.Integer or MachineParameterValueType.Decimal ? 0 : null,
        Max = type is MachineParameterValueType.Integer or MachineParameterValueType.Decimal ? 100 : null,
        IsReadOnly = isReadOnly, IsVisible = isVisible, LiveTagId = "live"
    });

    private static ResourceDictionary Resources() => Load("Resources/MachineSettings/ParameterEditors.xaml");

    private static ResourceDictionary Load(string componentPath) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Scada.App;component/{componentPath}", UriKind.Relative));

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
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new Xunit.Sdk.XunitException("WPF STA test did not complete within 30 seconds.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
