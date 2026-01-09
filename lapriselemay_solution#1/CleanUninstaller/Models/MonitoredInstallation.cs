using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace CleanUninstaller.Models;

/// <summary>
/// Représente une installation surveillée avec tous ses changements détectés
/// </summary>
public partial class MonitoredInstallation : ObservableObject
{
    /// <summary>
    /// Identifiant unique
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Nom de l'application installée (fourni par l'utilisateur ou détecté)
    /// </summary>
    [ObservableProperty]
    private string _name = "Installation sans nom";

    /// <summary>
    /// Description ou notes
    /// </summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>
    /// Date de début du monitoring
    /// </summary>
    public DateTime StartTime { get; init; } = DateTime.Now;

    /// <summary>
    /// Date de fin du monitoring
    /// </summary>
    [ObservableProperty]
    private DateTime? _endTime;

    /// <summary>
    /// Statut du monitoring
    /// </summary>
    [ObservableProperty]
    private MonitoringStatus _status = MonitoringStatus.NotStarted;

    /// <summary>
    /// Chemin de l'installeur exécuté (optionnel)
    /// </summary>
    public string? InstallerPath { get; init; }

    /// <summary>
    /// ID du snapshot avant installation
    /// </summary>
    public string? BeforeSnapshotId { get; set; }

    /// <summary>
    /// ID du snapshot après installation
    /// </summary>
    public string? AfterSnapshotId { get; set; }

    /// <summary>
    /// Liste des changements détectés
    /// </summary>
    public List<SystemChange> Changes { get; init; } = [];

    /// <summary>
    /// Indique si cette installation a été désinstallée avec succès
    /// </summary>
    [ObservableProperty]
    private bool _isUninstalled;

    /// <summary>
    /// Date de désinstallation
    /// </summary>
    [ObservableProperty]
    private DateTime? _uninstallDate;

    /// <summary>
    /// Statistiques des changements
    /// </summary>
    [JsonIgnore]
    public ChangeStatistics Statistics => new()
    {
        TotalChanges = Changes.Count,
        FilesCreated = Changes.Count(c => c.Category == SystemChangeCategory.File && c.ChangeType == ChangeType.Created),
        FilesModified = Changes.Count(c => c.Category == SystemChangeCategory.File && c.ChangeType == ChangeType.Modified),
        FoldersCreated = Changes.Count(c => c.Category == SystemChangeCategory.Folder && c.ChangeType == ChangeType.Created),
        RegistryKeysCreated = Changes.Count(c => c.Category == SystemChangeCategory.RegistryKey && c.ChangeType == ChangeType.Created),
        RegistryValuesCreated = Changes.Count(c => c.Category == SystemChangeCategory.RegistryValue && c.ChangeType == ChangeType.Created),
        RegistryValuesModified = Changes.Count(c => c.Category == SystemChangeCategory.RegistryValue && c.ChangeType == ChangeType.Modified),
        ServicesCreated = Changes.Count(c => c.Category == SystemChangeCategory.Service && c.ChangeType == ChangeType.Created),
        ScheduledTasksCreated = Changes.Count(c => c.Category == SystemChangeCategory.ScheduledTask && c.ChangeType == ChangeType.Created),
        FirewallRulesCreated = Changes.Count(c => c.Category == SystemChangeCategory.FirewallRule && c.ChangeType == ChangeType.Created),
        StartupEntriesCreated = Changes.Count(c => c.Category == SystemChangeCategory.StartupEntry && c.ChangeType == ChangeType.Created),
        DriversCreated = Changes.Count(c => c.Category == SystemChangeCategory.Driver && c.ChangeType == ChangeType.Created),
        ComObjectsCreated = Changes.Count(c => c.Category == SystemChangeCategory.ComObject && c.ChangeType == ChangeType.Created),
        FileAssociationsChanged = Changes.Count(c => c.Category == SystemChangeCategory.FileAssociation),
        FontsCreated = Changes.Count(c => c.Category == SystemChangeCategory.Font && c.ChangeType == ChangeType.Created),
        ShellExtensionsCreated = Changes.Count(c => c.Category == SystemChangeCategory.ShellExtension && c.ChangeType == ChangeType.Created),
        TotalSizeAdded = Changes.Where(c => c.ChangeType == ChangeType.Created).Sum(c => c.Size)
    };

    /// <summary>
    /// Durée du monitoring
    /// </summary>
    [JsonIgnore]
    public TimeSpan Duration => (EndTime ?? DateTime.Now) - StartTime;

    /// <summary>
    /// Durée formatée
    /// </summary>
    [JsonIgnore]
    public string FormattedDuration
    {
        get
        {
            var duration = Duration;
            if (duration.TotalHours >= 1)
                return $"{duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
            if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes}m {duration.Seconds}s";
            return $"{duration.Seconds}s";
        }
    }

    /// <summary>
    /// Statut formaté
    /// </summary>
    [JsonIgnore]
    public string StatusText => Status switch
    {
        MonitoringStatus.NotStarted => "Non démarré",
        MonitoringStatus.TakingSnapshot => "Capture en cours...",
        MonitoringStatus.Monitoring => "Surveillance active",
        MonitoringStatus.Paused => "En pause",
        MonitoringStatus.Analyzing => "Analyse en cours...",
        MonitoringStatus.Completed => "Terminé",
        MonitoringStatus.Error => "Erreur",
        MonitoringStatus.Cancelled => "Annulé",
        _ => "Inconnu"
    };

    /// <summary>
    /// Icône du statut
    /// </summary>
    [JsonIgnore]
    public string StatusIcon => Status switch
    {
        MonitoringStatus.NotStarted => "\uE768",     // Play
        MonitoringStatus.TakingSnapshot => "\uE895", // Processing
        MonitoringStatus.Monitoring => "\uE7B3",     // Recording
        MonitoringStatus.Paused => "\uE769",         // Pause
        MonitoringStatus.Analyzing => "\uE9D9",      // Analyzing
        MonitoringStatus.Completed => "\uE73E",      // Checkmark
        MonitoringStatus.Error => "\uE783",          // Error
        MonitoringStatus.Cancelled => "\uE711",      // Cancel
        _ => "\uE897"                                 // Help
    };

    /// <summary>
    /// Taille totale formatée
    /// </summary>
    [JsonIgnore]
    public string FormattedTotalSize => FormatSize(Statistics.TotalSizeAdded);

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 o";

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
/// Statut du monitoring
/// </summary>
public enum MonitoringStatus
{
    NotStarted,
    TakingSnapshot,
    Monitoring,
    Paused,
    Analyzing,
    Completed,
    Error,
    Cancelled
}

/// <summary>
/// Statistiques des changements
/// </summary>
public class ChangeStatistics
{
    public int TotalChanges { get; init; }
    public int FilesCreated { get; init; }
    public int FilesModified { get; init; }
    public int FoldersCreated { get; init; }
    public int RegistryKeysCreated { get; init; }
    public int RegistryValuesCreated { get; init; }
    public int RegistryValuesModified { get; init; }
    public int ServicesCreated { get; init; }
    public int ScheduledTasksCreated { get; init; }
    public int FirewallRulesCreated { get; init; }
    public int StartupEntriesCreated { get; init; }
    public int DriversCreated { get; init; }
    public int ComObjectsCreated { get; init; }
    public int FileAssociationsChanged { get; init; }
    public int FontsCreated { get; init; }
    public int ShellExtensionsCreated { get; init; }
    public long TotalSizeAdded { get; init; }
}
