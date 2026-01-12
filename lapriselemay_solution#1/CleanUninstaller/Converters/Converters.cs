using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using CleanUninstaller.Models;
using CleanUninstaller.Helpers;
using Windows.UI;

namespace CleanUninstaller.Converters;

/// <summary>
/// Convertit un booléen en Visibility
/// </summary>
public partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is bool b && b;
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) boolValue = !boolValue;
        
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

/// <summary>
/// Convertit un niveau de confiance en couleur
/// </summary>
public partial class ConfidenceToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var confidence = value is ConfidenceLevel level ? level : ConfidenceLevel.None;
        
        var colorHex = confidence switch
        {
            ConfidenceLevel.VeryHigh => "#107C10",  // Vert foncé
            ConfidenceLevel.High => "#498205",      // Vert
            ConfidenceLevel.Medium => "#CA5010",    // Orange
            ConfidenceLevel.Low => "#D13438",       // Rouge
            _ => "#6E6E6E"                          // Gris
        };

        return new SolidColorBrush(CommonHelpers.ParseHexColor(colorHex));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit un type d'installeur en icône
/// </summary>
public partial class InstallerTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var type = value is InstallerType t ? t : InstallerType.Unknown;
        
        return type switch
        {
            InstallerType.Msi => "\uE74C",          // Box
            InstallerType.InnoSetup => "\uE756",    // Download
            InstallerType.Nsis => "\uE7B8",         // Package
            InstallerType.InstallShield => "\uE8B7", // Folder
            InstallerType.Msix => "\uE71D",         // Store
            InstallerType.Wix => "\uE912",          // Settings
            InstallerType.ClickOnce => "\uE71B",    // Globe
            InstallerType.Portable => "\uE8F1",     // Library
            _ => "\uE74C"                           // Default box
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit un type de résidu en icône
/// </summary>
public partial class ResidualTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var type = value is ResidualType t ? t : ResidualType.File;
        
        return type switch
        {
            ResidualType.File => "\uE8A5",           // File
            ResidualType.Folder => "\uE8B7",         // Folder
            ResidualType.RegistryKey => "\uE74C",    // Registry
            ResidualType.RegistryValue => "\uE8F1",  // Value
            ResidualType.Service => "\uE912",        // Service
            ResidualType.ScheduledTask => "\uE787",  // Clock
            ResidualType.Firewall => "\uE785",       // Shield
            ResidualType.StartupEntry => "\uE768",   // Power
            ResidualType.Certificate => "\uEB95",    // Certificate
            _ => "\uE8E5"                            // Unknown
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit une taille en octets en chaîne formatée
/// </summary>
public partial class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var bytes = value is long l ? l : (value is int i ? i : 0);
        return CommonHelpers.FormatSizeOrDash(bytes);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit un statut de programme en couleur
/// </summary>
public partial class ProgramStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is ProgramStatus s ? s : ProgramStatus.Installed;
        
        var colorHex = status switch
        {
            ProgramStatus.Installed => "#6E6E6E",     // Gris
            ProgramStatus.Scanning => "#0078D4",      // Bleu
            ProgramStatus.Uninstalling => "#CA5010",  // Orange
            ProgramStatus.Uninstalled => "#107C10",   // Vert
            ProgramStatus.Error => "#D13438",         // Rouge
            ProgramStatus.PartiallyRemoved => "#FF8C00", // Orange foncé
            _ => "#6E6E6E"
        };

        return new SolidColorBrush(CommonHelpers.ParseHexColor(colorHex));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit une chaîne en Visibility (Visible si non vide, Collapsed sinon)
/// </summary>
public partial class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isEmpty = string.IsNullOrWhiteSpace(value?.ToString());
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) isEmpty = !isEmpty;
        
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit null en Visibility
/// </summary>
public partial class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isNull = value == null;
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) isNull = !isNull;
        
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit une chaîne vide en Visibility
/// </summary>
public partial class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isEmpty = string.IsNullOrWhiteSpace(value as string);
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) isEmpty = !isEmpty;
        
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverse un booléen
/// </summary>
public partial class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && !b;
    }
}

/// <summary>
/// Convertisseur de string hexadécimal vers SolidColorBrush
/// </summary>
public partial class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string colorStr && colorStr.StartsWith('#'))
        {
            return new SolidColorBrush(CommonHelpers.ParseHexColor(colorStr));
        }
        return new SolidColorBrush(Color.FromArgb(255, 110, 110, 110));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit un booléen en icône (check ou vide)
/// </summary>
public partial class BoolToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is bool b && b;
        return boolValue ? "\uE73E" : ""; // Checkmark or empty
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit un booléen en couleur (vert si vrai, gris sinon)
/// </summary>
public partial class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is bool b && b;
        var color = boolValue 
            ? Color.FromArgb(255, 16, 124, 16)   // Vert
            : Color.FromArgb(255, 110, 110, 110); // Gris
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}


/// <summary>
/// Convertit un booléen en Visibility inverse (Collapsed si vrai, Visible sinon)
/// </summary>
public partial class BoolToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is bool b && b;
        return boolValue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility v && v == Visibility.Collapsed;
    }
}

/// <summary>
/// Convertit zéro en Visible (pour afficher message quand liste vide)
/// </summary>
public partial class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var number = value is int i ? i : (value is long l ? (int)l : 0);
        return number == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Convertit null en booléen (false si null, true sinon)
/// </summary>
public partial class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
