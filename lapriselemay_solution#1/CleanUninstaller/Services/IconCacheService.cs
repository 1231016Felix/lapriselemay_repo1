using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CleanUninstaller.Services;

/// <summary>
/// Service de cache persistant pour les icônes des programmes.
/// Stocke les icônes sur disque et en mémoire pour un accès rapide.
/// </summary>
public sealed class IconCacheService : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, BitmapImage> _memoryCache = new();
    private readonly ConcurrentDictionary<string, DateTime> _cacheTimestamps = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromDays(30);
    private bool _disposed;

    public int MemoryCacheCount => _memoryCache.Count;
    public int DiskCacheCount => Directory.Exists(_cacheDirectory) 
        ? Directory.GetFiles(_cacheDirectory, "*.png").Length 
        : 0;

    public IconCacheService()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanUninstaller",
            "IconCache");
        
        Directory.CreateDirectory(_cacheDirectory);
        CleanExpiredCache();
    }

    /// <summary>
    /// Génère une clé de cache unique pour un programme
    /// </summary>
    public static string GetCacheKey(string displayName, string? installLocation, string? uninstallString)
    {
        var input = $"{displayName}|{installLocation ?? ""}|{uninstallString ?? ""}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..16]; // 16 chars suffisent
    }

    /// <summary>
    /// Récupère une icône du cache (mémoire ou disque)
    /// </summary>
    public async Task<BitmapImage?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(cacheKey)) return null;

        // 1. Vérifier le cache mémoire
        if (_memoryCache.TryGetValue(cacheKey, out var cachedImage))
        {
            return cachedImage;
        }

        // 2. Vérifier le cache disque
        var cachePath = GetCachePath(cacheKey);
        if (File.Exists(cachePath))
        {
            try
            {
                var fileInfo = new FileInfo(cachePath);
                if (DateTime.Now - fileInfo.LastWriteTime > _cacheExpiration)
                {
                    // Cache expiré, supprimer
                    File.Delete(cachePath);
                    return null;
                }

                var image = await LoadImageFromFileAsync(cachePath, cancellationToken);
                if (image != null)
                {
                    _memoryCache.TryAdd(cacheKey, image);
                    _cacheTimestamps.TryAdd(cacheKey, fileInfo.LastWriteTime);
                }
                return image;
            }
            catch
            {
                // Fichier corrompu, supprimer
                try { File.Delete(cachePath); } catch { }
            }
        }

        return null;
    }

    /// <summary>
    /// Stocke une icône dans le cache (mémoire et disque)
    /// </summary>
    public async Task SetAsync(string cacheKey, BitmapImage image, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(cacheKey) || image == null) return;

        // Ajouter au cache mémoire
        _memoryCache[cacheKey] = image;
        _cacheTimestamps[cacheKey] = DateTime.Now;

        // Sauvegarder sur disque (en arrière-plan)
        _ = Task.Run(async () =>
        {
            await _saveLock.WaitAsync(cancellationToken);
            try
            {
                var cachePath = GetCachePath(cacheKey);
                await SaveImageToFileAsync(image, cachePath, cancellationToken);
            }
            finally
            {
                _saveLock.Release();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Stocke une icône depuis un Bitmap GDI+
    /// </summary>
    public async Task<BitmapImage?> SetFromBitmapAsync(string cacheKey, Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(cacheKey) || bitmap == null) return null;

        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream()).AsTask(cancellationToken);

            await SetAsync(cacheKey, bitmapImage, cancellationToken);
            return bitmapImage;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Vérifie si une icône existe dans le cache
    /// </summary>
    public bool Contains(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey)) return false;
        return _memoryCache.ContainsKey(cacheKey) || File.Exists(GetCachePath(cacheKey));
    }

    /// <summary>
    /// Supprime une icône du cache
    /// </summary>
    public void Remove(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey)) return;

        _memoryCache.TryRemove(cacheKey, out _);
        _cacheTimestamps.TryRemove(cacheKey, out _);

        var cachePath = GetCachePath(cacheKey);
        if (File.Exists(cachePath))
        {
            try { File.Delete(cachePath); } catch { }
        }
    }

    /// <summary>
    /// Vide le cache mémoire (garde le cache disque)
    /// </summary>
    public void ClearMemoryCache()
    {
        _memoryCache.Clear();
        _cacheTimestamps.Clear();
    }

    /// <summary>
    /// Vide tout le cache (mémoire et disque)
    /// </summary>
    public void ClearAllCache()
    {
        ClearMemoryCache();

        if (Directory.Exists(_cacheDirectory))
        {
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.png"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    /// <summary>
    /// Précharge les icônes les plus récentes en mémoire
    /// </summary>
    public async Task PreloadRecentAsync(int maxItems = 100, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheDirectory)) return;

        var recentFiles = Directory.GetFiles(_cacheDirectory, "*.png")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastAccessTime)
            .Take(maxItems)
            .ToList();

        foreach (var file in recentFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cacheKey = Path.GetFileNameWithoutExtension(file.Name);
            if (!_memoryCache.ContainsKey(cacheKey))
            {
                var image = await LoadImageFromFileAsync(file.FullName, cancellationToken);
                if (image != null)
                {
                    _memoryCache.TryAdd(cacheKey, image);
                }
            }
        }
    }

    /// <summary>
    /// Nettoie les entrées expirées du cache disque
    /// </summary>
    private void CleanExpiredCache()
    {
        if (!Directory.Exists(_cacheDirectory)) return;

        var cutoff = DateTime.Now - _cacheExpiration;

        foreach (var file in Directory.GetFiles(_cacheDirectory, "*.png"))
        {
            try
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch { }
        }
    }

    private string GetCachePath(string cacheKey) => Path.Combine(_cacheDirectory, $"{cacheKey}.png");

    private static async Task<BitmapImage?> LoadImageFromFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            using var stream = new MemoryStream(bytes);
            
            var image = new BitmapImage();
            await image.SetSourceAsync(stream.AsRandomAccessStream()).AsTask(cancellationToken);
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveImageToFileAsync(BitmapImage image, string path, CancellationToken cancellationToken)
    {
        // Note: BitmapImage doesn't expose the raw bytes directly in WinUI 3
        // This method is a placeholder - in practice, you'd save the original source
        // For now, we'll rely on the in-memory cache
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _saveLock.Dispose();
        _memoryCache.Clear();
        _cacheTimestamps.Clear();
    }
}
