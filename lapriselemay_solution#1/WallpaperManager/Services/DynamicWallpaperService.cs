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
    private readonly Lock _lock = new();
    private DynamicWallpaper? _activeDynamic;
    private string? _currentVariantId;
    private bool _disposed;
    
    public event EventHandler<TimeVariant>? VariantChanged;
    
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
            
            ApplyCurrentVariant();
            ScheduleNextChange();
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
            _activeDynamic = null;
            _currentVariantId = null;
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
            
            ApplyCurrentVariant();
            ScheduleNextChange();
        }
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
        
        _currentVariantId = variant.Id;
        
        try
        {
            WallpaperApi.SetWallpaper(variant.FilePath, WallpaperStyle.Fill);
            System.Diagnostics.Debug.WriteLine($"DynamicWallpaperService: Appliqué '{variant.Label}' ({variant.StartTimeFormatted})");
            
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
    
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
        }
        
        _timer.Elapsed -= OnTimerElapsed;
        _timer.Dispose();
    }
}
