using CleanUninstaller.Models;

namespace CleanUninstaller.Services.Interfaces;

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
