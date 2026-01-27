using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WallpaperManager.Models;

namespace WallpaperManager.Converters;

/// <summary>
/// Converter de base utilisant MarkupExtension pour une syntaxe XAML simplifiée.
/// Usage: {local:BoolToVisibility} au lieu de {Binding Converter={StaticResource BoolToVisibilityConverter}}
/// </summary>
public abstract class ConverterMarkupExtension<T> : MarkupExtension, IValueConverter 
    where T : class, new()
{
    private static T? _converter;
    
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new T();
    }
    
    public abstract object? Convert(object value, Type targetType, object parameter, CultureInfo culture);
    
    public virtual object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException($"{GetType().Name} ne supporte pas ConvertBack");
}

/// <summary>
/// Multi-converter de base utilisant MarkupExtension.
/// </summary>
public abstract class MultiConverterMarkupExtension<T> : MarkupExtension, IMultiValueConverter 
    where T : class, new()
{
    private static T? _converter;
    
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new T();
    }
    
    public abstract object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture);
    
    public virtual object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException($"{GetType().Name} ne supporte pas ConvertBack");
}

#region Boolean Converters

/// <summary>
/// Convertit un booléen en Visibility.
/// Paramètre "Invert" pour inverser le résultat.
/// Usage: {conv:BoolToVisibility}
/// </summary>
public class BoolToVisibility : ConverterMarkupExtension<BoolToVisibility>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var bValue = value is bool b && b;
        var invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) bValue = !bValue;
        
        return bValue ? Visibility.Visible : Visibility.Collapsed;
    }
    
    public override object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// Inverse un booléen.
/// Usage: {conv:InverseBool}
/// </summary>
public class InverseBool : ConverterMarkupExtension<InverseBool>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
    
    public override object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>
/// Convertit null/empty en booléen.
/// Usage: {conv:NullToBool}
/// </summary>
public class NullToBool : ConverterMarkupExtension<NullToBool>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null or string { Length: 0 };
        var invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);
        return invert ? isNull : !isNull;
    }
}

#endregion

#region Visibility Converters

/// <summary>
/// Convertit null/empty en Visibility.
/// Usage: {conv:NullToVisibility}
/// </summary>
public class NullToVisibility : ConverterMarkupExtension<NullToVisibility>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null or string { Length: 0 };
        var invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) isNull = !isNull;
        
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>
/// Convertit un count en Visibility.
/// Usage: {conv:CountToVisibility}
/// </summary>
public class CountToVisibility : ConverterMarkupExtension<CountToVisibility>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            System.Collections.ICollection c => c.Count,
            System.Collections.IEnumerable e => e.Cast<object>().Count(),
            _ => 0
        };
        
        var hasItems = count > 0;
        var invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) hasItems = !hasItems;
        
        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// Convertit une comparaison d'égalité en Visibility.
/// Usage: {conv:EqualToVisibility} avec ConverterParameter=ValeurAttendue
/// </summary>
public class EqualToVisibility : ConverterMarkupExtension<EqualToVisibility>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEqual = Equals(value?.ToString(), parameter?.ToString());
        return isEqual ? Visibility.Visible : Visibility.Collapsed;
    }
}

#endregion

#region String Converters

/// <summary>
/// Formate un nombre en taille de fichier lisible.
/// Usage: {conv:FileSizeFormat}
/// </summary>
public class FileSizeFormat : ConverterMarkupExtension<FileSizeFormat>
{
    private static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB"];
    
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => 0L
        };
        
        if (bytes == 0) return "0 B";
        
        var magnitude = (int)Math.Log(bytes, 1024);
        magnitude = Math.Min(magnitude, Suffixes.Length - 1);
        
        var adjustedSize = bytes / Math.Pow(1024, magnitude);
        
        return magnitude == 0 
            ? $"{bytes} B" 
            : $"{adjustedSize:F1} {Suffixes[magnitude]}";
    }
}

/// <summary>
/// Formate une résolution (Width x Height).
/// Usage: {conv:ResolutionFormat}
/// </summary>
public class ResolutionFormat : MultiConverterMarkupExtension<ResolutionFormat>
{
    public override object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is int width && values[1] is int height)
        {
            if (width == 0 || height == 0)
                return "—";
            return $"{width} × {height}";
        }
        return "—";
    }
}

/// <summary>
/// Tronque une chaîne avec ellipsis.
/// Usage: {conv:TruncateString} avec ConverterParameter=50 (longueur max)
/// </summary>
public class TruncateString : ConverterMarkupExtension<TruncateString>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string str || string.IsNullOrEmpty(str))
            return string.Empty;
        
        var maxLength = 50;
        if (parameter is string param && int.TryParse(param, out var len))
            maxLength = len;
        
        return str.Length <= maxLength ? str : string.Concat(str.AsSpan(0, maxLength - 3), "...");
    }
}

#endregion

#region Color Converters

/// <summary>
/// Convertit un booléen en couleur.
/// Usage: {conv:BoolToColor} avec ConverterParameter=TrueColor|FalseColor
/// </summary>
public class BoolToColor : ConverterMarkupExtension<BoolToColor>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isTrue = value is bool b && b;
        var colors = (parameter?.ToString() ?? "#22C55E|#6B7280").Split('|');
        
        var colorStr = isTrue ? colors[0] : (colors.Length > 1 ? colors[1] : "#6B7280");
        
        try
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(colorStr);
            return new System.Windows.Media.SolidColorBrush(color);
        }
        catch
        {
            return WpfBrushes.Gray;
        }
    }
}

