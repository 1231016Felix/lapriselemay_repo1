using System.IO;
using System.Text.Json;

namespace TempCleaner.Services;

/// <summary>
/// Service de gestion des préférences utilisateur
/// </summary>
public class SettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TempCleaner");
    
    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
    
    /// <summary>
    /// Préférences sauvegardées
    /// </summary>
    public class UserSettings
    {
        public Dictionary<string, bool> EnabledProfiles { get; set; } = new();
        public DateTime LastSaved { get; set; }
    }
    
    /// <summary>
    /// Charger les préférences
    /// </summary>
    public UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
        }
        catch
        {
            // En cas d'erreur, retourner les paramètres par défaut
        }
        
        return new UserSettings();
    }
    
    /// <summary>
    /// Sauvegarder les préférences
    /// </summary>
    public void Save(UserSettings settings)
    {
        try
        {
            // Créer le dossier si nécessaire
            if (!Directory.Exists(SettingsFolder))
                Directory.CreateDirectory(SettingsFolder);
            
            settings.LastSaved = DateTime.Now;
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Ignorer les erreurs de sauvegarde
        }
    }
    
    /// <summary>
    /// Sauvegarder l'état des profils
    /// </summary>
    public void SaveProfiles(IEnumerable<Models.CleanerProfile> profiles)
    {
        var settings = new UserSettings
        {
            EnabledProfiles = profiles.ToDictionary(p => p.Name, p => p.IsEnabled)
        };
        Save(settings);
    }
    
    /// <summary>
    /// Appliquer les préférences sauvegardées aux profils
    /// </summary>
    public void ApplyToProfiles(IEnumerable<Models.CleanerProfile> profiles)
    {
        var settings = Load();
        
        if (settings.EnabledProfiles.Count == 0)
            return;
        
        foreach (var profile in profiles)
        {
            if (settings.EnabledProfiles.TryGetValue(profile.Name, out bool isEnabled))
            {
                profile.IsEnabled = isEnabled;
            }
        }
    }
}
