namespace Shared.Core.Helpers;

/// <summary>
/// Utilitaire pour formater les tailles de fichiers en format lisible.
/// </summary>
public static class SizeFormatter
{
    private static readonly string[] Suffixes = ["o", "Ko", "Mo", "Go", "To", "Po"];
    private static readonly string[] SuffixesEn = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Formate une taille en octets en chaîne lisible (français).
    /// </summary>
    /// <param name="bytes">Taille en octets</param>
    /// <param name="decimals">Nombre de décimales (défaut: 1)</param>
    /// <returns>Chaîne formatée (ex: "1,5 Go")</returns>
    public static string Format(long bytes, int decimals = 1)
    {
        if (bytes <= 0) return "0 o";
        
        var i = 0;
        var size = (double)bytes;
        while (size >= 1024 && i < Suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        
        return $"{size.ToString($"N{decimals}")} {Suffixes[i]}";
    }

    /// <summary>
    /// Formate une taille en octets en chaîne lisible (anglais).
    /// </summary>
    /// <param name="bytes">Taille en octets</param>
    /// <param name="decimals">Nombre de décimales (défaut: 1)</param>
    /// <returns>Chaîne formatée (ex: "1.5 GB")</returns>
    public static string FormatEnglish(long bytes, int decimals = 1)
    {
        if (bytes <= 0) return "0 B";
        
        var i = 0;
        var size = (double)bytes;
        while (size >= 1024 && i < SuffixesEn.Length - 1)
        {
            size /= 1024;
            i++;
        }
        
        return $"{size.ToString($"N{decimals}")} {SuffixesEn[i]}";
    }

    /// <summary>
    /// Formate une taille ou retourne "0 o" si nulle/négative.
    /// </summary>
    public static string FormatOrZero(long bytes) => bytes > 0 ? Format(bytes) : "0 o";

    /// <summary>
    /// Formate une taille ou retourne une chaîne vide si nulle/négative.
    /// </summary>
    public static string FormatOrEmpty(long bytes) => bytes > 0 ? Format(bytes) : string.Empty;

    /// <summary>
    /// Formate une taille ou retourne "Inconnu" si nulle/négative.
    /// </summary>
    public static string FormatOrUnknown(long bytes) => bytes > 0 ? Format(bytes) : "Inconnu";
}
