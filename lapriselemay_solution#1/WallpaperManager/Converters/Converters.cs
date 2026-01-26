using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.IO;
using WallpaperManager.Services;

namespace WallpaperManager.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool bValue = value is bool b && b;
        bool invert = parameter?.ToString() == "Invert";
        
        if (invert) bValue = !bValue;
        
        return bValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

public class PathToThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;
        
        // Vérifier que le fichier existe avant de générer le thumbnail
        if (!File.Exists(path))
            return null;
        
        // Essayer le cache mémoire d'abord
        var cached = ThumbnailService.Instance.GetThumbnailSync(path);
        if (cached != null)
            return cached;
        
        // Si pas en cache, charger directement l'image (pour les nouveaux fichiers)
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.DecodePixelWidth = 280;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class UnsplashUrlToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrEmpty(url))
            return null;
        
        // Vérifier que l'URL est valide
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = uri;
            bitmap.DecodePixelWidth = 250; // Limite la taille pour économiser la RAM
            bitmap.EndInit();
            // Pas de Freeze() ici car le chargement HTTP est asynchrone
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class WallpaperTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            Models.WallpaperType.Static => "🖼️",
            Models.WallpaperType.Animated => "🎞️",
            Models.WallpaperType.Video => "🎬",
            _ => "📄"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null or string { Length: 0 };
        var invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) isNull = !isNull;
        
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            System.Collections.ICollection c => c.Count,
            _ => 0
        };
        
        var hasItems = count > 0;
        var invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);
        
        if (invert) hasItems = !hasItems;
        
        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
            return ts.ToString(@"hh\:mm");
        return "--:--";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && TimeSpan.TryParse(s, out var ts))
            return ts;
        return TimeSpan.Zero;
    }
}

public class MultiSelectToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            System.Collections.IList list => list.Count,
            _ => 0
        };
        
        return count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class WallpaperTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.WallpaperType type)
        {
            var targetType2 = parameter?.ToString()?.ToLowerInvariant() ?? "video";
            
            return targetType2 switch
            {
                "video" => type == Models.WallpaperType.Video ? Visibility.Visible : Visibility.Collapsed,
                "animated" => type == Models.WallpaperType.Animated ? Visibility.Visible : Visibility.Collapsed,
                "static" => type == Models.WallpaperType.Static ? Visibility.Visible : Visibility.Collapsed,
                _ => Visibility.Collapsed
            };
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// === CONVERTERS POUR WALLPAPERS DYNAMIQUES ===

/// <summary>
/// Convertit un TimeSpan en position sur une timeline (pourcentage)
/// </summary>
public class TimeSpanToPositionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
        {
            return ts.TotalHours / 24.0 * 100.0;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double position)
        {
            var hours = position / 100.0 * 24.0;
            return TimeSpan.FromHours(hours);
        }
        return TimeSpan.Zero;
    }
}

/// <summary>
/// Convertit un DynamicMode en texte lisible
/// </summary>
public class DynamicModeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            Models.DynamicMode.Manual => "⏰ Manuel",
            Models.DynamicMode.SunBased => "☀️ Basé sur le soleil",
            Models.DynamicMode.WeatherBased => "🌤️ Basé sur la météo",
            _ => "Manuel"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convertit un DynamicTransitionType en texte lisible
/// </summary>
public class DynamicTransitionToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            Models.DynamicTransitionType.None => "Aucune",
            Models.DynamicTransitionType.Fade => "Fondu",
            Models.DynamicTransitionType.Slide => "Glissement",
            _ => "Aucune"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Compare deux valeurs pour l'égalité (pour les indicateurs actifs)
/// </summary>
public class EqualityToBoolConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2)
        {
            return Equals(values[0], values[1]);
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convertit un booléen HasImage en couleur de bordure
/// </summary>
public class HasImageToBorderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool hasImage && hasImage)
        {
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Vert
        }
        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)); // Gris
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convertit un mode DynamicMode en visibilité pour les options solaires
/// </summary>
public class SunModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.DynamicMode mode)
        {
            return mode == Models.DynamicMode.SunBased ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convertit la latitude/longitude en nom de ville approximatif
/// </summary>
public class CoordinatesToCityConverter : IMultiValueConverter
{
    private static readonly Dictionary<(double lat, double lon), string> KnownCities = new()
    {
        { (45.5, -73.6), "Montréal" },
        { (48.9, 2.3), "Paris" },
        { (51.5, -0.1), "Londres" },
        { (40.7, -74.0), "New York" },
        { (35.7, 139.7), "Tokyo" },
        { (34.1, -118.2), "Los Angeles" },
        { (41.9, 12.5), "Rome" },
        { (52.5, 13.4), "Berlin" },
        { (55.8, 37.6), "Moscou" },
        { (39.9, 116.4), "Pékin" }
    };

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double lat && values[1] is double lon)
        {
            // Chercher la ville la plus proche
            var closest = KnownCities
                .OrderBy(kv => Math.Abs(kv.Key.lat - lat) + Math.Abs(kv.Key.lon - lon))
                .FirstOrDefault();
            
            var distance = Math.Abs(closest.Key.lat - lat) + Math.Abs(closest.Key.lon - lon);
            
            if (distance < 2)
                return $"📍 {closest.Value}";
            
            return $"📍 {lat:F2}°, {lon:F2}°";
        }
        return "📍 Position inconnue";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convertit le nombre de variantes configurées en texte de progression
/// </summary>
public class VariantProgressConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is int configured && values[1] is int total)
        {
            if (configured == total)
                return $"✅ {configured}/{total} configurées";
            if (configured == 0)
                return $"⚠️ 0/{total} configurées";
            return $"🔶 {configured}/{total} configurées";
        }
        return "Configuration en cours...";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// Vérifie si un wallpaper dynamique est actuellement actif
/// </summary>
public class IsActiveDynamicConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string id) return false;
        
        try
        {
            return App.IsInitialized && App.DynamicService.ActiveWallpaper?.Id == id;
        }
        catch
        {
            return false;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convertit une position de timeline (0-100%) en position canvas
/// </summary>
public class TimelinePositionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && 
            values[0] is double position && 
            values[1] is double containerWidth)
        {
            // Position est en pourcentage (0-100), on la convertit en pixels
            // On soustrait la moitié de la largeur de l'élément (8px pour un cercle de 16px)
            return (position / 100.0 * containerWidth) - 8;
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Arrondit un nombre vers le bas (floor)
/// </summary>
public class FloorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return (int)Math.Floor(d);
        if (value is float f)
            return (int)Math.Floor(f);
        if (value is decimal m)
            return (int)Math.Floor(m);
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convertit la fraction d'une heure en minutes (ex: 0.5 -> 30)
/// </summary>
public class FractionToMinutesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            var fraction = d - Math.Floor(d);
            return (int)(fraction * 60);
        }
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convertit un TimeSpan en heures décimales pour le slider
/// </summary>
public class TimeSpanToDecimalHoursConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
            return ts.TotalHours;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double hours)
            return TimeSpan.FromHours(hours);
        return TimeSpan.Zero;
    }
}
