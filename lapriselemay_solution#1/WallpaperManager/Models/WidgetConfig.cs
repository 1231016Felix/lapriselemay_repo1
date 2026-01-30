using System.Text.Json.Serialization;

namespace WallpaperManager.Models;

/// <summary>
/// Configuration d'un widget sur le bureau.
/// </summary>
public class WidgetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public WidgetType Type { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    
    // Position sur l'écran
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    
    // Taille (optionnel, certains widgets ont taille fixe)
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;
    
    // Opacité du fond (0.0 - 1.0)
    public double BackgroundOpacity { get; set; } = 0.7;
    
    // Configuration spécifique au type de widget
    public Dictionary<string, object> Settings { get; set; } = [];
    
    // Écran cible (0 = principal)
    public int TargetScreen { get; set; } = 0;
}

/// <summary>
/// Types de widgets disponibles.
/// </summary>
public enum WidgetType
{
    SystemMonitor,
    Weather,
    DiskStorage,
    Battery,
    Clock,
    Calendar,
    Notes,
    MediaPlayer,
    Shortcuts,
    Quote,
    RssFeed,
    QuickNotes
}

/// <summary>
/// Configuration globale des widgets.
/// </summary>
public class WidgetsSettings
{
    public bool WidgetsEnabled { get; set; } = true;
    public bool ShowOnAllDesktops { get; set; } = true;
    public string GlobalHotkeyToggle { get; set; } = "Ctrl+Shift+W";
    public List<WidgetConfig> Widgets { get; set; } = [];
    
    // Paramètres météo
    public string WeatherCity { get; set; } = "Montreal";
    public double WeatherLatitude { get; set; } = 45.5017;
    public double WeatherLongitude { get; set; } = -73.5673;
    public string WeatherUnits { get; set; } = "metric"; // metric ou imperial
    
    // Intervalle de rafraîchissement (minutes pour météo, secondes pour les autres)
    public int WeatherRefreshInterval { get; set; } = 30;
    public int SystemMonitorRefreshInterval { get; set; } = 2;
}
