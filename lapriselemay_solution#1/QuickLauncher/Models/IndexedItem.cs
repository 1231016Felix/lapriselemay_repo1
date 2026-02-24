namespace QuickLauncher.Models;

/// <summary>
/// Élément indexé stocké dans le cache mémoire et la base SQLite.
/// 
/// <b>Immutable après construction</b> : les propriétés utilisent <c>init</c>,
/// ce qui garantit qu'un thread de recherche ne verra jamais un objet en état
/// intermédiaire. Lorsqu'un usage est enregistré, un nouvel objet est créé
/// via <see cref="WithUsageRecorded"/> et swappé atomiquement dans le
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>.
/// 
/// <b>Séparation des responsabilités</b> : <c>IndexedItem</c> porte uniquement les
/// données persistées (DB + cache). Les propriétés UI (NativeIcon, Score, IsInfoBlock)
/// vivent dans <see cref="SearchResult"/>, construit à la volée par le SearchService.
/// 
/// <b>Pré-normalisation (Point #5)</b> : <see cref="NameNormalized"/> et
/// <see cref="DescriptionLower"/> sont calculés une seule fois à la construction,
/// éliminant ~4000 allocations ToLowerInvariant()/StripEmojis() par frappe
/// sur le hot path de recherche.
/// </summary>
public sealed class IndexedItem
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ResultType Type { get; init; }
    public DateTime LastUsed { get; init; }
    public int UseCount { get; init; }

    /// <summary>
    /// Nom nettoyé (emojis retirés) et mis en minuscules, calculé une seule fois.
    /// Utilisé par SearchAlgorithms sur le hot path pour éviter les allocations répétées.
    /// </summary>
    public string NameNormalized { get; init; } = string.Empty;
    
    /// <summary>
    /// Description en minuscules, calculée une seule fois.
    /// Utilisée par le scoring de description pour éviter les allocations répétées.
    /// </summary>
    public string DescriptionLower { get; init; } = string.Empty;
    
    /// <summary>
    /// Chemin complet en minuscules, calculé une seule fois.
    /// Utilisé par le path fuzzy matching pour éviter ToLowerInvariant() sur le hot path.
    /// </summary>
    public string PathLower { get; init; } = string.Empty;

    /// <summary>
    /// Crée une copie avec UseCount+1 et LastUsed mis à jour.
    /// Utilisé par <see cref="Services.IndexingService.RecordUsage"/> pour
    /// le swap atomique dans le ConcurrentDictionary (résout le data race).
    /// </summary>
    public IndexedItem WithUsageRecorded() => new()
    {
        Path = Path,
        Name = Name,
        Description = Description,
        Type = Type,
        UseCount = UseCount + 1,
        LastUsed = DateTime.UtcNow,
        NameNormalized = NameNormalized,
        DescriptionLower = DescriptionLower,
        PathLower = PathLower
    };

    /// <summary>
    /// Convertit en <see cref="SearchResult"/> pour l'affichage dans l'UI.
    /// Les propriétés UI (NativeIcon, Score, DisplayIcon) sont initialisées
    /// à leurs valeurs par défaut et enrichies ensuite par le SearchService.
    /// </summary>
    public SearchResult ToSearchResult() => new()
    {
        Path = Path,
        Name = Name,
        Description = Description,
        Type = Type,
        LastUsed = LastUsed,
        UseCount = UseCount
    };
    
    /// <summary>
    /// Fabrique centralisée : crée un IndexedItem avec les champs normalisés pré-calculés.
    /// Tous les sites de création doivent passer par cette méthode pour garantir que
    /// NameNormalized et DescriptionLower sont toujours initialisés.
    /// </summary>
    public static IndexedItem Create(string path, string name, string description, ResultType type,
        int useCount = 0, DateTime lastUsed = default) => new()
    {
        Path = path,
        Name = name,
        Description = description,
        Type = type,
        UseCount = useCount,
        LastUsed = lastUsed,
        NameNormalized = Services.SearchAlgorithms.StripEmojis(name).ToLowerInvariant(),
        DescriptionLower = description.ToLowerInvariant(),
        PathLower = path.ToLowerInvariant()
    };
}
