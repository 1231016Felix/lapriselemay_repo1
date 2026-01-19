using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using QuickLauncher.Models;

namespace QuickLauncher.Converters;

/// <summary>
/// Convertisseur pour afficher une icône native ou un emoji de fallback.
/// </summary>
public class IconToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasNativeIcon = value is ImageSource imageSource && imageSource != null;
        var isNativeIconParam = parameter as string == "Native";
        
        if (isNativeIconParam)
        {
            return hasNativeIcon ? Visibility.Visible : Visibility.Collapsed;
        }
        else // "Emoji" parameter - fallback
        {
            return hasNativeIcon ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur pour l'inverse d'un booléen vers Visibility.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur multi-valeur pour déterminer si une icône native doit être affichée.
/// </summary>
public class NativeIconVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 1 && values[0] is ImageSource imageSource && imageSource != null)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur pour masquer le contexte menu selon le type de résultat.
/// </summary>
public class ResultTypeToMenuVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ResultType type)
        {
            // Masquer certaines options pour certains types
            var menuItem = parameter as string;
            
            return menuItem switch
            {
                "RunAsAdmin" => type is ResultType.Application or ResultType.Script 
                    ? Visibility.Visible 
                    : Visibility.Collapsed,
                "OpenWith" => type is ResultType.File 
                    ? Visibility.Visible 
                    : Visibility.Collapsed,
                "Terminal" => type is ResultType.File or ResultType.Folder 
                    ? Visibility.Visible 
                    : Visibility.Collapsed,
                "FileActions" => type is ResultType.Application or ResultType.File or 
                                 ResultType.Folder or ResultType.Script or ResultType.StoreApp
                    ? Visibility.Visible 
                    : Visibility.Collapsed,
                _ => Visibility.Visible
            };
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
