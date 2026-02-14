namespace QuickLauncher.Services;

/// <summary>
/// Interface pour le service de recherche universel.
/// </summary>
public interface IUniversalSearchService
{
    /// <summary>
    /// Profondeur maximale de recherche (configurable).
    /// </summary>
    int MaxSearchDepth { get; set; }

    /// <summary>
    /// Recherche des fichiers en utilisant la meilleure méthode disponible.
    /// </summary>
    Task<List<Models.SearchResult>> SearchAsync(
        string query,
        string? searchScope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vérifie quel moteur de recherche est disponible.
    /// </summary>
    SearchEngineStatus GetAvailableEngine();

    /// <summary>
    /// Force une revérification de tous les moteurs de recherche.
    /// </summary>    void RefreshEngineDetection();

    /// <summary>
    /// Retourne des informations détaillées sur le moteur de recherche disponible.
    /// </summary>
    SearchEngineInfo GetEngineInfo(bool forceRefresh = false);

    /// <summary>
    /// Vide le cache de recherche.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Retourne les statistiques du cache.
    /// </summary>
    (int EntryCount, int TotalResults) GetCacheStats();

    /// <summary>
    /// Retourne la liste des dossiers par défaut scannés.
    /// </summary>
    string[] GetDefaultSearchPaths();
}