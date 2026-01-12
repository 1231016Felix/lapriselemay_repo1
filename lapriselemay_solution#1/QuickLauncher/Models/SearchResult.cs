namespace QuickLauncher.Models;

/// <summary>
/// Types de résultats de recherche.
/// </summary>
public enum ResultType
{
    Application,
    StoreApp,
    File,
    Folder,
    Script,
    WebSearch,
    Command,
    Calculator,
    SystemCommand,
    SearchHistory
}

/// <summary>
/// Résultat de recherche avec scoring et métadonnées.
/// </summary>
public sealed class SearchResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ResultType Type { get; set; }
    public int Score { get; set; }
    public DateTime LastUsed { get; set; }
    public int UseCount { get; set; }
    
    private string? _customIcon;
    
    public string DisplayIcon
    {
        get => _customIcon ?? GetDefaultIcon();
        set => _customIcon = value;
    }
    
    private string GetDefaultIcon() => Type switch
    {
        ResultType.Application => "🚀",
        ResultType.StoreApp => "🪟",
        ResultType.File => "📄",
        ResultType.Folder => "📁",
        ResultType.Script => "⚡",
        ResultType.WebSearch => "🔍",
        ResultType.Command => "⌨️",
        ResultType.Calculator => "🧮",
        ResultType.SystemCommand => "⚙️",
        ResultType.SearchHistory => "🕐",
        _ => "📌"
    };
    
    public override string ToString() => $"{DisplayIcon} {Name}";
}
