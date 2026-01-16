using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Core.Configuration;

/// <summary>
/// Classe de base pour les paramètres d'application avec sérialisation JSON.
/// Utilise le pattern Singleton thread-safe avec lazy loading.
/// </summary>
/// <typeparam name="T">Type concret des paramètres (doit hériter de cette classe)</typeparam>
public abstract class JsonSettingsBase<T> where T : JsonSettingsBase<T>, new()
{
    private static T? _instance;
    private static readonly Lock _lock = new();
    private static string? _customPath;
    
    /// <summary>
    /// Options de sérialisation JSON partagées
    /// </summary>
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Nom de l'application (utilisé pour le chemin par défaut)
    /// </summary>
    protected abstract string AppName { get; }
    
    /// <summary>
    /// Nom du fichier de paramètres (défaut: settings.json)
    /// </summary>
    protected virtual string FileName => "settings.json";
    
    /// <summary>
    /// Dossier de stockage (défaut: ApplicationData)
    /// </summary>
    protected virtual Environment.SpecialFolder StorageFolder => Environment.SpecialFolder.ApplicationData;

    /// <summary>
    /// Chemin complet du fichier de paramètres
    /// </summary>
    [JsonIgnore]
    public string FilePath => _customPath ?? GetDefaultPath();

    /// <summary>
    /// Instance singleton des paramètres (thread-safe, lazy loading)
    /// </summary>
    [JsonIgnore]
    public static T Current
    {
        get
        {
            if (_instance is null)
            {
                lock (_lock)
                {
                    _instance ??= Load();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Définit un chemin personnalisé pour le fichier de paramètres
    /// </summary>
    /// <param name="path">Chemin complet du fichier</param>
    public static void SetCustomPath(string path)
    {
        _customPath = path;
        _instance = null; // Force le rechargement
    }

    /// <summary>
    /// Charge les paramètres depuis le fichier
    /// </summary>
    /// <param name="path">Chemin optionnel (utilise le chemin par défaut si null)</param>
    /// <returns>Instance des paramètres</returns>
    public static T Load(string? path = null)
    {
        var instance = new T();
        var filePath = path ?? instance.GetDefaultPath();
        
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur chargement paramètres: {ex.Message}");
        }
        
        // Créer les valeurs par défaut
        instance.SetDefaults();
        instance.Save(filePath);
        return instance;
    }

    /// <summary>
    /// Sauvegarde les paramètres dans le fichier
    /// </summary>
    /// <param name="path">Chemin optionnel (utilise le chemin par défaut si null)</param>
    public void Save(string? path = null)
    {
        var filePath = path ?? FilePath;
        
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize((T)this, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde paramètres: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sauvegarde les paramètres de manière asynchrone
    /// </summary>
    /// <param name="path">Chemin optionnel</param>
    /// <param name="cancellationToken">Token d'annulation</param>
    public async Task SaveAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        var filePath = path ?? FilePath;
        
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize((T)this, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde async paramètres: {ex.Message}");
        }
    }

    /// <summary>
    /// Recharge les paramètres depuis le fichier
    /// </summary>
    public static void Reload()
    {
        lock (_lock)
        {
            _instance = Load();
        }
    }

    /// <summary>
    /// Réinitialise les paramètres aux valeurs par défaut
    /// </summary>
    public static T Reset()
    {
        lock (_lock)
        {
            _instance = new T();
            _instance.SetDefaults();
            _instance.Save();
            return _instance;
        }
    }

    /// <summary>
    /// Définit les valeurs par défaut.
    /// Override cette méthode dans les classes dérivées pour initialiser les valeurs.
    /// </summary>
    protected virtual void SetDefaults()
    {
        // Les classes dérivées peuvent override pour définir des valeurs par défaut
    }

    /// <summary>
    /// Obtient le chemin par défaut du fichier de paramètres
    /// </summary>
    private string GetDefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(StorageFolder),
            AppName,
            FileName);
    }
}
