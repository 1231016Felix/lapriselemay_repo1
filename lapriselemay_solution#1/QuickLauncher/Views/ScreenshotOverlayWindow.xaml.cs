using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuickLauncher.Services;
using Point = System.Windows.Point;
using Screen = System.Windows.Forms.Screen;
using Rectangle = System.Drawing.Rectangle;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Canvas = System.Windows.Controls.Canvas;

namespace QuickLauncher.Views;

/// <summary>
/// Overlay plein écran pour la capture de région.
/// Capture l'écran avant de s'afficher, puis permet à l'utilisateur
/// de dessiner un rectangle de sélection.
/// </summary>
public partial class ScreenshotOverlayWindow : Window
{
    private Point _startPoint;
    private bool _isDragging;
    private Bitmap? _fullScreenBitmap;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;
    // Bounds physiques de l'écran virtuel au moment de la capture.
    // Stockés pour pouvoir retraduire correctement les coordonnées WPF (DIP)
    // en coordonnées pixel du bitmap, peu importe les offsets multi-écrans.
    private Rectangle _virtualScreenBounds;

    /// <summary>
    /// Image capturée de la région sélectionnée (null si annulé).
    /// </summary>
    public Bitmap? CapturedRegion { get; private set; }

    public ScreenshotOverlayWindow()
    {
        // Capturer l'écran AVANT d'afficher l'overlay
        CaptureFullScreen();
        
        InitializeComponent();
        
        Loaded += OnLoaded;
    }

