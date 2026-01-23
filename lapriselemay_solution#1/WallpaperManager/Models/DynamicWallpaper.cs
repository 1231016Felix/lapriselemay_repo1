using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WallpaperManager.Models;

/// <summary>
/// Représente un pack de wallpapers dynamiques qui changent selon l'heure
/// </summary>
public class DynamicWallpaper : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    private string _name = string.Empty;
    public string Name 
    { 
        get => _name; 
        set { _name = value; OnPropertyChanged(); } 
    }
    
    public string? Description { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Liste des variantes horaires triées par heure de début
    /// </summary>
    public ObservableCollection<TimeVariant> Variants { get; set; } = [];
    
    [JsonIgnore]
    public string? PreviewPath => Variants.FirstOrDefault()?.FilePath;
    
    [JsonIgnore]
    public int VariantCount => Variants.Count;
    
    /// <summary>
    /// Obtient la variante appropriée pour l'heure actuelle
    /// </summary>
    public TimeVariant? GetCurrentVariant()
    {
        if (Variants.Count == 0) return null;
        if (Variants.Count == 1) return Variants[0];
        
        var now = DateTime.Now.TimeOfDay;
        
        // Trouver la variante dont l'heure de début est la plus proche (avant l'heure actuelle)
        TimeVariant? current = null;
        foreach (var variant in Variants.OrderBy(v => v.StartTime))
        {
            if (variant.StartTime <= now)
            {
                current = variant;
            }
        }
        
        // Si aucune trouvée (on est avant la première), prendre la dernière (de la veille)
        return current ?? Variants.OrderBy(v => v.StartTime).Last();
    }
    
    /// <summary>
    /// Obtient la prochaine variante et l'heure à laquelle elle s'applique
    /// </summary>
    public (TimeVariant? variant, TimeSpan timeUntil) GetNextVariant()
    {
        if (Variants.Count <= 1) return (null, TimeSpan.MaxValue);
        
        var now = DateTime.Now.TimeOfDay;
        var sortedVariants = Variants.OrderBy(v => v.StartTime).ToList();
        
        foreach (var variant in sortedVariants)
        {
            if (variant.StartTime > now)
            {
                return (variant, variant.StartTime - now);
            }
        }
        
        // La prochaine est demain matin
        var first = sortedVariants.First();
        var timeUntil = TimeSpan.FromHours(24) - now + first.StartTime;
        return (first, timeUntil);
    }
}

/// <summary>
/// Une variante de wallpaper pour une période de la journée
/// </summary>
public class TimeVariant : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Heure de début de cette variante (ex: 06:00 pour le matin)
    /// </summary>
    public TimeSpan StartTime { get; set; }
    
    private string _filePath = string.Empty;
    /// <summary>
    /// Chemin vers le fichier image
    /// </summary>
    public string FilePath 
    { 
        get => _filePath; 
        set 
        { 
            _filePath = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(Exists));
            OnPropertyChanged(nameof(HasImage));
        } 
    }
    
    /// <summary>
    /// Nom descriptif optionnel (ex: "Aube", "Midi", "Crépuscule", "Nuit")
    /// </summary>
    public string? Label { get; set; }
    
    [JsonIgnore]
    public string StartTimeFormatted => $"{StartTime.Hours:D2}:{StartTime.Minutes:D2}";
    
    [JsonIgnore]
    public bool Exists => !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath);
    
    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(FilePath);
}

/// <summary>
/// Préréglages de périodes pour faciliter la création
/// </summary>
public static class TimePresets
{
    public static readonly (string Label, TimeSpan Time)[] FourPeriods =
    [
        ("Aube", TimeSpan.FromHours(6)),
        ("Jour", TimeSpan.FromHours(12)),
        ("Crépuscule", TimeSpan.FromHours(18)),
        ("Nuit", TimeSpan.FromHours(22))
    ];
    
    public static readonly (string Label, TimeSpan Time)[] SixPeriods =
    [
        ("Aube", TimeSpan.FromHours(5)),
        ("Matin", TimeSpan.FromHours(8)),
        ("Midi", TimeSpan.FromHours(12)),
        ("Après-midi", TimeSpan.FromHours(15)),
        ("Crépuscule", TimeSpan.FromHours(19)),
        ("Nuit", TimeSpan.FromHours(22))
    ];
    
    public static readonly (string Label, TimeSpan Time)[] TwoPeriods =
    [
        ("Jour", TimeSpan.FromHours(7)),
        ("Nuit", TimeSpan.FromHours(19))
    ];
}
