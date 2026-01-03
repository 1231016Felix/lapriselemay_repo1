using CommunityToolkit.Mvvm.ComponentModel;

namespace CleanUninstaller.Models;

/// <summary>
/// Représente un élément résiduel laissé par un programme désinstallé
/// </summary>
public partial class ResidualItem : ObservableObject
{
    /// <summary>
    /// Chemin vers l'élément (fichier, dossier, clé de registre, etc.)
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Type de résidu
    /// </summary>
    public ResidualType Type { get; init; }

    /// <summary>
    /// Taille en octets (pour les fichiers/dossiers)
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Niveau de confiance que cet élément est bien un résidu
    /// </summary>
    public ConfidenceLevel Confidence { get; init; }

    /// <summary>
    /// Description détaillée de l'élément
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Nom du programme associé
    /// </summary>
    public string ProgramName { get; init; } = "";

    /// <summary>
    /// Raison de la détection
    /// </summary>
    public string Reason { get; init; } = "";

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
        _ => "Inconnu"
    };

    /// <summary>
    /// Taille formatée pour l'affichage
    /// </summary>
    public string FormattedSize => FormatSize(Size);

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
        _ => "\uE8E5"
    };

    /// <summary>
    /// Couleur basée sur le niveau de confiance
    /// </summary>
    public string ConfidenceColor => Confidence switch
    {
        ConfidenceLevel.VeryHigh => "#107C10",  // Vert
        ConfidenceLevel.High => "#498205",      // Vert clair
        ConfidenceLevel.Medium => "#CA5010",    // Orange
        ConfidenceLevel.Low => "#D13438",       // Rouge
        _ => "#6E6E6E"                          // Gris
    };

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        
        string[] suffixes = ["o", "Ko", "Mo", "Go"];
        int i = 0;
        double size = bytes;
        
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        
        return $"{size:N1} {suffixes[i]}";
    }
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
    Certificate
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
