using System.Windows.Threading;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Configuration de la rotation intelligente selon l'heure.
/// </summary>
public class SmartRotationSettings
{
    /// <summary>
    /// Activer la rotation automatique selon l'heure.
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Heure de début de la période "jour" (collection claire).
    /// </summary>
    public TimeSpan DayStartTime { get; set; } = new TimeSpan(7, 0, 0); // 07:00
    
    /// <summary>
    /// Heure de début de la période "nuit" (collection sombre).
    /// </summary>
    public TimeSpan NightStartTime { get; set; } = new TimeSpan(19, 0, 0); // 19:00
    
    /// <summary>
    /// Changer le fond d'écran à chaque changement de période.
    /// </summary>
    public bool ChangeOnPeriodTransition { get; set; } = true;
    
    /// <summary>
    /// Continuer la rotation normale au sein de chaque période.
    /// </summary>
    public bool RotateWithinPeriod { get; set; } = true;
}

/// <summary>
/// Période de la journée pour la rotation intelligente.
/// </summary>
public enum DayPeriod
{
    Night,      // Nuit → Collection Sombre
    Day         // Jour → Collection Claire
}

/// <summary>
/// Service de rotation intelligente des fonds d'écran selon l'heure.
/// Utilise les collections de luminosité (Sombre, Claire).
/// </summary>
public sealed class SmartRotationService : IDisposable
{
    private readonly DispatcherTimer _periodCheckTimer;
    private readonly Func<BrightnessCategory, List<Wallpaper>> _getWallpapersByCategory;
    private readonly Action<Wallpaper> _applyWallpaper;
    
    private DayPeriod _currentPeriod;
    private bool _disposed;
    
    public SmartRotationSettings Settings { get; set; } = new();
    
    public event EventHandler<DayPeriod>? PeriodChanged;
    
    public DayPeriod CurrentPeriod => _currentPeriod;
    
    /// <summary>
    /// Crée une nouvelle instance du service.
    /// </summary>
    /// <param name="getWallpapersByCategory">Fonction pour obtenir les wallpapers d'une catégorie</param>
    /// <param name="applyWallpaper">Action pour appliquer un wallpaper</param>
    public SmartRotationService(
        Func<BrightnessCategory, List<Wallpaper>> getWallpapersByCategory,
        Action<Wallpaper> applyWallpaper)
    {
        _getWallpapersByCategory = getWallpapersByCategory;
        _applyWallpaper = applyWallpaper;
        
        _periodCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _periodCheckTimer.Tick += OnPeriodCheckTick;
        
        _currentPeriod = GetCurrentPeriod();
    }
    
    /// <summary>
    /// Démarre la surveillance des périodes et applique un fond d'écran.
    /// </summary>
    public void Start()
    {
        if (!Settings.Enabled) return;
        
        _currentPeriod = GetCurrentPeriod();
        _periodCheckTimer.Start();
        
        // Appliquer immédiatement un fond de la période actuelle
        if (Settings.ChangeOnPeriodTransition)
        {
            ApplyRandomFromCurrentPeriod();
        }
        
        System.Diagnostics.Debug.WriteLine($"SmartRotation démarré. Période actuelle: {_currentPeriod}");
    }
    
    /// <summary>
    /// Démarre la surveillance des périodes sans appliquer de fond d'écran.
    /// Utilisé lors de l'initialisation pour éviter un changement inutile.
    /// </summary>
    public void StartWithoutApply()
    {
        if (!Settings.Enabled) return;
        
        _currentPeriod = GetCurrentPeriod();
        _periodCheckTimer.Start();
        
        System.Diagnostics.Debug.WriteLine($"SmartRotation démarré (sans application). Période actuelle: {_currentPeriod}");
    }
    
    /// <summary>
    /// Arrête la surveillance.
    /// </summary>
    public void Stop()
    {
        _periodCheckTimer.Stop();
    }
    
