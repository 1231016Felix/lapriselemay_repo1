namespace QuickLauncher.Services;

public class ShortcutInfo
{
    public string TargetPath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public static class ShortcutHelper
{
    // Simplement retourner null - on utilise le chemin .lnk directement
    // Windows sait ouvrir les .lnk sans qu'on ait besoin de résoudre la cible
    public static ShortcutInfo? ResolveShortcut(string shortcutPath)
    {
        return null;
    }
}
