using System.ComponentModel;
using System.Runtime.CompilerServices;
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
/// 
/// DTO pur : ne contient plus de logique de chargement d'icônes.
/// Le chargement est délégué à <see cref="Services.IIconLoader"/> (Amélioration #1).
/// Implémente INotifyPropertyChanged uniquement pour la notification WPF de NativeIcon.
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
    
    /// <summary>
    /// Si true, masque l'icône et le badge de catégorie (bloc info unifié, ex: météo).
    /// </summary>
    public bool IsInfoBlock { get; set; }
    public int Score { get; set; }
    public DateTime LastUsed { get; set; }
    public int UseCount { get; set; }
    
    private string? _customIcon;
    private ImageSource? _nativeIcon;
    
    /// <summary>
    /// Icône native extraite du fichier (ImageSource).
    /// Setter déclenche PropertyChanged pour mettre à jour le binding WPF.
    /// Le chargement est géré externalement par <see cref="Services.IIconLoader"/>.
    /// </summary>
    public ImageSource? NativeIcon
    {
        get => _nativeIcon;
        set
        {
            _nativeIcon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNativeIcon));
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
