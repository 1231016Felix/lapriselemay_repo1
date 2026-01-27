using WallpaperManager.Models;

namespace WallpaperManager.Services.Abstractions;

/// <summary>
/// Interface pour les services d'API d'images en ligne.
/// </summary>
public interface IImageApiService : IDisposable
{
    /// <summary>
    /// Nom du service (ex: "Unsplash", "Pexels").
    /// </summary>
    string ServiceName { get; }
    
    /// <summary>
    /// Indique si le service est configuré (clé API présente).
    /// </summary>
    bool IsConfigured { get; }
}

/// <summary>
/// Interface pour le service de rotation des wallpapers.
/// </summary>
public interface IWallpaperRotationService : IDisposable
{
    bool IsRunning { get; }
    Wallpaper? CurrentWallpaper { get; }
    
    event EventHandler<Wallpaper>? WallpaperChanged;
    event EventHandler<bool>? RotationStateChanged;
    event EventHandler<Wallpaper>? AnimatedWallpaperRequested;
    
    void Start();
    void Stop();
    void Pause();
    void Resume();
    void Next();
    void Previous();
    void SetInterval(int minutes);
    void RefreshPlaylist();
    void SetPlaylist(IEnumerable<Wallpaper> wallpapers);
    void ApplyWallpaper(Wallpaper wallpaper);
    void SetTransitionService(TransitionService? service);
}

/// <summary>
/// Interface pour le service de wallpapers animés.
/// </summary>
public interface IAnimatedWallpaperService : IDisposable
{
    bool IsPlaying { get; }
    
    event EventHandler<bool>? PlaybackStateChanged;
    
    void Play(Wallpaper wallpaper);
    void Stop();
    void Pause();
    void Resume();
    void SetVolume(int volume);
    void ReleaseLibVLC();
}

/// <summary>
/// Interface pour le service de wallpapers dynamiques.
/// </summary>
public interface IDynamicWallpaperService : IDisposable
{
    DynamicWallpaper? ActiveWallpaper { get; }
    bool IsActive { get; }
    
    event EventHandler<DynamicWallpaper?>? ActiveWallpaperChanged;
    event EventHandler<TimeVariant?>? VariantChanged;
    
    void Activate(DynamicWallpaper wallpaper);
    void Stop();
    void Refresh();
}

/// <summary>
/// Interface pour le service de transitions.
/// </summary>
public interface ITransitionService : IDisposable
{
    TimeSpan TransitionDuration { get; set; }
    TransitionEffect CurrentEffect { get; set; }
    bool IsEnabled { get; }
    
    Task TransitionToAsync(string newWallpaperPath, Action onComplete);
}

/// <summary>
/// Interface pour le service de raccourcis clavier.
/// </summary>
public interface IHotkeyService : IDisposable
{
    event EventHandler? NextWallpaperRequested;
    event EventHandler? PreviousWallpaperRequested;
    event EventHandler? ToggleFavoriteRequested;
    event EventHandler? TogglePauseRequested;
    
    void Initialize(System.Windows.Window window);
    void RegisterHotkeys();
    void UnregisterHotkeys();
    void ReloadHotkeys();
}

/// <summary>
/// Interface pour le service de miniatures.
/// </summary>
public interface IThumbnailService : IDisposable
{
    double CacheHitRate { get; }
    int CachedCount { get; }
    long CacheSizeBytes { get; }
    
    event EventHandler<string>? ThumbnailGenerated;
    
    Task<System.Windows.Media.Imaging.BitmapSource?> GetThumbnailAsync(
        string filePath,
        ThumbnailPriority priority = ThumbnailPriority.Visible,
        CancellationToken cancellationToken = default);
    
    System.Windows.Media.Imaging.BitmapSource? GetThumbnailSync(string filePath);
    
    void PreloadForVisibleRange(IEnumerable<string> visiblePaths, IEnumerable<string> nearbyPaths);
    void PreloadBackground(IEnumerable<string> paths);
    void InvalidateCache(string filePath);
    int TrimMemoryCache();
    void CleanupOldCache();
    Task ClearAllCacheAsync();
    Task PreloadThumbnailsAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface pour le service de paramètres.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }
    IReadOnlyList<Wallpaper> Wallpapers { get; }
    IReadOnlyList<DynamicWallpaper> DynamicWallpapers { get; }
    IReadOnlyList<Collection> Collections { get; }
    int WallpaperCount { get; }
    
    void AddWallpaper(Wallpaper wallpaper);
    void AddWallpapers(IEnumerable<Wallpaper> wallpapers);
    bool RemoveWallpaper(string id);
    Wallpaper? GetWallpaperByPath(string filePath);
    
    void AddDynamicWallpaper(DynamicWallpaper wallpaper);
    bool RemoveDynamicWallpaper(string id);
    
    void AddCollection(Collection collection);
    bool RemoveCollection(string id);
    void AddWallpaperToCollection(string collectionId, string wallpaperId);
    void RemoveWallpaperFromCollection(string collectionId, string wallpaperId);
    List<Wallpaper> GetWallpapersInCollection(string collectionId);
    
    void MarkDirty();
    void Save();
    void Shutdown();
}
