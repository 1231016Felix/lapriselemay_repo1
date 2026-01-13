using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Alias pour éviter les conflits avec System.Drawing
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WallpaperManager.Services;

/// <summary>
/// Service de prévisualisation live des fonds d'écran.
/// Permet de voir un aperçu du fond d'écran sans l'appliquer définitivement.
/// </summary>
public sealed class WallpaperPreviewService : IDisposable
{
    #region Native Methods

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    #endregion

    private Window? _previewWindow;
    private string? _originalWallpaper;
    private bool _isPreviewActive;
    private bool _disposed;

    public event EventHandler? PreviewStarted;
    public event EventHandler? PreviewEnded;

    public bool IsPreviewActive => _isPreviewActive;

    /// <summary>
    /// Démarre une prévisualisation du fond d'écran spécifié.
    /// </summary>
    /// <param name="imagePath">Chemin vers l'image à prévisualiser</param>
    /// <param name="useOverlay">Si true, utilise une fenêtre overlay au lieu de changer le fond</param>
    public void StartPreview(string imagePath, bool useOverlay = true)
    {
        if (_isPreviewActive || string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath))
            return;

        _isPreviewActive = true;

        if (useOverlay)
        {
            StartOverlayPreview(imagePath);
        }
        else
        {
            StartSystemPreview(imagePath);
        }

        PreviewStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Arrête la prévisualisation et restaure le fond d'écran original.
    /// </summary>
    public void StopPreview()
    {
        if (!_isPreviewActive) return;

        _isPreviewActive = false;

        // Fermer la fenêtre overlay si elle existe
        if (_previewWindow != null)
        {
            _previewWindow.Close();
            _previewWindow = null;
        }

        // Restaurer le fond d'écran original si nécessaire
        if (!string.IsNullOrEmpty(_originalWallpaper))
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, _originalWallpaper, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            _originalWallpaper = null;
        }

        PreviewEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applique définitivement le fond d'écran prévisualisé.
    /// </summary>
    /// <param name="imagePath">Chemin vers l'image à appliquer</param>
    /// <returns>True si l'application a réussi</returns>
    public bool ApplyPreviewedWallpaper(string imagePath)
    {
        StopPreview();

        try
        {
            return SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        catch
        {
            return false;
        }
    }

    #region Preview Methods

    /// <summary>
    /// Prévisualisation avec fenêtre overlay transparente
    /// </summary>
    private void StartOverlayPreview(string imagePath)
    {
        try
        {
            // Charger l'image
            BitmapImage? bitmap;
            
            if (WebPService.IsWebPFile(imagePath))
            {
                bitmap = WebPService.LoadWebPImage(imagePath);
            }
            else
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath);
                bitmap.EndInit();
                bitmap.Freeze();
            }

            if (bitmap == null) return;

            // Créer la fenêtre de prévisualisation
            _previewWindow = new Window
            {
                Title = "Wallpaper Preview",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = MediaBrushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                WindowState = WindowState.Maximized,
                ResizeMode = ResizeMode.NoResize
            };

            // Ajouter un indicateur visuel de prévisualisation
            var grid = new System.Windows.Controls.Grid();
            
            // Image de fond
            var imageControl = new System.Windows.Controls.Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill
            };
            grid.Children.Add(imageControl);

