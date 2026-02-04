using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
    Bookmark,  // Favoris des navigateurs (Chrome, Edge, Firefox)
    Note       // Notes rapides de l'utilisateur
}

/// <summary>
/// Résultat de recherche avec scoring et métadonnées.
/// Implémente INotifyPropertyChanged pour notifier l'UI des changements d'icône.
/// </summary>
public sealed class SearchResult : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
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
    /// Chargement paresseux avec notification.
    /// </summary>
    public ImageSource? NativeIcon
    {
        get
        {
            if (!_nativeIconLoaded && ShouldLoadNativeIcon())
            {
                _nativeIconLoaded = true;
                // Charger de manière asynchrone et notifier
                _ = LoadIconInternalAsync();
            }
            return _nativeIcon;
        }
        set
        {
            _nativeIcon = value;
            _nativeIconLoaded = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNativeIcon));
        }
    }
    
    private async Task LoadIconInternalAsync()
    {
        try
        {
            var path = Path;
            var name = Name;
            System.Diagnostics.Debug.WriteLine($"[SearchResult] Starting icon load for '{name}' path='{path}' type={Type}");
            
            var icon = await Task.Run(() => Services.IconExtractorService.GetIcon(path));
            
            System.Diagnostics.Debug.WriteLine($"[SearchResult] Icon loaded for '{name}': {(icon != null ? "OK" : "NULL")}");
            
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    _nativeIcon = icon;
                    System.Diagnostics.Debug.WriteLine($"[SearchResult] PropertyChanged fired for '{name}'");
                    OnPropertyChanged(nameof(NativeIcon));
                    OnPropertyChanged(nameof(HasNativeIcon));
                });
            }
            else
            {
                _nativeIcon = icon;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchResult] Icon error for '{Name}': {ex.Message}");
        }
    }
    
    /// <summary>
    /// Indique si une icône native valide est disponible.
    /// </summary>
    public bool HasNativeIcon => _nativeIcon != null;
    
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
            ResultType.Bookmark => true,  // Permettre le chargement d'icônes pour les favoris
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
        ResultType.Note => "📝",
        _ => "📌"
    };
    
    public override string ToString() => $"{DisplayIcon} {Name}";
}
