using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperManager.Services;

namespace WallpaperManager.Converters;

/// <summary>
/// Converter optimisé pour le lazy loading des miniatures.
/// Retourne immédiatement le cache ou un placeholder, puis notifie l'UI quand la miniature est prête.
/// </summary>
public class LazyThumbnailConverter : IValueConverter, IMultiValueConverter
{
    private static readonly BitmapImage _placeholder;
    private static readonly BitmapImage _loadingPlaceholder;
    
    static LazyThumbnailConverter()
    {
        // Créer un placeholder statique (gris foncé)
        _placeholder = CreatePlaceholder(System.Windows.Media.Color.FromRgb(60, 60, 65), "📷");
        _loadingPlaceholder = CreatePlaceholder(System.Windows.Media.Color.FromRgb(45, 45, 48), "⏳");
    }
    
    private static BitmapImage CreatePlaceholder(System.Windows.Media.Color bgColor, string icon)
    {
        // Créer un placeholder simple en mémoire
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(
                new SolidColorBrush(bgColor),
                null,
                new Rect(0, 0, ThumbnailService.ThumbnailWidth, ThumbnailService.ThumbnailHeight));
            
            var text = new FormattedText(
                icon,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                32,
                System.Windows.Media.Brushes.DarkGray,
                96);
            
            context.DrawText(text, new System.Windows.Point(
                (ThumbnailService.ThumbnailWidth - text.Width) / 2,
                (ThumbnailService.ThumbnailHeight - text.Height) / 2));
        }
        
        var renderBitmap = new RenderTargetBitmap(
            ThumbnailService.ThumbnailWidth,
            ThumbnailService.ThumbnailHeight,
            96, 96,
            PixelFormats.Pbgra32);
        renderBitmap.Render(visual);
        
        // Convertir en BitmapImage frozen
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
        
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        
        return bitmap;
    }
    
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return _placeholder;
        
        if (!File.Exists(path))
            return _placeholder;
        
        // 1. Essayer le cache mémoire (instantané)
        var cached = ThumbnailService.Instance.GetThumbnailSync(path);
        if (cached != null)
            return cached;
        
        // 2. Déclencher le chargement en arrière-plan
        _ = LoadThumbnailAsync(path);
        
        // 3. Retourner le placeholder de chargement
        return _loadingPlaceholder;
    }
    
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Pour MultiBinding avec le FilePath
        if (values.Length > 0 && values[0] is string path)
        {
            return Convert(path, targetType, parameter, culture)!;
        }
        return _placeholder;
    }
    
    private static async Task LoadThumbnailAsync(string path)
    {
        try
        {
            // Charger avec priorité visible
            await ThumbnailService.Instance.GetThumbnailAsync(path, ThumbnailPriority.Visible);
            // L'UI sera notifiée via ThumbnailService.ThumbnailGenerated
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur chargement thumbnail: {ex.Message}");
        }
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
    
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Source d'image observable qui se met à jour automatiquement quand la miniature est prête.
/// À utiliser avec les bindings pour un rafraîchissement automatique de l'UI.
/// </summary>
public class ThumbnailImageSource : INotifyPropertyChanged
{
    private static readonly Lazy<ThumbnailImageSource> _instance = new(() => new ThumbnailImageSource());
    public static ThumbnailImageSource Instance => _instance.Value;
    
    private readonly ConditionalWeakTable<string, WeakReference<object?>> _pathToBindingTarget = new();
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private ThumbnailImageSource()
    {
        // S'abonner aux notifications du ThumbnailService
        ThumbnailService.Instance.ThumbnailGenerated += OnThumbnailGenerated;
    }
    
    private void OnThumbnailGenerated(object? sender, string filePath)
    {
        // Notifier que cette propriété a changé (pour le binding)
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Thumbnail[{filePath}]"));
    }
    
    /// <summary>
    /// Indexeur pour obtenir une miniature par chemin.
    /// </summary>
    public ImageSource? this[string path]
    {
        get
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            
            return ThumbnailService.Instance.GetThumbnailSync(path);
        }
    }
}

/// <summary>
/// Extension de binding pour les miniatures avec rafraîchissement automatique.
/// Usage: Source="{Binding FilePath, Converter={StaticResource AsyncThumbnailConverter}}"
/// </summary>
public class AsyncThumbnailConverter : IValueConverter
{
    private static readonly ImageSource _placeholder = CreateGrayPlaceholder();
    private static readonly ImageSource _loadingPlaceholder = CreateLoadingPlaceholder();
    
    private static ImageSource CreateGrayPlaceholder()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 35)),
                null,
                new Rect(0, 0, 180, 120));
        }
        
        var bitmap = new RenderTargetBitmap(180, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
    
    private static ImageSource CreateLoadingPlaceholder()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 45)),
                null,
                new Rect(0, 0, 180, 120));
            
            var text = new FormattedText(
                "⏳",
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                24,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 110)),
                96);
            
            context.DrawText(text, new System.Windows.Point(
                (180 - text.Width) / 2,
                (120 - text.Height) / 2));
        }
        
        var bitmap = new RenderTargetBitmap(180, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
    
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return _placeholder;
        
        if (!File.Exists(path))
            return _placeholder;
        
        // Essayer le cache mémoire d'abord (instantané)
        var cached = ThumbnailService.Instance.GetThumbnailSync(path);
        if (cached != null)
            return cached;
        
        // Déclencher le chargement et retourner le placeholder
        _ = ThumbnailService.Instance.GetThumbnailAsync(path, ThumbnailPriority.Visible);
        return _loadingPlaceholder;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
