using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.ViewModels;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace QuickLauncher.Views;

public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private readonly ISettingsProvider _settingsProvider;
    private readonly NotesService _notesService;
    
    // === Animation state ===
    // Compteur de génération pour invalider les callbacks de hide si un Show arrive entre-temps
    private int _hideGeneration;
    private bool _isAnimatingHide;
    /// <summary>
    /// Compteur de génération de recherche pour invalider les animations stagger d'un batch précédent.
    /// </summary>
    private int _searchGeneration;
    
    // Flag pour désactiver le stagger des items pendant l'animation d'ouverture
    private bool _isShowAnimating;
    
    // Easing functions gelées pour réutilisation (évite la recréation)
    private static readonly IEasingFunction EaseOut;
    private static readonly IEasingFunction EaseIn;
    private static readonly IEasingFunction BounceOut;
    
    /// <summary>
    /// Framerate cible pour les animations (60 fps pour la fluidité).
    /// </summary>
    private const int AnimationFrameRate = 60;
    
    /// <summary>
    /// BitmapCache partagé pour le caching GPU des éléments pendant les animations.
    /// Permet de rasteriser l'élément en texture GPU → les transforms/opacity sont hardware-accelerated
    /// même quand AllowsTransparency=True (qui force le rendu software par défaut).
    /// </summary>
    private static readonly BitmapCache SharedGpuCache;
    
    static LauncherWindow()
    {
        // Geler les fonctions d'easing pour performance (immuables, thread-safe)
        // QuadraticEase plus léger que CubicEase — moins de calcul par frame
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        easeOut.Freeze();
        EaseOut = easeOut;
        
        var easeIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        easeIn.Freeze();
        EaseIn = easeIn;
        
        var bounceOut = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 };
        bounceOut.Freeze();
        BounceOut = bounceOut;
        
        SharedGpuCache = new BitmapCache { EnableClearType = false, RenderAtScale = 1 };
        SharedGpuCache.Freeze();
    }
    
    /// <summary>
    /// Durée d'animation courante lue depuis les paramètres.
    /// </summary>
    private Duration AnimDuration => new(TimeSpan.FromMilliseconds(_settings.Appearance.AnimationDurationMs));
    
    /// <summary>
    /// Durée d'animation items (légèrement plus courte que la fenêtre).
    /// </summary>
    private Duration ItemAnimDuration => new(TimeSpan.FromMilliseconds(Math.Max(50, _settings.Appearance.AnimationDurationMs - 20)));
    
    /// <summary>
    /// Accès rapide aux paramètres actuels (toujours à jour via ISettingsProvider).
    /// </summary>
    private AppSettings _settings => _settingsProvider.Current;
    
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;
    
    public LauncherWindow(LauncherViewModel viewModel, ISettingsProvider settingsProvider, NotesService notesService)
    {
        InitializeComponent();
        
        _settingsProvider = settingsProvider;
        _notesService = notesService;
        _viewModel = viewModel;
        DataContext = _viewModel;
        
        SetupEventHandlers();
        ApplySettings();
    }
    
    private void SetupEventHandlers()
    {
        _viewModel.RequestHide += (_, _) => HideWindow();
        _viewModel.RequestOpenSettings += (_, _) =>
        {
            // Le ViewModel appelle RequestHide juste avant → forcer immédiat pour éviter
            // que le dialog modal bloque le thread pendant que l'animation tourne
            HideWindowImmediate();
            RequestOpenSettings?.Invoke(this, EventArgs.Empty);
        };
        _viewModel.RequestQuit += (_, _) => RequestQuit?.Invoke(this, EventArgs.Empty);
        _viewModel.RequestReindex += (_, _) =>
        {
            HideWindowImmediate();
            RequestReindex?.Invoke(this, EventArgs.Empty);
        };
        _viewModel.RequestRename += OnRequestRename;
        _viewModel.ShowNotification += OnShowNotification;
        _viewModel.RequestCaretAtEnd += (_, _) => Dispatcher.BeginInvoke(() => SearchBox.CaretIndex = SearchBox.Text.Length);
        _viewModel.RequestScreenCapture += OnRequestScreenCapture;
        
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.SearchText))
            {
                ClearButton.Visibility = string.IsNullOrEmpty(_viewModel.SearchText) 
                    ? Visibility.Collapsed 
                    : Visibility.Visible;
            }
        };
        
        // Incrémenter la génération de recherche quand la liste est nettoyée
        // pour invalider les animations stagger du batch précédent
        _viewModel.Results.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                _searchGeneration++;
        };
    }
    
    private void OnRequestRename(object? sender, string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var dialog = new RenameDialog(name) { Owner = this };
        
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
        {
            var success = FileActionsService.Rename(path, dialog.NewName);
            if (success)
            {
                RequestReindex?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                MessageBox.Show("Impossible de renommer le fichier.", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private void OnShowNotification(object? sender, string message)
    {
        // Pour l'instant, on peut utiliser une notification simple
        // TODO: Implémenter un toast notification
        System.Diagnostics.Debug.WriteLine($"[Notification] {message}");
    }

    private void OnRequestScreenCapture(object? sender, string? mode)
    {
        try
        {
            // Masquer immédiatement (sans animation) pour ne pas apparaître sur la capture
            HideWindowImmediate();
            
            var captureMode = mode?.ToLowerInvariant();
            
            if (captureMode is "snip" or "region" or "select")
            {
                // Capture de région avec overlay → ouvre l'annotateur
                var overlay = new ScreenshotOverlayWindow();
                if (overlay.ShowDialog() == true && overlay.CapturedRegion != null)
                {
                    var annotationWindow = new AnnotationWindow(overlay.CapturedRegion);
                    annotationWindow.ShowDialog();

                    if (!string.IsNullOrEmpty(annotationWindow.SavedFilePath))
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{annotationWindow.SavedFilePath}\"");
                }
            }
            else
            {
                // Capture plein écran ou écran principal → sauvegarde directe, sans rien afficher
                var primaryOnly = captureMode is "primary" or "main";
                var bitmap = CaptureScreen(primaryOnly);
                
                if (bitmap != null)
                {
                    var filePath = SaveScreenshotDirect(bitmap);
                    bitmap.Dispose();
                    
                    if (filePath != null)
                    {
                        // Copier aussi dans le presse-papier
                        var bitmapSource = LoadBitmapSourceFromFile(filePath);
                        if (bitmapSource != null)
                            System.Windows.Clipboard.SetImage(bitmapSource);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScreenCapture ERROR] {ex}");
            MessageBox.Show($"Erreur lors de la capture :\n{ex.GetType().Name}: {ex.Message}\n\nStack:\n{ex.StackTrace}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Sauvegarde directe d'un bitmap sans ouvrir l'annotateur.
    /// </summary>
    private static string? SaveScreenshotDirect(System.Drawing.Bitmap bitmap)
    {
        try
        {
            var screenshotsFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Screenshots");
            System.IO.Directory.CreateDirectory(screenshotsFolder);

            var fileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            var filePath = System.IO.Path.Combine(screenshotsFolder, fileName);

            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            return filePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveScreenshotDirect ERROR] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Charge un BitmapSource depuis un fichier PNG pour le presse-papier.
    /// </summary>
    private static System.Windows.Media.Imaging.BitmapSource? LoadBitmapSourceFromFile(string filePath)
    {
        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(filePath);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    private static System.Drawing.Bitmap? CaptureScreen(bool primaryOnly)
    {
        try
        {
            var bounds = primaryOnly
                ? System.Windows.Forms.Screen.PrimaryScreen!.Bounds
                : System.Windows.Forms.Screen.AllScreens
                    .Select(s => s.Bounds)
                    .Aggregate(System.Drawing.Rectangle.Union);

            var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
    
    private void ApplySettings()
    {
        Opacity = _settings.Appearance.WindowOpacity;
        SettingsButton.Visibility = _settings.Appearance.ShowSettingsButton ? Visibility.Visible : Visibility.Collapsed;
    }
    
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        
        if (e.OriginalSource is System.Windows.Controls.TextBox or System.Windows.Controls.ListBoxItem)
            return;
            
        DragMove();
    }
    
    public void FocusSearchBox()
    {
        // Reload synchrone — le ViewModel a besoin des settings à jour pour Reset()
        _settingsProvider.Reload();
        ApplySettings();
        _viewModel.ReloadSettings();
        _viewModel.Reset();
        CenterOnScreen();
        
        // L'animation démarre après le setup complet
        PlayShowAnimation();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
    
    #region Animations
    
    /// <summary>
    /// Annule proprement toutes les animations en cours sur le MainBorder et ShadowBorder,
    /// libérant les propriétés pour qu'elles puissent être réassignées.
    /// </summary>
    private void ClearAllAnimations()
    {
        MainBorder.BeginAnimation(OpacityProperty, null);
        MainBorderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        MainBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MainBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ShadowBorder.BeginAnimation(OpacityProperty, null);
    }
    
    /// <summary>
    /// Remet le MainBorder et ShadowBorder dans leur état visuel « caché » sans animation.
    /// </summary>
    private void ResetToHiddenState()
    {
        MainBorder.Opacity = 0;
        ShadowBorder.Opacity = 0;
        MainBorderTranslate.Y = -6;
        MainBorderScale.ScaleX = 1;
        MainBorderScale.ScaleY = 1;
    }
    
    /// <summary>
    /// Animation d'apparition selon le style configuré.
    /// Invalide tout hide en cours pour éviter que son Completed ne cache la fenêtre.
    /// </summary>
    private void PlayShowAnimation()
    {
        // Invalider tout callback de hide en cours
        _hideGeneration++;
        _isAnimatingHide = false;
        _isShowAnimating = true;
        
        // Nettoyer les animations précédentes
        ClearAllAnimations();
        
        if (!_settings.Appearance.EnableAnimations)
        {
            MainBorder.Opacity = 1;
            ShadowBorder.Opacity = 1;
            MainBorderTranslate.Y = 0;
            MainBorderScale.ScaleX = 1;
            MainBorderScale.ScaleY = 1;
            _isShowAnimating = false;
            return;
        }
        
        // Cache GPU temporaire pendant l'animation de la fenêtre (~140ms).
        // Rasterise le contenu en texture → opacity/transform deviennent GPU-composited.
        // Retiré dans le Completed pour ne pas forcer de re-rasterisation à chaque frappe clavier.
        MainBorder.CacheMode = SharedGpuCache;
        
        var dur = AnimDuration;
        
        // Animation du shadow (fondu simple, toujours)
        ShadowBorder.BeginAnimation(OpacityProperty, MakeAnim(0, 1, dur, TimeSpan.Zero, EaseOut));
        
        // Animation pilote (opacity) — on y attache le retrait du cache
        DoubleAnimation opacityAnim;
        
        switch (_settings.Appearance.AnimationStyle)
        {
            case AnimationStyle.FadeSlide:
                MainBorder.Opacity = 0;
                MainBorderTranslate.Y = -6;
                opacityAnim = MakeAnim(0, 1, dur, TimeSpan.Zero, EaseOut);
                MainBorderTranslate.BeginAnimation(TranslateTransform.YProperty, MakeAnim(-6, 0, dur, TimeSpan.Zero, EaseOut));
                break;
                
            case AnimationStyle.Fade:
                MainBorder.Opacity = 0;
                MainBorderTranslate.Y = 0;
                opacityAnim = MakeAnim(0, 1, dur, TimeSpan.Zero, EaseOut);
                break;
                
            case AnimationStyle.Scale:
                MainBorder.Opacity = 0;
                MainBorderTranslate.Y = 0;
                MainBorderScale.ScaleX = 0.95;
                MainBorderScale.ScaleY = 0.95;
                opacityAnim = MakeAnim(0, 1, dur, TimeSpan.Zero, EaseOut);
                MainBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty, MakeAnim(0.95, 1, dur, TimeSpan.Zero, EaseOut));
                MainBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(0.95, 1, dur, TimeSpan.Zero, EaseOut));
                break;
                
            case AnimationStyle.Slide:
                MainBorder.Opacity = 1;
                MainBorderTranslate.Y = -8;
                opacityAnim = MakeAnim(1, 1, dur, TimeSpan.Zero, null); // dummy pour callback
                MainBorderTranslate.BeginAnimation(TranslateTransform.YProperty, MakeAnim(-8, 0, dur, TimeSpan.Zero, EaseOut));
                break;
                
            case AnimationStyle.Pop:
                MainBorder.Opacity = 0;
                MainBorderTranslate.Y = 0;
                MainBorderScale.ScaleX = 0.88;
                MainBorderScale.ScaleY = 0.88;
                opacityAnim = MakeAnim(0, 1, dur, TimeSpan.Zero, EaseOut);
                MainBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty, MakeAnim(0.88, 1, dur, TimeSpan.Zero, BounceOut));
                MainBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(0.88, 1, dur, TimeSpan.Zero, BounceOut));
                break;
                
            default:
                opacityAnim = MakeAnim(0, 1, dur, TimeSpan.Zero, EaseOut);
                break;
        }
        
        // Retirer le cache GPU après l'animation pour que le rendu texte/ClearType soit normal
        opacityAnim.Completed += (_, _) =>
        {
            MainBorder.CacheMode = null;
            _isShowAnimating = false;
        };
        MainBorder.BeginAnimation(OpacityProperty, opacityAnim);
    }
    
    /// <summary>
    /// Animation de disparition selon le style configuré.
    /// Hide() n'est appelé que dans le Completed, et seulement si la génération correspond.
    /// </summary>
    private void PlayHideAnimation()
    {
        if (_isAnimatingHide) return;
        
        // Sauvegarder la position maintenant (avant le Hide)
        SaveWindowPosition();
        
        if (!_settings.Appearance.EnableAnimations)
        {
            _viewModel.Reset();
            ClearAllAnimations();
            ResetToHiddenState();
            Hide();
            return;
        }
        
        _isAnimatingHide = true;
        var gen = _hideGeneration;
        var dur = AnimDuration;
        
        // Cache GPU temporaire pendant l'animation de fermeture
        MainBorder.CacheMode = SharedGpuCache;
        
        // Animation du shadow (fondu sortant)
        ShadowBorder.BeginAnimation(OpacityProperty, MakeAnim(ShadowBorder.Opacity, 0, dur, TimeSpan.Zero, EaseIn));
        
        // L'animation « pilote » qui portera le Completed callback
        DoubleAnimation pilot;
        
        switch (_settings.Appearance.AnimationStyle)
        {
            case AnimationStyle.FadeSlide:
                pilot = MakeAnimWithCallback(MainBorder.Opacity, 0, dur, EaseIn, gen);
                MainBorder.BeginAnimation(OpacityProperty, pilot);
                MainBorderTranslate.BeginAnimation(TranslateTransform.YProperty,
                    MakeAnim(MainBorderTranslate.Y, -4, dur, TimeSpan.Zero, EaseIn));
                break;
                
            case AnimationStyle.Fade:
                pilot = MakeAnimWithCallback(MainBorder.Opacity, 0, dur, EaseIn, gen);
                MainBorder.BeginAnimation(OpacityProperty, pilot);
                break;
                
            case AnimationStyle.Scale:
                pilot = MakeAnimWithCallback(MainBorder.Opacity, 0, dur, EaseIn, gen);
                MainBorder.BeginAnimation(OpacityProperty, pilot);
                MainBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    MakeAnim(MainBorderScale.ScaleX, 0.95, dur, TimeSpan.Zero, EaseIn));
                MainBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    MakeAnim(MainBorderScale.ScaleY, 0.95, dur, TimeSpan.Zero, EaseIn));
                break;
                
            case AnimationStyle.Slide:
                pilot = MakeAnimWithCallback(0, 0, dur, EaseIn, gen); // dummy pour callback
                var slideAnim = MakeAnim(MainBorderTranslate.Y, -8, dur, TimeSpan.Zero, EaseIn);
                MainBorderTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
                // Utiliser le slide comme pilote via un fondu rapide de l'opacity
                MainBorder.BeginAnimation(OpacityProperty, pilot);
                break;
                
            case AnimationStyle.Pop:
                pilot = MakeAnimWithCallback(MainBorder.Opacity, 0, dur, EaseIn, gen);
                MainBorder.BeginAnimation(OpacityProperty, pilot);
                MainBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    MakeAnim(MainBorderScale.ScaleX, 0.88, dur, TimeSpan.Zero, EaseIn));
                MainBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    MakeAnim(MainBorderScale.ScaleY, 0.88, dur, TimeSpan.Zero, EaseIn));
                break;
                
            default:
                pilot = MakeAnimWithCallback(1, 0, dur, EaseIn, gen);
                MainBorder.BeginAnimation(OpacityProperty, pilot);
                break;
        }
    }
    
    /// <summary>
    /// Crée une animation pilote avec le callback Completed pour le hide.
    /// </summary>
    private DoubleAnimation MakeAnimWithCallback(double from, double to, Duration duration,
        IEasingFunction? easing, int generation)
    {
        var anim = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = easing
        };
        Timeline.SetDesiredFrameRate(anim, AnimationFrameRate);
        
        anim.Completed += (_, _) =>
        {
            if (generation != _hideGeneration) return;
            
            _viewModel.Reset();
            ClearAllAnimations();
            ResetToHiddenState();
            MainBorder.CacheMode = null;
            Hide();
            _isAnimatingHide = false;
        };
        
        return anim;
    }
    
    #endregion
    
    /// <summary>
    /// Animation d'apparition staggerée pour chaque item de la liste de résultats.
    /// Chaque item apparaît avec un délai progressif basé sur son index.
    /// Respecte les paramètres d'animation (activation, style, durée et stagger).
    /// Applique un BitmapCache GPU pendant l'animation pour fluidité maximale.
    /// </summary>
    private void ResultItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBoxItem item) return;
        
        // S'assurer que l'item est toujours visible (pas de manipulation d'opacité)
        // Nécessaire pour les containers recyclés qui pourraient garder une opacité résiduelle
        item.BeginAnimation(OpacityProperty, null);
        item.Opacity = 1;
        
        // Pendant l'animation d'ouverture ou si animations désactivées, pas de stagger
        if (!_settings.Appearance.EnableAnimations || _isShowAnimating)
            return;
        
        // Déterminer l'index de l'item dans la liste
        var index = ResultsList.ItemContainerGenerator.IndexFromContainer(item);
        if (index < 0) index = 0;
        
        // Capturer la génération courante pour invalider si une nouvelle recherche arrive
        var generation = _searchGeneration;
        
        var dur = ItemAnimDuration;
        var staggerMs = Math.Max(0, _settings.Appearance.StaggerDelayMs);
        var beginTime = TimeSpan.FromMilliseconds(index * staggerMs);
        
        var tg = item.RenderTransform as TransformGroup;
        var scale = tg?.Children.OfType<ScaleTransform>().FirstOrDefault();
        var translate = tg?.Children.OfType<TranslateTransform>().FirstOrDefault();
        
        // Activer le cache GPU pendant l'animation pour que les transforms
        // soient composés par le GPU plutôt que re-rendus par le CPU à chaque frame
        item.CacheMode = SharedGpuCache;
        
        // Animation pilote pour le cleanup — on utilise la transform principale
        // Plus de manipulation d'opacité : les items restent visibles, seuls les transforms bougent
        DoubleAnimation? pilotAnim = null;
        
        switch (_settings.Appearance.AnimationStyle)
        {
            case AnimationStyle.FadeSlide:
            case AnimationStyle.Slide:
                var slideFrom = _settings.Appearance.AnimationStyle == AnimationStyle.FadeSlide ? 4.0 : 6.0;
                pilotAnim = MakeAnim(slideFrom, 0, dur, beginTime, EaseOut);
                translate?.BeginAnimation(TranslateTransform.YProperty, pilotAnim);
                break;
                
            case AnimationStyle.Scale:
                pilotAnim = MakeAnim(0.96, 1, dur, beginTime, EaseOut);
                scale?.BeginAnimation(ScaleTransform.ScaleXProperty, pilotAnim);
                scale?.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(0.96, 1, dur, beginTime, EaseOut));
                break;
                
            case AnimationStyle.Pop:
                pilotAnim = MakeAnim(0.90, 1, dur, beginTime, BounceOut);
                scale?.BeginAnimation(ScaleTransform.ScaleXProperty, pilotAnim);
                scale?.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(0.90, 1, dur, beginTime, BounceOut));
                break;
                
            case AnimationStyle.Fade:
            default:
                // Fade seul sans opacité = pas d'animation visible, juste le cleanup
                pilotAnim = MakeAnim(0, 0, dur, beginTime, null);
                break;
        }
        
        // Retirer le cache GPU une fois l'animation terminée pour économiser la VRAM
        // et permettre au ClearType de fonctionner normalement sur le texte
        if (pilotAnim != null)
        {
            pilotAnim.Completed += (_, _) =>
            {
                if (generation == _searchGeneration)
                    item.CacheMode = null;
            };
        }
    }
    
    /// <summary>
    /// Crée une DoubleAnimation avec BeginTime pour le stagger, DesiredFrameRate fixe et gelée si possible.
    /// </summary>
    private static DoubleAnimation MakeAnim(double from, double to, Duration duration,
        TimeSpan beginTime, IEasingFunction? easing)
    {
        var anim = new DoubleAnimation(from, to, duration)
        {
            BeginTime = beginTime,
            EasingFunction = easing
        };
        Timeline.SetDesiredFrameRate(anim, AnimationFrameRate);
        return anim;
    }
    
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Raccourcis Alt+1 à Alt+9 pour lancer un résultat par index
        // En WPF, quand Alt est enfoncé, e.Key == Key.System et la vraie touche est dans e.SystemKey
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.System)
        {
            var index = e.SystemKey switch
            {
                Key.D1 => 0,
                Key.D2 => 1,
                Key.D3 => 2,
                Key.D4 => 3,
                Key.D5 => 4,
                Key.D6 => 5,
                Key.D7 => 6,
                Key.D8 => 7,
                Key.D9 => 8,
                Key.NumPad1 => 0,
                Key.NumPad2 => 1,
                Key.NumPad3 => 2,
                Key.NumPad4 => 3,
                Key.NumPad5 => 4,
                Key.NumPad6 => 5,
                Key.NumPad7 => 6,
                Key.NumPad8 => 7,
                Key.NumPad9 => 8,
                _ => -1
            };
            
            if (index >= 0 && index < _viewModel.Results.Count)
            {
                _viewModel.SelectedIndex = index;
                _viewModel.ExecuteCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }
        
        // Raccourcis avec Ctrl
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.OemComma:
                    HideWindowImmediate();
                    RequestOpenSettings?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                    
                case Key.R:
                    HideWindowImmediate();
                    RequestReindex?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                    
                case Key.Q:
                    RequestQuit?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                    
                case Key.T:
                    // Ouvrir terminal
                    if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Results.Count)
                    {
                        var item = _viewModel.Results[_viewModel.SelectedIndex];
                        FileActionExecutor.Execute(FileActionType.OpenInTerminal, item.Path);
                    }
                    e.Handled = true;
                    return;
            }
        }
        
        // Ctrl+Enter: Exécuter en admin
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            _viewModel.ExecuteAsAdminCommand.Execute(null);
            e.Handled = true;
            return;
        }
        
        // Ctrl+Shift+Enter: Ouvrir en navigation privée (pour les bookmarks/URLs)
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Enter)
        {
            if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Results.Count)
            {
                var item = _viewModel.Results[_viewModel.SelectedIndex];
                if (item.Type is ResultType.Bookmark or ResultType.WebSearch)
                {
                    FileActionExecutor.Execute(FileActionType.OpenPrivate, item.Path);
                    HideWindow();
                }
            }
            e.Handled = true;
            return;
        }
        
        // Raccourcis sans modificateurs
        switch (e.Key)
        {
            case Key.Tab:
                // Accepter la suggestion fantôme (ghost suggestion)
                if (_viewModel.AcceptGhostSuggestion())
                {
                    e.Handled = true;
                }
                break;
            
            case Key.Right:
                // Accepter la suggestion fantôme avec flèche droite (quand le caret est en fin de texte)
                if (SearchBox.CaretIndex == SearchBox.Text.Length 
                    && !string.IsNullOrEmpty(_viewModel.GhostSuggestionText))
                {
                    _viewModel.AcceptGhostSuggestion();
                    e.Handled = true;
                }
                break;
                
            case Key.Escape:
                HideWindow();
                e.Handled = true;
                break;
                
            case Key.Enter:
                _viewModel.ExecuteCommand.Execute(null);
                e.Handled = true;
                break;
                
            case Key.Down:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;
                
            case Key.Up:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;
                
            case Key.F2:
                // Renommer
                if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Results.Count)
                {
                    var item = _viewModel.Results[_viewModel.SelectedIndex];
                    if (item.Type is ResultType.File or ResultType.Folder or ResultType.Application)
                    {
                        OnRequestRename(this, item.Path);
                    }
                }
                e.Handled = true;
                break;
                
            case Key.Delete:
                // Supprimer
                if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Results.Count)
                {
                    var item = _viewModel.Results[_viewModel.SelectedIndex];
                    if (item.Type is ResultType.File or ResultType.Folder)
                    {
                        ConfirmAndDelete(item);
                    }
                    else if (item.Type == ResultType.Note && item.Path.StartsWith(":note:id:"))
                    {
                        // Supprimer la note
                        if (int.TryParse(item.Path[9..], out var noteId))
                        {
                            _notesService.DeleteNote(noteId);
                            OnShowNotification(this, "🗑️ Note supprimée");
                            _viewModel.ForceRefresh();
                        }
                    }
                }
                e.Handled = true;
                break;
        }
    }
    
    private void ConfirmAndDelete(SearchResult item)
    {
        var result = MessageBox.Show(
            $"Voulez-vous vraiment supprimer '{item.Name}' ?\n\nLe fichier sera envoyé à la corbeille.",
            "Confirmer la suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            var success = FileActionsService.DeleteToRecycleBin(item.Path);
            if (success)
            {
                RequestReindex?.Invoke(this, EventArgs.Empty);
                HideWindow();
            }
            else
            {
                MessageBox.Show("Impossible de supprimer le fichier.", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Pendant un hide animé, Deactivated peut fire → on ignore
        if (_isAnimatingHide) return;
        HideWindow();
    }
    
    private void HideWindow()
    {
        if (!IsVisible || _isAnimatingHide) return;
        PlayHideAnimation();
    }
    
    /// <summary>
    /// Masque la fenêtre immédiatement sans animation.
    /// Utilisé quand on ouvre un dialog modal (Settings) ou quand on doit être synchrone.
    /// </summary>
    private void HideWindowImmediate()
    {
        // Invalider tout hide animé en cours
        _hideGeneration++;
        _isAnimatingHide = false;
        
        SaveWindowPosition();
        ClearAllAnimations();
        ResetToHiddenState();
        MainBorder.CacheMode = null;
        
        _viewModel.Reset();
        Hide();
    }
    
    private void SaveWindowPosition()
    {
        if (_settings.Appearance.WindowPosition == "Remember")
        {
            _settingsProvider.Update(s =>
            {
                s.Appearance.LastWindowLeft = Left;
                s.Appearance.LastWindowTop = Top;
            });
        }
    }
    
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HideWindowImmediate();
        RequestOpenSettings?.Invoke(this, EventArgs.Empty);
    }
    
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SearchText = string.Empty;
        SearchBox.Focus();
    }
    
    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.SingleClickLaunch)
        {
            _viewModel.ExecuteCommand.Execute(null);
        }
    }
    
    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Lancement en clic simple si activé
        if (_settings.SingleClickLaunch && ResultsList.SelectedItem != null)
        {
            // Vérifier qu'on a bien cliqué sur un item (pas sur le scrollbar)
            var item = ItemsControl.ContainerFromElement(ResultsList, (DependencyObject)e.OriginalSource) as ListBoxItem;
            if (item != null)
            {
                _viewModel.ExecuteCommand.Execute(null);
            }
        }
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Forcer le rendu hardware pour cette fenêtre (contourne le software rendering d'AllowsTransparency)
        var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource?.CompositionTarget != null)
            hwndSource.CompositionTarget.RenderMode = RenderMode.Default;
        
        CenterOnScreen();
    }
    
    private void CenterOnScreen()
    {
        var workArea = SystemParameters.WorkArea; // Zone sans la barre des tâches
        var windowWidth = Width;
        var windowHeight = ActualHeight > 0 ? ActualHeight : 150;
        // Marge de sécurité en bas pour ne jamais coller à la barre des tâches
        const double bottomMargin = 10;
        
        switch (_settings.Appearance.WindowPosition)
        {
            case "Remember" when _settings.Appearance.LastWindowLeft.HasValue && _settings.Appearance.LastWindowTop.HasValue:
                var left = _settings.Appearance.LastWindowLeft.Value;
                var top = _settings.Appearance.LastWindowTop.Value;
                
                if (left >= workArea.Left && left + windowWidth <= workArea.Right &&
                    top >= workArea.Top && top + windowHeight <= workArea.Bottom)
                {
                    Left = left;
                    Top = top;
                }
                else
                {
                    goto default;
                }
                break;
                
            case "Top":
                Left = workArea.Left + (workArea.Width - windowWidth) / 2;
                Top = workArea.Top + 60;
                break;
                
            default:
                Left = workArea.Left + (workArea.Width - windowWidth) / 2;
                Top = workArea.Top + workArea.Height / 4;
                break;
        }
        
        // Limiter MaxHeight dynamiquement pour ne jamais dépasser la zone de travail.
        // La fenêtre utilise SizeToContent=Height, donc WPF la redimensionne automatiquement
        // en respectant MaxHeight. Le Grid.Margin="20" ajoute 40px (ombre).
        var availableHeight = workArea.Bottom - Top - bottomMargin;
        MaxHeight = Math.Max(200, availableHeight);
    }

    #region Context Menu Handlers

    /// <summary>
    /// Génère dynamiquement le menu contextuel en fonction du résultat sélectionné.
    /// </summary>
    private void ResultContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu)
            return;
        
        contextMenu.Items.Clear();
        
        // Récupérer le SearchResult depuis le ListBoxItem
        if (contextMenu.PlacementTarget is not ListBoxItem listBoxItem ||
            listBoxItem.DataContext is not SearchResult result)
            return;
        
        // Obtenir les actions disponibles pour ce résultat
        var isPinned = _settings.Search.IsPinned(result.Path);
        var actions = FileActionProvider.GetActionsForResult(result, isPinned);
        
        // Créer le style pour les items
        var menuItemStyle = (Style)FindResource("DarkMenuItemStyle");
        
        int? lastCategory = null;
        
        foreach (var action in actions)
        {
            // Ajouter un séparateur entre les catégories
            var currentCategory = GetActionCategory(action.ActionType);
            if (lastCategory.HasValue && currentCategory != lastCategory.Value)
            {
                contextMenu.Items.Add(new Separator { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x3E, 0x3E)) });
            }
            lastCategory = currentCategory;
            
            var menuItem = new MenuItem
            {
                Header = $"{action.Icon} {action.Name}",
                Style = menuItemStyle,
                InputGestureText = action.Shortcut,
                Tag = action
            };
            
            // Couleur spéciale pour Supprimer
            if (action.ActionType == FileActionType.Delete)
            {
                menuItem.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
            }
            
            menuItem.Click += (s, args) =>
            {
                if (s is MenuItem mi && mi.Tag is FileAction fileAction)
                {
                    ExecuteContextAction(fileAction, result);
                }
            };
            
            contextMenu.Items.Add(menuItem);
        }
    }
    
    /// <summary>
    /// Détermine la catégorie d'une action pour le regroupement dans le menu.
    /// Retourne un int pour regrouper les actions visuellement avec des séparateurs.
    /// </summary>
    private static int GetActionCategory(FileActionType actionType)
    {
        return actionType switch
        {
            // Groupe 1: Lancement
            FileActionType.Open or FileActionType.OpenWith or FileActionType.RunAsAdmin 
                or FileActionType.OpenPrivate or FileActionType.EditInEditor => 1,
            // Groupe 2: Navigation
            FileActionType.OpenLocation or FileActionType.OpenInExplorer 
                or FileActionType.OpenInTerminal or FileActionType.OpenInVSCode => 2,
            // Groupe 3: Presse-papiers
            FileActionType.CopyPath or FileActionType.CopyName or FileActionType.CopyUrl => 3,
            // Groupe 4: Opérations
            FileActionType.Compress or FileActionType.SendByEmail => 4,
            // Groupe 5: Modification
            FileActionType.Rename or FileActionType.Delete or FileActionType.Properties => 5,
            // Groupe 6: Épingles
            FileActionType.Pin or FileActionType.Unpin => 6,
            _ => 0
        };
    }
    
    /// <summary>
    /// Exécute une action du menu contextuel.
    /// </summary>
    private void ExecuteContextAction(FileAction action, SearchResult result)
    {
        // Cas spécial pour Rename
        if (action.ActionType == FileActionType.Rename)
        {
            OnRequestRename(this, result.Path);
            return;
        }
        
        // Cas spécial pour Delete avec confirmation
        if (action.ActionType == FileActionType.Delete)
        {
            ConfirmAndDelete(result);
            return;
        }
        
        // Cas spécial pour Pin
        if (action.ActionType == FileActionType.Pin)
        {
            _settings.Search.PinItem(result.Name, result.Path, result.Type, result.DisplayIcon);
            _settingsProvider.Save();
            OnShowNotification(this, "⭐ Épinglé");
            return;
        }
        
        // Cas spécial pour Unpin
        if (action.ActionType == FileActionType.Unpin)
        {
            _settings.Search.UnpinItem(result.Path);
            _settingsProvider.Save();
            OnShowNotification(this, "📌 Désépinglé");
            // Rafraîchir si on était dans la vue des épingles
            if (string.IsNullOrWhiteSpace(_viewModel.SearchText))
            {
                _viewModel.Reset();
            }
            return;
        }
        
        // Exécuter l'action
        var success = action.Execute(result.Path);
        
        if (success)
        {
            // Notification de succès selon l'action
            var message = action.ActionType switch
            {
                FileActionType.CopyUrl => "🔗 URL copiée",
                FileActionType.CopyPath => "📋 Chemin copié",
                FileActionType.CopyName => "📋 Nom copié",
                FileActionType.Compress => "🗜️ Archive ZIP créée",
                FileActionType.SendByEmail => "📧 Email en cours de création...",
                _ => null
            };
            
            if (message != null)
                OnShowNotification(this, message);
            
            // Fermer après certaines actions de lancement
            if (action.ActionType is FileActionType.Open 
                or FileActionType.RunAsAdmin 
                or FileActionType.OpenPrivate
                or FileActionType.OpenInTerminal
                or FileActionType.OpenInVSCode
                or FileActionType.OpenWith
                or FileActionType.EditInEditor
                or FileActionType.OpenLocation
                or FileActionType.OpenInExplorer
                or FileActionType.SendByEmail)
            {
                HideWindow();
            }
        }
        else
        {
            // Notification d'échec pour les actions qui ne sont pas gérées par l'UI
            if (action.ActionType == FileActionType.OpenInVSCode)
                OnShowNotification(this, "❌ VS Code introuvable");
        }
    }

    #endregion
}
