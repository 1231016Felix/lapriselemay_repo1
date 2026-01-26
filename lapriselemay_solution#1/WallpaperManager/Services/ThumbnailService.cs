using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Threading.Channels;

namespace WallpaperManager.Services;

/// <summary>
/// Priorité de chargement des miniatures.
/// </summary>
public enum ThumbnailPriority
{
    /// <summary>Élément visible à l'écran - priorité maximale</summary>
    Visible = 0,
    /// <summary>Élément proche de la zone visible - préchargement</summary>
    NearVisible = 1,
    /// <summary>Préchargement en arrière-plan</summary>
    Background = 2
}

/// <summary>
/// Requête de chargement de miniature.
/// </summary>
internal record ThumbnailRequest(
    string FilePath, 
    ThumbnailPriority Priority, 
    TaskCompletionSource<BitmapSource?> Completion);

/// <summary>
/// Service de gestion des miniatures avec cache LRU et chargement par priorité.
/// Optimisé pour les grandes bibliothèques avec virtualisation UI.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    private static readonly Lazy<ThumbnailService> _instance = new(() => new ThumbnailService());
    public static ThumbnailService Instance => _instance.Value;
    
    private readonly string _cacheFolder;
    
    // Cache LRU avec limite de 100MB pour les miniatures en mémoire
    private readonly LruCache<string, BitmapSource> _memoryCache;
    
    // File de priorité pour les requêtes de miniatures
    private readonly Channel<ThumbnailRequest> _highPriorityChannel;
    private readonly Channel<ThumbnailRequest> _lowPriorityChannel;
    
    // Suivi des requêtes en cours pour éviter les doublons
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BitmapSource?>> _pendingRequests = new();
    
    // Sémaphores pour limiter la charge
    private readonly SemaphoreSlim _diskSemaphore = new(2);   // Max 2 lectures disque simultanées
    private readonly SemaphoreSlim _generateSemaphore = new(4); // Max 4 générations simultanées
    
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workerTasks;
    private volatile bool _disposed;
    
    // Statistiques de performance
    private long _cacheHits;
    private long _cacheMisses;
    private long _diskHits;
    
    public const int ThumbnailWidth = 280;
    public const int ThumbnailHeight = 180;
    
    // Limite de taille du cache mémoire (100 MB)
    private const long MaxCacheSizeBytes = 100 * 1024 * 1024;
    
    /// <summary>
    /// Taux de hit du cache mémoire (0-100%).
    /// </summary>
    public double CacheHitRate
    {
        get
        {
            var total = _cacheHits + _cacheMisses;
            return total > 0 ? (_cacheHits * 100.0) / total : 0;
        }
    }
    
    /// <summary>
    /// Nombre de miniatures en cache mémoire.
    /// </summary>
    public int CachedCount => _memoryCache.Count;
    
    /// <summary>
    /// Taille approximative du cache mémoire en bytes.
    /// </summary>
    public long CacheSizeBytes => _memoryCache.CurrentSizeBytes;
    
    private ThumbnailService()
    {
        _cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallpaperManager", "ThumbnailCache");
        
        EnsureCacheFolderExists();
        
        // Cache LRU avec limite de taille
        _memoryCache = new LruCache<string, BitmapSource>(
            MaxCacheSizeBytes, 
            EstimateBitmapSize);
        
        // Channels avec priorité
        _highPriorityChannel = Channel.CreateUnbounded<ThumbnailRequest>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        _lowPriorityChannel = Channel.CreateBounded<ThumbnailRequest>(
            new BoundedChannelOptions(500) 
            { 
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false, 
                SingleWriter = false 
            });
        
        // Démarrer les workers
        _workerTasks = new Task[Environment.ProcessorCount];
        for (int i = 0; i < _workerTasks.Length; i++)
        {
            _workerTasks[i] = Task.Run(() => ProcessRequestsAsync(_cts.Token));
        }
    }
    
    private void EnsureCacheFolderExists()
    {
        if (!Directory.Exists(_cacheFolder))
            Directory.CreateDirectory(_cacheFolder);
    }
    
    /// <summary>
    /// Estime la taille en mémoire d'un BitmapSource.
    /// </summary>
    private static long EstimateBitmapSize(BitmapSource bitmap)
    {
        // Estimation: largeur * hauteur * bytes par pixel + overhead
        var bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
        return (long)bitmap.PixelWidth * bitmap.PixelHeight * bytesPerPixel + 1024;
    }
    
    /// <summary>
    /// Obtient une miniature avec priorité spécifiée.
    /// </summary>
    public Task<BitmapSource?> GetThumbnailAsync(
        string filePath, 
        ThumbnailPriority priority = ThumbnailPriority.Visible,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            return Task.FromResult<BitmapSource?>(null);
        
        // 1. Vérifier le cache mémoire (instantané)
        if (_memoryCache.TryGet(filePath, out var cached))
        {
            Interlocked.Increment(ref _cacheHits);
            return Task.FromResult<BitmapSource?>(cached);
        }
        
        Interlocked.Increment(ref _cacheMisses);
        
        // 2. Vérifier si une requête est déjà en cours pour ce fichier
        var tcs = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        if (!_pendingRequests.TryAdd(filePath, tcs))
        {
            // Une requête est déjà en cours, attendre son résultat
            if (_pendingRequests.TryGetValue(filePath, out var existingTcs))
                return existingTcs.Task;
        }
        
        // 3. Ajouter à la file appropriée
        var request = new ThumbnailRequest(filePath, priority, tcs);
        var channel = priority == ThumbnailPriority.Background ? _lowPriorityChannel : _highPriorityChannel;
        
        // Essayer d'écrire dans le channel
        if (!channel.Writer.TryWrite(request))
        {
            // Channel plein (ne devrait pas arriver pour unbounded)
            _pendingRequests.TryRemove(filePath, out _);
            tcs.SetResult(null);
        }
        
        // Gérer l'annulation
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                if (_pendingRequests.TryRemove(filePath, out var pendingTcs))
                    pendingTcs.TrySetCanceled();
            });
        }
        
        return tcs.Task;
    }
    
    /// <summary>
    /// Version synchrone pour le converter XAML.
    /// Retourne le cache ou null, déclenche le chargement en arrière-plan.
    /// </summary>
    public BitmapSource? GetThumbnailSync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;
        
        // Cache mémoire seulement (instantané)
        if (_memoryCache.TryGet(filePath, out var cached))
        {
            Interlocked.Increment(ref _cacheHits);
            return cached;
        }
        
        // Vérifier le cache disque (synchrone mais rapide)
        var cacheKey = GetCacheKey(filePath);
        var cachePath = Path.Combine(_cacheFolder, $"{cacheKey}.jpg");
        
        if (File.Exists(cachePath))
        {
            try
            {
                var bitmap = LoadFromDiskSync(cachePath);
                if (bitmap != null)
                {
                    _memoryCache.Set(filePath, bitmap);
                    Interlocked.Increment(ref _diskHits);
                    return bitmap;
                }
            }
            catch
            {
                try { File.Delete(cachePath); } catch { }
            }
        }
        
        Interlocked.Increment(ref _cacheMisses);
        
        // Déclencher la génération en arrière-plan avec priorité visible
        _ = GetThumbnailAsync(filePath, ThumbnailPriority.Visible);
        
        return null;
    }
    
    /// <summary>
    /// Événement déclenché quand une miniature est générée.
    /// </summary>
    public event EventHandler<string>? ThumbnailGenerated;
    
    /// <summary>
    /// Précharge les miniatures pour les éléments visibles et proches.
    /// </summary>
    public void PreloadForVisibleRange(IEnumerable<string> visiblePaths, IEnumerable<string> nearbyPaths)
    {
        // Les éléments visibles avec priorité haute
        foreach (var path in visiblePaths)
        {
            if (!_memoryCache.TryGet(path, out _))
            {
                _ = GetThumbnailAsync(path, ThumbnailPriority.Visible);
            }
        }
        
        // Les éléments proches avec priorité moyenne
        foreach (var path in nearbyPaths)
        {
            if (!_memoryCache.TryGet(path, out _))
            {
                _ = GetThumbnailAsync(path, ThumbnailPriority.NearVisible);
            }
        }
    }
    
    /// <summary>
    /// Précharge en arrière-plan (basse priorité).
    /// </summary>
    public void PreloadBackground(IEnumerable<string> paths)
    {
        foreach (var path in paths.Take(100)) // Limiter pour éviter de surcharger
        {
            if (!_memoryCache.TryGet(path, out _))
            {
                _ = GetThumbnailAsync(path, ThumbnailPriority.Background);
            }
        }
    }
    
    /// <summary>
    /// Worker qui traite les requêtes de miniatures.
    /// </summary>
    private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ThumbnailRequest? request = null;
            
            try
            {
                // Priorité aux requêtes haute priorité
                if (_highPriorityChannel.Reader.TryRead(out request))
                {
                    // Traiter immédiatement
                }
                else if (_lowPriorityChannel.Reader.TryRead(out request))
                {
                    // Traiter la basse priorité
                }
                else
                {
                    // Attendre une nouvelle requête (haute priorité d'abord)
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    
                    var highTask = _highPriorityChannel.Reader.WaitToReadAsync(linkedCts.Token).AsTask();
                    var lowTask = _lowPriorityChannel.Reader.WaitToReadAsync(linkedCts.Token).AsTask();
                    
                    var completedTask = await Task.WhenAny(highTask, lowTask).ConfigureAwait(false);
                    
                    // Vérifier quel channel a des données
                    if (_highPriorityChannel.Reader.TryRead(out request))
                    {
                        // Haute priorité
                    }
                    else if (_lowPriorityChannel.Reader.TryRead(out request))
                    {
                        // Basse priorité
                    }
                    else
                    {
                        continue;
                    }
                }
                
                if (request != null)
                {
                    await ProcessRequestAsync(request).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur worker thumbnail: {ex.Message}");
                
                if (request != null)
                {
                    _pendingRequests.TryRemove(request.FilePath, out _);
                    request.Completion.TrySetResult(null);
                }
            }
        }
    }
    
    private async Task ProcessRequestAsync(ThumbnailRequest request)
    {
        try
        {
            // Vérifier si déjà en cache (peut avoir été chargé pendant l'attente)
            if (_memoryCache.TryGet(request.FilePath, out var cached))
            {
                request.Completion.TrySetResult(cached);
                _pendingRequests.TryRemove(request.FilePath, out _);
                return;
            }
            
            // Vérifier si le fichier existe
            if (!File.Exists(request.FilePath))
            {
                request.Completion.TrySetResult(null);
                _pendingRequests.TryRemove(request.FilePath, out _);
                return;
            }
            
            BitmapSource? thumbnail = null;
            
            // 1. Essayer le cache disque
            var cacheKey = GetCacheKey(request.FilePath);
            var cachePath = Path.Combine(_cacheFolder, $"{cacheKey}.jpg");
            
            if (File.Exists(cachePath))
            {
                await _diskSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    thumbnail = await LoadFromCacheAsync(cachePath).ConfigureAwait(false);
                    if (thumbnail != null)
                    {
                        Interlocked.Increment(ref _diskHits);
                    }
                }
                finally
                {
                    _diskSemaphore.Release();
                }
            }
            
            // 2. Générer si pas en cache disque
            if (thumbnail == null)
            {
                await _generateSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    thumbnail = await GenerateThumbnailAsync(request.FilePath).ConfigureAwait(false);
                    
                    if (thumbnail != null)
                    {
                        // Sauvegarder dans le cache disque
                        await SaveToCacheAsync(thumbnail, cachePath).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _generateSemaphore.Release();
                }
            }
            
            // 3. Mettre en cache mémoire
            if (thumbnail != null)
            {
                _memoryCache.Set(request.FilePath, thumbnail);
                
                // Notifier l'UI
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    ThumbnailGenerated?.Invoke(this, request.FilePath);
                });
            }
            
            request.Completion.TrySetResult(thumbnail);
        }
        finally
        {
            _pendingRequests.TryRemove(request.FilePath, out _);
        }
    }
    
    private static BitmapSource? LoadFromDiskSync(string cachePath)
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
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
    
    private async Task<BitmapSource?> GenerateThumbnailAsync(string filePath)
    {
        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            // Pour les vidéos, extraire via Windows Shell
            if (extension is ".mp4" or ".webm" or ".avi" or ".mkv" or ".mov" or ".wmv")
            {
                var videoThumb = await ExtractVideoThumbnailAsync(filePath).ConfigureAwait(false);
                if (videoThumb != null)
                    return videoThumb;
                
                return await Task.Run(CreateVideoPlaceholder).ConfigureAwait(false);
            }
            
            // Pour les images
            return await Task.Run(() =>
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.DecodePixelWidth = ThumbnailWidth;
                bitmap.EndInit();
                bitmap.Freeze();
                
                return (BitmapSource)bitmap;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur génération thumbnail: {ex.Message}");
            return null;
        }
    }
    
    private Task<BitmapSource?> ExtractVideoThumbnailAsync(string filePath)
    {
        var tcs = new TaskCompletionSource<BitmapSource?>();
        
        var thread = new Thread(() =>
        {
            try
            {
                var result = ExtractVideoThumbnailCore(filePath);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur thread STA: {ex.Message}");
                tcs.SetResult(null);
            }
        });
        
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        
        return tcs.Task;
    }
    
    private BitmapSource? ExtractVideoThumbnailCore(string filePath)
    {
        IntPtr hBitmap = IntPtr.Zero;
        IShellItem? shellItem = null;
        
        try
        {
            var guid = typeof(IShellItem).GUID;
            var hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref guid, out shellItem);
            if (hr != 0 || shellItem == null)
                return null;
            
            var factory = (IShellItemImageFactory)shellItem;
            var size = new SIZE { cx = ThumbnailWidth, cy = ThumbnailHeight };
            
            hr = factory.GetImage(size, SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, out hBitmap);
            
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                hr = factory.GetImage(size, SIIGBF.SIIGBF_BIGGERSIZEOK, out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                    return null;
            }
            
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            
            if (source.PixelWidth > ThumbnailWidth || source.PixelHeight > ThumbnailHeight)
            {
                var scale = Math.Min(
                    (double)ThumbnailWidth / source.PixelWidth,
                    (double)ThumbnailHeight / source.PixelHeight);
                
                var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                transformed.Freeze();
                return transformed;
            }
            
            source.Freeze();
            return source;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            
            if (shellItem != null)
                Marshal.ReleaseComObject(shellItem);
        }
    }
    
    #region Windows Shell COM Interop
    
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);
    
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }
    
    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10
    }
    
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
    
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }
    
    #endregion
    
    private BitmapSource CreateVideoPlaceholder()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)),
                null, new Rect(0, 0, ThumbnailWidth, ThumbnailHeight));
            
            var playIcon = new FormattedText("▶",
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 48, System.Windows.Media.Brushes.White, 96);
            
            context.DrawText(playIcon, new System.Windows.Point(
                (ThumbnailWidth - playIcon.Width) / 2,
                (ThumbnailHeight - playIcon.Height) / 2));
            
            var label = new FormattedText("VIDEO",
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, System.Windows.Media.Brushes.Gray, 96);
            
            context.DrawText(label, new System.Windows.Point(
                (ThumbnailWidth - label.Width) / 2, ThumbnailHeight - 30));
        }
        
        var renderBitmap = new RenderTargetBitmap(ThumbnailWidth, ThumbnailHeight, 96, 96, PixelFormats.Pbgra32);
        renderBitmap.Render(visual);
        renderBitmap.Freeze();
        return renderBitmap;
    }
    
    private static async Task<BitmapSource?> LoadFromCacheAsync(string cachePath)
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
                try { File.Delete(cachePath); } catch { }
                return null;
            }
        }).ConfigureAwait(false);
    }
    
    private static async Task SaveToCacheAsync(BitmapSource thumbnail, string cachePath)
    {
        await Task.Run(() =>
        {
            try
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                using var stream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde cache: {ex.Message}");
            }
        }).ConfigureAwait(false);
    }
    
    private static string GetCacheKey(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var input = $"{filePath}|{fileInfo.LastWriteTimeUtc.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..32];
    }
    
    public void InvalidateCache(string filePath)
    {
        _memoryCache.Remove(filePath);
        
        var cacheKey = GetCacheKey(filePath);
        var cachePath = Path.Combine(_cacheFolder, $"{cacheKey}.jpg");
        
        try { if (File.Exists(cachePath)) File.Delete(cachePath); }
        catch { }
    }
    
    /// <summary>
    /// Nettoie les entrées du cache mémoire non accédées depuis plus de 5 minutes.
    /// Appelé périodiquement pour libérer la mémoire.
    /// </summary>
    public int TrimMemoryCache()
    {
        return _memoryCache.EvictOlderThan(TimeSpan.FromMinutes(5));
    }
    
    public void CleanupOldCache()
    {
        try
        {
            if (!Directory.Exists(_cacheFolder)) return;
            
            var cutoff = DateTime.Now.AddDays(-30);
            var deletedCount = 0;
            
            foreach (var file in Directory.EnumerateFiles(_cacheFolder, "*.jpg"))
            {
                try
                {
                    if (File.GetLastAccessTime(file) < cutoff)
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                }
                catch { }
            }
            
            if (deletedCount > 0)
                System.Diagnostics.Debug.WriteLine($"Cache nettoyé: {deletedCount} fichier(s)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur nettoyage cache: {ex.Message}");
        }
    }
    
    public async Task ClearAllCacheAsync()
    {
        _memoryCache.Clear();
        
        await Task.Run(() =>
        {
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
        }).ConfigureAwait(false);
    }
    
    public async Task PreloadThumbnailsAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var tasks = filePaths.Select(path => GetThumbnailAsync(path, ThumbnailPriority.Background, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _cts.Cancel();
        
        try
        {
            Task.WaitAll(_workerTasks, TimeSpan.FromSeconds(2));
        }
        catch { }
        
        _cts.Dispose();
        _memoryCache.Clear();
        _diskSemaphore.Dispose();
        _generateSemaphore.Dispose();
    }
}
