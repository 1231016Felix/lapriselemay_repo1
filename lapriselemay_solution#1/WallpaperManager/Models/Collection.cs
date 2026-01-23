using System.ComponentModel;

namespace WallpaperManager.Models;

public class Collection : INotifyPropertyChanged
{
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }
    }
    
    public string? Description { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Liste des IDs de wallpapers dans cette collection
    /// </summary>
    public List<string> WallpaperIds { get; set; } = [];
    
    public int Count => WallpaperIds.Count;
    
    public event PropertyChangedEventHandler? PropertyChanged;
}
