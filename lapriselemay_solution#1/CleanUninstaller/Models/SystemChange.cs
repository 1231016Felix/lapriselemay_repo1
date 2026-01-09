using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CleanUninstaller.Models;

/// <summary>
/// Représente un changement système détecté pendant le monitoring d'installation
/// </summary>
public partial class SystemChange : ObservableObject
{
    /// <summary>
    /// Type de changement
    /// </summary>
    public ChangeType ChangeType { get; init; }

    /// <summary>
    /// Type d'élément modifié
    /// </summary>
    public SystemChangeCategory Category { get; init; }

    /// <summary>
    /// Chemin ou identifiant de l'élément
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Ancienne valeur (si modification)
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// Nouvelle valeur
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    /// Taille en octets (pour les fichiers)
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Date/heure du changement
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>
    /// Processus ayant effectué le changement (si détecté)
    /// </summary>
    public string? ProcessName { get; init; }

    /// <summary>
    /// Description détaillée du changement
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Indique si ce changement est sélectionné pour suppression
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// Indique si ce changement a été annulé/supprimé
    /// </summary>
    [ObservableProperty]
    private bool _isReverted;

    /// <summary>
    /// Message d'erreur si la suppression a échoué
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Nom affiché du type de changement
    /// </summary>
    public string ChangeTypeName => ChangeType switch
    {
        ChangeType.Created => "Créé",
        ChangeType.Modified => "Modifié",
        ChangeType.Deleted => "Supprimé",
        ChangeType.Renamed => "Renommé",
        _ => "Inconnu"
    };

    /// <summary>
    /// Nom affiché de la catégorie
    /// </summary>
    public string CategoryName => Category switch
    {
        SystemChangeCategory.File => "Fichier",
        SystemChangeCategory.Folder => "Dossier",
        SystemChangeCategory.RegistryKey => "Clé de registre",
        SystemChangeCategory.RegistryValue => "Valeur de registre",
        SystemChangeCategory.Service => "Service",
        SystemChangeCategory.ScheduledTask => "Tâche planifiée",
        SystemChangeCategory.FirewallRule => "Règle pare-feu",
        SystemChangeCategory.StartupEntry => "Démarrage",
        SystemChangeCategory.EnvironmentVariable => "Variable d'env.",
        SystemChangeCategory.Driver => "Pilote",
        SystemChangeCategory.ComObject => "Objet COM",
        SystemChangeCategory.FileAssociation => "Association fichier",
        SystemChangeCategory.Font => "Police",
        SystemChangeCategory.ShellExtension => "Extension shell",
        _ => "Inconnu"
    };

    /// <summary>
    /// Icône de la catégorie (Segoe Fluent Icons)
    /// </summary>
    public string CategoryIcon => Category switch
    {
        SystemChangeCategory.File => "\uE8A5",
        SystemChangeCategory.Folder => "\uE8B7",
        SystemChangeCategory.RegistryKey => "\uE74C",
        SystemChangeCategory.RegistryValue => "\uE8F1",
        SystemChangeCategory.Service => "\uE912",
        SystemChangeCategory.ScheduledTask => "\uE787",
        SystemChangeCategory.FirewallRule => "\uE785",
        SystemChangeCategory.StartupEntry => "\uE768",
        SystemChangeCategory.EnvironmentVariable => "\uE8F9",
        SystemChangeCategory.Driver => "\uE964",      // Hardware
        SystemChangeCategory.ComObject => "\uE943",   // Code
        SystemChangeCategory.FileAssociation => "\uE8E5", // Link
        SystemChangeCategory.Font => "\uE8D2",        // Font
        SystemChangeCategory.ShellExtension => "\uE8B7", // Folder
        _ => "\uE8E5"
    };

    /// <summary>
    /// Icône du type de changement
    /// </summary>
    public string ChangeTypeIcon => ChangeType switch
    {
        ChangeType.Created => "\uE710",      // Add
        ChangeType.Modified => "\uE70F",     // Edit
        ChangeType.Deleted => "\uE74D",      // Delete
        ChangeType.Renamed => "\uE8AC",      // Rename
        _ => "\uE897"                         // Help
    };

    /// <summary>
    /// Couleur du type de changement
    /// </summary>
    public SolidColorBrush ChangeTypeColor => ChangeType switch
    {
        ChangeType.Created => new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)),   // Vert
        ChangeType.Modified => new SolidColorBrush(Color.FromArgb(255, 255, 185, 0)),  // Jaune
        ChangeType.Deleted => new SolidColorBrush(Color.FromArgb(255, 209, 52, 56)),   // Rouge
        ChangeType.Renamed => new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),   // Bleu
        _ => new SolidColorBrush(Color.FromArgb(255, 110, 110, 110))                    // Gris
    };

    /// <summary>
    /// Chemin affiché (nom de fichier ou chemin court)
    /// </summary>
    public string DisplayPath
    {
        get
        {
            if (Category is SystemChangeCategory.File or SystemChangeCategory.Folder)
            {
                return System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
            }
            return Path.Length > 80 ? $"...{Path[^77..]}" : Path;
        }
    }

    /// <summary>
    /// Taille formatée pour l'affichage
    /// </summary>
    public string FormattedSize => Size > 0 ? FormatSize(Size) : "";

    /// <summary>
    /// Horodatage formaté
    /// </summary>
    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["o", "Ko", "Mo", "Go"];
        var i = 0;
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
/// Type de changement
/// </summary>
public enum ChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}

/// <summary>
/// Catégorie de changement système
/// </summary>
public enum SystemChangeCategory
{
    File,
    Folder,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask,
    FirewallRule,
    StartupEntry,
    EnvironmentVariable,
    Driver,
    ComObject,
    FileAssociation,
    Font,
    ShellExtension
}
