using System.Windows;
using System.Windows.Controls;

namespace Scada.App.Hmi;

public sealed class FaceplateTemplateSelector : DataTemplateSelector
{
    public const string MotorTemplateKey = "MotorFaceplateTemplate";
    public const string PumpTemplateKey = "PumpFaceplateTemplate";
    public const string ValveTemplateKey = "ValveFaceplateTemplate";
    public const string AnalogTemplateKey = "AnalogFaceplateTemplate";

    public static string GetTemplateKey(HmiEquipmentKind kind) => kind switch
    {
        HmiEquipmentKind.Motor => MotorTemplateKey,
        HmiEquipmentKind.Pump => PumpTemplateKey,
        HmiEquipmentKind.Valve => ValveTemplateKey,
        _ => AnalogTemplateKey
    };

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not HmiEquipmentContext context)
        {
            return base.SelectTemplate(item, container);
        }

        var key = GetTemplateKey(context.Kind);
        return (container as FrameworkElement)?.TryFindResource(key) as DataTemplate
            ?? Application.Current?.TryFindResource(key) as DataTemplate;
    }
}
