using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Service de gestion des paramètres et données de l'application.
/// Thread-safe avec sauvegarde automatique différée.
/// </summary>
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
    
    // Timer pour la sauvegarde automatique différée
    private static System.Timers.Timer? _autoSaveTimer;
    private const int AutoSaveDelayMs = 5000; // 5 secondes
    
    /// <summary>
    /// Paramètres actuels de l'application.
    /// Note: Les modifications sont thread-safe mais les objets retournés peuvent être modifiés.
    /// Utilisez MarkDirty() après modification.
    /// </summary>
    public static AppSettings Current
    {
        get { lock (_lock) return _current; }
        private set { lock (_lock) _current = value; }
    }
    
    /// <summary>
    /// Liste en lecture seule des wallpapers.
    /// Pour modifier, utilisez les méthodes AddWallpaper/RemoveWallpaper.
    /// </summary>
    public static IReadOnlyList<Wallpaper> Wallpapers
    {
        get { lock (_lock) return _wallpapers.ToList().AsReadOnly(); }
    }
    
    /// <summary>
    /// Liste en lecture seule des wallpapers dynamiques.
    /// Pour modifier, utilisez les méthodes AddDynamicWallpaper/RemoveDynamicWallpaper.
    /// </summary>
    public static IReadOnlyList<DynamicWallpaper> DynamicWallpapers
    {
        get { lock (_lock) return _dynamicWallpapers.ToList().AsReadOnly(); }
    }
    
    /// <summary>
    /// Liste en lecture seule des collections.
    /// Pour modifier, utilisez les méthodes AddCollection/RemoveCollection.
    /// </summary>
    public static IReadOnlyList<Collection> Collections
    {
        get { lock (_lock) return _collections.ToList().AsReadOnly(); }
    }
    
    /// <summary>
    /// Nombre total de wallpapers (accès rapide sans copie de liste).
    /// </summary>
    public static int WallpaperCount
    {
        get { lock (_lock) return _wallpapers.Count; }
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
            
            // Initialiser le timer de sauvegarde automatique
            InitializeAutoSave();
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
    
    private static void InitializeAutoSave()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = new System.Timers.Timer(AutoSaveDelayMs)
        {
            AutoReset = false
        };
        _autoSaveTimer.Elapsed += (_, _) =>
        {
            if (_isDirty)
            {
                Save();
            }
        };
    }
    
    /// <summary>
    /// Déclenche une sauvegarde différée (après le délai AutoSaveDelayMs).
    /// Utile pour éviter les sauvegardes multiples lors de modifications rapides.
    /// </summary>
    public static void ScheduleSave()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Start();
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
            
            // Écrire dans des fichiers temporaires d'abord pour éviter la corruption
            var settingsTempPath = SettingsPath + ".tmp";
            var dataTempPath = DataPath + ".tmp";
            
            File.WriteAllText(settingsTempPath, settingsJson);
            File.WriteAllText(dataTempPath, dataJson);
            
            // Remplacer les fichiers originaux
            File.Move(settingsTempPath, SettingsPath, overwrite: true);
            File.Move(dataTempPath, DataPath, overwrite: true);
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
            
            // Écrire dans des fichiers temporaires d'abord
            var settingsTempPath = SettingsPath + ".tmp";
            var dataTempPath = DataPath + ".tmp";
            
            await File.WriteAllTextAsync(settingsTempPath, settingsJson, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(dataTempPath, dataJson, cancellationToken).ConfigureAwait(false);
            
            // Remplacer les fichiers originaux
            File.Move(settingsTempPath, SettingsPath, overwrite: true);
            File.Move(dataTempPath, DataPath, overwrite: true);
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
    
    /// <summary>
    /// Ajoute un wallpaper. Évite les doublons par chemin de fichier.
    /// </summary>
    /// <returns>True si ajouté, false si doublon</returns>
    public static bool AddWallpaper(Wallpaper wallpaper)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        
        lock (_lock)
        {
            if (_wallpapers.Exists(w => w.FilePath.Equals(wallpaper.FilePath, StringComparison.OrdinalIgnoreCase)))
                return false;
            
            _wallpapers.Add(wallpaper);
            _isDirty = true;
        }
        
        ScheduleSave();
        return true;
    }
    
    /// <summary>
    /// Ajoute plusieurs wallpapers en une seule opération.
    /// </summary>
    /// <returns>Nombre de wallpapers ajoutés</returns>
    public static int AddWallpapers(IEnumerable<Wallpaper> wallpapers)
    {
        ArgumentNullException.ThrowIfNull(wallpapers);
        
        var count = 0;
        lock (_lock)
        {
            foreach (var wallpaper in wallpapers)
            {
                if (!_wallpapers.Exists(w => w.FilePath.Equals(wallpaper.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    _wallpapers.Add(wallpaper);
                    count++;
                }
            }
            
            if (count > 0)
                _isDirty = true;
        }
        
        if (count > 0)
            ScheduleSave();
        
        return count;
    }
    
    public static void RemoveWallpaper(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        lock (_lock)
        {
            var wallpaper = _wallpapers.Find(w => w.Id == id);
            if (wallpaper != null)
            {
                _wallpapers.Remove(wallpaper);
                
                // Retirer aussi des collections
                foreach (var collection in _collections)
                {
                    collection.WallpaperIds.Remove(id);
                }
                
                _isDirty = true;
            }
        }
        
        ScheduleSave();
    }
    
    public static Wallpaper? GetWallpaper(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock) return _wallpapers.Find(w => w.Id == id);
    }
    
    /// <summary>
    /// Recherche un wallpaper par son chemin de fichier.
    /// </summary>
    public static Wallpaper? GetWallpaperByPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        lock (_lock) return _wallpapers.Find(w => w.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
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
        
        ScheduleSave();
    }
    
    public static void RemoveDynamicWallpaper(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        lock (_lock)
        {
            _dynamicWallpapers.RemoveAll(d => d.Id == id);
            _isDirty = true;
        }
        
        ScheduleSave();
    }
    
    public static DynamicWallpaper? GetDynamicWallpaper(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock) return _dynamicWallpapers.Find(d => d.Id == id);
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
        
        ScheduleSave();
    }
    
    public static void RemoveCollection(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        
        lock (_lock)
        {
            _collections.RemoveAll(c => c.Id == id);
            _isDirty = true;
        }
        
        ScheduleSave();
    }
    
    public static Collection? GetCollection(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock) return _collections.Find(c => c.Id == id);
    }
    
    public static void AddWallpaperToCollection(string collectionId, string wallpaperId)
    {
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(wallpaperId)) return;
        
        lock (_lock)
        {
            var collection = _collections.Find(c => c.Id == collectionId);
            if (collection != null && !collection.WallpaperIds.Contains(wallpaperId))
            {
                collection.WallpaperIds.Add(wallpaperId);
                _isDirty = true;
            }
        }
        
        ScheduleSave();
    }
    
    public static void RemoveWallpaperFromCollection(string collectionId, string wallpaperId)
    {
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(wallpaperId)) return;
        
        lock (_lock)
        {
            var collection = _collections.Find(c => c.Id == collectionId);
            if (collection != null && collection.WallpaperIds.Remove(wallpaperId))
            {
                _isDirty = true;
            }
        }
        
        ScheduleSave();
    }
    
    public static List<Wallpaper> GetWallpapersInCollection(string collectionId)
    {
        if (string.IsNullOrEmpty(collectionId)) return [];
        
        lock (_lock)
        {
            var collection = _collections.Find(c => c.Id == collectionId);
            if (collection == null || collection.WallpaperIds.Count == 0) return [];
            
            // Utiliser un HashSet pour la recherche O(1) au lieu de O(n)
            var idsSet = new HashSet<string>(collection.WallpaperIds);
            return _wallpapers.Where(w => idsSet.Contains(w.Id)).ToList();
        }
    }
    
    public static void MarkDirty()
    {
        _isDirty = true;
        ScheduleSave();
    }
    
    public static bool IsDirty => _isDirty;
    
    private static void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(AppDataPath))
            Directory.CreateDirectory(AppDataPath);
        
        var wallpaperFolder = Current.WallpaperFolder;
        if (!Directory.Exists(wallpaperFolder))
            Directory.CreateDirectory(wallpaperFolder);
    }
    
    /// <summary>
    /// Libère les ressources du timer. À appeler lors de la fermeture de l'application.
    /// </summary>
    public static void Shutdown()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
        
        // Sauvegarder si nécessaire
        if (_isDirty)
        {
            Save();
        }
    }
    
    private sealed class WallpaperData
    {
        public List<Wallpaper> Wallpapers { get; set; } = [];
        public List<DynamicWallpaper> DynamicWallpapers { get; set; } = [];
        public List<Collection> Collections { get; set; } = [];
    }
}
