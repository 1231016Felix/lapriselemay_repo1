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
    double LightPercentage
);

/// <summary>
/// Service d'analyse de luminosité des images.
/// 
/// Algorithme v2 — Améliorations vs la v1 (seuil brut à 127.5) :
///
///   1. Lightness perceptuelle CIE L* (0-100) au lieu de la luminance brute (0-255).
///      La luminance brute surestime l'obscurité des tons moyens; L* est conçu
///      pour correspondre à la perception humaine (norme ISO 11664-4).
///
///   2. Pondération par zones : le tiers supérieur de l'image (ciel / arrière-plan)
///      pèse 1.5x plus que le tiers inférieur (sol / premier plan). Un paysage avec
///      un ciel bleu lumineux et un sol rocheux sombre est perçu comme "clair" par
///      un humain, meme si la majorité brute des pixels est sous le seuil.
///
///   3. "Bright sky rescue" : si la moyenne pondérée est juste sous le seuil mais
///      que la zone haute est clairement lumineuse (ciel dégagé, coucher de soleil),
///      l'image est reclassée comme claire -- ce qui évite les faux "sombres" sur les
///      paysages de jour.
/// </summary>
public static class ImageBrightnessAnalyzer
{
    // ──────────────────── Seuils de classification (echelle CIE L* : 0-100) ────────────────────

    /// <summary>
    /// Seuil L* par pixel pour compter dark vs light.
    /// L*=50 est le gris moyen perceptuel ; 42 compense le biais humain
    /// qui percoit les scenes de jour comme "claires" meme si le sol est sombre.
    /// </summary>
    private const double PixelLightnessThreshold = 42.0;

    /// <summary>
    /// Seuil L* pondere pour la categorie globale.
    /// En dessous = Sombre, au dessus = Clair.
    /// </summary>
    private const double CategoryLightnessThreshold = 45.0;

    /// <summary>
    /// Seuil L* de la zone haute pour le "bright sky rescue" :
    /// si le ciel est clairement lumineux, l'image est percue comme claire
    /// meme si la moyenne ponderee est legerement sous le seuil.
    /// </summary>
    private const double BrightSkyThreshold = 55.0;

    /// <summary>
    /// Marge sous le seuil principal dans laquelle le bright sky rescue s'applique.
    /// </summary>
    private const double BrightSkyRescueMargin = 6.0;

    // ──────────────────── Poids par zone (image divisee en tiers) ────────────────────
    private const double TopZoneWeight = 1.5;     // Ciel / arriere-plan
    private const double MiddleZoneWeight = 1.0;   // Sujet principal
    private const double BottomZoneWeight = 0.7;   // Sol / premier plan

    // Taille d'echantillonnage pour performance
    private const int SampleWidth = 100;
    private const int SampleHeight = 100;

    /// <summary>
    /// Analyse la luminosite d'une image.
    /// </summary>
    /// <param name="filePath">Chemin vers l'image</param>
    /// <returns>Resultat de l'analyse ou null si erreur</returns>
    public static BrightnessAnalysisResult? Analyze(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            // Charger l'image avec mise a l'echelle pour performance
            var bitmap = LoadAndResizeImage(filePath);
            if (bitmap == null)
                return null;

            // Convertir en format accessible (BGRA, 4 octets par pixel)
            var formatConverted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);

            var width = formatConverted.PixelWidth;
            var height = formatConverted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[height * stride];

            formatConverted.CopyPixels(pixels, stride, 0);

            // Limites des zones (tiers horizontaux)
            int topEnd = Math.Max(1, height / 3);
            int midEnd = Math.Max(topEnd + 1, height * 2 / 3);

            double totalRawBrightness = 0;
            double weightedLightnessSum = 0;
            double totalWeight = 0;
            double topZoneLightnessSum = 0;
            int topZonePixelCount = 0;
            int darkPixels = 0;
            int lightPixels = 0;
            int totalPixels = width * height;

            for (int y = 0; y < height; y++)
            {
                bool isTopZone = y < topEnd;
                double zoneWeight;

                if (isTopZone)
                    zoneWeight = TopZoneWeight;
                else if (y < midEnd)
                    zoneWeight = MiddleZoneWeight;
                else
                    zoneWeight = BottomZoneWeight;

                int rowStart = y * stride;

                for (int x = 0; x < width; x++)
                {
                    int i = rowStart + x * 4;
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    // byte a = pixels[i + 3]; // Alpha non utilise

                    // Luminance brute (ITU-R BT.601) — conservee pour affichage
                    double rawBrightness = 0.299 * r + 0.587 * g + 0.114 * b;
                    totalRawBrightness += rawBrightness;

                    // Lightness perceptuelle CIE L* (0-100)
                    double lightness = RgbToPerceivedLightness(r, g, b);

                    weightedLightnessSum += lightness * zoneWeight;
                    totalWeight += zoneWeight;

                    if (isTopZone)
                    {
                        topZoneLightnessSum += lightness;
                        topZonePixelCount++;
                    }

                    if (lightness < PixelLightnessThreshold)
                        darkPixels++;
                    else
                        lightPixels++;
                }
            }

