using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickLauncher.Models;

public enum AutoReindexMode
{
    Interval,
    ScheduledTime
}

/// <summary>
/// Paramètres de l'application avec sérialisation JSON optimisée.
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
    
    // === Dossiers et fichiers ===
    public List<string> IndexedFolders { get; set; } = GetDefaultIndexedFolders();
    public List<string> FileExtensions { get; set; } = [..Constants.DefaultFileExtensions];
    
    // === Scripts et recherche ===
    public List<CustomScript> Scripts { get; set; } = [];
    public List<WebSearchEngine> SearchEngines { get; set; } = GetDefaultSearchEngines();

    // === Paramètres généraux ===
    public int MaxResults { get; set; } = Constants.DefaultMaxResults;
    public bool ShowInTaskbar { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool CloseAfterLaunch { get; set; } = true;
    public bool ShowIndexingStatus { get; set; } = true;
    public bool MinimizeOnStartup { get; set; } = true;
    public bool EnableSearchHistory { get; set; } = true;
    public int MaxSearchHistory { get; set; } = Constants.DefaultMaxSearchHistory;
    public bool SingleClickLaunch { get; set; }
    
    // === Apparence ===
    public double WindowOpacity { get; set; } = 1.0;
    public string AccentColor { get; set; } = Constants.Colors.DefaultAccent;
    public bool EnableAnimations { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public bool ShowSettingsButton { get; set; } = true;
    
    // === Position fenêtre ===
    public string WindowPosition { get; set; } = "Center";
    public double? LastWindowLeft { get; set; }
    public double? LastWindowTop { get; set; }
    
    // === Raccourci clavier ===
    public HotkeySettings Hotkey { get; set; } = new();
    
    // === Indexation ===
    public int SearchDepth { get; set; } = Constants.DefaultSearchDepth;
    public bool IndexHiddenFolders { get; set; }
    
    // === Réindexation automatique ===
    public bool AutoReindexEnabled { get; set; }
    public AutoReindexMode AutoReindexMode { get; set; } = AutoReindexMode.Interval;
    public int AutoReindexIntervalMinutes { get; set; } = 60;
    public string AutoReindexScheduledTime { get; set; } = "03:00";
    
    // === Historique de recherche ===
    public List<string> SearchHistory { get; set; } = [];

    private static List<string> GetDefaultIndexedFolders() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    ];
    
    private static List<WebSearchEngine> GetDefaultSearchEngines() =>
    [
        new() { Prefix = "g", Name = "Google", UrlTemplate = "https://www.google.com/search?q={query}" },
        new() { Prefix = "yt", Name = "YouTube", UrlTemplate = "https://www.youtube.com/results?search_query={query}" },
        new() { Prefix = "gh", Name = "GitHub", UrlTemplate = "https://github.com/search?q={query}" },
        new() { Prefix = "so", Name = "Stack Overflow", UrlTemplate = "https://stackoverflow.com/search?q={query}" }
    ];

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                return settings ?? new AppSettings();
            }
        }
        catch { /* Retourne les paramètres par défaut en cas d'erreur */ }
        
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* Ignore les erreurs de sauvegarde */ }
    }
    
    public void AddToSearchHistory(string query)
    {
        if (!EnableSearchHistory || string.IsNullOrWhiteSpace(query)) return;
        
        SearchHistory.Remove(query);
        SearchHistory.Insert(0, query);
        
        if (SearchHistory.Count > MaxSearchHistory)
            SearchHistory.RemoveRange(MaxSearchHistory, SearchHistory.Count - MaxSearchHistory);
    }
    
    public void ClearSearchHistory() => SearchHistory.Clear();
    
    public static void Reset()
    {
        try
        {
            if (File.Exists(SettingsPath))
                File.Delete(SettingsPath);
        }
        catch { /* Ignore les erreurs */ }
    }
    
    public static string GetSettingsPath() => SettingsPath;
}

/// <summary>
/// Configuration du raccourci clavier global.
/// </summary>
public sealed class HotkeySettings
{
    public bool UseAlt { get; set; } = true;
    public bool UseCtrl { get; set; }
    public bool UseShift { get; set; }
    public bool UseWin { get; set; }
    public string Key { get; set; } = "Space";
    
    [JsonIgnore]
    public string DisplayText => string.Join("+", GetModifiers().Append(Key));
    
    private IEnumerable<string> GetModifiers()
    {
        if (UseCtrl) yield return "Ctrl";
        if (UseAlt) yield return "Alt";
        if (UseShift) yield return "Shift";
        if (UseWin) yield return "Win";
    }
}

/// <summary>
/// Configuration d'un script personnalisé.
/// </summary>
public sealed class CustomScript
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool RunAsAdmin { get; set; }
    public string Keyword { get; set; } = string.Empty;
}

/// <summary>
/// Configuration d'un moteur de recherche web.
/// </summary>
public sealed class WebSearchEngine
{
    public string Prefix { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UrlTemplate { get; set; } = string.Empty;
}