    /// <summary>
    /// Force une vérification immédiate de la période.
    /// Utile après un réveil du système ou un changement d'heure.
    /// </summary>
    public void ForceCheckPeriod()
    {
        if (!Settings.Enabled) return;
        
        System.Diagnostics.Debug.WriteLine("SmartRotation: Vérification forcée de la période");
        
        var newPeriod = GetCurrentPeriod();
        
        if (newPeriod != _currentPeriod)
        {
            var oldPeriod = _currentPeriod;
            _currentPeriod = newPeriod;
            
            System.Diagnostics.Debug.WriteLine($"SmartRotation: Période changée pendant veille/absence {oldPeriod} → {newPeriod}");
            
            PeriodChanged?.Invoke(this, newPeriod);
            
            if (Settings.ChangeOnPeriodTransition)
            {
                ApplyRandomFromCurrentPeriod();
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"SmartRotation: Période inchangée ({_currentPeriod})");
        }
    }
    
    /// <summary>
    /// Vérifie si la période a changé et applique un nouveau fond si nécessaire.
    /// </summary>
    private void OnPeriodCheckTick(object? sender, EventArgs e)
    {
        var newPeriod = GetCurrentPeriod();
        
        if (newPeriod != _currentPeriod)
        {
            var oldPeriod = _currentPeriod;
            _currentPeriod = newPeriod;
            
            System.Diagnostics.Debug.WriteLine($"SmartRotation: Changement de période {oldPeriod} → {newPeriod}");
            
            PeriodChanged?.Invoke(this, newPeriod);
            
            if (Settings.ChangeOnPeriodTransition)
            {
                ApplyRandomFromCurrentPeriod();
            }
        }
    }
    
    /// <summary>
    /// Détermine la période actuelle selon l'heure.
    /// </summary>
    public DayPeriod GetCurrentPeriod()
    {
        var now = DateTime.Now.TimeOfDay;
        
        // Jour: de DayStartTime à NightStartTime
        // Nuit: de NightStartTime à DayStartTime
        
        if (now >= Settings.DayStartTime && now < Settings.NightStartTime)
            return DayPeriod.Day;
        
        return DayPeriod.Night;
    }
    
    /// <summary>
    /// Obtient la catégorie de luminosité correspondant à une période.
    /// </summary>
    public static BrightnessCategory GetCategoryForPeriod(DayPeriod period) => period switch
    {
        DayPeriod.Night => BrightnessCategory.Dark,
        DayPeriod.Day => BrightnessCategory.Light,
        _ => BrightnessCategory.Light
    };
    
    /// <summary>
    /// Obtient la période correspondant à une catégorie de luminosité.
    /// </summary>
    public static DayPeriod GetPeriodForCategory(BrightnessCategory category) => category switch
    {
        BrightnessCategory.Dark => DayPeriod.Night,
        BrightnessCategory.Light => DayPeriod.Day,
        _ => DayPeriod.Day
    };
    
    /// <summary>
    /// Applique un fond d'écran aléatoire de la période actuelle.
    /// </summary>
    public void ApplyRandomFromCurrentPeriod()
    {
        var category = GetCategoryForPeriod(_currentPeriod);
        var wallpapers = _getWallpapersByCategory(category);
        
        if (wallpapers.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"SmartRotation: Aucun fond d'écran dans la catégorie {category}");
            return;
        }
        
        var random = new Random();
        var wallpaper = wallpapers[random.Next(wallpapers.Count)];
        
        _applyWallpaper(wallpaper);
        
        System.Diagnostics.Debug.WriteLine($"SmartRotation: Appliqué '{wallpaper.DisplayName}' ({category})");
    }
    
    /// <summary>
    /// Applique le prochain fond d'écran de la période actuelle.
    /// </summary>
    public void NextInCurrentPeriod()
    {
        ApplyRandomFromCurrentPeriod();
    }
    
    /// <summary>
    /// Obtient les wallpapers de la période actuelle.
    /// </summary>
    public List<Wallpaper> GetCurrentPeriodWallpapers()
    {
        var category = GetCategoryForPeriod(_currentPeriod);
        return _getWallpapersByCategory(category);
    }
    
    /// <summary>
    /// Obtient le nom de la période en français.
    /// </summary>
    public static string GetPeriodName(DayPeriod period) => period switch
    {
        DayPeriod.Night => "Nuit",
        DayPeriod.Day => "Jour",
        _ => "Inconnu"
    };
    
    /// <summary>
    /// Obtient l'icône de la période.
    /// </summary>
    public static string GetPeriodIcon(DayPeriod period) => period switch
    {
        DayPeriod.Night => "🌙",
        DayPeriod.Day => "☀️",
        _ => "❓"
    };
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _periodCheckTimer.Stop();
    }
}
