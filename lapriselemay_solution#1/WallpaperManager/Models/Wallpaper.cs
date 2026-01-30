using System.IO;
using System.Text.Json.Serialization;

namespace WallpaperManager.Models;

public enum WallpaperType
{
    Static,      // Image statique (JPG, PNG, BMP)
    Animated,    // GIF animé
    Video        // Vidéo (MP4, WebM)
}

public enum WallpaperFit
{
    Fill,        // Remplir l'écran
    Fit,         // Adapter à l'écran
    Stretch,     // Étirer
    Tile,        // Mosaïque
    Center,      // Centrer
    Span         // Étendre sur plusieurs écrans
}

/// <summary>
/// Catégorie de luminosité d'un fond d'écran.
/// </summary>
public enum BrightnessCategory
{
    Dark,       // Sombre
    Neutral,    // Neutre
    Light       // Clair
}

public class Wallpaper
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ThumbnailPath { get; set; }
    public WallpaperType Type { get; set; } = WallpaperType.Static;
    public WallpaperFit Fit { get; set; } = WallpaperFit.Fill;
    public DateTime AddedDate { get; set; } = DateTime.Now;
    public bool IsFavorite { get; set; }
    public string? UnsplashId { get; set; }
    public string? SourceId { get; set; }  // ID unique de la source (pexels_123, pixabay_456, etc.)
    public string? Author { get; set; }
    public string? AuthorUrl { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public string[] Tags { get; set; } = [];
    public string? FileHash { get; set; }  // MD5 pour détection doublons
    
    // Analyse de luminosité (IA)
    public BrightnessCategory? BrightnessCategory { get; set; }
    public double? AverageBrightness { get; set; }
    
    // Propriétés calculées - ne pas sérialiser
    [JsonIgnore]
    public string DisplayName => !string.IsNullOrEmpty(Name) 
        ? Name 
        : Path.GetFileNameWithoutExtension(FilePath);
    
    [JsonIgnore]
    public string Resolution => Width > 0 && Height > 0 
        ? $"{Width} × {Height}" 
        : "Inconnu";
    
    [JsonIgnore]
    public string FileSizeFormatted => FormatFileSize(FileSize);
    
    [JsonIgnore]
    public bool Exists => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    
    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        
        ReadOnlySpan<string> sizes = ["B", "KB", "MB", "GB"];
        var order = 0;
        var size = (double)bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }
}
