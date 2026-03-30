using System.Globalization;
using System.Windows.Data;

namespace X3DCcdInspector.Converters;

public class LoadBarWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double loadPercent && values[1] is double parentWidth)
        {
            var clamped = Math.Clamp(loadPercent, 0, 100);
            return parentWidth * (clamped / 100.0);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
