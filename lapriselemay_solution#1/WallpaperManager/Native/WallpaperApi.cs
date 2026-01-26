using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallpaperManager.Native;

public static partial class WallpaperApi
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;
    
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SystemParametersInfoW(int uAction, int uParam, string lpvParam, int fuWinIni);
    
    private static readonly object _lock = new();
    
    public static void SetWallpaper(string path, WallpaperStyle style = WallpaperStyle.Fill)
    {
        SetWallpaperInternal(path, style, broadcastChange: true);
    }
    
    /// <summary>
    /// Applique un fond d'écran sans envoyer de message de changement à Explorer.
    /// Utilisé pendant les transitions pour éviter le scintillement des icônes.
    /// </summary>
    public static void SetWallpaperSilent(string path, WallpaperStyle style = WallpaperStyle.Fill)
    {
        SetWallpaperInternal(path, style, broadcastChange: false);
    }
    
    private static void SetWallpaperInternal(string path, WallpaperStyle style, bool broadcastChange)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        
        lock (_lock)
        {
            // Définir le style dans le registre
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                if (key != null)
                {
                    var (wallpaperStyle, tileWallpaper) = GetStyleValues(style);
                    key.SetValue("WallpaperStyle", wallpaperStyle);
                    key.SetValue("TileWallpaper", tileWallpaper);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur registre: {ex.Message}");
            }
            
            // Appliquer le fond d'écran
            // SPIF_SENDCHANGE cause un rafraîchissement d'Explorer qui peut faire scintiller les icônes
            int flags = SPIF_UPDATEINIFILE;
            if (broadcastChange)
            {
                flags |= SPIF_SENDCHANGE;
            }
            SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, path, flags);
        }
    }
    
    public static string? GetCurrentWallpaper()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return key?.GetValue("Wallpaper") as string;
        }
        catch
        {
            return null;
        }
    }
    
    public static WallpaperStyle? GetCurrentStyle()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            if (key == null) return null;
            
            var styleValue = key.GetValue("WallpaperStyle") as string ?? "10";
            var tileValue = key.GetValue("TileWallpaper") as string ?? "0";
            
            return (styleValue, tileValue) switch
            {
                ("10", "0") => WallpaperStyle.Fill,
                ("6", "0") => WallpaperStyle.Fit,
                ("2", "0") => WallpaperStyle.Stretch,
                ("0", "1") => WallpaperStyle.Tile,
                ("0", "0") => WallpaperStyle.Center,
                ("22", "0") => WallpaperStyle.Span,
                _ => WallpaperStyle.Fill
            };
        }
        catch
        {
            return null;
        }
    }
    
    private static (string wallpaperStyle, string tileWallpaper) GetStyleValues(WallpaperStyle style) => style switch
    {
        WallpaperStyle.Fill => ("10", "0"),
        WallpaperStyle.Fit => ("6", "0"),
        WallpaperStyle.Stretch => ("2", "0"),
        WallpaperStyle.Tile => ("0", "1"),
        WallpaperStyle.Center => ("0", "0"),
        WallpaperStyle.Span => ("22", "0"),
        _ => ("10", "0")
    };
}

public enum WallpaperStyle
{
    Fill,
    Fit,
    Stretch,
    Tile,
    Center,
    Span
}
