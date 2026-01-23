using System.Timers;
using WallpaperManager.Models;
using WallpaperManager.Native;

namespace WallpaperManager.Services;

public sealed class WallpaperRotationService : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly Random _random = Random.Shared;
    private readonly Lock _playlistLock = new();
    private readonly Lock _stateLock = new();
    
    private List<Wallpaper> _playlist = [];
    private int _currentIndex = -1;
    private bool _isPaused;
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
            lock (_playlistLock)
            {
                if (_currentIndex < 0 || _currentIndex >= _playlist.Count)
                    return null;
                
                return _playlist[_currentIndex];
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
        lock (_stateLock)
        {
            if (_disposed) return;
            
            LoadPlaylist();
            
            int playlistCount;
            lock (_playlistLock)
            {
                playlistCount = _playlist.Count;
            }
            
            if (playlistCount == 0)
                return;
            
            var intervalMs = Math.Max(SettingsService.Current.RotationIntervalMinutes, 1) * 60 * 1000;
            _timer.Interval = intervalMs;
            _remainingTimeMs = 0; // Réinitialiser le temps restant
            _lastTickTime = DateTime.UtcNow;
            _timer.Start();
            _isPaused = false;
        }
        
        RotationStateChanged?.Invoke(this, true);
    }
    
    public void Stop()
    {
        lock (_stateLock)
        {
            _timer.Stop();
            _isPaused = false;
        }
        
        RotationStateChanged?.Invoke(this, false);
    }
    
    public void Pause()
    {
        lock (_stateLock)
        {
            if (_isPaused || !_timer.Enabled) return;
            
            // Calculer le temps restant avant le prochain changement
            var elapsed = (DateTime.UtcNow - _lastTickTime).TotalMilliseconds;
            _remainingTimeMs = Math.Max(_timer.Interval - elapsed, 1000); // Minimum 1 seconde
            
            _timer.Stop();
            _isPaused = true;
            
            System.Diagnostics.Debug.WriteLine($"Rotation en pause - Temps restant: {_remainingTimeMs / 1000:F0}s");
        }
        
        RotationStateChanged?.Invoke(this, false);
    }
    
    public void Resume()
    {
        lock (_stateLock)
        {
            if (!_isPaused || _disposed) return;
            
            int playlistCount;
            lock (_playlistLock)
            {
                playlistCount = _playlist.Count;
            }
            
            if (playlistCount == 0) return;
            
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
        }
        
        RotationStateChanged?.Invoke(this, true);
    }
    
    public void Next()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
        }
        
        lock (_playlistLock)
        {
            var count = _playlist.Count;
            if (count == 0)
                return;
            
            if (SettingsService.Current.RandomOrder)
            {
                var newIndex = _random.Next(count);
                // Éviter de répéter le même si possible
                if (count > 1 && newIndex == _currentIndex)
                    newIndex = (newIndex + 1) % count;
                _currentIndex = newIndex;
            }
            else
            {
                _currentIndex = (_currentIndex + 1) % count;
            }
        }
        
        ApplyCurrentWallpaper();
    }
    
    public void Previous()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
        }
        
        lock (_playlistLock)
        {
            var count = _playlist.Count;
            if (count == 0)
                return;
            
            _currentIndex = _currentIndex <= 0 ? count - 1 : _currentIndex - 1;
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
    
    /// <summary>
    /// Définit une playlist personnalisée (pour les collections)
    /// </summary>
    public void SetPlaylist(IEnumerable<Wallpaper> wallpapers)
    {
        lock (_playlistLock)
        {
            _playlist = wallpapers
                .Where(w => w.Type == WallpaperType.Static)
                .ToList();
            _currentIndex = -1;
        }
    }
    
    private void LoadPlaylist()
    {
        lock (_playlistLock)
        {
            _playlist = SettingsService.Wallpapers
                .Where(w => w.Type == WallpaperType.Static)
                .ToList();
            
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
        bool shouldProceed;
        
        lock (_stateLock)
        {
            if (_disposed) return;
            
            // Remettre l'intervalle normal après une reprise avec temps restant
            var normalInterval = Math.Max(SettingsService.Current.RotationIntervalMinutes, 1) * 60 * 1000;
            if (Math.Abs(_timer.Interval - normalInterval) > 1000)
            {
                _timer.Interval = normalInterval;
            }
            
            _lastTickTime = DateTime.UtcNow;
            shouldProceed = !_isPaused;
        }
        
        if (shouldProceed)
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
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            
            _timer.Stop();
        }
        
        _timer.Elapsed -= OnTimerElapsed;
        _timer.Dispose();
    }
}
