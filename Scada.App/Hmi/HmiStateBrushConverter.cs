using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Scada.App.Hmi;
public sealed class HmiStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is HmiVisualState state ? state switch
    { HmiVisualState.Running => new SolidColorBrush(Color.FromRgb(46,139,87)), HmiVisualState.Fault => new SolidColorBrush(Color.FromRgb(179,58,58)), HmiVisualState.Warning => new SolidColorBrush(Color.FromRgb(196,127,0)), HmiVisualState.BadQuality => new SolidColorBrush(Color.FromRgb(122,78,0)), HmiVisualState.Unknown => new SolidColorBrush(Color.FromRgb(107,114,128)), _ => new SolidColorBrush(Color.FromRgb(113,128,150)) } : Brushes.Gray;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
