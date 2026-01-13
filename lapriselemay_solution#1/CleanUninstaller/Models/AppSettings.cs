namespace CleanUninstaller.Models;

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
