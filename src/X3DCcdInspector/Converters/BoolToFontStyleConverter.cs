using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace X3DCcdInspector.Converters;

public class BoolToFontStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? FontStyles.Italic : FontStyles.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
