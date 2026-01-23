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
    private Media? _currentMedia;
    private Window? _videoWindow;
    private LibVLCSharp.WPF.VideoView? _videoView;
    private bool _isPlaying;
    private volatile bool _disposed;
    private volatile bool _libVLCInitialized;
    private volatile bool _libVLCInitializing;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    
    public event EventHandler<bool>? PlaybackStateChanged;
    public bool IsPlaying => _isPlaying;
    
    public AnimatedWallpaperService()
    {
        // Pré-initialiser LibVLC en arrière-plan pour éviter le lag au premier Play
        _ = InitializeLibVLCAsync();
    }
    
    /// <summary>
    /// Initialise LibVLC de manière asynchrone en arrière-plan
    /// </summary>
    private async Task InitializeLibVLCAsync()
    {
        if (_libVLCInitialized || _libVLCInitializing || _disposed) return;
        
        await _initSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_libVLCInitialized || _disposed) return;
            _libVLCInitializing = true;
            
            await Task.Run(() =>
            {
                try
                {
                    Core.Initialize();
                    lock (_lock)
                    {
                        if (_disposed) return;
                        _libVLC = new LibVLC(
                            "--no-audio", 
                            "--input-repeat=65535", 
                            "--quiet",
                            "--no-video-title-show"
                        );
                        _libVLCInitialized = true;
                    }
                    System.Diagnostics.Debug.WriteLine("LibVLC initialisé avec succès (async)");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur init LibVLC: {ex.Message}");
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _libVLCInitializing = false;
            _initSemaphore.Release();
        }
    }
    
    /// <summary>
    /// S'assure que LibVLC est initialisé (attend si nécessaire)
    /// </summary>
    private async Task EnsureLibVLCInitializedAsync()
    {
        if (_libVLCInitialized && _libVLC != null) return;
        
        await InitializeLibVLCAsync().ConfigureAwait(false);
    }
    
    public void Play(Wallpaper wallpaper)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        
        if (_disposed)
            return;
        
        if (wallpaper.Type is not WallpaperType.Video and not WallpaperType.Animated)
            return;
        
        if (!System.IO.File.Exists(wallpaper.FilePath))
        {
            System.Diagnostics.Debug.WriteLine($"Fichier introuvable: {wallpaper.FilePath}");
            return;
        }
        
        // Lancer l'opération de manière asynchrone pour ne pas bloquer l'UI
        _ = PlayAsync(wallpaper);
    }
    
    private async Task PlayAsync(Wallpaper wallpaper)
    {
        try
        {
            // Initialiser LibVLC en arrière-plan si nécessaire
            await EnsureLibVLCInitializedAsync().ConfigureAwait(false);
            
            if (_libVLC == null || _disposed)
                return;
            
            // Arrêter la lecture précédente (sur le thread UI)
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StopInternal();
            }).Task.ConfigureAwait(false);
            
            // Invalider le cache WorkerW (peut être fait hors UI)
            DesktopWindowApi.InvalidateCache();
            
            // Créer le média en arrière-plan (opération potentiellement lente)
            Media? media = null;
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_disposed || _libVLC == null) return;
                    media = new Media(_libVLC, wallpaper.FilePath, FromType.FromPath);
                    media.AddOption(":input-repeat=65535");
                }
            }).ConfigureAwait(false);
            
            if (media == null || _disposed) return;
            
            // Créer la fenêtre et démarrer la lecture sur le thread UI
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        media.Dispose();
                        return;
                    }
                    
                    try
                    {
                        _currentMedia = media;
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
            }).Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur PlayAsync: {ex.Message}");
        }
    }

    private void CreateVideoWindow()
    {
        // Créer le MediaPlayer d'abord
        _mediaPlayer = new MediaPlayer(_libVLC!)
        {
            Volume = Math.Clamp(SettingsService.Current.AnimatedVolume, 0, 100),
            EnableHardwareDecoding = true
        };
        
        _mediaPlayer.EncounteredError += OnMediaPlayerError;
        _mediaPlayer.EndReached += OnMediaEndReached;
        
        // Créer le VideoView avec stretch explicite
        _videoView = new LibVLCSharp.WPF.VideoView
        {
            MediaPlayer = _mediaPlayer,
            Background = System.Windows.Media.Brushes.Black,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch
        };
        
        // Wrapper Grid pour garantir l'étirement du contenu
        var container = new System.Windows.Controls.Grid
        {
            Background = System.Windows.Media.Brushes.Black
        };
        container.Children.Add(_videoView);
        
        // Créer la fenêtre - les dimensions seront définies par SetAsDesktopChild
        // qui utilise les pixels physiques du WorkerW (pas les dimensions WPF logiques)
        _videoWindow = new Window
        {
            Title = "WallpaperManager_Video",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = false,
            Background = System.Windows.Media.Brushes.Black,
            // Taille temporaire - sera ajustée par SetAsDesktopChild avec les pixels physiques
            Left = 0,
            Top = 0,
            Width = 800,
            Height = 600,
            Content = container
        };
        
        // Gestionnaire pour configurer la fenêtre après initialisation
        _videoWindow.SourceInitialized += OnVideoWindowSourceInitialized;
        
        // Afficher la fenêtre (nécessaire avant SetParent)
        _videoWindow.Show();
    }
    
    private void OnVideoWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (_videoWindow == null) return;
        
        var handle = new WindowInteropHelper(_videoWindow).Handle;
        System.Diagnostics.Debug.WriteLine($"Handle de la fenêtre vidéo: {handle}");
        
        // Configurer comme enfant du bureau
        var success = DesktopWindowApi.SetAsDesktopChild(handle);
        System.Diagnostics.Debug.WriteLine($"SetAsDesktopChild: {success}");
        
        if (success)
        {
            // Après SetWindowPos, forcer WPF à synchroniser avec la taille Win32
            // Utiliser les dimensions de l'écran virtuel (plein écran sans taskbar)
            var screenWidth = System.Windows.SystemParameters.PrimaryScreenWidth;
            var screenHeight = System.Windows.SystemParameters.PrimaryScreenHeight;
            
            // Définir la taille WPF en unités logiques (WPF gère le DPI automatiquement)
            _videoWindow.Width = screenWidth;
            _videoWindow.Height = screenHeight;
            
            System.Diagnostics.Debug.WriteLine($"WPF dimensions: {screenWidth}x{screenHeight}");
            
            // Forcer la mise à jour complète du layout
            _videoWindow.InvalidateVisual();
            _videoWindow.UpdateLayout();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("SetAsDesktopChild a échoué, tentative de fallback...");
            // Fallback: envoyer juste à l'arrière
            DesktopWindowApi.SendToBack(handle);
        }
    }
    
    private void OnMediaPlayerError(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("Erreur MediaPlayer détectée");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Stop);
    }
    
    private void OnMediaEndReached(object? sender, EventArgs e)
    {
        // Relancer la lecture en boucle
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_lock)
            {
                if (_mediaPlayer != null && _currentMedia != null && !_disposed)
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Play(_currentMedia);
                }
            }
        });
    }
    
    private void SetupMediaPlayer(Wallpaper wallpaper)
    {
        if (_mediaPlayer == null || _libVLC == null || _currentMedia == null)
            return;
        
        // Configurer VLC pour étirer/couvrir tout l'espace disponible
        _mediaPlayer.Scale = 0; // 0 = auto-scale pour remplir la fenêtre
        
        // Forcer le ratio d'aspect de l'écran pour éviter le letterboxing
        var screenWidth = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
        var screenHeight = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
        // Format: "width:height" - force VLC à utiliser exactement ce ratio
        _mediaPlayer.AspectRatio = $"{screenWidth}:{screenHeight}";
        
        System.Diagnostics.Debug.WriteLine($"Lecture de: {wallpaper.FilePath}");
        _mediaPlayer.Play(_currentMedia);
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isPlaying = false;
        }
        
        // Utiliser BeginInvoke pour ne pas bloquer
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_lock)
            {
                CleanupResources();
            }
        });
        
        PlaybackStateChanged?.Invoke(this, false);
    }
    
    /// <summary>
    /// Version interne de Stop appelée depuis le thread UI
    /// </summary>
    private void StopInternal()
    {
        lock (_lock)
        {
            _isPlaying = false;
            CleanupResources();
        }
        
        PlaybackStateChanged?.Invoke(this, false);
    }
    
    private void CleanupResources()
    {
        try
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.EncounteredError -= OnMediaPlayerError;
                _mediaPlayer.EndReached -= OnMediaEndReached;
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }
            
            if (_currentMedia != null)
            {
                _currentMedia.Dispose();
                _currentMedia = null;
            }
            
            if (_videoView != null)
            {
                _videoView.MediaPlayer = null;
                _videoView = null;
            }
            
            if (_videoWindow != null)
            {
                _videoWindow.SourceInitialized -= OnVideoWindowSourceInitialized;
                _videoWindow.Close();
                _videoWindow = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur nettoyage: {ex.Message}");
        }
    }
    
    public void ReleaseLibVLC()
    {
        lock (_lock)
        {
            if (_isPlaying) return;
            
            if (_libVLC != null)
            {
                _libVLC.Dispose();
                _libVLC = null;
                _libVLCInitialized = false;
            }
        }
        
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: false);
    }
    
    public void Pause()
    {
        lock (_lock)
        {
            if (!_isPlaying || _disposed) return;
            _mediaPlayer?.Pause();
            _isPlaying = false;
        }
        PlaybackStateChanged?.Invoke(this, false);
    }
    
    public void Resume()
    {
        lock (_lock)
        {
            if (_isPlaying || _disposed || _mediaPlayer == null) return;
            _mediaPlayer.Play();
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
        
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            
            _isPlaying = false;
            CleanupResources();
            
            _libVLC?.Dispose();
            _libVLC = null;
            _libVLCInitialized = false;
        }
        
        _initSemaphore.Dispose();
    }
}
