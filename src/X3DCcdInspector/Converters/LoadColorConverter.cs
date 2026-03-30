using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace X3DCcdInspector.Converters;

public class LoadColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double load)
        {
            string key = load switch
            {
                <= 15 => "CoreIdleBrush",
                <= 40 => "CoreModerateBrush",
                _ => "CoreHotBrush"
            };

            return Application.Current?.TryFindResource(key) as SolidColorBrush
                ?? new SolidColorBrush(Colors.Transparent);
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
