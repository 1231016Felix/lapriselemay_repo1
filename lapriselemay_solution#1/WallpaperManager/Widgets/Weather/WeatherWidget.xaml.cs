using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace WallpaperManager.Widgets.Weather;

/// <summary>
/// Widget affichant la météo actuelle et les prévisions.
/// </summary>
public partial class WeatherWidget : WpfUserControl
{
    public WeatherWidget()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Convertisseur inverse pour BooleanToVisibility.
/// true = Collapsed, false = Visible
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Visibility.Collapsed;
        return Visibility.Visible;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur pour afficher la probabilité de précipitations seulement si > 10%.
/// </summary>
public class PrecipVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int prob && prob > 10)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur pour afficher un élément si la valeur n'est pas null ou vide.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && !string.IsNullOrEmpty(str))
            return Visibility.Visible;
        return Visibility.Collapsed;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
