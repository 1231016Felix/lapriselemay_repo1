using System.IO;
using System.Text.Json;
using System.Timers;
using WallpaperManager.Models;
using WallpaperManager.Native;

namespace WallpaperManager.Services;

/// <summary>
/// Service gérant les wallpapers dynamiques qui changent selon l'heure
/// </summary>
public sealed class DynamicWallpaperService : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly System.Timers.Timer _sunUpdateTimer;
    private readonly Lock _lock = new();
    private DynamicWallpaper? _activeDynamic;
    private string? _currentVariantId;
    private bool _disposed;
    private DateTime _lastSunCalculation = DateTime.MinValue;
    
    public event EventHandler<TimeVariant>? VariantChanged;
    public event EventHandler<DynamicWallpaper>? WallpaperActivated;
    public event EventHandler? WallpaperDeactivated;
    public event EventHandler<string>? TransitionStarted;
    public event EventHandler? TransitionCompleted;
    
    public bool IsActive => _activeDynamic != null;
    public DynamicWallpaper? ActiveWallpaper => _activeDynamic;
    public TimeVariant? CurrentVariant => _activeDynamic?.GetCurrentVariant();
    
    public DynamicWallpaperService()
    {
        _timer = new System.Timers.Timer
        {
            AutoReset = false
        };
        _timer.Elapsed += OnTimerElapsed;
        
        // Timer pour recalculer les heures solaires chaque jour
        _sunUpdateTimer = new System.Timers.Timer
        {
            Interval = TimeSpan.FromHours(6).TotalMilliseconds,
            AutoReset = true
        };
        _sunUpdateTimer.Elapsed += OnSunUpdateTimerElapsed;
    }
    
    /// <summary>
    /// Active un wallpaper dynamique
    /// </summary>
    public void Activate(DynamicWallpaper dynamic)
    {
        lock (_lock)
        {
            if (_disposed) return;
            
            _timer.Stop();
            _activeDynamic = dynamic;
            _currentVariantId = null;
            
            if (dynamic.Variants.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("DynamicWallpaperService: Aucune variante configurée");
                return;
            }
            
            // Si mode solaire, mettre à jour les heures
            if (dynamic.Mode == DynamicMode.SunBased)
            {
                UpdateSunBasedTimes(dynamic);
                _sunUpdateTimer.Start();
            }
            
            ApplyCurrentVariant();
            ScheduleNextChange();
            
            WallpaperActivated?.Invoke(this, dynamic);
        }
    }
    
    /// <summary>
    /// Désactive le wallpaper dynamique
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _timer.Stop();
            _sunUpdateTimer.Stop();
            _activeDynamic = null;
            _currentVariantId = null;
            
            WallpaperDeactivated?.Invoke(this, EventArgs.Empty);
        }
    }
    
    /// <summary>
    /// Rafraîchit l'affichage (après réveil système par exemple)
    /// </summary>
    public void Refresh()
    {
        lock (_lock)
        {
            if (_activeDynamic == null || _disposed) return;
            
            // Recalculer les heures solaires si nécessaire
            if (_activeDynamic.Mode == DynamicMode.SunBased && 
                DateTime.Now.Date != _lastSunCalculation.Date)
            {
                UpdateSunBasedTimes(_activeDynamic);
            }
            
            ApplyCurrentVariant();
            ScheduleNextChange();
        }
    }
    
    /// <summary>
    /// Prévisualise une variante spécifique sans l'activer
    /// </summary>
    public void PreviewVariant(TimeVariant variant)
    {
        if (!variant.Exists) return;
        
        try
        {
            WallpaperApi.SetWallpaper(variant.FilePath, WallpaperStyle.Fill);
            System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Prévisualisation '{variant.Label}' ({variant.StartTimeFormatted})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Erreur prévisualisation - {ex.Message}");
        }
    }
    
    /// <summary>
    /// Prévisualise le wallpaper pour une heure spécifique
    /// </summary>
    public void PreviewTime(TimeSpan time)
    {
        lock (_lock)
        {
            if (_activeDynamic == null) return;
            
            var variant = _activeDynamic.GetVariantForTime(time);
            if (variant != null)
            {
                PreviewVariant(variant);
            }
        }
    }
    
    /// <summary>
    /// Met à jour les heures basées sur le soleil
    /// </summary>
    public void UpdateSunBasedTimes(DynamicWallpaper dynamic)
    {
        if (dynamic.Mode != DynamicMode.SunBased) return;
        
        try
        {
            var sunTimes = SunCalculatorService.CalculateToday(dynamic.Latitude, dynamic.Longitude);
            _lastSunCalculation = DateTime.Now;
            
            // Mapper les variantes aux heures solaires
            foreach (var variant in dynamic.Variants)
            {
                variant.StartTime = variant.Label?.ToLowerInvariant() switch
                {
                    "aube" or "dawn" => sunTimes.Dawn,
                    "lever" or "lever du soleil" or "sunrise" => sunTimes.Sunrise,
                    "midi" or "noon" => sunTimes.SolarNoon,
                    "heure dorée" or "golden hour" => sunTimes.GoldenHourPM,
                    "coucher" or "coucher du soleil" or "sunset" => sunTimes.Sunset,
                    "crépuscule" or "dusk" => sunTimes.Dusk,
                    "nuit" or "night" => sunTimes.Dusk.Add(TimeSpan.FromHours(1)),
                    _ => variant.StartTime // Garder l'heure existante si pas de correspondance
                };
            }
            
            dynamic.SortVariants();
            
            System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Heures solaires mises à jour - Lever: {sunTimes.Sunrise:hh\\:mm}, Coucher: {sunTimes.Sunset:hh\\:mm}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Erreur calcul solaire - {ex.Message}");
        }
    }
    
    /// <summary>
    /// Exporte un pack de wallpapers dynamiques
    /// </summary>
    public static async Task<string> ExportPackAsync(IEnumerable<DynamicWallpaper> wallpapers, string filePath, bool includeImages = false)
    {
        var pack = new DynamicWallpaperPack
        {
            Author = Environment.UserName
        };
        
        foreach (var wp in wallpapers)
        {
            var export = new DynamicWallpaperExport
            {
                Name = wp.Name,
                Description = wp.Description,
                Mode = wp.Mode,
                TransitionType = wp.TransitionType,
                TransitionDuration = wp.TransitionDuration,
                Latitude = wp.Latitude,
                Longitude = wp.Longitude
            };
            
            foreach (var variant in wp.Variants)
            {
                var variantExport = new TimeVariantExport
                {
                    StartTime = variant.StartTime,
                    Label = variant.Label,
                    FileName = variant.FileName
                };
                
                // Optionnellement inclure les images en base64
                if (includeImages && variant.Exists)
                {
                    try
                    {
                        variantExport.ImageData = await File.ReadAllBytesAsync(variant.FilePath);
                    }
                    catch
                    {
                        // Ignorer les erreurs de lecture
                    }
                }
                
                export.Variants.Add(variantExport);
            }
            
            pack.Wallpapers.Add(export);
        }
        
        var json = JsonSerializer.Serialize(pack, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        await File.WriteAllTextAsync(filePath, json);
        return filePath;
    }
    
    /// <summary>
    /// Importe un pack de wallpapers dynamiques
    /// </summary>
    public static async Task<List<DynamicWallpaper>> ImportPackAsync(string filePath, string? imageOutputDirectory = null)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var pack = JsonSerializer.Deserialize<DynamicWallpaperPack>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        if (pack == null) return [];
        
        var result = new List<DynamicWallpaper>();
        var outputDir = imageOutputDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "WallpaperManager",
            "Imported"
        );
        
        Directory.CreateDirectory(outputDir);
        
        foreach (var wpExport in pack.Wallpapers)
        {
            var wp = new DynamicWallpaper
            {
                Name = wpExport.Name,
                Description = wpExport.Description,
                Mode = wpExport.Mode,
                TransitionType = wpExport.TransitionType,
                TransitionDuration = wpExport.TransitionDuration,
                Latitude = wpExport.Latitude,
                Longitude = wpExport.Longitude
            };
            
            foreach (var variantExport in wpExport.Variants)
            {
                var variant = new TimeVariant
                {
                    StartTime = variantExport.StartTime,
                    Label = variantExport.Label
                };
                
                // Extraire l'image si incluse
                if (variantExport.ImageData != null && variantExport.FileName != null)
                {
                    var imagePath = Path.Combine(outputDir, $"{wp.Id}_{variantExport.FileName}");
                    await File.WriteAllBytesAsync(imagePath, variantExport.ImageData);
                    variant.FilePath = imagePath;
                }
                
                wp.Variants.Add(variant);
            }
            
            result.Add(wp);
        }
        
        return result;
    }
    
    private void ApplyCurrentVariant()
    {
        if (_activeDynamic == null) return;
        
        var variant = _activeDynamic.GetCurrentVariant();
        if (variant == null) return;
        
        // Éviter de réappliquer la même variante
        if (variant.Id == _currentVariantId) return;
        
        if (!variant.Exists)
        {
            System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Fichier manquant pour {variant.Label}");
            return;
        }
        
        var previousVariantId = _currentVariantId;
        _currentVariantId = variant.Id;
        
        try
        {
            // Appliquer avec ou sans transition
            if (_activeDynamic.TransitionType != DynamicTransitionType.None && previousVariantId != null)
            {
                TransitionStarted?.Invoke(this, variant.FilePath);
                // Note: La transition visuelle serait gérée par le TransitionService si disponible
            }
            
            WallpaperApi.SetWallpaper(variant.FilePath, WallpaperStyle.Fill);
            System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Appliqué '{variant.Label}' ({variant.StartTimeFormatted})");
            
            TransitionCompleted?.Invoke(this, EventArgs.Empty);
            VariantChanged?.Invoke(this, variant);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Erreur - {ex.Message}");
        }
    }
    
    private void ScheduleNextChange()
    {
        if (_activeDynamic == null) return;
        
        var (nextVariant, timeUntil) = _activeDynamic.GetNextVariant();
        
        if (nextVariant == null || timeUntil.TotalMilliseconds <= 0)
        {
            // Vérifier toutes les heures par sécurité
            _timer.Interval = TimeSpan.FromHours(1).TotalMilliseconds;
        }
        else
        {
            // Ajouter 1 seconde pour être sûr d'être après l'heure
            _timer.Interval = Math.Max(1000, timeUntil.TotalMilliseconds + 1000);
        }
        
        _timer.Start();
        System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Prochain changement dans {timeUntil:hh\\:mm\\:ss}");
    }
    
    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _activeDynamic == null) return;
            
            ApplyCurrentVariant();
            ScheduleNextChange();
        }
    }
    
    private void OnSunUpdateTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _activeDynamic == null) return;
            
            // Recalculer au changement de jour
            if (_activeDynamic.Mode == DynamicMode.SunBased && 
                DateTime.Now.Date != _lastSunCalculation.Date)
            {
                UpdateSunBasedTimes(_activeDynamic);
                ScheduleNextChange();
            }
        }
    }
    
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _sunUpdateTimer.Stop();
        }
        
        _timer.Elapsed -= OnTimerElapsed;
        _sunUpdateTimer.Elapsed -= OnSunUpdateTimerElapsed;
        _timer.Dispose();
        _sunUpdateTimer.Dispose();
    }
}
