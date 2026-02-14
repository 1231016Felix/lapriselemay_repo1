using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Interface pour le chargement asynchrone d'icônes natives.
/// Découple le modèle SearchResult de la logique d'extraction d'icônes.
/// </summary>
public interface IIconLoader
{
    /// <summary>
    /// Charge les icônes natives pour une liste de résultats de recherche.
    /// Met à jour <see cref="SearchResult.NativeIcon"/> via le Dispatcher UI.
    /// </summary>
    Task LoadIconsAsync(IReadOnlyList<SearchResult> results, CancellationToken cancellationToken = default);
}
