using System.Globalization;
using System.Windows.Data;

namespace AutoPilotAgent.UI.Converters;

public sealed class BooleanNegationConverter : IValueConverter
{
    public static BooleanNegationConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }

        return false;
    }
}
