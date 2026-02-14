using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Interface pour le service de gestion des thèmes.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Événement déclenché quand le thème change.
    /// </summary>
    event EventHandler<string>? ThemeChanged;

    /// <summary>
    /// Thème actuellement appliqué.
    /// </summary>
    string CurrentTheme { get; }

    /// <summary>
    /// Initialise le service de thème.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Applique le thème en fonction des paramètres.
    /// </summary>
    void ApplyThemeFromSettings(AppSettings? settings = null);
    /// <summary>
    /// Applique un thème spécifique.
    /// </summary>
    void ApplyTheme(string theme);

    /// <summary>
    /// Applique une couleur d'accent personnalisée.
    /// </summary>
    void ApplyAccentColor(string hexColor);

    /// <summary>
    /// Détecte si Windows est en mode clair.
    /// </summary>
    bool IsWindowsInLightMode();

    /// <summary>
    /// Retourne une description lisible du mode de thème actuel.
    /// </summary>
    string GetThemeModeDescription(ThemeMode mode, AppSettings? settings = null);

    /// <summary>
    /// Libère les ressources du service.
    /// </summary>
    void Shutdown();
}