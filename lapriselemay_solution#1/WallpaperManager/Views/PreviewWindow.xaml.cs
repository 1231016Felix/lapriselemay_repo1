using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using WallpaperManager.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Cursors = System.Windows.Input.Cursors;

namespace WallpaperManager.Views;

public partial class PreviewWindow : Window
{
    private readonly List<Wallpaper> _wallpapers;
    private int _currentIndex;
    private Wallpaper? _currentWallpaper;
    
    // LibVLC pour les vidéos
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    
    public event EventHandler<Wallpaper>? ApplyRequested;
    
    public PreviewWindow(Wallpaper wallpaper) : this([wallpaper], 0) { }
    
    public PreviewWindow(IEnumerable<Wallpaper> wallpapers, int startIndex = 0)
    {
        InitializeComponent();
        
        _wallpapers = wallpapers.ToList();
        _currentIndex = Math.Clamp(startIndex, 0, Math.Max(0, _wallpapers.Count - 1));
        
        if (_wallpapers.Count > 1)
        {
            BtnPrev.Visibility = Visibility.Visible;
            BtnNext.Visibility = Visibility.Visible;
        }
        
        ShowCurrentItem();
        
        // Afficher l'overlay au démarrage puis le masquer
        InfoOverlay.Opacity = 1;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            FadeOutOverlay();
        };
        timer.Start();
    }
    
    private void ShowCurrentItem()
    {
        if (_currentIndex < 0 || _currentIndex >= _wallpapers.Count)
            return;
        
        _currentWallpaper = _wallpapers[_currentIndex];
        
        // Arrêter la vidéo précédente si nécessaire
        StopVideo();
        
        try
        {
            if (_currentWallpaper.Type == WallpaperType.Video || 
                _currentWallpaper.Type == WallpaperType.Animated)
            {
                ShowVideo(_currentWallpaper);
            }
            else
            {
                ShowImage(_currentWallpaper);
            }
            
            TitleText.Text = _currentWallpaper.DisplayName;
            
            var typeLabel = _currentWallpaper.Type switch
            {
                WallpaperType.Video => " • Vidéo",
                WallpaperType.Animated => " • GIF",
                _ => ""
            };
            
            InfoText.Text = $"{_currentWallpaper.Resolution} • {_currentWallpaper.FileSizeFormatted}{typeLabel} • {_currentIndex + 1}/{_wallpapers.Count}";
        }
        catch (Exception ex)
        {
            TitleText.Text = "Erreur de chargement";
            InfoText.Text = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Erreur prévisualisation: {ex}");
        }
    }
    
    private void ShowImage(Wallpaper wallpaper)
    {
        // Afficher l'image, masquer la vidéo
        PreviewImage.Visibility = Visibility.Visible;
        VideoView.Visibility = Visibility.Collapsed;
        
        if (!File.Exists(wallpaper.FilePath))
        {
            TitleText.Text = "Fichier introuvable";
            return;
        }
        
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(wallpaper.FilePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        
        PreviewImage.Source = bitmap;
    }
    
    private void ShowVideo(Wallpaper wallpaper)
    {
        if (!File.Exists(wallpaper.FilePath))
        {
            TitleText.Text = "Fichier introuvable";
            return;
        }

        // Masquer l'image, afficher la vidéo
        PreviewImage.Visibility = Visibility.Collapsed;
        VideoView.Visibility = Visibility.Visible;
        
        try
        {
            // Initialiser LibVLC si nécessaire
            if (_libVLC == null)
            {
                Core.Initialize();
                _libVLC = new LibVLC(
                    "--no-video-title-show",
                    "--input-repeat=65535"
                );
            }
            
            // Créer le MediaPlayer
            _mediaPlayer = new MediaPlayer(_libVLC)
            {
                Volume = 50,  // Volume modéré pour la prévisualisation
                EnableHardwareDecoding = true
            };
            
            // Assigner au VideoView
            VideoView.MediaPlayer = _mediaPlayer;
            
            // Créer et jouer le media
            _currentMedia = new Media(_libVLC, wallpaper.FilePath, FromType.FromPath);
            _currentMedia.AddOption(":input-repeat=65535");
            
            _mediaPlayer.Play(_currentMedia);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur lecture vidéo: {ex}");
            
            // Fallback: afficher un message
            PreviewImage.Visibility = Visibility.Visible;
            VideoView.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            TitleText.Text = "Impossible de lire la vidéo";
            InfoText.Text = ex.Message;
        }
    }
    
    private void StopVideo()
    {
        try
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }
            
            if (_currentMedia != null)
            {
                _currentMedia.Dispose();
                _currentMedia = null;
            }
            
            VideoView.MediaPlayer = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur arrêt vidéo: {ex.Message}");
        }
    }
    
    private void FadeOutOverlay()
    {
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        InfoOverlay.BeginAnimation(OpacityProperty, animation);
    }
    
    private void FadeInOverlay()
    {
        var animation = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
        InfoOverlay.BeginAnimation(OpacityProperty, animation);
    }
    
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Escape:
                Close();
                break;
            case System.Windows.Input.Key.Left:
                NavigatePrevious();
                break;
            case System.Windows.Input.Key.Right:
                NavigateNext();
                break;
            case System.Windows.Input.Key.Enter:
            case System.Windows.Input.Key.Space:
                ApplyCurrentWallpaper();
                break;
            case System.Windows.Input.Key.M:
                // Mute/Unmute
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Mute = !_mediaPlayer.Mute;
                }
                break;
        }
    }
    
    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button)
            return;
        
        Close();
    }
    
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Nettoyer les ressources LibVLC
        StopVideo();
        
        _libVLC?.Dispose();
        _libVLC = null;
    }
    
    private void NavigatePrevious()
    {
        if (_wallpapers.Count <= 1) return;
        
        _currentIndex = _currentIndex <= 0 ? _wallpapers.Count - 1 : _currentIndex - 1;
        ShowCurrentItem();
    }
    
    private void NavigateNext()
    {
        if (_wallpapers.Count <= 1) return;
        
        _currentIndex = (_currentIndex + 1) % _wallpapers.Count;
        ShowCurrentItem();
    }
    
    private void ApplyCurrentWallpaper()
    {
        if (_currentWallpaper != null)
        {
            ApplyRequested?.Invoke(this, _currentWallpaper);
            Close();
        }
    }
    
    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        ApplyCurrentWallpaper();
    }
    
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    
    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        NavigatePrevious();
    }
    
    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        NavigateNext();
    }
    
    private void InfoOverlay_MouseEnter(object sender, MouseEventArgs e)
    {
        FadeInOverlay();
        Cursor = Cursors.Arrow;
    }
    
    private void InfoOverlay_MouseLeave(object sender, MouseEventArgs e)
    {
        FadeOutOverlay();
        Cursor = Cursors.None;
    }
}
