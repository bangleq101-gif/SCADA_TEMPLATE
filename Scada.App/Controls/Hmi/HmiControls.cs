using System.Windows;
using System.Windows.Controls;
using Scada.App.Hmi;

namespace Scada.App.Controls.Hmi;
public abstract class HmiControlBase : Control
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(nameof(Context), typeof(HmiEquipmentContext), typeof(HmiControlBase));
    public HmiEquipmentContext? Context { get => (HmiEquipmentContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
}
public sealed class MotorControl : HmiControlBase { }
public sealed class PumpControl : HmiControlBase { }
public sealed class ValveControl : HmiControlBase { }
public sealed class TankControl : HmiControlBase { }
public sealed class PipeControl : HmiControlBase { }
public sealed class ConveyorControl : HmiControlBase { }
public sealed class IndicatorControl : HmiControlBase { }
public sealed class FaceplateHost : ContentControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(nameof(Context), typeof(HmiEquipmentContext), typeof(FaceplateHost));
    public HmiEquipmentContext? Context { get => (HmiEquipmentContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
}
