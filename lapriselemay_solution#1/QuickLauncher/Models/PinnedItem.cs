namespace QuickLauncher.Models;

/// <summary>
/// Item épinglé dans la barre de raccourcis du launcher.
/// </summary>
public sealed class PinnedItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ResultType Type { get; set; }
    public string? Icon { get; set; }
    public DateTime PinnedAt { get; set; }
    public int Order { get; set; }
    
    public SearchResult ToSearchResult()
    {
        var correctedType = GetCorrectedType();
        return new SearchResult
        {
            Name = Name, Path = Path, Type = correctedType,
            Description = "⭐ Épinglé",
            DisplayIcon = Icon ?? GetDefaultIcon(),
            Score = 10000 + (1000 - Order)
        };
    }

    /// <summary>
    /// Corrige le type si incohérent avec le chemin (migration de données).
    /// Ex: un favori (URL) sauvé comme AppControl au lieu de Bookmark.
    /// </summary>
    private ResultType GetCorrectedType()
    {
        if (!string.IsNullOrEmpty(Path) && (Path.StartsWith("http://") || Path.StartsWith("https://")))
            return ResultType.Bookmark;
        if (!string.IsNullOrEmpty(Path) && Path.Contains('!'))
            return ResultType.StoreApp;
        return Type;
    }
    
    private string GetDefaultIcon() => Type switch
    {
        ResultType.Application => "🚀", ResultType.StoreApp => "🪧",
        ResultType.File => "📄", ResultType.Folder => "📁",
        ResultType.Script => "⚡", ResultType.Bookmark => "⭐", _ => "📌"
    };
}