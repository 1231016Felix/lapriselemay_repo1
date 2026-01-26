using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using WallpaperManager.Models;
using WallpaperManager.Native;
using WpfApplication = System.Windows.Application;

namespace WallpaperManager.Services;

/// <summary>
/// Type d'effet de transition entre les fonds d'écran.
/// </summary>
public enum TransitionEffect
{
    None,
    Fade,
    SlideLeft,
    SlideRight,
    SlideUp,
    SlideDown,
    Zoom,
    Dissolve
}

/// <summary>
/// Service gérant les effets de transition entre les fonds d'écran.
/// Utilise une fenêtre overlay plein écran pour afficher l'animation.
/// </summary>
public sealed class TransitionService : IDisposable
{
    private TransitionWindow? _transitionWindow;
    private volatile bool _disposed;
    private bool _isTransitioning;
    
    public TimeSpan TransitionDuration { get; set; } = TimeSpan.FromMilliseconds(500);
    public TransitionEffect CurrentEffect { get; set; } = TransitionEffect.Fade;
    public bool IsEnabled => SettingsService.Current.TransitionEnabled && CurrentEffect != TransitionEffect.None;
    
    /// <summary>
    /// Exécute une transition vers un nouveau fond d'écran.
    /// La fenêtre de transition est placée derrière les icônes du bureau
    /// pour éviter tout scintillement pendant le changement de fond d'écran.
    /// </summary>
    /// <param name="newWallpaperPath">Chemin du nouveau fond d'écran</param>
    /// <param name="onComplete">Action à exécuter une fois la transition terminée (appliquer le vrai wallpaper)</param>
    public async Task TransitionToAsync(string newWallpaperPath, Action onComplete)
    {
        if (!IsEnabled || _isTransitioning || string.IsNullOrEmpty(newWallpaperPath))
        {
            onComplete?.Invoke();
            return;
        }
        
        if (!File.Exists(newWallpaperPath))
        {
            onComplete?.Invoke();
            return;
        }
        
        _isTransitioning = true;
        
        try
        {
            await WpfApplication.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Charger la nouvelle image en premier
                    var newImage = await LoadImageAsync(newWallpaperPath).ConfigureAwait(true);
                    if (newImage == null)
                    {
                        onComplete?.Invoke();
                        return;
                    }
                    
                    // Créer la fenêtre de transition
                    _transitionWindow = new TransitionWindow();
                    _transitionWindow.SetNewImage(newImage);
                    _transitionWindow.Show();
                    
                    // Attendre que la fenêtre soit intégrée derrière les icônes
                    await Task.Delay(50).ConfigureAwait(true);
                    
                    // Lancer l'animation (visible derrière les icônes)
                    await _transitionWindow.PlayTransitionAsync(CurrentEffect, TransitionDuration).ConfigureAwait(true);
                    
                    // Appliquer le vrai wallpaper pendant que notre image est visible
                    // Comme notre fenêtre est derrière les icônes, ils ne disparaissent jamais
                    onComplete?.Invoke();
                    
                    // Attendre que Windows applique le wallpaper
                    await Task.Delay(150).ConfigureAwait(true);
                    
                    // Fermer la fenêtre de transition
                    _transitionWindow.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur transition: {ex.Message}");
                    onComplete?.Invoke();
                }
                finally
                {
                    _transitionWindow = null;
                }
            });
        }
        finally
        {
            _isTransitioning = false;
        }
    }
    
    private static async Task<BitmapImage?> LoadImageAsync(string path)
    {
        return await Task.Run(() =>
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }).ConfigureAwait(false);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        WpfApplication.Current?.Dispatcher.Invoke(() =>
        {
            _transitionWindow?.Close();
            _transitionWindow = null;
        });
    }
}

/// <summary>
/// Fenêtre plein écran transparente pour afficher les transitions.
/// Placée derrière les icônes du bureau pour éviter le scintillement.
/// </summary>
internal class TransitionWindow : Window
{
    private readonly System.Windows.Controls.Image _newImageControl;
    private readonly Grid _container;
    private bool _isEmbedded;
    
    public TransitionWindow()
    {
        // Configuration fenêtre plein écran
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = false; // Pas de transparence pour de meilleures performances
        Background = System.Windows.Media.Brushes.Black;
        
        // Couvrir tous les écrans
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen != null)
        {
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;
        }
        
        // Créer le conteneur
        _container = new Grid { Background = System.Windows.Media.Brushes.Black };
        
        // Image pour la nouvelle image
        _newImageControl = new System.Windows.Controls.Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        
        _container.Children.Add(_newImageControl);
        Content = _container;
        
