using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Abstraction de la couche de persistance SQLite pour les items indexés (Point #6).
/// 
/// Sépare les responsabilités : IndexingService orchestre l'indexation et gère le cache mémoire,
/// IndexRepository encapsule tout l'accès SQLite (CRUD, bulk, purge).
/// 
/// <b>Stratégie de connexion héritée d'IndexingService :</b>
/// <list type="bullet">
///   <item>Connexion persistante + lock : opérations CRUD rapides (RecordUsage, AddOrUpdate, Remove)</item>
///   <item>Connexions éphémères : opérations bulk longues (SaveBulkAsync, PurgeStaleAsync)</item>
/// </list>
/// </summary>
public interface IIndexRepository : IDisposable
{
    /// <summary>Charge tous les items depuis la DB.</summary>
    List<IndexedItem> LoadAll(CancellationToken token = default);
    
    /// <summary>Sauvegarde en masse avec préservation des UseCount/LastUsed existants.</summary>
    Task SaveBulkAsync(List<IndexedItem> items, Action<int>? onProgress, CancellationToken token);
    
    /// <summary>Incrémente UseCount et met à jour LastUsed.</summary>
    void RecordUsage(string path);
    
    /// <summary>Insère ou met à jour un item unique (préserve UseCount existant).</summary>
    void AddOrUpdate(IndexedItem item);
    
    /// <summary>Supprime un item par son chemin.</summary>
    /// <returns>True si un item a été effectivement supprimé.</returns>
    bool Remove(string path);
    
    /// <summary>Supprime tous les items dont le chemin commence par le dossier spécifié.</summary>
    /// <returns>Nombre d'items supprimés.</returns>
    int RemoveByFolder(string folderPath);
    
    /// <summary>Supprime les items dont IndexedAt est antérieur au seuil (post-réindexation).</summary>
    /// <returns>Nombre d'items purgés.</returns>
    Task<int> PurgeStaleAsync(CancellationToken token);
    
    /// <summary>Supprime des items spécifiques par chemin (purge volatiles périmés).</summary>
    Task<int> PurgePathsAsync(IReadOnlyList<string> paths, CancellationToken token);
}
