using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using CleanUninstaller.Helpers;

namespace CleanUninstaller.Models;

/// <summary>
/// Représente un élément résiduel laissé par un programme désinstallé
/// </summary>
public partial class ResidualItem : ObservableObject
{
    /// <summary>
    /// Chemin vers l'élément (fichier, dossier, clé de registre, etc.)
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Type de résidu
    /// </summary>
    public ResidualType Type { get; set; }

    /// <summary>
    /// Taille en octets (pour les fichiers/dossiers)
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Niveau de confiance que cet élément est bien un résidu
    /// </summary>
    public ConfidenceLevel Confidence { get; set; }

    /// <summary>
    /// Description détaillée de l'élément
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Nom du programme associé
    /// </summary>
    public string ProgramName { get; set; } = "";

    /// <summary>
    /// Raison de la détection
    /// </summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// Indique si l'élément est sélectionné pour suppression
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// Indique si l'élément a été supprimé
    /// </summary>
    [ObservableProperty]
    private bool _isDeleted;

    /// <summary>
    /// Message d'erreur si la suppression a échoué
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Nom affiché du type
    /// </summary>
    public string TypeName => Type switch
    {
        ResidualType.File => "Fichier",
        ResidualType.Folder => "Dossier",
        ResidualType.RegistryKey => "Clé de registre",
        ResidualType.RegistryValue => "Valeur de registre",
        ResidualType.Service => "Service",
        ResidualType.ScheduledTask => "Tâche planifiée",
        ResidualType.Firewall => "Règle de pare-feu",
        ResidualType.StartupEntry => "Entrée de démarrage",
        ResidualType.Certificate => "Certificat",
        ResidualType.EnvironmentPath => "Variable PATH",
        ResidualType.EnvironmentVariable => "Variable d'environnement",
        ResidualType.ComComponent => "Composant COM",
        ResidualType.FileAssociation => "Association de fichiers",
        _ => "Inconnu"
    };

    /// <summary>
    /// Taille formatée pour l'affichage
    /// </summary>
    public string FormattedSize => Size > 0 ? CommonHelpers.FormatSize(Size) : "";

    /// <summary>
    /// Chemin affiché (nom de fichier ou chemin court)
    /// </summary>
    public string DisplayPath => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;

    /// <summary>
    /// Icône du type (Segoe Fluent Icons)
    /// </summary>
    public string TypeIcon => Type switch
    {
        ResidualType.File => "\uE8A5",
        ResidualType.Folder => "\uE8B7",
        ResidualType.RegistryKey => "\uE74C",
        ResidualType.RegistryValue => "\uE8F1",
        ResidualType.Service => "\uE912",
        ResidualType.ScheduledTask => "\uE787",
        ResidualType.Firewall => "\uE785",
        ResidualType.StartupEntry => "\uE768",
        ResidualType.Certificate => "\uEB95",
        ResidualType.EnvironmentPath => "\uE943",
        ResidualType.EnvironmentVariable => "\uE8F9",
        ResidualType.ComComponent => "\uE943",
        ResidualType.FileAssociation => "\uE8E5",
        _ => "\uE8E5"
    };

    /// <summary>
    /// Couleur basée sur le niveau de confiance (vert-jaune-rouge)
    /// </summary>
    public string ConfidenceColor => Confidence switch
    {
        ConfidenceLevel.VeryHigh => "#107C10",  // Vert foncé - Très sûr
        ConfidenceLevel.High => "#498205",      // Vert clair - Sûr
        ConfidenceLevel.Medium => "#FFB900",    // Jaune - Attention
        ConfidenceLevel.Low => "#D13438",       // Rouge - Risqué
        _ => "#6E6E6E"                          // Gris
    };

    /// <summary>
    /// Brush basé sur le niveau de confiance (pour binding direct)
    /// </summary>
    public SolidColorBrush ConfidenceBrush => Confidence switch
    {
        ConfidenceLevel.VeryHigh => new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)),   // Vert foncé
        ConfidenceLevel.High => new SolidColorBrush(Color.FromArgb(255, 73, 130, 5)),       // Vert clair
        ConfidenceLevel.Medium => new SolidColorBrush(Color.FromArgb(255, 255, 185, 0)),   // Jaune
        ConfidenceLevel.Low => new SolidColorBrush(Color.FromArgb(255, 209, 52, 56)),      // Rouge
        _ => new SolidColorBrush(Color.FromArgb(255, 110, 110, 110))                        // Gris
    };

    /// <summary>
    /// Texte descriptif du niveau de confiance
    /// </summary>
    public string ConfidenceText => Confidence switch
    {
        ConfidenceLevel.VeryHigh => "Très sûr",
        ConfidenceLevel.High => "Sûr",
        ConfidenceLevel.Medium => "Incertain",
        ConfidenceLevel.Low => "Risqué",
        _ => "Inconnu"
    };

    /// <summary>
    /// Indique si l'élément est risqué (jaune ou rouge)
    /// </summary>
    public bool IsRisky => Confidence <= ConfidenceLevel.Medium;
}

/// <summary>
/// Type d'élément résiduel
/// </summary>
public enum ResidualType
{
    File,
    Folder,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask,
    Firewall,
    StartupEntry,
    Certificate,
    EnvironmentPath,
    EnvironmentVariable,
    ComComponent,
    FileAssociation
}

/// <summary>
/// Niveau de confiance
/// </summary>
public enum ConfidenceLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    VeryHigh = 4
}
