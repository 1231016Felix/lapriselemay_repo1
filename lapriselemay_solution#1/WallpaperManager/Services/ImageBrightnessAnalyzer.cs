using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Résultat de l'analyse de luminosité.
/// </summary>
public record BrightnessAnalysisResult(
    double AverageBrightness,
    BrightnessCategory Category,
    double DarkPercentage,
    double NeutralPercentage,
    double LightPercentage
);

/// <summary>
/// Service d'analyse de luminosité des images.
/// Utilise l'algorithme de luminance perçue (formule ITU-R BT.601).
/// </summary>
public static class ImageBrightnessAnalyzer
{
    // Seuils de luminosité (0-255)
    private const double DarkThreshold = 85;
    private const double LightThreshold = 170;
    
    // Taille d'échantillonnage pour performance
    private const int SampleWidth = 100;
    private const int SampleHeight = 100;
    
    /// <summary>
    /// Analyse la luminosité d'une image.
    /// </summary>
    /// <param name="filePath">Chemin vers l'image</param>
    /// <returns>Résultat de l'analyse ou null si erreur</returns>
    public static BrightnessAnalysisResult? Analyze(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;
            
            // Charger l'image avec mise à l'échelle pour performance
            var bitmap = LoadAndResizeImage(filePath);
            if (bitmap == null)
                return null;
            
            // Convertir en format accessible
            var formatConverted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            
            var width = formatConverted.PixelWidth;
            var height = formatConverted.PixelHeight;
            var stride = width * 4; // 4 bytes per pixel (BGRA)
            var pixels = new byte[height * stride];
            
            formatConverted.CopyPixels(pixels, stride, 0);
            
            // Analyser les pixels
            double totalBrightness = 0;
            int darkPixels = 0;
            int neutralPixels = 0;
            int lightPixels = 0;
            int totalPixels = width * height;
            
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                // byte a = pixels[i + 3]; // Alpha non utilisé
                
                // Luminance perçue (formule ITU-R BT.601)
                double brightness = 0.299 * r + 0.587 * g + 0.114 * b;
                totalBrightness += brightness;
                
                if (brightness < DarkThreshold)
                    darkPixels++;
                else if (brightness > LightThreshold)
                    lightPixels++;
                else
                    neutralPixels++;
            }
            
            double avgBrightness = totalBrightness / totalPixels;
            double darkPct = (double)darkPixels / totalPixels * 100;
            double neutralPct = (double)neutralPixels / totalPixels * 100;
            double lightPct = (double)lightPixels / totalPixels * 100;
            
            // Déterminer la catégorie
            var category = DetermineCategory(avgBrightness, darkPct, lightPct);
            
            return new BrightnessAnalysisResult(
                avgBrightness,
                category,
                darkPct,
                neutralPct,
                lightPct
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur analyse luminosité: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Analyse asynchrone de la luminosité.
    /// </summary>
    public static Task<BrightnessAnalysisResult?> AnalyzeAsync(string filePath)
    {
        return Task.Run(() => Analyze(filePath));
    }
    
    /// <summary>
    /// Analyse plusieurs images en parallèle.
    /// </summary>
    public static async Task<Dictionary<string, BrightnessAnalysisResult>> AnalyzeBatchAsync(
        IEnumerable<string> filePaths,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, BrightnessAnalysisResult>();
        var paths = filePaths.ToList();
        var processed = 0;
        
        // Limiter la parallélisation pour ne pas surcharger
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = cancellationToken
        };
        
        await Parallel.ForEachAsync(paths, options, async (path, ct) =>
        {
            var result = await AnalyzeAsync(path);
            if (result != null)
            {
                lock (results)
                {
                    results[path] = result;
                }
            }
            
            var current = Interlocked.Increment(ref processed);
            progress?.Report(current * 100 / paths.Count);
        });
        
        return results;
    }
    
    /// <summary>
    /// Détermine la catégorie en fonction de la luminosité moyenne et de la distribution.
    /// </summary>
    private static BrightnessCategory DetermineCategory(double avgBrightness, double darkPct, double lightPct)
    {
        // Méthode hybride : luminosité moyenne + distribution dominante
        
        // Si plus de 60% des pixels sont d'une catégorie, c'est cette catégorie
        if (darkPct >= 60)
            return BrightnessCategory.Dark;
        if (lightPct >= 60)
            return BrightnessCategory.Light;
        
        // Sinon, utiliser la luminosité moyenne
        if (avgBrightness < DarkThreshold)
            return BrightnessCategory.Dark;
        if (avgBrightness > LightThreshold)
            return BrightnessCategory.Light;
        
        return BrightnessCategory.Neutral;
    }
    
    /// <summary>
    /// Charge et redimensionne une image pour l'analyse.
    /// </summary>
    private static BitmapSource? LoadAndResizeImage(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            
            var frame = decoder.Frames[0];
            
            // Calculer le facteur de réduction
            double scaleX = (double)SampleWidth / frame.PixelWidth;
            double scaleY = (double)SampleHeight / frame.PixelHeight;
            double scale = Math.Min(scaleX, scaleY);
            
            if (scale >= 1)
                return frame; // Image déjà petite
            
            // Redimensionner
            var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            
            // Forcer le chargement en mémoire
            var cached = new WriteableBitmap(scaled);
            cached.Freeze();
            
            return cached;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Obtient le nom de la catégorie en français.
    /// </summary>
    public static string GetCategoryName(BrightnessCategory category) => category switch
    {
        BrightnessCategory.Dark => "Sombre",
        BrightnessCategory.Light => "Clair",
        BrightnessCategory.Neutral => "Neutre",
        _ => "Inconnu"
    };
    
    /// <summary>
    /// Obtient l'icône de la catégorie.
    /// </summary>
    public static string GetCategoryIcon(BrightnessCategory category) => category switch
    {
        BrightnessCategory.Dark => "🌙",
        BrightnessCategory.Light => "☀️",
        BrightnessCategory.Neutral => "⚖️",
        _ => "❓"
    };
}
