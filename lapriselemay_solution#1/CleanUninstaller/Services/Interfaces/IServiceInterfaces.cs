using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

/// <summary>
/// Interface pour le service de logging
/// </summary>
public interface ILoggerService
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// Interface pour le service de registre
/// </summary>
public interface IRegistryService
{
    Task<List<InstalledProgram>> GetInstalledProgramsAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
    bool KeyExists(string keyPath);
    void DeleteKey(string keyPath);
    Task<int> CalculateMissingSizesAsync(List<InstalledProgram> programs, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface pour le scanner de programmes
/// </summary>
public interface IProgramScannerService
{
    Task<List<InstalledProgram>> ScanAllProgramsAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface pour le scanner de résidus
/// </summary>
public interface IResidualScannerService
{
    /// <summary>
    /// Scan complet des résidus pour un programme
    /// </summary>
    Task<List<ResidualItem>> ScanResidualsAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Alias pour ScanResidualsAsync
    /// </summary>
    Task<List<ResidualItem>> ScanAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface pour le service de désinstallation
/// </summary>
public interface IUninstallService
{
    Task<UninstallResult> UninstallAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UninstallResult> UninstallSilentAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UninstallResult> ForceUninstallAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<List<UninstallResult>> BatchUninstallAsync(
        IEnumerable<InstalledProgram> programs,
        bool silent = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UninstallResult> UninstallProgramAsync(
        InstalledProgram program,
        bool silent = true,
        bool scanResiduals = true,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> CreateRestorePointAsync(string description);

    Task<CleanupResult> CleanupResidualsAsync(
        IEnumerable<ResidualItem> residuals,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface pour le service de paramètres
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Paramètres actuels de l'application
    /// </summary>
    AppSettings Settings { get; }
    
    /// <summary>
    /// Sauvegarde les paramètres (synchrone)
    /// </summary>
    void Save();
    
    /// <summary>
    /// Charge les paramètres (synchrone)
    /// </summary>
    void Load();
    
    /// <summary>
    /// Sauvegarde les paramètres (asynchrone)
    /// </summary>
    Task SaveAsync();
    
    /// <summary>
    /// Charge les paramètres (asynchrone)
    /// </summary>
    Task LoadAsync();
    
    /// <summary>
    /// Crée une sauvegarde du registre avant désinstallation
    /// </summary>
    Task<string?> CreateRegistryBackupAsync(string programName);
    
    /// <summary>
    /// Obtient le chemin du dossier de backups
    /// </summary>
    string GetBackupsFolder();
    
    /// <summary>
    /// Nettoie les anciennes sauvegardes (garde les plus récentes)
    /// </summary>
    void CleanupOldBackups(int keepCount = 10);
    
    /// <summary>
    /// Réinitialise les paramètres aux valeurs par défaut
    /// </summary>
    Task ResetToDefaultsAsync();
}

/// <summary>
/// Interface pour le service des apps Windows Store
/// </summary>
public interface IWindowsAppService
{
    Task<List<InstalledProgram>> GetStoreAppsAsync(CancellationToken cancellationToken = default);
    Task<bool> UninstallStoreAppAsync(string packageFullName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface pour le service de détection avancée
/// </summary>
public interface IAdvancedDetectionService
{
    Task<List<ResidualItem>> DeepScanAsync(
        InstalledProgram program,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface pour le moniteur d'installation
/// </summary>
public interface IInstallationMonitorService
{
    bool IsMonitoring { get; }
    void StartMonitoring();
    void StopMonitoring();
    Task<InstallationSnapshot> TakeSnapshotAsync(CancellationToken cancellationToken = default);
    Task<List<SystemChange>> CompareSnapshotsAsync(
        InstallationSnapshot before,
        InstallationSnapshot after,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Modèle pour les paramètres de l'application
/// </summary>
public class AppSettings
{
    #region Désinstallation
    
    /// <summary>
    /// Créer un point de restauration Windows avant la désinstallation
    /// </summary>
    public bool CreateRestorePointBeforeUninstall { get; set; } = true;
    
    /// <summary>
    /// Analyser les résidus après désinstallation (anciennement ThoroughAnalysisEnabled)
    /// </summary>
    public bool ScanResidualsAfterUninstall { get; set; } = true;
    
    /// <summary>
    /// Alias pour compatibilité avec l'ancien nom
    /// </summary>
    public bool ThoroughAnalysisEnabled 
    { 
        get => ScanResidualsAfterUninstall; 
        set => ScanResidualsAfterUninstall = value; 
    }
    
    /// <summary>
    /// Préférer la désinstallation silencieuse (sans interaction utilisateur)
    /// </summary>
    public bool PreferQuietUninstall { get; set; } = true;
    
    /// <summary>
    /// Alias pour CreateRestorePointBeforeUninstall (compatibilité)
    /// </summary>
    public bool CreateRestorePoint 
    { 
        get => CreateRestorePointBeforeUninstall; 
        set => CreateRestorePointBeforeUninstall = value; 
    }
    
    #endregion
    
    #region Batch/Lot
    
    /// <summary>
    /// Utiliser le traitement parallèle pour la désinstallation en lot
    /// </summary>
    public bool UseParallelBatchUninstall { get; set; } = false;
    
    /// <summary>
    /// Nombre maximum de désinstallations simultanées
    /// </summary>
    public int MaxParallelUninstalls { get; set; } = 2;
    
    #endregion
    
    #region Affichage
    
    /// <summary>
    /// Afficher les composants système
    /// </summary>
    public bool ShowSystemApps { get; set; } = false;
    
    /// <summary>
    /// Afficher les applications Windows Store
    /// </summary>
    public bool ShowWindowsApps { get; set; } = true;
    
    #endregion
    
    #region Sauvegarde
    
    /// <summary>
    /// Créer une sauvegarde du registre avant nettoyage
    /// </summary>
    public bool CreateRegistryBackup { get; set; } = true;
    
    #endregion
    
    #region Apparence
    
    /// <summary>
    /// Thème de l'application (0 = Système, 1 = Clair, 2 = Sombre)
    /// </summary>
    public int Theme { get; set; } = 0;
    
    #endregion
    
    #region Logging
    
    /// <summary>
    /// Niveau de log (Debug, Info, Warning, Error)
    /// </summary>
    public string LogLevel { get; set; } = "Info";
    
    #endregion
}

// Note: CleanupResult et CleanupError sont définis dans Models/UninstallResult.cs
