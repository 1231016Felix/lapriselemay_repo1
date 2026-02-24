using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace QuickLauncher.Services;

/// <summary>
/// Service de capture d'écran extrait de LauncherWindow (Point #3).
/// Encapsule les opérations bitmap (capture, sauvegarde, conversion WPF)
/// qui n'ont aucune dépendance UI directe.
/// 
/// Les dialogues interactifs (overlay de sélection, annotateur) restent
/// dans le code-behind car ils nécessitent des fenêtres WPF modales.
/// </summary>
public sealed class ScreenCaptureService
{
    private readonly ILogger _logger;

    public ScreenCaptureService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Capture l'écran complet ou l'écran principal uniquement.
    /// </summary>
    /// <param name="primaryOnly">True pour capturer uniquement l'écran principal.</param>
    /// <returns>Le bitmap capturé, ou null en cas d'erreur. L'appelant doit disposer le bitmap.</returns>
    public Bitmap? CaptureScreen(bool primaryOnly)
    {
        try
        {
            var bounds = primaryOnly
                ? System.Windows.Forms.Screen.PrimaryScreen!.Bounds
                : System.Windows.Forms.Screen.AllScreens
                    .Select(s => s.Bounds)
                    .Aggregate(Rectangle.Union);

            var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            return bitmap;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur capture d'écran: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sauvegarde un bitmap en PNG dans le dossier Screenshots de l'utilisateur.
    /// </summary>
    /// <returns>Le chemin complet du fichier sauvegardé, ou null en cas d'erreur.</returns>
    public string? SaveScreenshot(Bitmap bitmap)
    {
        try
        {
            var screenshotsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Screenshots");
            Directory.CreateDirectory(screenshotsFolder);

            var fileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            var filePath = Path.Combine(screenshotsFolder, fileName);

            bitmap.Save(filePath, ImageFormat.Png);
            _logger.Info($"Capture sauvegardée: {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Erreur sauvegarde capture: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Charge un fichier image en BitmapSource WPF (frozen, thread-safe).
    /// Utilisé pour copier la capture dans le presse-papier.
    /// </summary>
    public static BitmapSource? LoadBitmapSource(string filePath)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
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
}
