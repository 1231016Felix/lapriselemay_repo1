using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WallpaperManager.Models;

/// <summary>
/// IDs des collections système (non modifiables).
/// </summary>
public static class SystemCollectionIds
{
    public const string Favorites = "__favorites__";
    public const string Dark = "__dark__";
    public const string Light = "__light__";
    public const string Animated = "__animated__";
    
    public static bool IsSystemCollection(string? id) =>
        id == Favorites || id == Dark || id == Light || id == Animated;
    
    public static bool IsBrightnessCollection(string? id) =>
        id == Dark || id == Light;
}

/// <summary>
/// Représente une collection de fonds d'écran regroupés par l'utilisateur.
/// </summary>
public class Collection : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    private string _name = "Nouvelle collection";
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }
    
    private string _icon = "📁";
    public string Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                OnPropertyChanged();
            }
        }
    }
    
    private string? _description;
    public string? Description
    {
        get => _description;
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged();
            }
        }
    }
    
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    
    private List<string> _wallpaperIds = [];
    
    // HashSet pour les recherches rapides O(1)
    private HashSet<string>? _wallpaperIdsSet;
    
    /// <summary>
    /// Liste des IDs de wallpapers dans cette collection.
    /// </summary>
    public List<string> WallpaperIds
    {
        get => _wallpaperIds;
        set
        {
            _wallpaperIds = value ?? [];
            _wallpaperIdsSet = null; // Invalider le cache
            OnPropertyChanged();
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
    
    /// <summary>
    /// Obtient un HashSet pour des recherches rapides (lazy-loaded).
    /// </summary>
    private HashSet<string> WallpaperIdsSet => _wallpaperIdsSet ??= new HashSet<string>(_wallpaperIds);
    
    /// <summary>
    /// Nombre de wallpapers dans la collection.
    /// </summary>
    [JsonIgnore]
    public int Count => _wallpaperIds.Count;
    
    /// <summary>
    /// Indique si la collection est vide.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => _wallpaperIds.Count == 0;
    
    /// <summary>
    /// Texte formaté du nombre d'éléments.
    /// </summary>
    [JsonIgnore]
    public string CountText => Count switch
    {
        0 => "Vide",
        1 => "1 fond d'écran",
        _ => $"{Count} fonds d'écran"
    };
    
    /// <summary>
    /// Ajoute un wallpaper à la collection s'il n'y est pas déjà.
    /// </summary>
    /// <returns>True si ajouté, false si déjà présent</returns>
    public bool AddWallpaper(string wallpaperId)
    {
        if (string.IsNullOrEmpty(wallpaperId) || WallpaperIdsSet.Contains(wallpaperId))
            return false;
        
        _wallpaperIds.Add(wallpaperId);
        _wallpaperIdsSet?.Add(wallpaperId);
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
        return true;
    }
    
    /// <summary>
    /// Retire un wallpaper de la collection.
    /// </summary>
    /// <returns>True si retiré, false si non trouvé</returns>
    public bool RemoveWallpaper(string wallpaperId)
    {
        if (string.IsNullOrEmpty(wallpaperId))
            return false;
        
        var removed = _wallpaperIds.Remove(wallpaperId);
        if (removed)
        {
            _wallpaperIdsSet?.Remove(wallpaperId);
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CountText));
        }
        return removed;
    }
    
    /// <summary>
    /// Vérifie si un wallpaper est dans la collection.
    /// Utilise un HashSet pour une recherche O(1).
    /// </summary>
    public bool Contains(string wallpaperId)
        => !string.IsNullOrEmpty(wallpaperId) && WallpaperIdsSet.Contains(wallpaperId);
    
    /// <summary>
    /// Force la notification de changement du compteur.
    /// Utile quand WallpaperIds est modifié directement (ex: via SettingsService).
    /// Invalide également le cache HashSet.
    /// </summary>
    public void NotifyCountChanged()
    {
        _wallpaperIdsSet = null; // Invalider le cache car la liste a pu changer
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }
}
