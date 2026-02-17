namespace QuickLauncher.Models.Settings;

/// <summary>
/// Paramètres liés à la recherche et à l'indexation.
/// Regroupe : profondeur, max résultats, scoring, historique.
/// </summary>
public sealed class SearchSettings
{
    // === Recherche générale ===
    public int MaxResults { get; set; } = Constants.DefaultMaxResults;
    public int SearchDepth { get; set; } = Constants.DefaultSearchDepth;
    public bool IndexHiddenFolders { get; set; }
    public bool IndexBrowserBookmarks { get; set; } = true;
    public bool EnableAliases { get; set; } = true;
    
    // === Recherche système (:find) ===
    public int SystemSearchDepth { get; set; } = 5;
    
    // === Historique ===
    public bool EnableSearchHistory { get; set; } = true;
    public int MaxSearchHistory { get; set; } = Constants.DefaultMaxSearchHistory;
    public List<HistoryItem> SearchHistory { get; set; } = [];
    
    // === Scoring ===
    public ScoringWeights ScoringWeights { get; set; } = new();
    
    // === Dossiers et fichiers indexés ===
    public List<string> IndexedFolders { get; set; } = GetDefaultIndexedFolders();
    public List<string> FileExtensions { get; set; } = [..Constants.DefaultFileExtensions];
    
    // === Items épinglés ===
    public List<PinnedItem> PinnedItems { get; set; } = [];
    
    // === Scripts personnalisés ===
    public List<CustomScript> Scripts { get; set; } = [];
    
    // === Moteurs de recherche web ===
    public List<WebSearchEngine> SearchEngines { get; set; } = GetDefaultSearchEngines();
    
    // === Surveillance fichiers ===
    public bool EnableFileWatcher { get; set; } = true;
    
    // === Réindexation automatique ===
    public bool AutoReindexEnabled { get; set; }
    public AutoReindexMode AutoReindexMode { get; set; } = AutoReindexMode.Interval;
    public int AutoReindexIntervalMinutes { get; set; } = 60;
    public string AutoReindexScheduledTime { get; set; } = "03:00";
    
    // === Gestion des items épinglés ===
    
    public void PinItem(string name, string path, ResultType type, string? icon = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (PinnedItems.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        
        PinnedItems.Add(new PinnedItem
        {
            Name = name, Path = path, Type = type, Icon = icon,
            PinnedAt = DateTime.Now, Order = PinnedItems.Count
        });
    }
    
    public bool UnpinItem(string path)
    {
        var item = PinnedItems.FirstOrDefault(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item == null) return false;
        PinnedItems.Remove(item);
        for (var i = 0; i < PinnedItems.Count; i++) PinnedItems[i].Order = i;
        return true;
    }
    
    public bool IsPinned(string path) =>
        PinnedItems.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    
    public void MovePinnedItemUp(string path)
    {
        var index = PinnedItems.FindIndex(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (index <= 0) return;
        (PinnedItems[index], PinnedItems[index - 1]) = (PinnedItems[index - 1], PinnedItems[index]);
        PinnedItems[index].Order = index;
        PinnedItems[index - 1].Order = index - 1;
    }
    
    public void MovePinnedItemDown(string path)
    {
        var index = PinnedItems.FindIndex(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= PinnedItems.Count - 1) return;
        (PinnedItems[index], PinnedItems[index + 1]) = (PinnedItems[index + 1], PinnedItems[index]);
        PinnedItems[index].Order = index;
        PinnedItems[index + 1].Order = index + 1;
    }
    
    /// <summary>
    /// Déplace un item épinglé d'une position à une autre (drag & drop).
    /// </summary>
    public void ReorderPinnedItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= PinnedItems.Count) return;
        if (toIndex < 0 || toIndex >= PinnedItems.Count) return;
        if (fromIndex == toIndex) return;
        
        var item = PinnedItems[fromIndex];
        PinnedItems.RemoveAt(fromIndex);
        PinnedItems.Insert(toIndex, item);
        
        // Recalculer tous les ordres
        for (var i = 0; i < PinnedItems.Count; i++)
            PinnedItems[i].Order = i;
    }
    
    public void AddToSearchHistory(HistoryItem item)
    {
        if (!EnableSearchHistory || item == null || string.IsNullOrWhiteSpace(item.Path)) return;
        SearchHistory.RemoveAll(h => h.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase));
        SearchHistory.Insert(0, item);
        if (SearchHistory.Count > MaxSearchHistory)
            SearchHistory.RemoveRange(MaxSearchHistory, SearchHistory.Count - MaxSearchHistory);
    }
    
    public void ClearSearchHistory() => SearchHistory.Clear();
    
    private static List<string> GetDefaultIndexedFolders() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    ];
    
    private static List<WebSearchEngine> GetDefaultSearchEngines() =>
    [
        new() { Prefix = "g", Name = "Google", UrlTemplate = "https://www.google.com/search?q={query}" },
        new() { Prefix = "yt", Name = "YouTube", UrlTemplate = "https://www.youtube.com/results?search_query={query}" },
        new() { Prefix = "gh", Name = "GitHub", UrlTemplate = "https://github.com/search?q={query}" },
        new() { Prefix = "so", Name = "Stack Overflow", UrlTemplate = "https://stackoverflow.com/search?q={query}" }
    ];
}
