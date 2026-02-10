namespace QuickLauncher.Models.Settings;

/// <summary>
/// Paramètres d'apparence et d'animation de la fenêtre.
/// </summary>
public sealed class AppearanceSettings
{
    // === Thème ===
    public string Theme { get; set; } = "Dark";
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;
    public string AccentColor { get; set; } = Constants.Colors.DefaultAccent;
    
    // === Thème automatique (jour/nuit) ===
    public string AutoThemeLightStart { get; set; } = "07:00";
    public string AutoThemeDarkStart { get; set; } = "19:00";
    
    // Rétro-compatibilité (ancien format, même valeurs)
    public string LightThemeStartTime { get; set; } = "07:00";
    public string DarkThemeStartTime { get; set; } = "19:00";
    
    // === Fenêtre ===
    public double WindowOpacity { get; set; } = 1.0;
    public string WindowPosition { get; set; } = "Center";
    public double? LastWindowLeft { get; set; }
    public double? LastWindowTop { get; set; }
    public bool ShowInTaskbar { get; set; }
    
    // === UI toggles ===
    public bool ShowSettingsButton { get; set; } = true;
    public bool ShowPreviewPanel { get; set; } = true;
    public bool ShowShortcutHints { get; set; } = true;
    public bool ShowCategoryBadges { get; set; } = true;
    public bool ShowIndexingStatus { get; set; } = true;
    public bool ShowGhostSuggestions { get; set; } = true;
    
    // === Animations ===
    public bool EnableAnimations { get; set; } = true;
    public int AnimationDurationMs { get; set; } = 140;
    public AnimationStyle AnimationStyle { get; set; } = AnimationStyle.FadeSlide;
    public int StaggerDelayMs { get; set; } = 30;
}