            // Badge "PRÉVISUALISATION" semi-transparent
            var previewBadge = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(MediaColor.FromArgb(180, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20, 10, 20, 10),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 50, 0, 0),
                Child = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    Children =
                    {
                        new System.Windows.Controls.TextBlock
                        {
                            Text = "PRÉVISUALISATION",
                            FontSize = 24,
                            FontWeight = FontWeights.Bold,
                            Foreground = MediaBrushes.White,
                            HorizontalAlignment = WpfHorizontalAlignment.Center
                        },
                        new System.Windows.Controls.TextBlock
                        {
                            Text = "Appuyez sur Échap pour annuler, Entrée pour appliquer",
                            FontSize = 14,
                            Foreground = new SolidColorBrush(MediaColor.FromRgb(200, 200, 200)),
                            HorizontalAlignment = WpfHorizontalAlignment.Center,
                            Margin = new Thickness(0, 5, 0, 0)
                        }
                    }
                }
            };
            grid.Children.Add(previewBadge);

            _previewWindow.Content = grid;

            // Gérer les raccourcis clavier
            _previewWindow.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    StopPreview();
                }
                else if (e.Key == System.Windows.Input.Key.Enter)
                {
                    ApplyPreviewedWallpaper(imagePath);
                }
            };

            // Fermer si on clique en dehors
            _previewWindow.MouseDown += (s, e) =>
            {
                if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                {
                    // Double-clic pour appliquer
                    if (e.ClickCount >= 2)
                    {
                        ApplyPreviewedWallpaper(imagePath);
                    }
                }
                else if (e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
                {
                    StopPreview();
                }
            };

            // Auto-fermeture après un délai (optionnel)
            // Décommenter pour activer:
            // var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            // timer.Tick += (s, e) => { timer.Stop(); StopPreview(); };
            // timer.Start();

            _previewWindow.Show();
        }
        catch
        {
            _isPreviewActive = false;
        }
    }

    /// <summary>
    /// Prévisualisation en changeant temporairement le fond d'écran système
    /// </summary>
    private void StartSystemPreview(string imagePath)
    {
        try
        {
            // Sauvegarder le fond d'écran actuel
            var currentWallpaper = new System.Text.StringBuilder(260);
            // Note: Cette méthode nécessite P/Invoke pour récupérer le chemin actuel
            // Pour simplifier, on utilise le registre
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            _originalWallpaper = key?.GetValue("WallPaper")?.ToString();

            // Appliquer le nouveau fond temporairement
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_SENDCHANGE);
        }
        catch
        {
            _isPreviewActive = false;
        }
    }

    #endregion

    #region Thumbnail Preview

    /// <summary>
    /// Génère une prévisualisation miniature avec le fond d'écran actuel en arrière-plan
    /// </summary>
    /// <param name="imagePath">Chemin vers l'image à prévisualiser</param>
    /// <param name="width">Largeur de la miniature</param>
    /// <param name="height">Hauteur de la miniature</param>
    /// <returns>Image de prévisualisation composite</returns>
    public BitmapSource? GenerateThumbnailPreview(string imagePath, int width = 400, int height = 225)
    {
        if (!System.IO.File.Exists(imagePath)) return null;

        try
        {
            BitmapImage? bitmap;
            
            if (WebPService.IsWebPFile(imagePath))
            {
                bitmap = WebPService.CreateThumbnail(imagePath, width, height);
            }
            else
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = width;
                bitmap.UriSource = new Uri(imagePath);
                bitmap.EndInit();
                bitmap.Freeze();
            }

            if (bitmap == null) return null;

            // Créer une prévisualisation avec cadre "écran"
            var drawingVisual = new DrawingVisual();
            using (var context = drawingVisual.RenderOpen())
            {
                // Fond (simule un écran)
                context.DrawRectangle(MediaBrushes.Black, null, new Rect(0, 0, width + 20, height + 40));
                
                // Cadre de l'écran
                context.DrawRectangle(
                    new SolidColorBrush(MediaColor.FromRgb(50, 50, 50)),
                    new MediaPen(MediaBrushes.Gray, 2),
                    new Rect(8, 8, width + 4, height + 4));

                // Image
                context.DrawImage(bitmap, new Rect(10, 10, width, height));

                // Base de l'écran (simule un pied de moniteur)
                var baseRect = new Rect(width / 2 - 30, height + 15, 80, 20);
                context.DrawRectangle(
                    new SolidColorBrush(MediaColor.FromRgb(60, 60, 60)),
                    null,
                    baseRect);
            }

            var renderBitmap = new RenderTargetBitmap(
                width + 20, height + 40, 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(drawingVisual);
            renderBitmap.Freeze();

            return renderBitmap;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopPreview();
    }
}
