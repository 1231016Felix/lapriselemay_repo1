using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace WallpaperManager.Widgets.SystemMonitor;

/// <summary>
/// Widget affichant les métriques système (CPU, RAM, GPU, réseau).
/// </summary>
public partial class SystemMonitorWidget : WpfUserControl
{
    public SystemMonitorWidget()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Convertisseur pour calculer la largeur de la barre en fonction du pourcentage.
/// </summary>
public class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return 0.0;
        
        // values[0] = pourcentage (0-100)
        // values[1] = largeur du conteneur
        
        double percent = 0;
        double containerWidth = 0;
        
        if (values[0] is double p)
            percent = p;
        else if (values[0] is int pi)
            percent = pi;
        else if (values[0] is uint pu)
            percent = pu;
        
        if (values[1] is double w)
            containerWidth = w;
        
        if (containerWidth <= 0)
            return 0.0;
        
        var ratio = Math.Clamp(percent / 100.0, 0, 1);
        return ratio * containerWidth;
    }
    
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur pour inverser un booléen et le convertir en Visibility.
/// true -> Collapsed, false -> Visible
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