        // Placer derrière les icônes une fois la fenêtre chargée
        Loaded += OnLoaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EmbedBehindIcons();
    }
    
    /// <summary>
    /// Place la fenêtre derrière les icônes du bureau.
    /// </summary>
    private void EmbedBehindIcons()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            var hwnd = helper.Handle;
            
            if (hwnd != IntPtr.Zero)
            {
                _isEmbedded = DesktopWindowApi.SetAsDesktopChild(hwnd);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur embed: {ex.Message}");
            _isEmbedded = false;
        }
    }
    
    public bool IsEmbedded => _isEmbedded;
    
    public void SetNewImage(BitmapImage image)
    {
        _newImageControl.Source = image;
        _newImageControl.Opacity = 0;
    }
    
    public async Task PlayTransitionAsync(TransitionEffect effect, TimeSpan duration)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        Storyboard storyboard;
        
        switch (effect)
        {
            case TransitionEffect.Fade:
                storyboard = CreateFadeTransition(duration);
                break;
            case TransitionEffect.SlideLeft:
                storyboard = CreateSlideTransition(duration, -ActualWidth, 0);
                break;
            case TransitionEffect.SlideRight:
                storyboard = CreateSlideTransition(duration, ActualWidth, 0);
                break;
            case TransitionEffect.SlideUp:
                storyboard = CreateSlideTransition(duration, 0, -ActualHeight);
                break;
            case TransitionEffect.SlideDown:
                storyboard = CreateSlideTransition(duration, 0, ActualHeight);
                break;
            case TransitionEffect.Zoom:
                storyboard = CreateZoomTransition(duration);
                break;
            case TransitionEffect.Dissolve:
                storyboard = CreateDissolveTransition(duration);
                break;
            default:
                tcs.SetResult(true);
                return;
        }
        
        storyboard.Completed += (s, e) => tcs.TrySetResult(true);
        storyboard.Begin(this);
        
        await tcs.Task.ConfigureAwait(false);
    }
    
    private Storyboard CreateFadeTransition(TimeSpan duration)
    {
        var storyboard = new Storyboard();
        
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        
        Storyboard.SetTarget(fadeIn, _newImageControl);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);
        
        return storyboard;
    }
    
    private Storyboard CreateSlideTransition(TimeSpan duration, double offsetX, double offsetY)
    {
        var storyboard = new Storyboard();
        
        // Préparer le transform
        var transform = new TranslateTransform(offsetX, offsetY);
        _newImageControl.RenderTransform = transform;
        _newImageControl.Opacity = 1;
        
        // Animation de translation X
        if (offsetX != 0)
        {
            var slideX = new DoubleAnimation
            {
                From = offsetX,
                To = 0,
                Duration = new Duration(duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slideX, _newImageControl);
            Storyboard.SetTargetProperty(slideX, new PropertyPath("RenderTransform.X"));
            storyboard.Children.Add(slideX);
        }
        
        // Animation de translation Y
        if (offsetY != 0)
        {
            var slideY = new DoubleAnimation
            {
                From = offsetY,
                To = 0,
                Duration = new Duration(duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slideY, _newImageControl);
            Storyboard.SetTargetProperty(slideY, new PropertyPath("RenderTransform.Y"));
            storyboard.Children.Add(slideY);
        }
        
        return storyboard;
    }
    
    private Storyboard CreateZoomTransition(TimeSpan duration)
    {
        var storyboard = new Storyboard();
        
        // Préparer le transform
        var transform = new ScaleTransform(1.5, 1.5);
        _newImageControl.RenderTransform = transform;
        _newImageControl.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        
        // Zoom in vers taille normale
        var scaleX = new DoubleAnimation
        {
            From = 1.5,
            To = 1,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleX, _newImageControl);
        Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.ScaleX"));
        storyboard.Children.Add(scaleX);
        
        var scaleY = new DoubleAnimation
        {
            From = 1.5,
            To = 1,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleY, _newImageControl);
        Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.ScaleY"));
        storyboard.Children.Add(scaleY);
        
        // Fade in simultané
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fadeIn, _newImageControl);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);
        
        return storyboard;
    }
    
    private Storyboard CreateDissolveTransition(TimeSpan duration)
    {
        var storyboard = new Storyboard();
        
        // Effet de dissolve = fade avec léger blur
        var blurEffect = new System.Windows.Media.Effects.BlurEffect { Radius = 10 };
        _newImageControl.Effect = blurEffect;
        
        // Fade in
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(duration),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fadeIn, _newImageControl);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);
        
        // Réduire le blur
        var blurAnim = new DoubleAnimation
        {
            From = 10,
            To = 0,
            Duration = new Duration(duration),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(blurAnim, _newImageControl);
        Storyboard.SetTargetProperty(blurAnim, new PropertyPath("Effect.Radius"));
        storyboard.Children.Add(blurAnim);
        
        return storyboard;
    }
}
