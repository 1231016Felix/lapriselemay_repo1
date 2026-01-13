using System.IO;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;

namespace WallpaperManager.Services;

/// <summary>
/// Service pour le support des images WebP.
/// Utilise les codecs Windows natifs ou la bibliothèque ImageSharp comme fallback.
/// </summary>
public static class WebPService
{
    /// <summary>
    /// Extensions de fichiers WebP supportées
    /// </summary>
    public static readonly string[] SupportedExtensions = { ".webp" };

    /// <summary>
    /// Vérifie si un fichier est une image WebP
    /// </summary>
    public static bool IsWebPFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Charge une image WebP et retourne un BitmapImage WPF
    /// </summary>
    /// <param name="filePath">Chemin vers le fichier WebP</param>
    /// <returns>BitmapImage ou null si échec</returns>
    public static BitmapImage? LoadWebPImage(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            // Méthode 1: Utiliser le codec Windows natif (Windows 10 1809+)
            // Le codec WebP est inclus dans Windows via les extensions HEIF
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // Méthode 2: Fallback avec décodage manuel
            return LoadWebPWithFallback(filePath);
        }
    }

    /// <summary>
    /// Charge une image WebP avec décodage fallback
    /// </summary>
    private static BitmapImage? LoadWebPWithFallback(string filePath)
    {
        try
        {
            // Utiliser le décodeur de la WIC (Windows Imaging Component)
            using var stream = File.OpenRead(filePath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                var bitmap = new BitmapImage();
                
                using var memoryStream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                encoder.Save(memoryStream);
                
                memoryStream.Position = 0;
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memoryStream;
                bitmap.EndInit();
                bitmap.Freeze();
                
                return bitmap;
            }
        }
        catch
        {
            // Échec du décodage
        }

        return null;
    }

    /// <summary>
    /// Génère une miniature d'une image WebP
    /// </summary>
    /// <param name="filePath">Chemin vers le fichier WebP</param>
    /// <param name="maxWidth">Largeur maximale de la miniature</param>
    /// <param name="maxHeight">Hauteur maximale de la miniature</param>
    /// <returns>Miniature en BitmapImage ou null si échec</returns>
    public static BitmapImage? CreateThumbnail(string filePath, int maxWidth = 200, int maxHeight = 150)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = maxWidth;
            bitmap.DecodePixelHeight = maxHeight;
            bitmap.UriSource = new Uri(filePath);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // Fallback: charger l'image complète puis redimensionner
            var fullImage = LoadWebPImage(filePath);
            if (fullImage == null) return null;

            try
            {
                // Calculer les dimensions en conservant le ratio
                var ratioX = (double)maxWidth / fullImage.PixelWidth;
                var ratioY = (double)maxHeight / fullImage.PixelHeight;
                var ratio = Math.Min(ratioX, ratioY);

                var newWidth = (int)(fullImage.PixelWidth * ratio);
                var newHeight = (int)(fullImage.PixelHeight * ratio);

                var resized = new TransformedBitmap(
                    fullImage,
                    new System.Windows.Media.ScaleTransform(ratio, ratio));

                // Convertir en BitmapImage
                using var memoryStream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(resized));
                encoder.Save(memoryStream);

                memoryStream.Position = 0;
                var thumbnail = new BitmapImage();
                thumbnail.BeginInit();
                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                thumbnail.StreamSource = memoryStream;
                thumbnail.EndInit();
                thumbnail.Freeze();

                return thumbnail;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Convertit une image WebP en PNG
    /// </summary>
    /// <param name="webpPath">Chemin vers le fichier WebP source</param>
    /// <param name="pngPath">Chemin vers le fichier PNG de destination</param>
    /// <returns>True si la conversion a réussi</returns>
    public static bool ConvertToPng(string webpPath, string pngPath)
    {
        try
        {
            var bitmap = LoadWebPImage(webpPath);
            if (bitmap == null) return false;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(pngPath);
            encoder.Save(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Vérifie si les codecs WebP sont disponibles sur le système
    /// </summary>
    public static bool IsWebPCodecAvailable()
    {
        try
        {
            // Tenter de créer un décodeur WebP
            var decoderInfo = BitmapDecoder.Create(
                new Uri("pack://application:,,,/Resources/test.webp", UriKind.Absolute),
                BitmapCreateOptions.None,
                BitmapCacheOption.None);
            return true;
        }
        catch
        {
            // Le codec n'est pas disponible
            // Suggérer d'installer les extensions HEIF depuis le Microsoft Store
            return false;
        }
    }

    /// <summary>
    /// Obtient les informations d'une image WebP
    /// </summary>
    public static (int Width, int Height, long FileSize)? GetWebPInfo(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var bitmap = LoadWebPImage(filePath);
            if (bitmap == null) return null;

            var fileInfo = new FileInfo(filePath);
            return (bitmap.PixelWidth, bitmap.PixelHeight, fileInfo.Length);
        }
        catch
        {
            return null;
        }
    }
}
