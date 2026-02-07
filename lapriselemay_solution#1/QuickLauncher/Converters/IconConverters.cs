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
                "Bookmark" => type is ResultType.Bookmark or ResultType.WebSearch
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

/// <summary>
/// Convertisseur pour afficher/masquer selon si la valeur est null.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value == null || (value is string s && string.IsNullOrEmpty(s));
        var inverse = parameter as string == "Inverse";
        
        if (inverse)
            return isNull ? Visibility.Visible : Visibility.Collapsed;
        
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur pour afficher/masquer selon le type de prévisualisation.
/// </summary>
public class PreviewTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FilePreviewType previewType && parameter is string expectedType)
        {
            var matches = expectedType switch
            {
                "Image" => previewType == FilePreviewType.Image,
                "Text" => previewType == FilePreviewType.Text,
                "Folder" => previewType == FilePreviewType.Folder,
                "Application" => previewType == FilePreviewType.Application,
                "Audio" => previewType == FilePreviewType.Audio,
                "Video" => previewType == FilePreviewType.Video,
                "Archive" => previewType == FilePreviewType.Archive,
                "Document" => previewType == FilePreviewType.Document,
                "None" => previewType == FilePreviewType.None,
                _ => false
            };
            
            return matches ? Visibility.Visible : Visibility.Collapsed;
        }
        
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur booléen vers icône de recherche.
/// </summary>
public class BoolToSearchIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSearching && isSearching)
            return "⏳";
        return "🔍";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur ResultType vers texte du badge de catégorie.
/// </summary>
public class ResultTypeToBadgeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ResultType type)
        {
            return type switch
            {
                ResultType.Application => "APP",
                ResultType.StoreApp => "STORE",
                ResultType.File => "FILE",
                ResultType.Folder => "DIR",
                ResultType.Script => "SCRIPT",
                ResultType.WebSearch => "WEB",
                ResultType.Command => "CMD",
                ResultType.Calculator => "CALC",
                ResultType.SystemCommand => "SYS",
                ResultType.SearchHistory => "HIST",
                ResultType.SystemControl => "CTRL",
                ResultType.Bookmark => "FAV",
                ResultType.Note => "NOTE",
                _ => "?"
            };
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertisseur ResultType vers couleur du badge de catégorie.
/// </summary>
public class ResultTypeToBadgeColorConverter : IValueConverter
{
    // Couleurs prédéfinies pour chaque catégorie
    private static readonly Dictionary<ResultType, System.Windows.Media.Color> BadgeColors = new()
    {
        { ResultType.Application, System.Windows.Media.Color.FromRgb(0x10, 0x7C, 0x10) },   // Vert
        { ResultType.StoreApp, System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4) },      // Bleu Windows
        { ResultType.File, System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88) },          // Gris
        { ResultType.Folder, System.Windows.Media.Color.FromRgb(0xFF, 0xB9, 0x00) },        // Jaune/Orange
        { ResultType.Script, System.Windows.Media.Color.FromRgb(0xE8, 0x11, 0x23) },        // Rouge
        { ResultType.WebSearch, System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4) },     // Bleu
        { ResultType.Command, System.Windows.Media.Color.FromRgb(0x68, 0x21, 0x7A) },       // Violet
        { ResultType.Calculator, System.Windows.Media.Color.FromRgb(0x00, 0x99, 0xBC) },    // Cyan
        { ResultType.SystemCommand, System.Windows.Media.Color.FromRgb(0x68, 0x21, 0x7A) }, // Violet
        { ResultType.SearchHistory, System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66) }, // Gris foncé
        { ResultType.SystemControl, System.Windows.Media.Color.FromRgb(0xFF, 0x8C, 0x00) }, // Orange
        { ResultType.Bookmark, System.Windows.Media.Color.FromRgb(0xFF, 0xC8, 0x00) },      // Jaune doré
        { ResultType.Note, System.Windows.Media.Color.FromRgb(0xE3, 0x00, 0x8C) }           // Rose
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ResultType type && BadgeColors.TryGetValue(type, out var color))
        {
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit l'AlternationIndex (0-based) en texte de raccourci "Alt+1" à "Alt+9".
/// Retourne une chaîne vide pour les index >= 9.
/// </summary>
public class AlternationToShortcutConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int index and >= 0 and < 9)
            return $"Alt+{index + 1}";
        return "";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Rend visible uniquement les éléments avec AlternationIndex 0-8 (Alt+1 à Alt+9).
/// </summary>
public class AlternationToShortcutVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int index and >= 0 and < 9
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