/// <summary>
/// Convertit un type de wallpaper en couleur de badge.
/// Usage: {conv:WallpaperTypeToColor}
/// </summary>
public class WallpaperTypeToColor : ConverterMarkupExtension<WallpaperTypeToColor>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            WallpaperType.Static => new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(59, 130, 246)),    // Blue
            WallpaperType.Animated => new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(168, 85, 247)),  // Purple
            WallpaperType.Video => new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(239, 68, 68)),      // Red
            _ => WpfBrushes.Gray
        };
    }
}

#endregion

#region Type-Specific Converters

/// <summary>
/// Convertit un WallpaperType en icône emoji.
/// Usage: {conv:WallpaperTypeIcon}
/// </summary>
public class WallpaperTypeIcon : ConverterMarkupExtension<WallpaperTypeIcon>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            WallpaperType.Static => "🖼️",
            WallpaperType.Animated => "🎞️",
            WallpaperType.Video => "🎬",
            _ => "📄"
        };
    }
}

/// <summary>
/// Convertit un WallpaperType en texte.
/// Usage: {conv:WallpaperTypeText}
/// </summary>
public class WallpaperTypeText : ConverterMarkupExtension<WallpaperTypeText>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            WallpaperType.Static => "Image",
            WallpaperType.Animated => "Animé",
            WallpaperType.Video => "Vidéo",
            _ => "Inconnu"
        };
    }
}

/// <summary>
/// Convertit un TransitionEffect en texte lisible.
/// Usage: {conv:TransitionEffectText}
/// </summary>
public class TransitionEffectText : ConverterMarkupExtension<TransitionEffectText>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            Services.TransitionEffect.None => "Aucune",
            Services.TransitionEffect.Fade => "Fondu",
            Services.TransitionEffect.SlideLeft => "Glissement gauche",
            Services.TransitionEffect.SlideRight => "Glissement droite",
            Services.TransitionEffect.SlideUp => "Glissement haut",
            Services.TransitionEffect.SlideDown => "Glissement bas",
            Services.TransitionEffect.Zoom => "Zoom",
            Services.TransitionEffect.Dissolve => "Dissolution",
            _ => value?.ToString() ?? "—"
        };
    }
}

#endregion

#region DateTime Converters

/// <summary>
/// Formate une date en texte relatif ou absolu.
/// Usage: {conv:RelativeDateTime}
/// </summary>
public class RelativeDateTime : ConverterMarkupExtension<RelativeDateTime>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var date = value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            _ => DateTime.MinValue
        };
        
        if (date == DateTime.MinValue)
            return "—";
        
        var diff = DateTime.Now - date;
        
        return diff.TotalDays switch
        {
            < 1 when diff.TotalHours < 1 => "Il y a quelques minutes",
            < 1 => $"Il y a {(int)diff.TotalHours}h",
            < 2 => "Hier",
            < 7 => $"Il y a {(int)diff.TotalDays} jours",
            < 30 => $"Il y a {(int)(diff.TotalDays / 7)} semaines",
            _ => date.ToString("d MMMM yyyy", culture)
        };
    }
}

/// <summary>
/// Formate un TimeSpan en HH:mm.
/// Usage: {conv:TimeSpanFormat}
/// </summary>
public class TimeSpanFormat : ConverterMarkupExtension<TimeSpanFormat>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
            return ts.ToString(@"hh\:mm");
        return "--:--";
    }
    
    public override object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && TimeSpan.TryParse(s, out var ts))
            return ts;
        return TimeSpan.Zero;
    }
}

#endregion

#region Math Converters

/// <summary>
/// Multiplie une valeur par un facteur.
/// Usage: {conv:Multiply} avec ConverterParameter=2.5
/// </summary>
public class Multiply : ConverterMarkupExtension<Multiply>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var val = System.Convert.ToDouble(value, culture);
        var factor = 1.0;
        
        if (parameter is string param && double.TryParse(param, NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
            factor = f;
        
        return val * factor;
    }
}

/// <summary>
/// Ajoute une valeur à une autre.
/// Usage: {conv:Add} avec ConverterParameter=10
/// </summary>
public class Add : ConverterMarkupExtension<Add>
{
    public override object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var val = System.Convert.ToDouble(value, culture);
        var offset = 0.0;
        
        if (parameter is string param && double.TryParse(param, NumberStyles.Any, CultureInfo.InvariantCulture, out var o))
            offset = o;
        
        return val + offset;
    }
}

/// <summary>
/// Calcule un pourcentage.
/// Usage: {conv:Percentage}
/// </summary>
public class Percentage : MultiConverterMarkupExtension<Percentage>
{
    public override object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2)
        {
            var current = System.Convert.ToDouble(values[0], culture);
            var total = System.Convert.ToDouble(values[1], culture);
            
            if (total == 0) return "0%";
            
            return $"{current / total * 100:F0}%";
        }
        return "—";
    }
}

#endregion
