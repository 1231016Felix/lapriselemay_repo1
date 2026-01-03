namespace CleanUninstaller.Models;

/// <summary>
/// Informations de progression pour les opérations longues
/// </summary>
public class ScanProgress
{
    public ScanProgress() { }

    public ScanProgress(int percentage, string statusMessage)
    {
        Percentage = percentage;
        StatusMessage = statusMessage;
    }

    /// <summary>
    /// Pourcentage de progression (0-100)
    /// </summary>
    public int Percentage { get; set; }

    /// <summary>
    /// Message de statut actuel
    /// </summary>
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// Élément en cours de traitement
    /// </summary>
    public string CurrentItem { get; set; } = string.Empty;

    /// <summary>
    /// Phase actuelle du scan
    /// </summary>
    public ScanPhase Phase { get; set; }

    /// <summary>
    /// Nombre d'éléments traités
    /// </summary>
    public int ProcessedCount { get; set; }

    /// <summary>
    /// Nombre total d'éléments à traiter
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Phase du scan
/// </summary>
public enum ScanPhase
{
    Initializing,
    ScanningRegistry,
    ScanningWindowsApps,
    LoadingIcons,
    CalculatingSizes,
    ScanningResiduals,
    Completed
}

/// <summary>
/// Options de filtrage pour la liste des programmes
/// </summary>
public class FilterOptions
{
    /// <summary>
    /// Texte de recherche
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Afficher les applications système
    /// </summary>
    public bool ShowSystemApps { get; set; } = false;

    /// <summary>
    /// Afficher les applications Windows Store
    /// </summary>
    public bool ShowWindowsApps { get; set; } = true;

    /// <summary>
    /// Tri actuel
    /// </summary>
    public SortOption SortBy { get; set; } = SortOption.Name;

    /// <summary>
    /// Ordre de tri descendant
    /// </summary>
    public bool SortDescending { get; set; } = false;
}

/// <summary>
/// Options de tri
/// </summary>
public enum SortOption
{
    Name,
    Publisher,
    Size,
    InstallDate
}

/// <summary>
/// Paramètres de l'application
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Créer une sauvegarde du registre avant nettoyage
    /// </summary>
    public bool CreateRegistryBackup { get; set; } = true;

    /// <summary>
    /// Créer un point de restauration avant désinstallation en lot
    /// </summary>
    public bool CreateRestorePoint { get; set; } = true;

    /// <summary>
    /// Utiliser la désinstallation silencieuse quand disponible
    /// </summary>
    public bool PreferQuietUninstall { get; set; } = true;

    /// <summary>
    /// Scanner automatiquement les résidus après désinstallation
    /// </summary>
    public bool AutoScanResiduals { get; set; } = true;

    /// <summary>
    /// Niveau de confiance minimum pour la sélection automatique des résidus (0-100)
    /// </summary>
    public int MinConfidenceForAutoSelect { get; set; } = 70;

    /// <summary>
    /// Emplacements supplémentaires à scanner pour les résidus
    /// </summary>
    public List<string> AdditionalScanPaths { get; set; } = [];

    /// <summary>
    /// Thème de l'application (0 = Système, 1 = Clair, 2 = Sombre)
    /// </summary>
    public int Theme { get; set; } = 0;
}
