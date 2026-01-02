using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WallpaperManager.Services;

/// <summary>
/// Service de gestion des miniatures avec cache disque et mémoire.
/// Évite de charger les images en taille réelle dans la liste.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    private static readonly Lazy<ThumbnailService> _instance = new(() => new ThumbnailService());
    public static ThumbnailService Instance => _instance.Value;
    
    private readonly string _cacheFolder;
    private readonly ConcurrentDictionary<string, BitmapSource> _memoryCache = new();
    private readonly SemaphoreSlim _semaphore = new(4); // Max 4 thumbnails en parallèle
    private bool _disposed;
    
    public const int ThumbnailWidth = 280;
    public const int ThumbnailHeight = 180;
    
    private ThumbnailService()
    {
        _cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallpaperManager", "ThumbnailCache");
        
        EnsureCacheFolderExists();
    }
    
    private void EnsureCacheFolderExists()
    {
        if (!Directory.Exists(_cacheFolder))
            Directory.CreateDirectory(_cacheFolder);
    }
    
    /// <summary>
    /// Obtient une miniature pour le fichier spécifié.
    /// Utilise le cache mémoire, puis le cache disque, puis génère la miniature.
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;
        
        // 1. Vérifier le cache mémoire
        if (_memoryCache.TryGetValue(filePath, out var cached))
            return cached;
        
        // 2. Vérifier le cache disque
        var cacheKey = GetCacheKey(filePath);
        var cachePath = Path.Combine(_cacheFolder, $"{cacheKey}.jpg");
        
        if (File.Exists(cachePath))
        {
            var thumbnail = await LoadFromCacheAsync(cachePath);
            if (thumbnail != null)
            {
                _memoryCache.TryAdd(filePath, thumbnail);
                return thumbnail;
            }
        }
        
        // 3. Générer la miniature
        await _semaphore.WaitAsync();
        try
        {
            // Double-check après avoir obtenu le sémaphore
            if (_memoryCache.TryGetValue(filePath, out cached))
                return cached;
            
            var thumbnail = await GenerateThumbnailAsync(filePath);
            if (thumbnail != null)
            {
                _memoryCache.TryAdd(filePath, thumbnail);
                await SaveToCacheAsync(thumbnail, cachePath);
            }
            return thumbnail;
        }
        finally
        {
            _semaphore.Release();
        }
    }
    
    /// <summary>
    /// Version synchrone pour le converter XAML (avec cache uniquement).
    /// </summary>
    public BitmapSource? GetThumbnailSync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;
        
        // Cache mémoire seulement pour éviter le blocage UI
        if (_memoryCache.TryGetValue(filePath, out var cached))
            return cached;
        
        // Vérifier cache disque
        var cacheKey = GetCacheKey(filePath);
        var cachePath = Path.Combine(_cacheFolder, $"{cacheKey}.jpg");
        
        if (File.Exists(cachePath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(cachePath, UriKind.Absolute);
                bitmap.DecodePixelWidth = ThumbnailWidth;
                bitmap.EndInit();
                bitmap.Freeze();
                
                _memoryCache.TryAdd(filePath, bitmap);
                return bitmap;
            }
            catch
            {
                // Cache corrompu, supprimer
                try { File.Delete(cachePath); } catch { }
            }
        }
        
        // Déclencher la génération en arrière-plan
        _ = Task.Run(async () =>
        {
            var thumbnail = await GetThumbnailAsync(filePath);
            if (thumbnail != null)
            {
                // Notifier l'UI qu'une nouvelle miniature est disponible
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    ThumbnailGenerated?.Invoke(this, filePath);
                });
            }
        });
        
        // Retourner null pour l'instant, l'UI sera mise à jour via l'événement
        return null;
    }
    
    /// <summary>
    /// Événement déclenché quand une miniature est générée en arrière-plan.
    /// </summary>
    public event EventHandler<string>? ThumbnailGenerated;
    
    private async Task<BitmapSource?> GenerateThumbnailAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                
                // Pour les vidéos, utiliser une image placeholder ou extraire une frame
                if (extension is ".mp4" or ".webm" or ".avi" or ".mkv")
                {
                    return CreateVideoPlaceholder();
                }
                
                // Pour les images
                using var stream = File.OpenRead(filePath);
                
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.DecodePixelWidth = ThumbnailWidth;
                bitmap.EndInit();
                bitmap.Freeze();
                
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur génération thumbnail: {ex.Message}");
                return null;
            }
        });
    }
    
    private BitmapSource CreateVideoPlaceholder()
    {
        // Créer une image placeholder pour les vidéos
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Fond gris foncé
            context.DrawRectangle(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
                null,
                new Rect(0, 0, ThumbnailWidth, ThumbnailHeight));
            
            // Icône play
            var playIcon = new FormattedText(
                "▶",
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                48,
                System.Windows.Media.Brushes.White,
                96);
            
            context.DrawText(playIcon,
                new System.Windows.Point(
                    (ThumbnailWidth - playIcon.Width) / 2,
                    (ThumbnailHeight - playIcon.Height) / 2));
            
            // Label "VIDEO"
            var label = new FormattedText(
                "VIDEO",
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                System.Windows.Media.Brushes.Gray,
                96);
            
            context.DrawText(label,
                new System.Windows.Point(
                    (ThumbnailWidth - label.Width) / 2,
                    ThumbnailHeight - 30));
        }
        
        var renderBitmap = new RenderTargetBitmap(
            ThumbnailWidth, ThumbnailHeight, 96, 96, PixelFormats.Pbgra32);
        renderBitmap.Render(visual);
        renderBitmap.Freeze();
        
        return renderBitmap;
    }
    
    private async Task<BitmapSource?> LoadFromCacheAsync(string cachePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(cachePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return (BitmapSource)bitmap;
            }
            catch
            {
                // Cache corrompu
                try { File.Delete(cachePath); } catch { }
                return null;
            }
        });
    }
    
    private async Task SaveToCacheAsync(BitmapSource thumbnail, string cachePath)
    {
        await Task.Run(() =>
        {
            try
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                
                using var stream = File.Create(cachePath);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde cache: {ex.Message}");
            }
        });
    }
    
    private static string GetCacheKey(string filePath)
    {
        // Utiliser le chemin + date de modification pour la clé
        var fileInfo = new FileInfo(filePath);
        var input = $"{filePath}|{fileInfo.LastWriteTimeUtc.Ticks}";
        
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..32]; // 32 caractères
    }
    
    /// <summary>
    /// Invalide le cache pour un fichier spécifique.
    /// </summary>
    public void InvalidateCache(string filePath)
    {
        _memoryCache.TryRemove(filePath, out _);
        
        var cacheKey = GetCacheKey(filePath);
        var cachePath = Path.Combine(_cacheFolder, $"{cacheKey}.jpg");
        
        try
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
        }
        catch { }
    }
    
    /// <summary>
    /// Nettoie les anciennes entrées du cache (fichiers de plus de 30 jours).
    /// </summary>
    public void CleanupOldCache()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-30);
            var files = Directory.GetFiles(_cacheFolder, "*.jpg");
            
            foreach (var file in files)
            {
                if (File.GetLastAccessTime(file) < cutoff)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur nettoyage cache: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Vide complètement le cache.
    /// </summary>
    public void ClearAllCache()
    {
        _memoryCache.Clear();
        
        try
        {
            if (Directory.Exists(_cacheFolder))
            {
                Directory.Delete(_cacheFolder, true);
                Directory.CreateDirectory(_cacheFolder);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur vidage cache: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Précharge les miniatures pour une liste de fichiers.
    /// </summary>
    public async Task PreloadThumbnailsAsync(IEnumerable<string> filePaths)
    {
        var tasks = filePaths.Select(GetThumbnailAsync);
        await Task.WhenAll(tasks);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _memoryCache.Clear();
        _semaphore.Dispose();
    }
}
