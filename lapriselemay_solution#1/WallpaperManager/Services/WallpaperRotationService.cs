using System.Timers;
using WallpaperManager.Models;
using WallpaperManager.Native;

namespace WallpaperManager.Services;

public sealed class WallpaperRotationService : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly Random _random = new();
    private readonly object _lock = new();
    
    private List<Wallpaper> _playlist = [];
    private int _currentIndex = -1;
    private volatile bool _isPaused;
    private bool _disposed;
    
    // Pour préserver le temps restant lors de pause/resume
    private DateTime _lastTickTime;
    private double _remainingTimeMs;
    
    public event EventHandler<Wallpaper>? WallpaperChanged;
    public event EventHandler<bool>? RotationStateChanged;
    
    public bool IsRunning => _timer.Enabled && !_isPaused;
    
    public Wallpaper? CurrentWallpaper
    {
        get
        {
            lock (_lock)
            {
                return _currentIndex >= 0 && _currentIndex < _playlist.Count 
                    ? _playlist[_currentIndex] 
                    : null;
            }
        }
    }
    
    public WallpaperRotationService()
    {
        _timer = new System.Timers.Timer
        {
            AutoReset = true
        };
        _timer.Elapsed += OnTimerElapsed;
    }
    
    public void Start()
    {
        if (_disposed) return;
        
        LoadPlaylist();
        
        lock (_lock)
        {
            if (_playlist.Count == 0)
                return;
        }
        
        var intervalMs = Math.Max(SettingsService.Current.RotationIntervalMinutes, 1) * 60 * 1000;
        _timer.Interval = intervalMs;
        _remainingTimeMs = 0; // Réinitialiser le temps restant
        _lastTickTime = DateTime.UtcNow;
        _timer.Start();
        _isPaused = false;
        
        // Ne pas appliquer immédiatement - attendre le premier intervalle
        
        RotationStateChanged?.Invoke(this, true);
    }
    
    public void Stop()
    {
        _timer.Stop();
        _isPaused = false;
        RotationStateChanged?.Invoke(this, false);
    }
    
    public void Pause()
    {
        if (_isPaused || !_timer.Enabled) return;
        
        // Calculer le temps restant avant le prochain changement
        var elapsed = (DateTime.UtcNow - _lastTickTime).TotalMilliseconds;
        _remainingTimeMs = Math.Max(_timer.Interval - elapsed, 1000); // Minimum 1 seconde
        
        _timer.Stop();
        _isPaused = true;
        
        System.Diagnostics.Debug.WriteLine($"Rotation en pause - Temps restant: {_remainingTimeMs / 1000:F0}s");
        RotationStateChanged?.Invoke(this, false);
    }
    
    public void Resume()
    {
        if (!_isPaused) return;
        
        lock (_lock)
        {
            if (_playlist.Count == 0) return;
        }
        
        _isPaused = false;
        
        // Reprendre avec le temps restant s'il y en avait
        if (_remainingTimeMs > 0)
        {
            System.Diagnostics.Debug.WriteLine($"Rotation reprise - Temps restant: {_remainingTimeMs / 1000:F0}s");
            _timer.Interval = _remainingTimeMs;
            _remainingTimeMs = 0;
        }
        
        _lastTickTime = DateTime.UtcNow;
        _timer.Start();
        RotationStateChanged?.Invoke(this, true);
    }
    
    public void Next()
    {
        lock (_lock)
        {
            if (_playlist.Count == 0)
                return;
            
            if (SettingsService.Current.RandomOrder)
            {
                var newIndex = _random.Next(_playlist.Count);
                // Éviter de répéter le même si possible
                if (_playlist.Count > 1 && newIndex == _currentIndex)
                    newIndex = (newIndex + 1) % _playlist.Count;
                _currentIndex = newIndex;
            }
            else
            {
                _currentIndex = (_currentIndex + 1) % _playlist.Count;
            }
        }
        
        ApplyCurrentWallpaper();
    }
    
    public void Previous()
    {
        lock (_lock)
        {
            if (_playlist.Count == 0)
                return;
            
            _currentIndex = _currentIndex <= 0 ? _playlist.Count - 1 : _currentIndex - 1;
        }
        
        ApplyCurrentWallpaper();
    }
    
    public void ApplyWallpaper(Wallpaper wallpaper)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        
        if (wallpaper.Type != WallpaperType.Static)
            return;
        
        try
        {
            var style = ConvertFitToStyle(wallpaper.Fit);
            WallpaperApi.SetWallpaper(wallpaper.FilePath, style);
            WallpaperChanged?.Invoke(this, wallpaper);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur application wallpaper: {ex.Message}");
        }
    }
    
    public void SetInterval(int minutes)
    {
        var validMinutes = Math.Max(minutes, 1);
        SettingsService.Current.RotationIntervalMinutes = validMinutes;
        _timer.Interval = validMinutes * 60 * 1000;
    }
    
    public void RefreshPlaylist()
    {
        LoadPlaylist();
    }
    
    private void LoadPlaylist()
    {
        lock (_lock)
        {
            var collectionId = SettingsService.Current.ActiveCollectionId;
            var wallpapers = SettingsService.Wallpapers;
            
            if (!string.IsNullOrEmpty(collectionId))
            {
                var collection = SettingsService.GetCollection(collectionId);
                if (collection != null)
                {
                    var wallpaperIds = new HashSet<string>(collection.WallpaperIds);
                    _playlist = wallpapers
                        .Where(w => wallpaperIds.Contains(w.Id) && w.Type == WallpaperType.Static)
                        .ToList();
                }
                else
                {
                    _playlist = [];
                }
            }
            else
            {
                _playlist = wallpapers
                    .Where(w => w.Type == WallpaperType.Static)
                    .ToList();
            }
            
            // Reset index si playlist change
            if (_currentIndex >= _playlist.Count)
                _currentIndex = -1;
        }
    }
    
    private void ApplyCurrentWallpaper()
    {
        var wallpaper = CurrentWallpaper;
        if (wallpaper != null)
        {
            ApplyWallpaper(wallpaper);
        }
    }
    
    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_disposed) return;
        
        // Remettre l'intervalle normal après une reprise avec temps restant
        var normalInterval = Math.Max(SettingsService.Current.RotationIntervalMinutes, 1) * 60 * 1000;
        if (Math.Abs(_timer.Interval - normalInterval) > 1000)
        {
            _timer.Interval = normalInterval;
        }
        
        _lastTickTime = DateTime.UtcNow;
        
        if (!_isPaused)
        {
            Next();
        }
    }
    
    private static WallpaperStyle ConvertFitToStyle(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fill => WallpaperStyle.Fill,
        WallpaperFit.Fit => WallpaperStyle.Fit,
        WallpaperFit.Stretch => WallpaperStyle.Stretch,
        WallpaperFit.Tile => WallpaperStyle.Tile,
        WallpaperFit.Center => WallpaperStyle.Center,
        WallpaperFit.Span => WallpaperStyle.Span,
        _ => WallpaperStyle.Fill
    };
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _timer.Stop();
        _timer.Dispose();
    }
}
