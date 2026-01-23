using System.IO;
using System.Text.Json.Serialization;

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
    public string HotkeyNextWallpaper { get; set; } = "Ctrl+Alt+Right";
    public string HotkeyPreviousWallpaper { get; set; } = "Ctrl+Alt+Left";
    public string HotkeyPauseRotation { get; set; } = "Ctrl+Alt+Space";
    
    // État de la fenêtre (pour restaurer après redémarrage)
    public bool WasInTrayOnLastExit { get; set; }
    
    private static string GetDefaultWallpaperFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "WallpaperManager");
}
