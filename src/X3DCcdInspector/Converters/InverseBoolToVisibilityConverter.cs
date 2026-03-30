using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace X3DCcdInspector.Converters;

/// <summary>
/// Converts boolean to Visibility with inverted logic:
/// true → Collapsed, false → Visible.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
