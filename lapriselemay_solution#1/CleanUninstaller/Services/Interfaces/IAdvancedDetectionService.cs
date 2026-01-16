using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le service de détection avancée des programmes et de leurs dépendances.
/// </summary>
public interface IAdvancedDetectionService
{
    /// <summary>
    /// Effectue un scan approfondi des résidus pour un programme.
    /// </summary>
    Task<List<ResidualItem>> DeepScanAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Détecte les programmes liés/dépendants d'un programme.
    /// </summary>
    List<InstalledProgram> FindRelatedPrograms(InstalledProgram program, List<InstalledProgram> allPrograms);

    /// <summary>
    /// Détecte les services Windows liés à un programme.
    /// </summary>
    Task<List<ServiceInfo>> FindRelatedServicesAsync(InstalledProgram program, CancellationToken cancellationToken = default);

    /// <summary>
    /// Détecte les entrées de démarrage liées à un programme.
    /// </summary>
    Task<List<StartupEntry>> FindStartupEntriesAsync(InstalledProgram program, CancellationToken cancellationToken = default);

    /// <summary>
    /// Détecte les tâches planifiées liées à un programme.
    /// </summary>
    Task<List<ScheduledTaskInfo>> FindScheduledTasksAsync(InstalledProgram program, CancellationToken cancellationToken = default);

    /// <summary>
    /// Détecte les règles de pare-feu liées à un programme.
    /// </summary>
    Task<List<FirewallRule>> FindFirewallRulesAsync(InstalledProgram program, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calcule la taille réelle d'un programme en analysant son dossier d'installation.
    /// </summary>
    Task<long> CalculateRealSizeAsync(InstalledProgram program, CancellationToken cancellationToken = default);
}
