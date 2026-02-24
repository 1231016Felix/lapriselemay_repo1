using QuickLauncher.Models;

namespace QuickLauncher.Services;

/// <summary>
/// Gestion centralisée des items épinglés.
/// Responsabilité unique : CRUD épingles, réordonnement, conversion en SearchResult.
/// Extrait de LauncherViewModel (Point #2).
/// </summary>
public sealed class PinnedItemsManager
{
    private readonly ISettingsProvider _settingsProvider;

    private AppSettings Settings => _settingsProvider.Current;

    public PinnedItemsManager(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    /// <summary>Nombre d'items épinglés.</summary>
    public int Count => Settings.Search.PinnedItems.Count;

    /// <summary>Vérifie si un chemin est épinglé.</summary>
    public bool IsPinned(string path) => Settings.Search.IsPinned(path);

    /// <summary>
    /// Vérifie si un résultat à l'index donné dans la liste affichée est un item épinglé.
    /// </summary>
    public bool IsResultPinned(int resultIndex, int resultCount)
    {
        if (resultIndex < 0 || resultIndex >= resultCount) return false;
        return resultIndex < Settings.Search.PinnedItems.Count;
    }

    /// <summary>
    /// Épingle un item et sauvegarde.
    /// </summary>
    /// <returns>Message de notification.</returns>
    public string Pin(string name, string path, ResultType type, string? icon)
    {
        _settingsProvider.Update(s => s.Search.PinItem(name, path, type, icon));
        return "⭐ Épinglé";
    }

    /// <summary>
    /// Désépingle un item et sauvegarde.
    /// </summary>
    /// <returns>Message de notification.</returns>
    public string Unpin(string path)
    {
        _settingsProvider.Update(s => s.Search.UnpinItem(path));
        return "📌 Désépinglé";
    }

    /// <summary>
    /// Réordonne un item épinglé par drag &amp; drop et sauvegarde.
    /// </summary>
    public void Reorder(int fromIndex, int toIndex)
    {
        _settingsProvider.Update(s => s.Search.ReorderPinnedItem(fromIndex, toIndex));
    }

    /// <summary>
    /// Retourne les items épinglés convertis en SearchResult, triés par ordre.
    /// </summary>
    public List<SearchResult> GetPinnedResults()
    {
        return Settings.Search.PinnedItems
            .OrderBy(p => p.Order)
            .Select(p => p.ToSearchResult())
            .ToList();
    }
}
