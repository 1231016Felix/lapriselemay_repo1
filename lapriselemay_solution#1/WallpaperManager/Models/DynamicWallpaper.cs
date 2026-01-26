using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WallpaperManager.Models;

/// <summary>
/// Mode de fonctionnement du wallpaper dynamique
/// </summary>
public enum DynamicMode
{
    /// <summary>Heures fixes définies manuellement</summary>
    Manual,
    /// <summary>Basé sur le lever/coucher du soleil</summary>
    SunBased,
    /// <summary>Basé sur la météo actuelle</summary>
    WeatherBased
}

/// <summary>
/// Type de transition entre les variantes
/// </summary>
public enum DynamicTransitionType
{
    None,
    Fade,
    Slide
}

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
    
    private string? _description;
    public string? Description 
    { 
        get => _description; 
        set { _description = value; OnPropertyChanged(); } 
    }
    
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Mode de fonctionnement (Manuel, Soleil, Météo)
    /// </summary>
    private DynamicMode _mode = DynamicMode.Manual;
    public DynamicMode Mode 
    { 
        get => _mode; 
        set { _mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSunBased)); OnPropertyChanged(nameof(IsWeatherBased)); } 
    }
    
    /// <summary>
    /// Type de transition entre variantes
    /// </summary>
    private DynamicTransitionType _transitionType = DynamicTransitionType.Fade;
    public DynamicTransitionType TransitionType 
    { 
        get => _transitionType; 
        set { _transitionType = value; OnPropertyChanged(); } 
    }
    
    /// <summary>
    /// Durée de la transition en millisecondes
    /// </summary>
    private int _transitionDuration = 1000;
    public int TransitionDuration 
    { 
        get => _transitionDuration; 
        set { _transitionDuration = Math.Max(0, Math.Min(5000, value)); OnPropertyChanged(); } 
    }
    
    /// <summary>
    /// Latitude pour le calcul du lever/coucher du soleil
    /// </summary>
    private double _latitude = 45.5017; // Montréal par défaut
    public double Latitude 
    { 
        get => _latitude; 
        set { _latitude = value; OnPropertyChanged(); } 
    }
    
    /// <summary>
    /// Longitude pour le calcul du lever/coucher du soleil
    /// </summary>
    private double _longitude = -73.5673; // Montréal par défaut
    public double Longitude 
    { 
        get => _longitude; 
        set { _longitude = value; OnPropertyChanged(); } 
    }
    
    /// <summary>
    /// Liste des variantes horaires triées par heure de début
    /// </summary>
    public ObservableCollection<TimeVariant> Variants { get; set; } = [];
    
    [JsonIgnore]
    public string? PreviewPath => Variants.FirstOrDefault(v => v.Exists)?.FilePath ?? Variants.FirstOrDefault()?.FilePath;
    
    [JsonIgnore]
    public int VariantCount => Variants.Count;
    
    [JsonIgnore]
    public bool IsSunBased => Mode == DynamicMode.SunBased;
    
    [JsonIgnore]
    public bool IsWeatherBased => Mode == DynamicMode.WeatherBased;
    
    [JsonIgnore]
    public int ConfiguredVariantCount => Variants.Count(v => v.Exists);
    
    [JsonIgnore]
    public string ConfigurationStatus => $"{ConfiguredVariantCount}/{VariantCount} configurées";
    
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
    /// Obtient la variante pour une heure spécifique (prévisualisation)
    /// </summary>
    public TimeVariant? GetVariantForTime(TimeSpan time)
    {
        if (Variants.Count == 0) return null;
        if (Variants.Count == 1) return Variants[0];
        
        TimeVariant? current = null;
        foreach (var variant in Variants.OrderBy(v => v.StartTime))
        {
            if (variant.StartTime <= time)
            {
                current = variant;
            }
        }
        
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
    
    /// <summary>
    /// Ajoute une nouvelle variante
    /// </summary>
    public TimeVariant AddVariant(TimeSpan startTime, string? label = null)
    {
        var variant = new TimeVariant
        {
            StartTime = startTime,
            Label = label ?? GetDefaultLabel(startTime)
        };
        
        // Insérer à la bonne position (trié par heure)
        var index = 0;
        foreach (var v in Variants)
        {
            if (v.StartTime > startTime) break;
            index++;
        }
        
        Variants.Insert(index, variant);
        OnPropertyChanged(nameof(VariantCount));
        OnPropertyChanged(nameof(ConfiguredVariantCount));
        OnPropertyChanged(nameof(ConfigurationStatus));
        
        return variant;
    }
    
    /// <summary>
    /// Supprime une variante
    /// </summary>
    public bool RemoveVariant(TimeVariant variant)
    {
        var result = Variants.Remove(variant);
        if (result)
        {
            OnPropertyChanged(nameof(VariantCount));
            OnPropertyChanged(nameof(ConfiguredVariantCount));
            OnPropertyChanged(nameof(ConfigurationStatus));
        }
        return result;
    }
    
    /// <summary>
    /// Trie les variantes par heure
    /// </summary>
    public void SortVariants()
    {
        var sorted = Variants.OrderBy(v => v.StartTime).ToList();
        Variants.Clear();
        foreach (var v in sorted)
        {
            Variants.Add(v);
        }
    }
    
    /// <summary>
    /// Crée une copie profonde de ce wallpaper dynamique
    /// </summary>
    public DynamicWallpaper Clone()
    {
        var clone = new DynamicWallpaper
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{Name} (copie)",
            Description = Description,
            Mode = Mode,
            TransitionType = TransitionType,
            TransitionDuration = TransitionDuration,
            Latitude = Latitude,
            Longitude = Longitude,
            CreatedDate = DateTime.Now
        };
        
        foreach (var variant in Variants)
        {
            clone.Variants.Add(new TimeVariant
            {
                StartTime = variant.StartTime,
                Label = variant.Label,
                FilePath = variant.FilePath
            });
        }
        
        return clone;
    }
    
    private static string GetDefaultLabel(TimeSpan time)
    {
        return time.Hours switch
        {
            >= 5 and < 8 => "Aube",
            >= 8 and < 12 => "Matin",
            >= 12 and < 14 => "Midi",
            >= 14 and < 17 => "Après-midi",
            >= 17 and < 20 => "Crépuscule",
            _ => "Nuit" // 20-23 et 0-4
        };
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
    private TimeSpan _startTime;
    public TimeSpan StartTime 
    { 
        get => _startTime; 
        set 
        { 
            _startTime = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(StartTimeFormatted));
            OnPropertyChanged(nameof(StartHour));
            OnPropertyChanged(nameof(StartMinute));
            OnPropertyChanged(nameof(TimelinePosition));
        } 
    }
    
    /// <summary>
    /// Heure (0-23) pour le binding
    /// </summary>
    [JsonIgnore]
    public int StartHour
    {
        get => StartTime.Hours;
        set => StartTime = new TimeSpan(value, StartTime.Minutes, 0);
    }
    
    /// <summary>
    /// Minutes (0-59) pour le binding
    /// </summary>
    [JsonIgnore]
    public int StartMinute
    {
        get => StartTime.Minutes;
        set => StartTime = new TimeSpan(StartTime.Hours, value, 0);
    }
    
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
            OnPropertyChanged(nameof(FileName));
        } 
    }
    
    /// <summary>
    /// Nom descriptif optionnel (ex: "Aube", "Midi", "Crépuscule", "Nuit")
    /// </summary>
    private string? _label;
    public string? Label 
    { 
        get => _label; 
        set { _label = value; OnPropertyChanged(); } 
    }
    
    [JsonIgnore]
    public string StartTimeFormatted => $"{StartTime.Hours:D2}:{StartTime.Minutes:D2}";
    
    [JsonIgnore]
    public bool Exists => !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath);
    
    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(FilePath);
    
    [JsonIgnore]
    public string? FileName => HasImage ? System.IO.Path.GetFileName(FilePath) : null;
    
    /// <summary>
    /// Position sur la timeline (0-100%)
    /// </summary>
    [JsonIgnore]
    public double TimelinePosition => StartTime.TotalHours / 24.0 * 100.0;
    
    /// <summary>
    /// Heures disponibles pour le ComboBox
    /// </summary>
    [JsonIgnore]
    public int[] AvailableHours => Enumerable.Range(0, 24).ToArray();
    
    /// <summary>
    /// Minutes disponibles pour le ComboBox
    /// </summary>
    [JsonIgnore]
    public int[] AvailableMinutes => [0, 15, 30, 45];
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
    
    /// <summary>
    /// Préréglage basé sur le soleil (heures calculées dynamiquement)
    /// </summary>
    public static readonly (string Label, TimeSpan Time)[] SunBased =
    [
        ("Aube", TimeSpan.FromHours(6)),      // Sera recalculé
        ("Lever", TimeSpan.FromHours(7)),     // Sera recalculé
        ("Jour", TimeSpan.FromHours(12)),
        ("Coucher", TimeSpan.FromHours(19)),  // Sera recalculé
        ("Crépuscule", TimeSpan.FromHours(20)), // Sera recalculé
        ("Nuit", TimeSpan.FromHours(22))
    ];
}

/// <summary>
/// Données d'export/import pour les packs dynamiques
/// </summary>
public class DynamicWallpaperPack
{
    public string Version { get; set; } = "1.0";
    public DateTime ExportDate { get; set; } = DateTime.Now;
    public string? Author { get; set; }
    public List<DynamicWallpaperExport> Wallpapers { get; set; } = [];
}

public class DynamicWallpaperExport
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DynamicMode Mode { get; set; }
    public DynamicTransitionType TransitionType { get; set; }
    public int TransitionDuration { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public List<TimeVariantExport> Variants { get; set; } = [];
}

public class TimeVariantExport
{
    public TimeSpan StartTime { get; set; }
    public string? Label { get; set; }
    public string? FileName { get; set; }
    public byte[]? ImageData { get; set; } // Base64 encoded image for portable packs
}
