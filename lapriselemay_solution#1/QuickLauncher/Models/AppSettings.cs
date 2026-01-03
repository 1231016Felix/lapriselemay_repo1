using System.IO;
using System.Text.Json;

namespace QuickLauncher.Models;

public enum AutoReindexMode
{
    Interval,
    ScheduledTime
}

public class AppSettings
{
    // === Dossiers et fichiers ===
    public List<string> IndexedFolders { get; set; } =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    ];
    
    public List<string> FileExtensions { get; set; } =
    [
        ".exe", ".lnk", ".bat", ".cmd", ".ps1", ".msi",
        ".txt", ".pdf", ".docx", ".xlsx", ".pptx",
        ".png", ".jpg", ".jpeg", ".gif", ".mp3", ".mp4"
    ];
    
    // === Scripts et recherche ===
    public List<CustomScript> Scripts { get; set; } = [];
    public List<WebSearchEngine> SearchEngines { get; set; } =
    [
        new() { Prefix = "g", Name = "Google", UrlTemplate = "https://www.google.com/search?q={query}" },
        new() { Prefix = "yt", Name = "YouTube", UrlTemplate = "https://www.youtube.com/results?search_query={query}" },
        new() { Prefix = "gh", Name = "GitHub", UrlTemplate = "https://github.com/search?q={query}" },
        new() { Prefix = "so", Name = "Stack Overflow", UrlTemplate = "https://stackoverflow.com/search?q={query}" }
    ];
    
    // === Paramètres généraux ===
    public int MaxResults { get; set; } = 8;
    public bool ShowInTaskbar { get; set; } = false;
    public bool StartWithWindows { get; set; } = true;
    public bool CloseAfterLaunch { get; set; } = true;
    public bool ShowIndexingStatus { get; set; } = true;
    public bool MinimizeOnStartup { get; set; } = true;
    public bool EnableSearchHistory { get; set; } = true;
    public int MaxSearchHistory { get; set; } = 10;
    
    // === Apparence ===
    public double WindowOpacity { get; set; } = 1.0;
    public string AccentColor { get; set; } = "#0078D4";
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
    public int SearchDepth { get; set; } = 5;
    public bool IndexHiddenFolders { get; set; } = false;
    
    // === Réindexation automatique ===
    public bool AutoReindexEnabled { get; set; } = false;
    public AutoReindexMode AutoReindexMode { get; set; } = AutoReindexMode.Interval;
    public int AutoReindexIntervalMinutes { get; set; } = 60;
    public string AutoReindexScheduledTime { get; set; } = "03:00";
    
    // === Historique de recherche ===
    public List<string> SearchHistory { get; set; } = [];
    
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickLauncher", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
    
    public void AddToSearchHistory(string query)
    {
        if (!EnableSearchHistory || string.IsNullOrWhiteSpace(query)) return;
        
        SearchHistory.Remove(query);
        SearchHistory.Insert(0, query);
        
        while (SearchHistory.Count > MaxSearchHistory)
            SearchHistory.RemoveAt(SearchHistory.Count - 1);
    }
    
    public void ClearSearchHistory() => SearchHistory.Clear();
    
    public static void Reset()
    {
        if (File.Exists(SettingsPath))
            File.Delete(SettingsPath);
    }
    
    public static string GetSettingsPath() => SettingsPath;
}

public class HotkeySettings
{
    public bool UseAlt { get; set; } = true;
    public bool UseCtrl { get; set; } = false;
    public bool UseShift { get; set; } = false;
    public bool UseWin { get; set; } = false;
    public string Key { get; set; } = "Space";
    
    public string DisplayText => string.Join("+", GetModifiers().Concat([Key]));
    
    private IEnumerable<string> GetModifiers()
    {
        if (UseCtrl) yield return "Ctrl";
        if (UseAlt) yield return "Alt";
        if (UseShift) yield return "Shift";
        if (UseWin) yield return "Win";
    }
}

public class CustomScript
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool RunAsAdmin { get; set; }
    public string Keyword { get; set; } = string.Empty;
}

public class WebSearchEngine
{
    public string Prefix { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UrlTemplate { get; set; } = string.Empty;
}