    private void CaptureFullScreen()
    {
        // S'assurer que le process est DPI-aware AVANT toute mesure d'écran.
        // Le manifest est censé le faire, mais on renforce ici au cas où
        // (lancement depuis le debugger, manifest non appliqué, etc.).
        EnsureDpiAwareness();

        // Récupérer les bounds physiques de l'écran virtuel via Win32 directement.
        // C'est plus fiable que Screen.AllScreens qui peut être affecté par
        // la virtualisation DPI si le process n'est pas correctement DPI-aware.
        int virtualX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int virtualY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int virtualW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int virtualH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        _virtualScreenBounds = new Rectangle(virtualX, virtualY, virtualW, virtualH);

        _fullScreenBitmap = new Bitmap(virtualW, virtualH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(_fullScreenBitmap);
        // CopyFromScreen prend des coordonnées d'écran (pixels physiques sur l'écran virtuel)
        graphics.CopyFromScreen(virtualX, virtualY, 0, 0, new System.Drawing.Size(virtualW, virtualH));
    }

    /// <summary>
    /// Force le process en PerMonitorV2 si possible. No-op si déjà appliqué via manifest.
    /// </summary>
    private static void EnsureDpiAwareness()
    {
        try
        {
            // -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            // L'API n'existe pas (< Windows 10 1703) ou le DPI awareness est déjà figé.
            // Dans les deux cas on continue silencieusement.
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Obtenir le scaling DPI de la fenêtre.
        // Note : sur multi-écran avec DPI hétérogènes, c'est le DPI de l'écran
        // où la fenêtre apparaît. Étant donné qu'on couvre TOUT l'espace virtuel
        // physique, on convertit pixels→DIP avec ce facteur uniforme — ce qui suffit
        // tant que WPF rend correctement (PerMonitorV2 prend le relais pour l'affichage).
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        // Positionner la fenêtre sur tout l'espace virtuel.
        // _virtualScreenBounds est en PIXELS PHYSIQUES (rempli par CaptureFullScreen via Win32).
        // WPF Left/Top/Width/Height attend des DIP → on divise par le scale.
        Left = _virtualScreenBounds.X / _dpiScaleX;
        Top = _virtualScreenBounds.Y / _dpiScaleY;
        Width = _virtualScreenBounds.Width / _dpiScaleX;
        Height = _virtualScreenBounds.Height / _dpiScaleY;

        // Afficher le screenshot capturé comme fond
        if (_fullScreenBitmap != null)
        {
            ScreenshotImage.Source = BitmapToSource(_fullScreenBitmap);
        }

        // Overlay sombre initial (couvre tout)
        UpdateDarkOverlay(null);
        
        Activate();
        Focus();
    }

    #region Mouse Handling

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _isDragging = true;
        InstructionText.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPoint = e.GetPosition(this);
        var rect = GetSelectionRect(_startPoint, currentPoint);

        // Mettre à jour le rectangle de sélection
        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width = Math.Max(1, rect.Width);
        SelectionBorder.Height = Math.Max(1, rect.Height);

        // Mettre à jour l'overlay sombre avec un trou
        UpdateDarkOverlay(rect);

        // Afficher les dimensions en pixels physiques
        var physW = (int)(rect.Width * _dpiScaleX);
        var physH = (int)(rect.Height * _dpiScaleY);
        SizeText.Text = $"{physW} × {physH}";
        SizeLabel.Visibility = Visibility.Visible;

        // Positionner le label sous la sélection
        var labelX = rect.X;
        var labelY = rect.Bottom + 8;
        if (labelY + 30 > ActualHeight)
            labelY = rect.Y - 30;

        Canvas.SetLeft(SizeLabel, labelX);
        Canvas.SetTop(SizeLabel, labelY);
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        var endPoint = e.GetPosition(this);
        var rect = GetSelectionRect(_startPoint, endPoint);

        // Ignorer les sélections trop petites (clic accidentel)
        if (rect.Width < 5 || rect.Height < 5)
        {
            InstructionText.Visibility = Visibility.Visible;
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            UpdateDarkOverlay(null);
            return;
        }

        // Extraire la région en pixels physiques
        CropSelection(rect);
        
        DialogResult = true;
        Close();
    }

    #endregion

    #region Helpers

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CapturedRegion = null;
            DialogResult = false;
            Close();
        }
    }

    private static Rect GetSelectionRect(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var w = Math.Abs(end.X - start.X);
        var h = Math.Abs(end.Y - start.Y);
        return new Rect(x, y, w, h);
    }

    private void CropSelection(Rect dipRect)
    {
        if (_fullScreenBitmap == null) return;

        // dipRect.X et dipRect.Y sont relatifs au coin haut-gauche de la fenêtre WPF.
        // Cette fenêtre couvre TOUT l'écran virtuel (positionnée à _virtualScreenBounds.X/Y
        // en pixels, traduit en DIP dans OnLoaded). Donc dipRect (0,0) correspond au
        // coin haut-gauche du bitmap (qui couvre aussi l'écran virtuel entier).
        // Pas besoin d'ajouter d'offset : le bitmap et la fenêtre partagent la même origine.
        var pixelX = (int)Math.Round(dipRect.X * _dpiScaleX);
        var pixelY = (int)Math.Round(dipRect.Y * _dpiScaleY);
        var pixelW = (int)Math.Round(dipRect.Width * _dpiScaleX);
        var pixelH = (int)Math.Round(dipRect.Height * _dpiScaleY);

        // Clamp aux limites du bitmap
        pixelX = Math.Max(0, Math.Min(pixelX, _fullScreenBitmap.Width - 1));
        pixelY = Math.Max(0, Math.Min(pixelY, _fullScreenBitmap.Height - 1));
        pixelW = Math.Min(pixelW, _fullScreenBitmap.Width - pixelX);
        pixelH = Math.Min(pixelH, _fullScreenBitmap.Height - pixelY);

        if (pixelW <= 0 || pixelH <= 0) return;

        var cropRect = new Rectangle(pixelX, pixelY, pixelW, pixelH);
        // Créer une copie INDÉPENDANTE (pas Clone qui peut partager les données pixel)
        var cropped = new Bitmap(pixelW, pixelH, _fullScreenBitmap.PixelFormat);
        using (var g = Graphics.FromImage(cropped))
        {
            g.DrawImage(_fullScreenBitmap, new Rectangle(0, 0, pixelW, pixelH), cropRect, GraphicsUnit.Pixel);
        }
        CapturedRegion = cropped;
    }

    /// <summary>
    /// Met à jour l'overlay sombre en créant un trou rectangulaire
    /// pour la zone sélectionnée.
    /// </summary>
    private void UpdateDarkOverlay(Rect? selectionRect)
    {
        var fullRect = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));

        if (selectionRect.HasValue && selectionRect.Value.Width > 0 && selectionRect.Value.Height > 0)
        {
            var holeRect = new RectangleGeometry(selectionRect.Value);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, holeRect);
            DarkOverlay.Data = combined;
        }
        else
        {
            DarkOverlay.Data = fullRect;
        }
    }

    private static BitmapSource BitmapToSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }

    protected override void OnClosed(EventArgs e)
    {
        // Ne pas disposer _fullScreenBitmap si CapturedRegion est un clone
        // Le bitmap original peut être libéré
        if (CapturedRegion == null)
            _fullScreenBitmap?.Dispose();
        else
            _fullScreenBitmap?.Dispose();
        
        base.OnClosed(e);
    }

    #endregion
}
