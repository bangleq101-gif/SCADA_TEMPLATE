using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Scada.App.Hmi;

public sealed class HmiFractionToLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fraction = value is double doubleValue ? doubleValue : 0d;
        var maximum = parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
        return Math.Clamp(fraction, 0d, 1d) * maximum;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