            double avgRawBrightness = totalRawBrightness / totalPixels;
            double weightedAvgLightness = weightedLightnessSum / totalWeight;
            double topZoneAvgLightness = topZonePixelCount > 0
                ? topZoneLightnessSum / topZonePixelCount
                : weightedAvgLightness;
            double darkPct = (double)darkPixels / totalPixels * 100;
            double lightPct = (double)lightPixels / totalPixels * 100;

            // Determiner la categorie avec l'algorithme ameliore
            var category = DetermineCategory(weightedAvgLightness, topZoneAvgLightness);

            return new BrightnessAnalysisResult(
                avgRawBrightness,
                category,
                darkPct,
                lightPct
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur analyse luminosite: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Analyse asynchrone de la luminosite.
    /// </summary>
    public static Task<BrightnessAnalysisResult?> AnalyzeAsync(string filePath)
    {
        return Task.Run(() => Analyze(filePath));
    }

    /// <summary>
    /// Analyse plusieurs images en parallele.
    /// </summary>
    public static async Task<Dictionary<string, BrightnessAnalysisResult>> AnalyzeBatchAsync(
        IEnumerable<string> filePaths,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, BrightnessAnalysisResult>();
        var paths = filePaths.ToList();
        var processed = 0;

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

    // ──────────────────── Classification ────────────────────

    /// <summary>
    /// Determine la categorie en fonction de la lightness perceptuelle ponderee par zones.
    /// 
    /// Logique :
    ///   1. Si la L* moyenne ponderee >= seuil → Clair
    ///   2. Sinon, si la L* est juste sous le seuil ET le ciel est lumineux → Clair
    ///      (Bright sky rescue : evite les faux "sombres" sur les paysages de jour)
    ///   3. Sinon → Sombre
    /// </summary>
    private static BrightnessCategory DetermineCategory(
        double weightedAvgLightness, double topZoneAvgLightness)
    {
        // 1. Clairement au-dessus du seuil → Clair
        if (weightedAvgLightness >= CategoryLightnessThreshold)
            return BrightnessCategory.Light;

        // 2. Bright sky rescue : juste sous le seuil + ciel lumineux → Clair
        //    Cas typique : paysage avec ciel bleu/rose et sol sombre (foret, rochers)
        if (weightedAvgLightness >= CategoryLightnessThreshold - BrightSkyRescueMargin
            && topZoneAvgLightness >= BrightSkyThreshold)
        {
            return BrightnessCategory.Light;
        }

        // 3. Sinon → Sombre
        return BrightnessCategory.Dark;
    }

    // ──────────────────── Conversion colorimetrique ────────────────────

    /// <summary>
    /// Convertit une couleur sRGB en lightness perceptuelle CIE L* (0-100).
    /// Formule : linearisation sRGB → luminance relative Y (BT.709) → CIE L*.
    /// </summary>
    private static double RgbToPerceivedLightness(byte r, byte g, byte b)
    {
        // Lineariser les canaux sRGB (inverse du gamma sRGB)
        double rLin = SrgbToLinear(r / 255.0);
        double gLin = SrgbToLinear(g / 255.0);
        double bLin = SrgbToLinear(b / 255.0);

        // Luminance relative Y (coefficients BT.709, corrects pour sRGB)
        double y = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;

        // CIE L* depuis Y (illuminant D65, Yn = 1.0)
        return y > 0.008856
            ? 116.0 * Math.Cbrt(y) - 16.0
            : 903.3 * y;
    }

    /// <summary>
    /// Inverse du gamma sRGB (sRGB compande → lineaire).
    /// </summary>
    private static double SrgbToLinear(double c)
    {
        return c <= 0.04045
            ? c / 12.92
            : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    // ──────────────────── Chargement image ────────────────────

    /// <summary>
    /// Charge et redimensionne une image pour l'analyse.
    /// </summary>
    private static BitmapSource? LoadAndResizeImage(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
                return null;

            var frame = decoder.Frames[0];

            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
                return null;

            double scaleX = (double)SampleWidth / frame.PixelWidth;
            double scaleY = (double)SampleHeight / frame.PixelHeight;
            double scale = Math.Min(scaleX, scaleY);

            if (scale >= 1)
            {
                var frozen = BitmapFrame.Create(frame);
                frozen.Freeze();
                return frozen;
            }

            var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            var cached = new WriteableBitmap(scaled);
            cached.Freeze();

            return cached;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur chargement image {filePath}: {ex.Message}");
            return null;
        }
    }

    // ──────────────────── Utilitaires UI ────────────────────

    /// <summary>
    /// Obtient le nom de la categorie en francais.
    /// </summary>
    public static string GetCategoryName(BrightnessCategory category) => category switch
    {
        BrightnessCategory.Dark => "Sombre",
        BrightnessCategory.Light => "Clair",
        _ => "Inconnu"
    };

    /// <summary>
    /// Obtient l'icone de la categorie.
    /// </summary>
    public static string GetCategoryIcon(BrightnessCategory category) => category switch
    {
        BrightnessCategory.Dark => "🌙",
        BrightnessCategory.Light => "☀️",
        _ => "❓"
    };
}
