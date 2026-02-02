using System.IO;
using System.Text.Json.Serialization;
using WallpaperManager.Services;

namespace WallpaperManager.Models;

public class AppSettings
{
    // Démarrage
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    
    // Rotation
    public bool RotationEnabled { get; set; }
    public int RotationIntervalMinutes { get; set; } = 30;
    public bool RandomOrder { get; set; } = true;
    
    // Unsplash
    public string? UnsplashApiKey { get; set; }
    public string UnsplashDefaultQuery { get; set; } = "nature landscape";
    public bool UnsplashAutoDownload { get; set; }
    public int UnsplashCacheCount { get; set; } = 20;
    
    // Pexels
    public string? PexelsApiKey { get; set; }
    
    // Pixabay
    public string? PixabayApiKey { get; set; }
    
    // Fonds animés
    public bool AnimatedWallpaperEnabled { get; set; }
    public int AnimatedVolume { get; set; }
    public bool PauseOnBattery { get; set; } = true;
    public bool PauseOnFullscreen { get; set; } = true;
    
    // Affichage
    public WallpaperFit DefaultFit { get; set; } = WallpaperFit.Fill;
    
    private string? _wallpaperFolder;
    public string WallpaperFolder
    {
        get => _wallpaperFolder ?? GetDefaultWallpaperFolder();
        set => _wallpaperFolder = value;
    }
    
    // Raccourcis clavier
    public bool HotkeysEnabled { get; set; } = true;
    public string HotkeyNextWallpaper { get; set; } = "Win+Alt+Right";
    public string HotkeyPreviousWallpaper { get; set; } = "Win+Alt+Left";
    public string HotkeyToggleFavorite { get; set; } = "Win+Alt+F";
    public string HotkeyPauseRotation { get; set; } = "Win+Alt+Space";
    
    // Transitions
    public bool TransitionEnabled { get; set; } = true;
    public TransitionEffect TransitionEffect { get; set; } = TransitionEffect.Fade;
    public int TransitionDurationMs { get; set; } = 500;
    
    // Rotation intelligente selon l'heure
    public bool SmartRotationEnabled { get; set; } = false;
    public TimeSpan SmartRotationDayStart { get; set; } = new TimeSpan(7, 0, 0);     // 07:00
    public TimeSpan SmartRotationNightStart { get; set; } = new TimeSpan(19, 0, 0);   // 19:00
    public bool SmartRotationChangeOnTransition { get; set; } = true;
    
    // État de la fenêtre (pour restaurer après redémarrage)
    public bool WasInTrayOnLastExit { get; set; }
    
    private static string GetDefaultWallpaperFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "WallpaperManager");
}
