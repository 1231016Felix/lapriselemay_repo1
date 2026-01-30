using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace WallpaperManager.Widgets.Battery;

public partial class BatteryWidget : WpfUserControl
{
    public BatteryWidget()
    {
        InitializeComponent();
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return 0.0;
        
        double percent = 0;
        double containerWidth = 0;
        
        if (values[0] is int pi)
            percent = pi;
        else if (values[0] is double pd)
            percent = pd;
        
        if (values[1] is double w)
            containerWidth = w;
        
        if (containerWidth <= 0)
            return 0.0;
        
        var ratio = Math.Clamp(percent / 100.0, 0, 1);
        return ratio * containerWidth;
    }
    
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
