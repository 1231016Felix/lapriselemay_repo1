namespace QuickLauncher.Models;

public enum ResultType
{
    Application,
    File,
    Folder,
    Script,
    WebSearch,
    Command,
    Calculator,
    SystemCommand,
    SearchHistory
}

public class SearchResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ResultType Type { get; set; }
    public string? IconPath { get; set; }
    public int Score { get; set; }
    public DateTime LastUsed { get; set; }
    public int UseCount { get; set; }
    
    // Icône personnalisable (pour les commandes système)
    private string? _customIcon;
    
    public string DisplayIcon
    {
        get => _customIcon ?? GetDefaultIcon();
        set => _customIcon = value;
    }
    
    private string GetDefaultIcon() => Type switch
    {
        ResultType.Application => "🚀",
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
}
