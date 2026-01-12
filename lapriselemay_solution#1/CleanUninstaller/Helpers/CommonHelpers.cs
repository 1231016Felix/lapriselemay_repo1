namespace CleanUninstaller.Helpers;

/// <summary>
/// Méthodes utilitaires partagées pour éviter la duplication de code
/// </summary>
public static class CommonHelpers
{
    private static readonly string[] SizeSuffixes = ["o", "Ko", "Mo", "Go", "To"];
    
    /// <summary>
    /// Formate une taille en octets en chaîne lisible
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "Inconnue";
        
        var size = (double)bytes;
        var suffixIndex = 0;
        
        while (size >= 1024 && suffixIndex < SizeSuffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }
        
        return $"{size:N1} {SizeSuffixes[suffixIndex]}";
    }
    
    /// <summary>
    /// Formate une taille avec "0 o" si zéro
    /// </summary>
    public static string FormatSizeOrZero(long bytes)
    {
        return bytes <= 0 ? "0 o" : FormatSize(bytes);
    }
    
    /// <summary>
    /// Formate une taille avec tiret si zéro
    /// </summary>
    public static string FormatSizeOrDash(long bytes)
    {
        return bytes <= 0 ? "—" : FormatSize(bytes);
    }
    
    /// <summary>
    /// Parse une couleur hexadécimale (#RRGGBB) en Color
    /// </summary>
    public static Windows.UI.Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        
        if (hex.Length != 6)
            return Windows.UI.Color.FromArgb(255, 110, 110, 110); // Gris par défaut
        
        return Windows.UI.Color.FromArgb(
            255,
            byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber));
    }
    
    /// <summary>
    /// Calcule la taille d'un dossier de manière optimisée
    /// </summary>
    public static long CalculateDirectorySize(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) 
            return 0;

        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch { return 0L; }
                });
        }
        catch
        {
            return 0;
        }
    }
}
