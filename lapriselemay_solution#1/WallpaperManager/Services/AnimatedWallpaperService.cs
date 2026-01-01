using System.Windows;
using System.Windows.Interop;
using WallpaperManager.Models;
using WallpaperManager.Native;
using LibVLCSharp.Shared;

namespace WallpaperManager.Services;

public sealed class AnimatedWallpaperService : IDisposable
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Window? _videoWindow;
    private volatile bool _isPlaying;
    private bool _disposed;
    private bool _libVLCInitialized;
    private readonly object _lock = new();
    
    public event EventHandler<bool>? PlaybackStateChanged;
    public bool IsPlaying => _isPlaying;
    
    public AnimatedWallpaperService()
    {
        // LibVLC sera initialisé à la demande pour économiser la RAM
    }
    
    private void EnsureLibVLCInitialized()
    {
        if (_libVLCInitialized || _disposed) return;
        
        lock (_lock)
        {
            if (_libVLCInitialized) return;
            
            try
            {
                Core.Initialize();
                _libVLC = new LibVLC("--no-audio", "--input-repeat=65535", "--quiet");
                _libVLCInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur init LibVLC: {ex.Message}");
            }
        }
    }
    
    public void Play(Wallpaper wallpaper)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        
        if (_disposed)
            return;
        
        if (wallpaper.Type != WallpaperType.Video && wallpaper.Type != WallpaperType.Animated)
            return;
        
        // Initialisation à la demande
        EnsureLibVLCInitialized();
        
        if (_libVLC == null)
            return;
        
        Stop();
        
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                try
                {
                    CreateVideoWindow();
                    SetupMediaPlayer(wallpaper);
                    
                    _isPlaying = true;
                    PlaybackStateChanged?.Invoke(this, true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur lecture vidéo: {ex.Message}");
                    CleanupResources();
                }
            }
        });
    }
    
    private void CreateVideoWindow()
    {
        _videoWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ShowInTaskbar = false,
            Topmost = false,
            Background = System.Windows.Media.Brushes.Black,
            Left = 0,
            Top = 0,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight
        };
        
        var videoView = new LibVLCSharp.WPF.VideoView();
        _videoWindow.Content = videoView;
        
        _mediaPlayer = new MediaPlayer(_libVLC!)
        {
            Volume = Math.Clamp(SettingsService.Current.AnimatedVolume, 0, 100)
        };
        videoView.MediaPlayer = _mediaPlayer;
        
        _videoWindow.Show();
        
        var handle = new WindowInteropHelper(_videoWindow).Handle;
        DesktopWindowApi.SetAsDesktopChild(handle);
    }
    
    private void SetupMediaPlayer(Wallpaper wallpaper)
    {
        if (_mediaPlayer == null || _libVLC == null)
            return;
        
        using var media = new Media(_libVLC, new Uri(wallpaper.FilePath));
        _mediaPlayer.Play(media);
    }
    
    public void Stop()
    {
        _isPlaying = false;
        
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (_lock)
            {
                CleanupResources();
            }
        });
        
        PlaybackStateChanged?.Invoke(this, false);
    }
    
    private void CleanupResources()
    {
        try
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }
            
            if (_videoWindow != null)
            {
                _videoWindow.Close();
                _videoWindow = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur nettoyage: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Libère complètement LibVLC pour économiser la RAM quand aucune vidéo n'est en cours.
    /// </summary>
    public void ReleaseLibVLC()
    {
        if (_isPlaying) return;
        
        lock (_lock)
        {
            if (_libVLC != null)
            {
                _libVLC.Dispose();
                _libVLC = null;
                _libVLCInitialized = false;
                
                // Forcer le GC pour libérer la mémoire native
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
    
    public void Pause()
    {
        lock (_lock)
        {
            _mediaPlayer?.Pause();
            _isPlaying = false;
        }
        PlaybackStateChanged?.Invoke(this, false);
    }
    
    public void Resume()
    {
        lock (_lock)
        {
            _mediaPlayer?.Play();
            _isPlaying = true;
        }
        PlaybackStateChanged?.Invoke(this, true);
    }
    
    public void SetVolume(int volume)
    {
        var clampedVolume = Math.Clamp(volume, 0, 100);
        
        lock (_lock)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = clampedVolume;
            }
        }
        
        SettingsService.Current.AnimatedVolume = clampedVolume;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        Stop();
        
        lock (_lock)
        {
            _libVLC?.Dispose();
            _libVLC = null;
        }
    }
}
