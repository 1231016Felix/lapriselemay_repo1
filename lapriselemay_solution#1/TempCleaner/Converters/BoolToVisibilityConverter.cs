using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TempCleaner.Converters;

/// <summary>
/// Convertit un booléen/entier en Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value switch
        {
            bool b => b,
            int i => i > 0,
            _ => false
        };

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) 
        => throw new NotSupportedException();
}

/// <summary>
/// Inverse de BoolToVisibilityConverter - retourne Visible quand false/0
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value switch
        {
            bool b => b,
            int i => i > 0,
            _ => false
        };

        return boolValue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) 
        => throw new NotSupportedException();
}
