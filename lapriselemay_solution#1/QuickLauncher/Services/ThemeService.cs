using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickLauncher.Models;

// Alias pour éviter les ambiguïtés avec System.Drawing et System.Windows.Forms
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace QuickLauncher.Services;

/// <summary>
/// Service de gestion des thèmes de l'application.
/// Supporte les thèmes Sombre, Clair, Système (suit Windows) et Auto (selon l'heure).
/// Utilise ISettingsProvider pour éviter les lectures disque répétées.
/// </summary>
public static class ThemeService
{
    private static string _currentTheme = "Dark";
    private static DispatcherTimer? _autoThemeTimer;
    private static ISettingsProvider? _settingsProvider;
    
    /// <summary>
    /// Événement déclenché quand le thème change.
    /// </summary>
    public static event EventHandler<string>? ThemeChanged;
    
    /// <summary>
    /// Thème actuellement appliqué.
    /// </summary>
    public static string CurrentTheme => _currentTheme;
    
    /// <summary>
    /// Initialise le service de thème avec le provider de settings centralisé.
    /// </summary>
    public static void Initialize(ISettingsProvider? settingsProvider = null)
    {
        _settingsProvider = settingsProvider;
        var settings = GetSettings();
        ApplyThemeFromSettings(settings);
        
        // Écouter les changements de thème Windows
        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        
        // Démarrer le timer pour le mode auto
        StartAutoThemeTimer();
    }
    
    /// <summary>
    /// Retourne les settings via le provider (priorité) ou chargement direct (fallback).
    /// </summary>
    private static AppSettings GetSettings() => _settingsProvider?.Current ?? AppSettings.Load();
    
    /// <summary>
    /// Applique le thème en fonction des paramètres.
    /// Utilise settings.Theme (string) qui est la propriété écrite par la fenêtre de paramètres.
    /// </summary>
    public static void ApplyThemeFromSettings(AppSettings? settings = null)
    {
        settings ??= GetSettings();
        
        var actualTheme = settings.Theme switch
        {
            "Light" => "Light",
            "Auto" => GetAutoTheme(settings),
            _ => "Dark"
        };
        
        ApplyThemeInternal(actualTheme);
    }
    
    /// <summary>
    /// Détermine le thème selon l'heure actuelle.
    /// Lit LightThemeStartTime/DarkThemeStartTime qui sont les propriétés écrites par la fenêtre de paramètres.
    /// </summary>
    private static string GetAutoTheme(AppSettings settings)
    {
        var now = DateTime.Now.TimeOfDay;
        
        // Lire les propriétés correctes (celles écrites par SettingsWindow)
        var lightStartStr = !string.IsNullOrEmpty(settings.LightThemeStartTime) 
            ? settings.LightThemeStartTime 
            : settings.AutoThemeLightStart;
        var darkStartStr = !string.IsNullOrEmpty(settings.DarkThemeStartTime) 
            ? settings.DarkThemeStartTime 
            : settings.AutoThemeDarkStart;
        
        if (TimeSpan.TryParse(lightStartStr, out var lightStart) &&
            TimeSpan.TryParse(darkStartStr, out var darkStart))
        {
            // Cas normal: lightStart < darkStart (ex: 07:00 - 19:00)
            if (lightStart < darkStart)
            {
                return (now >= lightStart && now < darkStart) ? "Light" : "Dark";
            }
            // Cas inversé: darkStart < lightStart (ex: 22:00 - 06:00)
            else
            {
                return (now >= darkStart || now < lightStart) ? "Dark" : "Light";
            }
        }
        
        // Valeurs par défaut si parsing échoue
        return (now.Hours >= 7 && now.Hours < 19) ? "Light" : "Dark";
    }
    
    /// <summary>
    /// Démarre le timer pour vérifier le changement de thème automatique.
    /// </summary>
    private static void StartAutoThemeTimer()
    {
        _autoThemeTimer?.Stop();
        _autoThemeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _autoThemeTimer.Tick += (_, _) =>
        {
            var settings = GetSettings();
            if (settings.Theme.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                var expectedTheme = GetAutoTheme(settings);
                if (expectedTheme != _currentTheme)
                {
                    ApplyThemeInternal(expectedTheme);
                }
            }
        };
        _autoThemeTimer.Start();
    }
    
    /// <summary>
    /// Applique un thème spécifique.
    /// </summary>
    /// <param name="theme">Nom du thème: "Dark", "Light", "System" ou "Auto"</param>
    public static void ApplyTheme(string theme)
    {
        var actualTheme = theme;
        
        // Si thème "System", détecter le thème Windows
        if (theme.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            actualTheme = IsWindowsInLightMode() ? "Light" : "Dark";
        }
        // Si thème "Auto", déterminer selon l'heure
        else if (theme.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            var settings = GetSettings();
            actualTheme = GetAutoTheme(settings);
        }
        
        ApplyThemeInternal(actualTheme);
    }
    
    /// <summary>
    /// Applique le thème réel (Dark ou Light).
    /// </summary>
    private static void ApplyThemeInternal(string actualTheme)
    {
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
        
        var settings = GetSettings();
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
        _autoThemeTimer?.Stop();
        _autoThemeTimer = null;
        SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
    }
    
    /// <summary>
    /// Retourne une description lisible du mode de thème actuel.
    /// </summary>
    public static string GetThemeModeDescription(ThemeMode mode, AppSettings? settings = null)
    {
        settings ??= GetSettings();
        return mode switch
        {
            ThemeMode.Dark => "🌙 Sombre",
            ThemeMode.Light => "☀️ Clair",
            ThemeMode.Auto => $"🌓 Auto ({settings.LightThemeStartTime} - {settings.DarkThemeStartTime})",
            _ => "🌙 Sombre"
        };
    }
}
