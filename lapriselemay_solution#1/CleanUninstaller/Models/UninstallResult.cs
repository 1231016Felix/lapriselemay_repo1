namespace CleanUninstaller.Models;

/// <summary>
/// Résultat d'une opération de désinstallation
/// </summary>
public class UninstallResult
{
    /// <summary>
    /// Indique si la désinstallation a réussi
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Nom du programme concerné
    /// </summary>
    public string ProgramName { get; set; } = "";

    /// <summary>
    /// Code de sortie du processus de désinstallation
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Message d'erreur éventuel
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Durée de la désinstallation
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Éléments résiduels trouvés après désinstallation
    /// </summary>
    public List<ResidualItem> Residuals { get; set; } = [];

    /// <summary>
    /// Taille totale des résidus en octets
    /// </summary>
    public long TotalResidualSize => Residuals.Sum(r => r.Size);

    /// <summary>
    /// Nombre de résidus trouvés
    /// </summary>
    public int ResidualCount { get; set; }

    /// <summary>
    /// Taille des résidus
    /// </summary>
    public long ResidualSize { get; set; }

    /// <summary>
    /// ID de la sauvegarde créée avant désinstallation
    /// </summary>
    public string? BackupId { get; set; }

    /// <summary>
    /// Nombre d'éléments supprimés avec succès
    /// </summary>
    public int DeletedCount { get; set; }

    /// <summary>
    /// Nombre d'éléments qui n'ont pas pu être supprimés
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Espace libéré en octets
    /// </summary>
    public long SpaceFreed { get; set; }
}

/// <summary>
/// Résultat d'un nettoyage de résidus
/// </summary>
public class CleanupResult
{
    /// <summary>
    /// Nombre d'éléments supprimés avec succès
    /// </summary>
    public int DeletedCount { get; set; }

    /// <summary>
    /// Nombre d'éléments qui n'ont pas pu être supprimés
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Espace libéré en octets
    /// </summary>
    public long SpaceFreed { get; set; }

    /// <summary>
    /// Liste des erreurs rencontrées
    /// </summary>
    public List<CleanupError> Errors { get; set; } = [];
}

/// <summary>
/// Erreur lors du nettoyage
/// </summary>
public class CleanupError
{
    /// <summary>
    /// Élément concerné
    /// </summary>
    public required ResidualItem Item { get; init; }

    /// <summary>
    /// Message d'erreur
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Exception sous-jacente
    /// </summary>
    public Exception? Exception { get; init; }
}
