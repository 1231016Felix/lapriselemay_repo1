using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickLauncher.Services;

/// <summary>
/// Service de cache d'icônes persistant sur disque.
/// Améliore significativement le temps de démarrage en évitant de réextraire les icônes.
/// </summary>
public static class IconCacheService
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppName,
        "IconCache");
    
    private static readonly ConcurrentDictionary<string, ImageSource?> _memoryCache = new();
    private static readonly ConcurrentDictionary<string, DateTime> _cacheTimestamps = new();
    
    private const int MaxMemoryCacheSize = 500;
    private const int CacheExpirationDays = 30;
    private const string CacheFileExtension = ".png";
    
    /// <summary>
    /// Initialise le service de cache.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            CleanExpiredCache();
            Debug.WriteLine($"[IconCache] Initialisé dans: {CacheDirectory}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconCache] Erreur d'initialisation: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Génère une clé de cache unique pour un chemin donné.
    /// </summary>
    private static string GetCacheKey(string path, bool largeIcon)
    {
        var input = $"{path}_{(largeIcon ? "L" : "S")}";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..32]; // 32 caractères
    }
    
    /// <summary>
    /// Obtient le chemin du fichier cache pour une clé donnée.
    /// </summary>
    private static string GetCacheFilePath(string cacheKey)
    {
        return Path.Combine(CacheDirectory, cacheKey + CacheFileExtension);
    }
    
    /// <summary>
    /// Tente de charger une icône depuis le cache.
    /// Retourne null si l'icône n'est pas en cache ou si le cache est expiré.
    /// </summary>
    public static ImageSource? TryGetFromCache(string path, bool largeIcon = true)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        
        var cacheKey = GetCacheKey(path, largeIcon);
        
        // 1. Vérifier le cache mémoire
        if (_memoryCache.TryGetValue(cacheKey, out var memoryIcon))
        {
            Debug.WriteLine($"[IconCache] Memory hit: {Path.GetFileName(path)}");
            return memoryIcon;
        }
        
        // 2. Vérifier le cache disque
        var cacheFilePath = GetCacheFilePath(cacheKey);
        if (!File.Exists(cacheFilePath))
            return null;
        
        try
        {
            var fileInfo = new FileInfo(cacheFilePath);
            
            // Vérifier l'expiration
            if ((DateTime.Now - fileInfo.LastWriteTime).TotalDays > CacheExpirationDays)
            {
                try { File.Delete(cacheFilePath); } catch { }
                return null;
            }
            
            // Charger depuis le disque
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(cacheFilePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            
            // Stocker en mémoire
            AddToMemoryCache(cacheKey, bitmap);
            
            Debug.WriteLine($"[IconCache] Disk hit: {Path.GetFileName(path)}");
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconCache] Erreur lecture cache: {ex.Message}");
            try { File.Delete(cacheFilePath); } catch { }
            return null;
        }
    }
    
    /// <summary>
    /// Sauvegarde une icône dans le cache (mémoire et disque).
    /// </summary>
    public static void SaveToCache(string path, ImageSource? icon, bool largeIcon = true)
    {
        if (string.IsNullOrEmpty(path) || icon == null)
            return;
        
        var cacheKey = GetCacheKey(path, largeIcon);
        
        // Sauvegarder en mémoire
        AddToMemoryCache(cacheKey, icon);
        
        // Sauvegarder sur disque en arrière-plan
        Task.Run(() => SaveToDiskAsync(cacheKey, icon));
    }
    
    private static async Task SaveToDiskAsync(string cacheKey, ImageSource icon)
    {
        try
        {
            var cacheFilePath = GetCacheFilePath(cacheKey);
            
            // S'assurer que le répertoire existe
            Directory.CreateDirectory(CacheDirectory);
            
            // Convertir en BitmapSource si nécessaire
            if (icon is not BitmapSource bitmapSource)
                return;
            
            // Encoder en PNG
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            
            // Écrire sur disque
            using var stream = new FileStream(cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
            
            Debug.WriteLine($"[IconCache] Saved to disk: {cacheKey}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconCache] Erreur sauvegarde: {ex.Message}");
        }
    }
    
    private static void AddToMemoryCache(string cacheKey, ImageSource? icon)
    {
        // Nettoyer si le cache est trop grand
        if (_memoryCache.Count >= MaxMemoryCacheSize)
        {
            // Supprimer les entrées les plus anciennes
            var toRemove = _cacheTimestamps
                .OrderBy(x => x.Value)
                .Take(MaxMemoryCacheSize / 4)
                .Select(x => x.Key)
                .ToList();
            
            foreach (var key in toRemove)
            {
                _memoryCache.TryRemove(key, out _);
                _cacheTimestamps.TryRemove(key, out _);
            }
        }
        
        _memoryCache[cacheKey] = icon;
        _cacheTimestamps[cacheKey] = DateTime.Now;
    }
    
    /// <summary>
    /// Supprime les fichiers de cache expirés.
    /// </summary>
    private static void CleanExpiredCache()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory))
                return;
            
            var expiredFiles = Directory.GetFiles(CacheDirectory, $"*{CacheFileExtension}")
                .Select(f => new FileInfo(f))
                .Where(f => (DateTime.Now - f.LastWriteTime).TotalDays > CacheExpirationDays)
                .ToList();
            
            foreach (var file in expiredFiles)
            {
                try { file.Delete(); } catch { }
            }
            
            if (expiredFiles.Count > 0)
                Debug.WriteLine($"[IconCache] Nettoyé {expiredFiles.Count} fichiers expirés");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconCache] Erreur nettoyage: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Vide complètement le cache (mémoire et disque).
    /// </summary>
    public static void ClearAll()
    {
        _memoryCache.Clear();
        _cacheTimestamps.Clear();
        
        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                foreach (var file in Directory.GetFiles(CacheDirectory))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            Debug.WriteLine("[IconCache] Cache vidé");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconCache] Erreur vidage: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Invalide une entrée spécifique du cache.
    /// </summary>
    public static void Invalidate(string path, bool largeIcon = true)
    {
        var cacheKey = GetCacheKey(path, largeIcon);
        
        _memoryCache.TryRemove(cacheKey, out _);
        _cacheTimestamps.TryRemove(cacheKey, out _);
        
        var cacheFilePath = GetCacheFilePath(cacheKey);
        try { if (File.Exists(cacheFilePath)) File.Delete(cacheFilePath); } catch { }
    }
    
    /// <summary>
    /// Obtient des statistiques sur le cache.
    /// </summary>
    public static (int MemoryCount, int DiskCount, long DiskSizeBytes) GetStats()
    {
        var memoryCount = _memoryCache.Count;
        var diskCount = 0;
        long diskSize = 0;
        
        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                var files = Directory.GetFiles(CacheDirectory, $"*{CacheFileExtension}");
                diskCount = files.Length;
                diskSize = files.Sum(f => new FileInfo(f).Length);
            }
        }
        catch { }
        
        return (memoryCount, diskCount, diskSize);
    }
}
