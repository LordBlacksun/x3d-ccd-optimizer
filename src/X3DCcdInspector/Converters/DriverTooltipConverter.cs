using System.Globalization;
using System.Windows.Data;

namespace X3DCcdInspector.Converters;

/// <summary>
/// Converts IsDriverAvailable boolean to a tooltip string.
/// When driver is available: no tooltip (null). When not: explains why disabled.
/// </summary>
public class DriverTooltipConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? "Set CCD preference for this game via AMD driver profile"
            : "AMD 3D V-Cache driver not detected. Per-game CCD preference requires the driver.";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
