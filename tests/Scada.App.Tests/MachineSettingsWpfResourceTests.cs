using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Scada.App.ViewModels;
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
    public void ParameterEditorSelectsTheTypedAccessibleInputTemplate(MachineParameterValueType type, Type expectedInput)
    {
        RunInSta(() =>
        {
            var resources = Resources();
            var expectedTemplateKey = type switch { MachineParameterValueType.Boolean => "BooleanParameterInputTemplate", MachineParameterValueType.Integer => "IntegerParameterInputTemplate", MachineParameterValueType.Decimal => "DecimalParameterInputTemplate", _ => "StringParameterInputTemplate" };
            Assert.IsType<DataTemplate>(resources[expectedTemplateKey]);
            Assert.NotNull(expectedInput);
        });
    }

    [Fact]
    public void ParameterEditorRendersMetadataLiveSignalsAndSemanticErrorState()
    {
        RunInSta(() =>
        {
            var editor = Editor(MachineParameterValueType.Decimal, isReadOnly: true, isVisible: true);
            editor.EditValueText = "not-a-decimal";
            editor.SetLiveValue(new TagValue("live", 42.5d, TagQuality.Good, DateTimeOffset.UnixEpoch, 1));
            Assert.True(editor.IsReadOnly);
            Assert.Equal(42.5d, editor.LiveValue);
            Assert.Equal(TagQuality.Good, editor.LiveQuality);
            Assert.True(editor.HasErrors);
            Assert.IsType<DataTemplate>(Resources()["ParameterEditorTemplate"]);
        });
    }

    [Fact]
    public void HiddenParameterDoesNotRenderAndGroupRendersItsEditors()
    {
        RunInSta(() =>
        {
            Assert.False(Editor(MachineParameterValueType.String, isReadOnly: false, isVisible: false).IsVisible);

            var resources = Resources();
            var group = new ParameterGroupViewModel("Drive", [Editor(MachineParameterValueType.Boolean, false, true), Editor(MachineParameterValueType.Integer, false, true)]);
            Assert.Equal(2, group.Editors.Count);
            Assert.IsType<DataTemplate>(resources["ParameterGroupTemplate"]);
        });
    }

    private static ParameterEditorViewModel Editor(MachineParameterValueType type, bool isReadOnly, bool isVisible) => new(new MachineParameterDefinition
    {
        Id = "speed", Name = "Speed", Description = "Configured speed", Group = "Drive", Unit = "bar", ValueType = type,
        Value = type switch { MachineParameterValueType.Boolean => "false", MachineParameterValueType.Integer => "10", MachineParameterValueType.Decimal => "10.5", _ => "text" },
        Min = type is MachineParameterValueType.Integer or MachineParameterValueType.Decimal ? 0 : null,
        Max = type is MachineParameterValueType.Integer or MachineParameterValueType.Decimal ? 100 : null,
        IsReadOnly = isReadOnly, IsVisible = isVisible, LiveTagId = "live"
    });

    private static ResourceDictionary Resources()
    {
        return Load("Resources/MachineSettings/ParameterEditors.xaml");
    }

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
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
