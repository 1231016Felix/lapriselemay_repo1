namespace TempCleaner.Helpers;

/// <summary>
/// Utilitaires partagés pour éviter la duplication de code
/// </summary>
public static class FileSizeHelper
{
    private static readonly string[] SizeSuffixes = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Formate une taille en bytes en chaîne lisible (ex: "1.5 GB")
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes == 0) return "0 B";
        
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < SizeSuffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:N2} {SizeSuffixes[suffixIndex]}";
    }
}
