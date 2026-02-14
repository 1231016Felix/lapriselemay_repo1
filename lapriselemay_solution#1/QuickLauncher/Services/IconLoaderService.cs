using System.Diagnostics;
using QuickLauncher.Models;

using Application = System.Windows.Application;

namespace QuickLauncher.Services;

/// <summary>
/// Service de chargement d'icônes natives pour les résultats de recherche.
/// Découple le modèle SearchResult de la logique d'extraction (IconExtractorService).
/// 
/// Amélioration #1 : Le SearchResult ne déclenche plus lui-même le chargement.
/// Amélioration #5 : Le tracking des chargements en cours évite les doublons.
/// </summary>
public sealed class IconLoaderService : IIconLoader
{
    /// <summary>
    /// Ensemble des chemins dont le chargement est en cours, pour éviter les doublons.
    /// </summary>
    private readonly HashSet<string> _loadingPaths = [];
    private readonly object _lock = new();

    /// <summary>
    /// Types de résultats pour lesquels on charge une icône native.
    /// </summary>
    private static bool ShouldLoadIcon(ResultType type) => type is
        ResultType.Application or ResultType.StoreApp or ResultType.File or
        ResultType.Folder or ResultType.Script or ResultType.Bookmark;

    public async Task LoadIconsAsync(IReadOnlyList<SearchResult> results, CancellationToken cancellationToken = default)
    {
        // Filtrer les résultats éligibles qui n'ont pas encore d'icône
        var toLoad = new List<SearchResult>();

        foreach (var result in results)
        {
            if (cancellationToken.IsCancellationRequested) return;

            if (!ShouldLoadIcon(result.Type) || result.HasNativeIcon)
                continue;

            lock (_lock)
            {
                // Éviter les chargements en doublon (Amélioration #5)
                if (!_loadingPaths.Add(result.Path))
                    continue;
            }

            toLoad.Add(result);
        }

        if (toLoad.Count == 0) return;

        // Charger en parallèle sur le thread pool
        var tasks = toLoad.Select(result => Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                var icon = IconExtractorService.GetIcon(result.Path);

                if (icon == null || cancellationToken.IsCancellationRequested) return;

                // Dispatcher vers le thread UI pour mettre à jour la propriété bindée
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    result.NativeIcon = icon;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IconLoader] Error loading icon for '{result.Name}': {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _loadingPaths.Remove(result.Path);
                }
            }
        }, cancellationToken));

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Recherche annulée, normal
        }
    }
}
