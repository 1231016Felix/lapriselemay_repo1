using System.Windows.Media;

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
    SearchHistory,
    SystemControl,
    Bookmark  // Favoris des navigateurs (Chrome, Edge, Firefox)
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
    private ImageSource? _nativeIcon;
    private bool _nativeIconLoaded;
    
    /// <summary>
    /// Icône native extraite du fichier (ImageSource).
    /// </summary>
    public ImageSource? NativeIcon
    {
        get
        {
            if (!_nativeIconLoaded && ShouldLoadNativeIcon())
            {
                _nativeIconLoaded = true;
                _nativeIcon = Services.IconExtractorService.GetIcon(Path);
            }
            return _nativeIcon;
        }
        set
        {
            _nativeIcon = value;
            _nativeIconLoaded = true;
        }
    }
    
    /// <summary>
    /// Indique si une icône native est disponible.
    /// </summary>
    public bool HasNativeIcon => NativeIcon != null;
    
    /// <summary>
    /// Icône emoji de fallback.
    /// </summary>
    public string DisplayIcon
    {
        get => _customIcon ?? GetDefaultIcon();
        set => _customIcon = value;
    }
    
    /// <summary>
    /// Détermine si on doit charger l'icône native pour ce type de résultat.
    /// </summary>
    private bool ShouldLoadNativeIcon()
    {
        return Type switch
        {
            ResultType.Application => true,
            ResultType.StoreApp => true,
            ResultType.File => true,
            ResultType.Folder => true,
            ResultType.Script => true,
            _ => false
        };
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
        ResultType.SystemControl => "🎛️",
        ResultType.Bookmark => "⭐",
        _ => "📌"
    };
    
    public override string ToString() => $"{DisplayIcon} {Name}";
}
