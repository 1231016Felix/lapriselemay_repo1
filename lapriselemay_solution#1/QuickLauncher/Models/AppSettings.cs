using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickLauncher.Models.Settings;

namespace QuickLauncher.Models;

/// <summary>
/// Agrégateur de paramètres. Regroupe les sections spécialisées et gère
/// la sérialisation / migration du format JSON.
/// 
/// Types support (PinnedItem, HistoryItem, ScoringWeights, etc.) sont dans leurs propres fichiers.
/// Commandes par défaut : voir <see cref="DefaultSystemCommands"/>.
/// Migration legacy : voir <see cref="LegacySettingsMigrator"/>.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppName);
    
    private static readonly string SettingsPath = Path.Combine(SettingsDir, Constants.SettingsFileName);
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    // ══════════════════════════════════════════════════════════
    //  SECTIONS
    // ══════════════════════════════════════════════════════════
    
    /// <summary>Section recherche, indexation, scoring, historique.</summary>
    public SearchSettings Search { get; set; } = new();
    
    /// <summary>Section apparence, thème, animations, fenêtre.</summary>
    public AppearanceSettings Appearance { get; set; } = new();
    
    /// <summary>Section intégrations : météo, traduction, IA, widgets.</summary>
    public IntegrationSettings Integrations { get; set; } = new();
    
    // ══════════════════════════════════════════════════════════
    //  PROPRIÉTÉS RACINE
    // ══════════════════════════════════════════════════════════
    
    /// <summary>Commandes de contrôle système (:volume, :lock, etc.).</summary>
    public List<SystemControlCommand> SystemCommands { get; set; } = DefaultSystemCommands.Create();
    
    /// <summary>Raccourci clavier global.</summary>
    public HotkeySettings Hotkey { get; set; } = new();
    
    /// <summary>Comportement général.</summary>
    public bool StartWithWindows { get; set; } = true;
    public bool CloseAfterLaunch { get; set; } = true;
    public bool MinimizeOnStartup { get; set; } = true;
    public bool SingleClickLaunch { get; set; }
    
    public void ResetSystemCommands() => SystemCommands = DefaultSystemCommands.Create();
    
    /// <summary>
    /// Copie profonde de tous les paramètres via round-trip JSON.
    /// 
    /// Utilisée par SettingsProvider pour le pattern clone-swap :
    /// les threads de recherche travaillent sur un snapshot stable
    /// pendant que le UI thread mute une nouvelle copie.
    /// 
    /// Le round-trip JSON est intrinsèquement sûr : toute nouvelle propriété
    /// ajoutée est automatiquement clonée sans intervention manuelle,
    /// éliminant le risque de partage de référence silencieux.
    /// </summary>
    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
    }
    
    // ══════════════════════════════════════════════════════════
    //  CHARGEMENT / SAUVEGARDE
    // ══════════════════════════════════════════════════════════
    
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            
            // Détection du format : ancien (plat) vs nouveau (sections)
            using var doc = JsonDocument.Parse(json);
            var isNewFormat = doc.RootElement.TryGetProperty("search", out _);
            
            AppSettings? settings;
            
            if (isNewFormat)
            {
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            }
            else
            {
                // Ancien format plat → migration déléguée à LegacySettingsMigrator
                settings = LegacySettingsMigrator.Migrate(json, JsonOptions);
            }
            
            if (settings != null)
            {
                DefaultSystemCommands.Migrate(settings.SystemCommands);
                System.Diagnostics.Trace.WriteLine($"[Settings] Chargé avec {settings.Search.PinnedItems.Count} épingles (format: {(isNewFormat ? "v2" : "legacy→v2")})");
                return settings;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Settings] Erreur chargement: {ex.Message}");
        }
        
        return new AppSettings();
    }

    /// <summary>
    /// Sauvegarde fire-and-forget : sérialise en mémoire (rapide, UI thread)
    /// puis écrit sur disque en arrière-plan via fichier temporaire + Move.
    /// 
    /// Utilisé pour les sauvegardes courantes où le process reste vivant.
    /// Pour le shutdown, utiliser <see cref="SaveSync"/> pour garantir
    /// que l'écriture est terminée avant la fin du process.
    /// </summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            // File.Move avec overwrite est atomique sur NTFS, ce qui garantit que le fichier
            // settings est toujours soit l'ancien soit le nouveau — jamais un état partiel.
            _ = Task.Run(() => WriteAtomically(json));
            System.Diagnostics.Trace.WriteLine($"[Settings] Sauvegardé (async) avec {Search.PinnedItems.Count} épingles");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Settings] Erreur sauvegarde: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sauvegarde synchrone bloquante : garantit que l'écriture est terminée au retour.
    /// 
    /// Utilisé par <see cref="Services.SettingsProvider.Dispose"/> et le callback
    /// du timer debounce (qui s'exécute déjà sur un thread ThreadPool).
    /// Ne jamais appeler depuis le UI thread en conditions normales.
    /// </summary>
    public void SaveSync()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            WriteAtomically(json);
            System.Diagnostics.Trace.WriteLine($"[Settings] Sauvegardé (sync) avec {Search.PinnedItems.Count} épingles");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Settings] Erreur sauvegarde sync: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Écriture atomique sur disque via fichier temporaire + Move.
    /// Factorisation commune à <see cref="Save"/> et <see cref="SaveSync"/>.
    /// </summary>
    private static void WriteAtomically(string json)
    {
        try
        {
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Settings] Erreur écriture atomique: {ex.Message}");
        }
    }
    
    public static void Reset()
    {
        try { if (File.Exists(SettingsPath)) File.Delete(SettingsPath); }
        catch { /* Ignore */ }
    }
    
    public static string GetSettingsPath() => SettingsPath;
}