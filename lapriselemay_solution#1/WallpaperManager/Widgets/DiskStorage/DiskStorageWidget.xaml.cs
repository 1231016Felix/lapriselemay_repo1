using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfPoint = System.Windows.Point;

namespace WallpaperManager.Widgets.DiskStorage;

public partial class DiskStorageWidget : WpfUserControl
{
    public DiskStorageWidget()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Convertit un pourcentage en point d'arc pour le graphique circulaire.
/// </summary>
public class PercentToArcConverter : IMultiValueConverter, IValueConverter
{
    private const double CenterX = 22;
    private const double CenterY = 22;
    private const double Radius = 20;
    
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 1 || values[0] is not double percent)
            return new WpfPoint(CenterX, 2);
        
        // Limiter entre 0.5% et 99.5% pour éviter les problèmes d'arc complet
        percent = Math.Clamp(percent, 0.5, 99.5);
        
        // Convertir en radians (0% = haut, sens horaire)
        double angle = (percent / 100.0) * 2 * Math.PI - Math.PI / 2;
        
        double x = CenterX + Radius * Math.Cos(angle);
        double y = CenterY + Radius * Math.Sin(angle);
        
        return new WpfPoint(x, y);
    }
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter?.ToString() == "IsLarge")
        {
            if (value is double percent)
                return percent > 50;
            return false;
        }
        
        return Convert([value], targetType, parameter!, culture);
    }
    
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
