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
/// Options de filtrage par taille
/// </summary>
public enum SizeFilter
{
    /// <summary>
    /// Toutes les tailles
    /// </summary>
    All,
    
    /// <summary>
    /// Moins de 10 Mo
    /// </summary>
    Small,
    
    /// <summary>
    /// Entre 10 Mo et 100 Mo
    /// </summary>
    Medium,
    
    /// <summary>
    /// Entre 100 Mo et 1 Go
    /// </summary>
    Large,
    
    /// <summary>
    /// Plus de 1 Go
    /// </summary>
    VeryLarge,
    
    /// <summary>
    /// Taille inconnue uniquement
    /// </summary>
    Unknown
}

// Note: AppSettings a été déplacé vers Services/Interfaces/IServiceInterfaces.cs
// pour centraliser toutes les définitions de types liés aux services
