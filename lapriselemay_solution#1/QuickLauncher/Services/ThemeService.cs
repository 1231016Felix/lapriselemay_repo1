using System.Windows.Media;
using Microsoft.Win32;
using QuickLauncher.Models;

// Alias pour éviter les ambiguïtés avec System.Drawing et System.Windows.Forms
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion des thèmes de l'application.
/// Supporte les thèmes Sombre, Clair et Système (suit Windows).
/// </summary>
public static class ThemeService
{
    private static string _currentTheme = "Dark";
    
    /// <summary>
    /// Événement déclenché quand le thème change.
    /// </summary>
    public static event EventHandler<string>? ThemeChanged;
    
    /// <summary>
    /// Thème actuellement appliqué.
    /// </summary>
    public static string CurrentTheme => _currentTheme;
    
    /// <summary>
    /// Initialise le service de thème et applique le thème des paramètres.
    /// </summary>
    public static void Initialize()
    {
        var settings = AppSettings.Load();
        ApplyTheme(settings.Theme);
        
        // Écouter les changements de thème Windows
        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
    }
    
    /// <summary>
    /// Applique un thème spécifique.
    /// </summary>
    /// <param name="theme">Nom du thème: "Dark", "Light", ou "System"</param>
    public static void ApplyTheme(string theme)
    {
        var actualTheme = theme;
        
        // Si thème "System", détecter le thème Windows
        if (theme.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            actualTheme = IsWindowsInLightMode() ? "Light" : "Dark";
        }
        
        _currentTheme = actualTheme;
        
        var app = WpfApplication.Current;
        if (app == null) return;
        
        var resources = app.Resources;
        
        if (actualTheme.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            ApplyLightTheme(resources);
        }
        else
        {
            ApplyDarkTheme(resources);
        }
        
        ThemeChanged?.Invoke(null, actualTheme);
    }
    
    /// <summary>
    /// Applique également une couleur d'accent personnalisée.
    /// </summary>
    public static void ApplyAccentColor(string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor)) return;
        
        try
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(hexColor);
            var app = WpfApplication.Current;
            if (app == null) return;
            
            app.Resources["AccentColor"] = color;
            app.Resources["AccentBrush"] = new SolidColorBrush(color);
            
            // Calculer les variantes hover/pressed
            var hoverColor = LightenColor(color, 0.1);
            var pressedColor = DarkenColor(color, 0.1);
            
            app.Resources["AccentHoverColor"] = hoverColor;
            app.Resources["AccentPressedColor"] = pressedColor;
            app.Resources["AccentHoverBrush"] = new SolidColorBrush(hoverColor);
        }
        catch
        {
            // Couleur invalide, ignorer
        }
    }
    
    private static void ApplyDarkTheme(System.Windows.ResourceDictionary resources)
    {
        resources["BackgroundBrush"] = new SolidColorBrush((WpfColor)resources["DarkBackgroundColor"]);
        resources["SurfaceBrush"] = new SolidColorBrush((WpfColor)resources["DarkSurfaceColor"]);
        resources["SurfaceAltBrush"] = new SolidColorBrush((WpfColor)resources["DarkSurfaceAltColor"]);
        resources["BorderBrush"] = new SolidColorBrush((WpfColor)resources["DarkBorderColor"]);
        resources["TextPrimaryBrush"] = new SolidColorBrush((WpfColor)resources["DarkTextPrimaryColor"]);
        resources["TextSecondaryBrush"] = new SolidColorBrush((WpfColor)resources["DarkTextSecondaryColor"]);
        resources["TextTertiaryBrush"] = new SolidColorBrush((WpfColor)resources["DarkTextTertiaryColor"]);
        resources["HoverBrush"] = new SolidColorBrush((WpfColor)resources["DarkHoverColor"]);
        resources["CodeBackgroundBrush"] = new SolidColorBrush((WpfColor)resources["DarkCodeBackgroundColor"]);
    }
    
    private static void ApplyLightTheme(System.Windows.ResourceDictionary resources)
    {
        resources["BackgroundBrush"] = new SolidColorBrush((WpfColor)resources["LightBackgroundColor"]);
        resources["SurfaceBrush"] = new SolidColorBrush((WpfColor)resources["LightSurfaceColor"]);
        resources["SurfaceAltBrush"] = new SolidColorBrush((WpfColor)resources["LightSurfaceAltColor"]);
        resources["BorderBrush"] = new SolidColorBrush((WpfColor)resources["LightBorderColor"]);
        resources["TextPrimaryBrush"] = new SolidColorBrush((WpfColor)resources["LightTextPrimaryColor"]);
        resources["TextSecondaryBrush"] = new SolidColorBrush((WpfColor)resources["LightTextSecondaryColor"]);
        resources["TextTertiaryBrush"] = new SolidColorBrush((WpfColor)resources["LightTextTertiaryColor"]);
        resources["HoverBrush"] = new SolidColorBrush((WpfColor)resources["LightHoverColor"]);
        resources["CodeBackgroundBrush"] = new SolidColorBrush((WpfColor)resources["LightCodeBackgroundColor"]);
    }
    
    /// <summary>
    /// Détecte si Windows est en mode clair.
    /// </summary>
    public static bool IsWindowsInLightMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            
            if (key != null)
            {
                var value = key.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 1;
                }
            }
        }
        catch
        {
            // En cas d'erreur, assumer mode sombre
        }
        
        return false;
    }
    
    private static void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        
        var settings = AppSettings.Load();
        if (settings.Theme.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            // Réappliquer le thème système
            WpfApplication.Current?.Dispatcher.InvokeAsync(() => ApplyTheme("System"));
        }
    }
    
    private static WpfColor LightenColor(WpfColor color, double amount)
    {
        return WpfColor.FromArgb(
            color.A,
            (byte)Math.Min(255, color.R + (255 - color.R) * amount),
            (byte)Math.Min(255, color.G + (255 - color.G) * amount),
            (byte)Math.Min(255, color.B + (255 - color.B) * amount));
    }
    
    private static WpfColor DarkenColor(WpfColor color, double amount)
    {
        return WpfColor.FromArgb(
            color.A,
            (byte)Math.Max(0, color.R * (1 - amount)),
            (byte)Math.Max(0, color.G * (1 - amount)),
            (byte)Math.Max(0, color.B * (1 - amount)));
    }
    
    /// <summary>
    /// Libère les ressources du service.
    /// </summary>
    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
    }
}
