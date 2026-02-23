namespace QuickLauncher.Models;

/// <summary>
/// Mode de réindexation automatique.
/// </summary>
public enum AutoReindexMode
{
    Interval,
    ScheduledTime
}

/// <summary>
/// Modes de thème pour l'apparence.
/// </summary>
public enum ThemeMode
{
    Dark,
    Light,
    Auto  // Basculer selon l'heure du jour
}

/// <summary>
/// Styles d'animation pour l'ouverture/fermeture de la fenêtre.
/// </summary>
public enum AnimationStyle
{
    FadeSlide,   // Fondu + glissement vertical
    Fade,        // Fondu simple
    Scale,       // Zoom depuis le centre
    Slide,       // Glissement seul
    Pop          // Zoom avec rebond
}