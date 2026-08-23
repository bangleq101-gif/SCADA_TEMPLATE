using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Scada.App.Controls.Hmi;
using Scada.App.Hmi;
using Xunit;

namespace Scada.App.Tests;

public sealed class HmiWpfResourceTests
{
    [Theory]
    [InlineData(typeof(MotorControl))]
    [InlineData(typeof(PumpControl))]
    [InlineData(typeof(ValveControl))]
    [InlineData(typeof(TankControl))]
    [InlineData(typeof(PipeControl))]
    [InlineData(typeof(ConveyorControl))]
    [InlineData(typeof(IndicatorControl))]
    public void EveryEquipmentControlHasLoadableTemplateAndAccessibleName(Type controlType)
    {
        RunInSta(() =>
        {
            var resources = Load("Resources/Hmi/HmiControls.xaml");
            var style = Assert.IsType<Style>(resources[controlType]);
            var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().Single(setter => setter.Property == Control.TemplateProperty).Value);
            var root = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());

            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(root)));
        });
    }

    [Theory]
    [InlineData(HmiEquipmentKind.Motor, FaceplateTemplateSelector.MotorTemplateKey)]
    [InlineData(HmiEquipmentKind.Pump, FaceplateTemplateSelector.PumpTemplateKey)]
    [InlineData(HmiEquipmentKind.Valve, FaceplateTemplateSelector.ValveTemplateKey)]
    [InlineData(HmiEquipmentKind.Tank, FaceplateTemplateSelector.AnalogTemplateKey)]
    [InlineData(HmiEquipmentKind.Indicator, FaceplateTemplateSelector.AnalogTemplateKey)]
    [InlineData(HmiEquipmentKind.Pipe, FaceplateTemplateSelector.AnalogTemplateKey)]
    [InlineData(HmiEquipmentKind.Conveyor, FaceplateTemplateSelector.AnalogTemplateKey)]
    public void FaceplateSelectorResolvesTheRenderedTemplateFromResources(HmiEquipmentKind kind, string expectedKey)
    {
        RunInSta(() =>
        {
            var resources = Load("Resources/Hmi/HmiFaceplates.xaml");
            var selector = Assert.IsType<FaceplateTemplateSelector>(resources["FaceplateTemplateSelector"]);
            var host = new FaceplateHost();
            host.Resources.MergedDictionaries.Add(resources);
            var context = new HmiEquipmentContext(new TestTagCache(), kind, "EQ1", "Equipment 1", RequiredTags(kind));

            var selected = selector.SelectTemplate(context, host);

            Assert.Same(resources[expectedKey], selected);
            Assert.NotNull(selected);
            Assert.IsAssignableFrom<FrameworkElement>(selected.LoadContent());
        });
    }

    [Fact]
    public void FaceplateHostTemplateBindsContextAndUsesTheSharedSelector()
    {
        RunInSta(() =>
        {
            var resources = Load("Resources/Hmi/HmiFaceplates.xaml");
            var context = new HmiEquipmentContext(new TestTagCache(), HmiEquipmentKind.Motor, "M1", "Motor 1", RequiredTags(HmiEquipmentKind.Motor));
            var host = new FaceplateHost { Context = context, Style = Assert.IsType<Style>(resources[typeof(FaceplateHost)]) };
            host.Resources.MergedDictionaries.Add(resources);

            host.ApplyTemplate();
            var presenter = FindDescendant<ContentPresenter>(host);

            Assert.NotNull(presenter);
            Assert.Same(context, presenter.Content);
            Assert.IsType<FaceplateTemplateSelector>(presenter.ContentTemplateSelector);

            presenter.ApplyTemplate();
            presenter.Measure(new Size(360, 200));
            presenter.Arrange(new Rect(0, 0, 360, 200));
            presenter.UpdateLayout();

            Assert.Contains(FindDescendants<TextBlock>(presenter).Select(text => text.Text), text => text == "Motor");
        });
    }

    [Fact]
    public void TankTemplateFillGeometryTracksTheClampedRuntimeFraction()
    {
        RunInSta(() =>
        {
            var resources = Load("Resources/Hmi/HmiControls.xaml");
            var cache = new TestTagCache();
            var context = new HmiEquipmentContext(cache, HmiEquipmentKind.Tank, "T1", "Tank 1", RequiredTags(HmiEquipmentKind.Tank));
            var tank = new TankControl { Context = context, Style = Assert.IsType<Style>(resources[typeof(TankControl)]) };

            context.Activate();
            cache.Publish(new Scada.Core.Tags.TagValue("level", 25d, Scada.Core.Tags.TagQuality.Good, DateTimeOffset.UnixEpoch, 1));
            tank.ApplyTemplate();
            tank.Measure(new Size(76, 76));
            tank.Arrange(new Rect(0, 0, 76, 76));
            tank.UpdateLayout();
            var fill = Assert.Single(FindDescendants<Rectangle>(tank));
            Assert.Equal(13d, fill.Height, 3);

            cache.Publish(new Scada.Core.Tags.TagValue("level", 75d, Scada.Core.Tags.TagQuality.Good, DateTimeOffset.UnixEpoch, 2));
            Assert.Equal(39d, fill.Height, 3);
        });
    }

    private static IReadOnlyDictionary<HmiTagRole, string> RequiredTags(HmiEquipmentKind kind) => kind switch
    {
        HmiEquipmentKind.Valve => new Dictionary<HmiTagRole, string> { [HmiTagRole.Position] = "position" },
        HmiEquipmentKind.Tank => new Dictionary<HmiTagRole, string> { [HmiTagRole.Level] = "level" },
        HmiEquipmentKind.Pipe => new Dictionary<HmiTagRole, string> { [HmiTagRole.Flow] = "flow" },
        HmiEquipmentKind.Indicator => new Dictionary<HmiTagRole, string> { [HmiTagRole.Value] = "value" },
        _ => new Dictionary<HmiTagRole, string> { [HmiTagRole.Run] = "run" }
    };

    private static ResourceDictionary Load(string componentPath) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Scada.App;component/{componentPath}", UriKind.Relative));

    private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendantOrNull<T>(child);
            if (nested is not null) return nested;
        }

        throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} was found in the template visual tree.");
    }

    private static T? FindDescendantOrNull<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendantOrNull<T>(child);
            if (nested is not null) return nested;
        }

        return null;
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
