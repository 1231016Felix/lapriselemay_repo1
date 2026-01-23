using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

public static class SettingsService
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WallpaperManager");
    
    private static readonly string SettingsPath = Path.Combine(AppDataPath, "settings.json");
    private static readonly string DataPath = Path.Combine(AppDataPath, "wallpapers.json");
    
    private static readonly Lock _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    
    private static AppSettings _current = new();
    private static List<Wallpaper> _wallpapers = [];
    private static List<DynamicWallpaper> _dynamicWallpapers = [];
    private static List<Collection> _collections = [];
    private static volatile bool _isDirty;
    
    public static AppSettings Current
    {
        get { lock (_lock) return _current; }
        private set { lock (_lock) _current = value; }
    }
    
    public static IReadOnlyList<Wallpaper> Wallpapers
    {
        get { lock (_lock) return _wallpapers.AsReadOnly(); }
    }
    
    public static IReadOnlyList<DynamicWallpaper> DynamicWallpapers
    {
        get { lock (_lock) return _dynamicWallpapers.AsReadOnly(); }
    }
    
    public static IReadOnlyList<Collection> Collections
    {
        get { lock (_lock) return _collections.AsReadOnly(); }
    }

    public static void Load()
    {
        try
        {
            EnsureDirectoriesExist();
            
            lock (_lock)
            {
                // Charger les paramètres
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
                }
                
                // Charger les données
                if (File.Exists(DataPath))
                {
                    var json = File.ReadAllText(DataPath);
                    var data = JsonSerializer.Deserialize<WallpaperData>(json, JsonOptions);
                    if (data != null)
                    {
                        _wallpapers = data.Wallpapers ?? [];
                        _dynamicWallpapers = data.DynamicWallpapers ?? [];
                        _collections = data.Collections ?? [];
                    }
                }
                
                // Valider les chemins des wallpapers (retirer les fichiers supprimés)
                var removedCount = _wallpapers.RemoveAll(w => string.IsNullOrEmpty(w.FilePath) || !File.Exists(w.FilePath));
                if (removedCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Retiré {removedCount} wallpaper(s) avec fichiers manquants");
                    _isDirty = true;
                }
                else
                {
                    _isDirty = false;
                }
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur JSON chargement settings: {ex.Message}");
            _current = new AppSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur chargement settings: {ex.Message}");
            _current = new AppSettings();
        }
    }
    
    public static void Save()
    {
        try
        {
            EnsureDirectoriesExist();
            
            string settingsJson, dataJson;
            lock (_lock)
            {
                settingsJson = JsonSerializer.Serialize(_current, JsonOptions);
                var data = new WallpaperData 
                { 
                    Wallpapers = _wallpapers,
                    DynamicWallpapers = _dynamicWallpapers,
                    Collections = _collections
                };
                dataJson = JsonSerializer.Serialize(data, JsonOptions);
                _isDirty = false;
            }
            
            File.WriteAllText(SettingsPath, settingsJson);
            File.WriteAllText(DataPath, dataJson);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur I/O sauvegarde settings: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde settings: {ex.Message}");
        }
    }
    
    public static async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoriesExist();
            
            string settingsJson, dataJson;
            lock (_lock)
            {
                settingsJson = JsonSerializer.Serialize(_current, JsonOptions);
                var data = new WallpaperData 
                { 
                    Wallpapers = _wallpapers,
                    DynamicWallpapers = _dynamicWallpapers,
                    Collections = _collections
                };
                dataJson = JsonSerializer.Serialize(data, JsonOptions);
                _isDirty = false;
            }
            
            await File.WriteAllTextAsync(SettingsPath, settingsJson, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(DataPath, dataJson, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ignorer
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde async settings: {ex.Message}");
        }
    }
    
    // === WALLPAPERS ===
    public static void AddWallpaper(Wallpaper wallpaper)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        
        lock (_lock)
        {
            if (_wallpapers.Any(w => w.FilePath.Equals(wallpaper.FilePath, StringComparison.OrdinalIgnoreCase)))
                return;
            
            _wallpapers.Add(wallpaper);
            _isDirty = true;
        }
    }
    
    public static void RemoveWallpaper(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        lock (_lock)
        {
            var wallpaper = _wallpapers.FirstOrDefault(w => w.Id == id);
            if (wallpaper != null)
            {
                _wallpapers.Remove(wallpaper);
                _isDirty = true;
            }
        }
    }
    
    public static Wallpaper? GetWallpaper(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock) return _wallpapers.FirstOrDefault(w => w.Id == id);
    }
    
    // === DYNAMIC WALLPAPERS ===
    public static void AddDynamicWallpaper(DynamicWallpaper dynamicWallpaper)
    {
        ArgumentNullException.ThrowIfNull(dynamicWallpaper);
        
        lock (_lock)
        {
            _dynamicWallpapers.Add(dynamicWallpaper);
            _isDirty = true;
        }
    }
    
    public static void RemoveDynamicWallpaper(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        lock (_lock)
        {
            _dynamicWallpapers.RemoveAll(d => d.Id == id);
            _isDirty = true;
        }
    }
    
    public static DynamicWallpaper? GetDynamicWallpaper(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock) return _dynamicWallpapers.FirstOrDefault(d => d.Id == id);
    }
    
    // === COLLECTIONS ===
    public static void AddCollection(Collection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        
        lock (_lock)
        {
            _collections.Add(collection);
            _isDirty = true;
        }
    }
    
    public static void RemoveCollection(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        lock (_lock)
        {
            _collections.RemoveAll(c => c.Id == id);
            _isDirty = true;
        }
    }
    
    public static Collection? GetCollection(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock) return _collections.FirstOrDefault(c => c.Id == id);
    }
    
    public static void AddWallpaperToCollection(string collectionId, string wallpaperId)
    {
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(wallpaperId)) return;
        
        lock (_lock)
        {
            var collection = _collections.FirstOrDefault(c => c.Id == collectionId);
            if (collection != null && !collection.WallpaperIds.Contains(wallpaperId))
            {
                collection.WallpaperIds.Add(wallpaperId);
                _isDirty = true;
            }
        }
    }
    
    public static void RemoveWallpaperFromCollection(string collectionId, string wallpaperId)
    {
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(wallpaperId)) return;
        
        lock (_lock)
        {
            var collection = _collections.FirstOrDefault(c => c.Id == collectionId);
            if (collection != null)
            {
                collection.WallpaperIds.Remove(wallpaperId);
                _isDirty = true;
            }
        }
    }
    
    public static List<Wallpaper> GetWallpapersInCollection(string collectionId)
    {
        if (string.IsNullOrEmpty(collectionId)) return [];
        
        lock (_lock)
        {
            var collection = _collections.FirstOrDefault(c => c.Id == collectionId);
            if (collection == null) return [];
            
            return _wallpapers.Where(w => collection.WallpaperIds.Contains(w.Id)).ToList();
        }
    }
    
    public static void MarkDirty() => _isDirty = true;
    public static bool IsDirty => _isDirty;
    
    private static void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(AppDataPath))
            Directory.CreateDirectory(AppDataPath);
        
        if (!Directory.Exists(Current.WallpaperFolder))
            Directory.CreateDirectory(Current.WallpaperFolder);
    }
    
    private sealed class WallpaperData
    {
        public List<Wallpaper> Wallpapers { get; set; } = [];
        public List<DynamicWallpaper> DynamicWallpapers { get; set; } = [];
        public List<Collection> Collections { get; set; } = [];
    }
}
