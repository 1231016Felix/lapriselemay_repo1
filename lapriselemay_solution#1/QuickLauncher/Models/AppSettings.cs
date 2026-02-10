using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickLauncher.Models.Settings;

namespace QuickLauncher.Models;

public enum AutoReindexMode
{
    Interval,
    ScheduledTime
}

/// <summary>
/// Modes de thème pour l'apparence.
/// </summary>
public enum ThemeMode
{
    Dark,
    Light,
    Auto  // Basculer selon l'heure du jour
}

/// <summary>
/// Styles d'animation pour l'ouverture/fermeture de la fenêtre.
/// </summary>
public enum AnimationStyle
{
    FadeSlide,   // Fondu + glissement vertical
    Fade,        // Fondu simple
    Scale,       // Zoom depuis le centre
    Slide,       // Glissement seul
    Pop          // Zoom avec rebond
}

/// <summary>
/// Agrégateur de paramètres. Regroupe les sections spécialisées et gère
/// la sérialisation / migration du format JSON.
/// 
/// Les propriétés proxy [JsonIgnore] permettent la compatibilité ascendante :
/// le code existant peut continuer d'écrire _settings.MaxResults au lieu de
/// _settings.Search.MaxResults. Les nouvelles classes doivent utiliser les sections.
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
    //  SECTIONS (source de vérité pour les nouveaux accès)
    // ══════════════════════════════════════════════════════════
    
    /// <summary>Section recherche, indexation, scoring, historique.</summary>
    public SearchSettings Search { get; set; } = new();
    
    /// <summary>Section apparence, thème, animations, fenêtre.</summary>
    public AppearanceSettings Appearance { get; set; } = new();
    
    /// <summary>Section intégrations : météo, traduction, IA, widgets.</summary>
    public IntegrationSettings Integrations { get; set; } = new();
    
    // ══════════════════════════════════════════════════════════
    //  PROPRIÉTÉS RACINE (non sectionnées — restent ici)
    // ══════════════════════════════════════════════════════════
    
    /// <summary>Commandes de contrôle système (:volume, :lock, etc.).</summary>
    public List<SystemControlCommand> SystemCommands { get; set; } = GetDefaultSystemCommands();
    
    /// <summary>Raccourci clavier global.</summary>
    public HotkeySettings Hotkey { get; set; } = new();
    
    /// <summary>Comportement général.</summary>
    public bool StartWithWindows { get; set; } = true;
    public bool CloseAfterLaunch { get; set; } = true;
    public bool MinimizeOnStartup { get; set; } = true;
    public bool SingleClickLaunch { get; set; }
    
    // ══════════════════════════════════════════════════════════
    //  PROXIES DE COMPATIBILITÉ — Search
    //  Permet à l'ancien code d'écrire _settings.MaxResults etc.
    //  TODO: migrer progressivement vers _settings.Search.MaxResults
    // ══════════════════════════════════════════════════════════
    
    [JsonIgnore] public int MaxResults { get => Search.MaxResults; set => Search.MaxResults = value; }
    [JsonIgnore] public int SearchDepth { get => Search.SearchDepth; set => Search.SearchDepth = value; }
    [JsonIgnore] public bool IndexHiddenFolders { get => Search.IndexHiddenFolders; set => Search.IndexHiddenFolders = value; }
    [JsonIgnore] public bool IndexBrowserBookmarks { get => Search.IndexBrowserBookmarks; set => Search.IndexBrowserBookmarks = value; }
    [JsonIgnore] public bool EnableAliases { get => Search.EnableAliases; set => Search.EnableAliases = value; }
    [JsonIgnore] public int SystemSearchDepth { get => Search.SystemSearchDepth; set => Search.SystemSearchDepth = value; }
    [JsonIgnore] public bool EnableSearchHistory { get => Search.EnableSearchHistory; set => Search.EnableSearchHistory = value; }
    [JsonIgnore] public int MaxSearchHistory { get => Search.MaxSearchHistory; set => Search.MaxSearchHistory = value; }
    [JsonIgnore] public List<HistoryItem> SearchHistory { get => Search.SearchHistory; set => Search.SearchHistory = value; }
    [JsonIgnore] public ScoringWeights ScoringWeights { get => Search.ScoringWeights; set => Search.ScoringWeights = value; }
    [JsonIgnore] public List<string> IndexedFolders { get => Search.IndexedFolders; set => Search.IndexedFolders = value; }
    [JsonIgnore] public List<string> FileExtensions { get => Search.FileExtensions; set => Search.FileExtensions = value; }
    [JsonIgnore] public List<PinnedItem> PinnedItems { get => Search.PinnedItems; set => Search.PinnedItems = value; }
    [JsonIgnore] public List<CustomScript> Scripts { get => Search.Scripts; set => Search.Scripts = value; }
    [JsonIgnore] public List<WebSearchEngine> SearchEngines { get => Search.SearchEngines; set => Search.SearchEngines = value; }
    [JsonIgnore] public bool EnableFileWatcher { get => Search.EnableFileWatcher; set => Search.EnableFileWatcher = value; }
    [JsonIgnore] public bool AutoReindexEnabled { get => Search.AutoReindexEnabled; set => Search.AutoReindexEnabled = value; }
    [JsonIgnore] public AutoReindexMode AutoReindexMode { get => Search.AutoReindexMode; set => Search.AutoReindexMode = value; }
    [JsonIgnore] public int AutoReindexIntervalMinutes { get => Search.AutoReindexIntervalMinutes; set => Search.AutoReindexIntervalMinutes = value; }
    [JsonIgnore] public string AutoReindexScheduledTime { get => Search.AutoReindexScheduledTime; set => Search.AutoReindexScheduledTime = value; }

    // ══════════════════════════════════════════════════════════
    //  PROXIES DE COMPATIBILITÉ — Appearance
    // ══════════════════════════════════════════════════════════
    
    [JsonIgnore] public string Theme { get => Appearance.Theme; set => Appearance.Theme = value; }
    [JsonIgnore] public ThemeMode ThemeMode { get => Appearance.ThemeMode; set => Appearance.ThemeMode = value; }
    [JsonIgnore] public string AccentColor { get => Appearance.AccentColor; set => Appearance.AccentColor = value; }
    [JsonIgnore] public double WindowOpacity { get => Appearance.WindowOpacity; set => Appearance.WindowOpacity = value; }
    [JsonIgnore] public string WindowPosition { get => Appearance.WindowPosition; set => Appearance.WindowPosition = value; }
    [JsonIgnore] public double? LastWindowLeft { get => Appearance.LastWindowLeft; set => Appearance.LastWindowLeft = value; }
    [JsonIgnore] public double? LastWindowTop { get => Appearance.LastWindowTop; set => Appearance.LastWindowTop = value; }
    [JsonIgnore] public bool ShowInTaskbar { get => Appearance.ShowInTaskbar; set => Appearance.ShowInTaskbar = value; }
    [JsonIgnore] public bool ShowSettingsButton { get => Appearance.ShowSettingsButton; set => Appearance.ShowSettingsButton = value; }
    [JsonIgnore] public bool ShowPreviewPanel { get => Appearance.ShowPreviewPanel; set => Appearance.ShowPreviewPanel = value; }
    [JsonIgnore] public bool ShowShortcutHints { get => Appearance.ShowShortcutHints; set => Appearance.ShowShortcutHints = value; }
    [JsonIgnore] public bool ShowCategoryBadges { get => Appearance.ShowCategoryBadges; set => Appearance.ShowCategoryBadges = value; }
    [JsonIgnore] public bool ShowIndexingStatus { get => Appearance.ShowIndexingStatus; set => Appearance.ShowIndexingStatus = value; }
    [JsonIgnore] public bool ShowGhostSuggestions { get => Appearance.ShowGhostSuggestions; set => Appearance.ShowGhostSuggestions = value; }
    [JsonIgnore] public bool EnableAnimations { get => Appearance.EnableAnimations; set => Appearance.EnableAnimations = value; }
    [JsonIgnore] public int AnimationDurationMs { get => Appearance.AnimationDurationMs; set => Appearance.AnimationDurationMs = value; }
    [JsonIgnore] public AnimationStyle AnimationStyle { get => Appearance.AnimationStyle; set => Appearance.AnimationStyle = value; }
    [JsonIgnore] public int StaggerDelayMs { get => Appearance.StaggerDelayMs; set => Appearance.StaggerDelayMs = value; }
    [JsonIgnore] public string AutoThemeLightStart { get => Appearance.AutoThemeLightStart; set => Appearance.AutoThemeLightStart = value; }
    [JsonIgnore] public string AutoThemeDarkStart { get => Appearance.AutoThemeDarkStart; set => Appearance.AutoThemeDarkStart = value; }
    [JsonIgnore] public string LightThemeStartTime { get => Appearance.LightThemeStartTime; set => Appearance.LightThemeStartTime = value; }
    [JsonIgnore] public string DarkThemeStartTime { get => Appearance.DarkThemeStartTime; set => Appearance.DarkThemeStartTime = value; }
    
    // ══════════════════════════════════════════════════════════
    //  PROXIES DE COMPATIBILITÉ — Integrations
    // ══════════════════════════════════════════════════════════
    
    [JsonIgnore] public string WeatherCity { get => Integrations.WeatherCity; set => Integrations.WeatherCity = value; }
    [JsonIgnore] public string WeatherUnit { get => Integrations.WeatherUnit; set => Integrations.WeatherUnit = value; }
    [JsonIgnore] public string TranslateTargetLang { get => Integrations.TranslateTargetLang; set => Integrations.TranslateTargetLang = value; }
    [JsonIgnore] public string TranslateSourceLang { get => Integrations.TranslateSourceLang; set => Integrations.TranslateSourceLang = value; }
    [JsonIgnore] public string AiProvider { get => Integrations.AiProvider; set => Integrations.AiProvider = value; }
    [JsonIgnore] public string AiApiUrl { get => Integrations.AiApiUrl; set => Integrations.AiApiUrl = value; }
    [JsonIgnore] public string AiApiKey { get => Integrations.AiApiKey; set => Integrations.AiApiKey = value; }
    [JsonIgnore] public string AiModel { get => Integrations.AiModel; set => Integrations.AiModel = value; }
    [JsonIgnore] public string AiSystemPrompt { get => Integrations.AiSystemPrompt; set => Integrations.AiSystemPrompt = value; }
    [JsonIgnore] public List<NoteWidgetInfo> NoteWidgets { get => Integrations.NoteWidgets; set => Integrations.NoteWidgets = value; }
    [JsonIgnore] public List<TimerWidgetInfo> TimerWidgets { get => Integrations.TimerWidgets; set => Integrations.TimerWidgets = value; }
    [JsonIgnore] public List<NoteItem> Notes { get => Integrations.Notes; set => Integrations.Notes = value; }

    // ══════════════════════════════════════════════════════════
    //  MÉTHODES PROXY DE COMPATIBILITÉ — Delegated to Search
    // ══════════════════════════════════════════════════════════
    
    public void PinItem(string name, string path, ResultType type, string? icon = null)
        => Search.PinItem(name, path, type, icon);
    
    public bool UnpinItem(string path) => Search.UnpinItem(path);
    public bool IsPinned(string path) => Search.IsPinned(path);
    public void MovePinnedItemUp(string path) => Search.MovePinnedItemUp(path);
    public void MovePinnedItemDown(string path) => Search.MovePinnedItemDown(path);
    public void AddToSearchHistory(HistoryItem item) => Search.AddToSearchHistory(item);
    public void ClearSearchHistory() => Search.ClearSearchHistory();
    
    // ══════════════════════════════════════════════════════════
    //  COMMANDES SYSTÈME (migration et réinitialisation)
    // ══════════════════════════════════════════════════════════
    
    public void ResetSystemCommands() => SystemCommands = GetDefaultSystemCommands();
    
    // ══════════════════════════════════════════════════════════
    //  CHARGEMENT / SAUVEGARDE / MIGRATION
    // ══════════════════════════════════════════════════════════
    
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            
            // Détection du format : ancien (plat) vs nouveau (sections)
            // Si le JSON contient une clé "search" au top-level, c'est le nouveau format.
            using var doc = JsonDocument.Parse(json);
            var isNewFormat = doc.RootElement.TryGetProperty("search", out _);
            
            AppSettings? settings;
            
            if (isNewFormat)
            {
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            }
            else
            {
                // Ancien format plat → désérialiser via le DTO legacy puis migrer
                settings = MigrateFromLegacyFormat(json);
            }
            
            if (settings != null)
            {
                settings.MigrateSystemCommands();
                System.Diagnostics.Debug.WriteLine($"[Settings] Chargé avec {settings.PinnedItems.Count} épingles (format: {(isNewFormat ? "v2" : "legacy→v2")})");
                return settings;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Erreur chargement: {ex.Message}");
        }
        
        return new AppSettings();
    }
    
    /// <summary>
    /// Migre l'ancien format JSON plat vers le nouveau format sectionné.
    /// Utilise JsonElement pour lire les propriétés à plat et peupler les sections.
    /// </summary>
    private static AppSettings MigrateFromLegacyFormat(string json)
    {
        // Stratégie: on désérialise tout dans un dictionnaire générique,
        // puis on reconstruit les sections manuellement.
        // Alternative plus simple: on désérialise dans un objet legacy qui a toutes
        // les propriétés à plat, puis on copie.
        var legacy = JsonSerializer.Deserialize<LegacyAppSettings>(json, JsonOptions);
        if (legacy == null) return new AppSettings();
        
        var settings = new AppSettings();
        
        // Search
        settings.Search.MaxResults = legacy.MaxResults;
        settings.Search.SearchDepth = legacy.SearchDepth;
        settings.Search.IndexHiddenFolders = legacy.IndexHiddenFolders;
        settings.Search.IndexBrowserBookmarks = legacy.IndexBrowserBookmarks;
        settings.Search.EnableAliases = legacy.EnableAliases;
        settings.Search.SystemSearchDepth = legacy.SystemSearchDepth;
        settings.Search.EnableSearchHistory = legacy.EnableSearchHistory;
        settings.Search.MaxSearchHistory = legacy.MaxSearchHistory;
        settings.Search.SearchHistory = legacy.SearchHistory ?? [];
        settings.Search.ScoringWeights = legacy.ScoringWeights ?? new ScoringWeights();
        settings.Search.IndexedFolders = legacy.IndexedFolders ?? settings.Search.IndexedFolders;
        settings.Search.FileExtensions = legacy.FileExtensions ?? settings.Search.FileExtensions;
        settings.Search.PinnedItems = legacy.PinnedItems ?? [];
        settings.Search.Scripts = legacy.Scripts ?? [];
        settings.Search.SearchEngines = legacy.SearchEngines ?? settings.Search.SearchEngines;
        settings.Search.EnableFileWatcher = legacy.EnableFileWatcher;
        settings.Search.AutoReindexEnabled = legacy.AutoReindexEnabled;
        settings.Search.AutoReindexMode = legacy.AutoReindexMode;
        settings.Search.AutoReindexIntervalMinutes = legacy.AutoReindexIntervalMinutes;
        settings.Search.AutoReindexScheduledTime = legacy.AutoReindexScheduledTime ?? "03:00";
        
        // Appearance
        settings.Appearance.Theme = legacy.Theme ?? "Dark";
        settings.Appearance.ThemeMode = legacy.ThemeMode;
        settings.Appearance.AccentColor = legacy.AccentColor ?? Constants.Colors.DefaultAccent;
        settings.Appearance.WindowOpacity = legacy.WindowOpacity;
        settings.Appearance.WindowPosition = legacy.WindowPosition ?? "Center";
        settings.Appearance.LastWindowLeft = legacy.LastWindowLeft;
        settings.Appearance.LastWindowTop = legacy.LastWindowTop;
        settings.Appearance.ShowInTaskbar = legacy.ShowInTaskbar;
        settings.Appearance.ShowSettingsButton = legacy.ShowSettingsButton;
        settings.Appearance.ShowPreviewPanel = legacy.ShowPreviewPanel;
        settings.Appearance.ShowShortcutHints = legacy.ShowShortcutHints;
        settings.Appearance.ShowCategoryBadges = legacy.ShowCategoryBadges;
        settings.Appearance.ShowIndexingStatus = legacy.ShowIndexingStatus;
        settings.Appearance.EnableAnimations = legacy.EnableAnimations;
        settings.Appearance.AnimationDurationMs = legacy.AnimationDurationMs;
        settings.Appearance.AnimationStyle = legacy.AnimationStyle;
        settings.Appearance.StaggerDelayMs = legacy.StaggerDelayMs;
        settings.Appearance.AutoThemeLightStart = legacy.AutoThemeLightStart ?? "07:00";
        settings.Appearance.AutoThemeDarkStart = legacy.AutoThemeDarkStart ?? "19:00";
        settings.Appearance.LightThemeStartTime = legacy.LightThemeStartTime ?? "07:00";
        settings.Appearance.DarkThemeStartTime = legacy.DarkThemeStartTime ?? "19:00";
        
        // Integrations
        settings.Integrations.WeatherCity = legacy.WeatherCity ?? "Montreal";
        settings.Integrations.WeatherUnit = legacy.WeatherUnit ?? "celsius";
        settings.Integrations.TranslateTargetLang = legacy.TranslateTargetLang ?? "en";
        settings.Integrations.TranslateSourceLang = legacy.TranslateSourceLang ?? "auto";
        settings.Integrations.AiProvider = legacy.AiProvider ?? "chatgpt";
        settings.Integrations.AiApiUrl = legacy.AiApiUrl ?? "https://api.openai.com/v1/chat/completions";
        settings.Integrations.AiApiKey = legacy.AiApiKey ?? string.Empty;
        settings.Integrations.AiModel = legacy.AiModel ?? "gpt-4o-mini";
        settings.Integrations.AiSystemPrompt = legacy.AiSystemPrompt ?? settings.Integrations.AiSystemPrompt;
        settings.Integrations.NoteWidgets = legacy.NoteWidgets ?? [];
        settings.Integrations.TimerWidgets = legacy.TimerWidgets ?? [];
        settings.Integrations.Notes = legacy.Notes ?? [];
        
        // Root
        settings.SystemCommands = legacy.SystemCommands ?? GetDefaultSystemCommands();
        settings.Hotkey = legacy.Hotkey ?? new HotkeySettings();
        settings.StartWithWindows = legacy.StartWithWindows;
        settings.CloseAfterLaunch = legacy.CloseAfterLaunch;
        settings.MinimizeOnStartup = legacy.MinimizeOnStartup;
        settings.SingleClickLaunch = legacy.SingleClickLaunch;
        
        // Sauvegarder immédiatement au nouveau format pour ne migrer qu'une seule fois
        settings.Save();
        System.Diagnostics.Debug.WriteLine("[Settings] Migration legacy → v2 effectuée");
        
        return settings;
    }

    /// <summary>
    /// Ajoute les commandes système manquantes et met à jour les catégories (migration).
    /// </summary>
    private void MigrateSystemCommands()
    {
        var defaultCommands = GetDefaultSystemCommands();
        var existingTypes = SystemCommands.Select(c => c.Type).ToHashSet();
        
        SystemCommands.RemoveAll(c => c.Type == SystemControlType.Notes || c.Type == SystemControlType.Timers);
        
        foreach (var cmd in defaultCommands)
        {
            if (!existingTypes.Contains(cmd.Type))
                SystemCommands.Add(cmd);
        }
        
        foreach (var existingCmd in SystemCommands)
        {
            if (string.IsNullOrEmpty(existingCmd.Category))
            {
                var defaultCmd = defaultCommands.FirstOrDefault(d => d.Type == existingCmd.Type);
                if (defaultCmd != null)
                    existingCmd.Category = defaultCmd.Category;
            }
        }
        
        var orderedTypes = defaultCommands.Select(c => c.Type).ToList();
        SystemCommands = SystemCommands
            .OrderBy(c => orderedTypes.IndexOf(c.Type))
            .ThenBy(c => c.Category)
            .ToList();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
            System.Diagnostics.Debug.WriteLine($"[Settings] Sauvegardé avec {PinnedItems.Count} épingles");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Erreur sauvegarde: {ex.Message}");
        }
    }
    
    public static void Reset()
    {
        try { if (File.Exists(SettingsPath)) File.Delete(SettingsPath); }
        catch { /* Ignore */ }
    }
    
    public static string GetSettingsPath() => SettingsPath;
    
    // ══════════════════════════════════════════════════════════
    //  COMMANDES SYSTÈME PAR DÉFAUT
    // ══════════════════════════════════════════════════════════
    
    private static List<SystemControlCommand> GetDefaultSystemCommands() =>
    [
        // Productivité
        new() { Type = SystemControlType.Timer, Name = "Minuterie", Prefix = "timer", Icon = "⏱️", Category = "Productivité",
                Description = "Créer une minuterie (ex: :timer 5m Pause café)", RequiresArgument = true, ArgumentHint = "[durée] [label]" },
        new() { Type = SystemControlType.Note, Name = "Nouvelle note", Prefix = "note", Icon = "📝", Category = "Productivité",
                Description = "Créer une note sur le bureau", RequiresArgument = true, ArgumentHint = "[contenu]" },
        new() { Type = SystemControlType.SystemSearch, Name = "Recherche système", Prefix = "find", Icon = "🔎", Category = "Productivité",
                Description = "Rechercher des fichiers sur tout le système", RequiresArgument = true, ArgumentHint = "[terme]" },
        new() { Type = SystemControlType.Screenshot, Name = "Capture d'écran", Prefix = "screenshot", Icon = "📸", Category = "Productivité",
                Description = "Prendre une capture d'écran", ArgumentHint = "[snip|primary]" },
        // Intégrations web
        new() { Type = SystemControlType.Weather, Name = "Météo", Prefix = "weather", Icon = "🌤️", Category = "Intégrations web",
                Description = "Afficher la météo actuelle (ex: :weather ou :weather Paris)", ArgumentHint = "[ville]" },
        new() { Type = SystemControlType.Translate, Name = "Traduction", Prefix = "translate", Icon = "🌐", Category = "Intégrations web",
                Description = "Traduire du texte (ex: :translate hello ou :translate fr bonjour)", RequiresArgument = true, ArgumentHint = "[lang] <texte>" },
        new() { Type = SystemControlType.AiChat, Name = "Assistant IA", Prefix = "ai", Icon = "🤖", Category = "Intégrations web",
                Description = "Poser une question à l'IA (ex: :ai qu'est-ce qu'une API REST?)", RequiresArgument = true, ArgumentHint = "<question>" },
        // Multimédia
        new() { Type = SystemControlType.Volume, Name = "Volume", Prefix = "volume", Icon = "🔊", Category = "Multimédia",
                Description = "Régler le volume (0-100, up, down)", RequiresArgument = true, ArgumentHint = "[0-100|up|down]" },
        new() { Type = SystemControlType.Mute, Name = "Muet", Prefix = "mute", Icon = "🔇", Category = "Multimédia",
                Description = "Basculer le mode muet" },
        new() { Type = SystemControlType.Brightness, Name = "Luminosité", Prefix = "brightness", Icon = "☀️", Category = "Multimédia",
                Description = "Régler la luminosité (0-100)", RequiresArgument = true, ArgumentHint = "[0-100]" },
        // Réseau
        new() { Type = SystemControlType.Wifi, Name = "WiFi", Prefix = "wifi", Icon = "📶", Category = "Réseau",
                Description = "Contrôler le WiFi", RequiresArgument = true, ArgumentHint = "[on|off|status]" },
        new() { Type = SystemControlType.FlushDns, Name = "Vider DNS", Prefix = "flushdns", Icon = "🌐", Category = "Réseau",
                Description = "Vider le cache DNS" },
        // Session
        new() { Type = SystemControlType.Lock, Name = "Verrouiller", Prefix = "lock", Icon = "🔒", Category = "Session", Description = "Verrouiller la session" },
        new() { Type = SystemControlType.Logoff, Name = "Déconnexion", Prefix = "logoff", Icon = "🚪", Category = "Session", Description = "Déconnecter la session" },
        new() { Type = SystemControlType.Sleep, Name = "Veille", Prefix = "sleep", Icon = "😴", Category = "Session", Description = "Mettre en veille" },
        new() { Type = SystemControlType.Hibernate, Name = "Hibernation", Prefix = "hibernate", Icon = "💤", Category = "Session", Description = "Mettre en hibernation" },
        new() { Type = SystemControlType.Shutdown, Name = "Éteindre", Prefix = "shutdown", Icon = "🔌", Category = "Session", Description = "Éteindre l'ordinateur" },
        new() { Type = SystemControlType.Restart, Name = "Redémarrer", Prefix = "restart", Icon = "🔄", Category = "Session", Description = "Redémarrer l'ordinateur" },
        // Système
        new() { Type = SystemControlType.OpenTaskManager, Name = "Gestionnaire tâches", Prefix = "taskmgr", Icon = "📊", Category = "Système", Description = "Ouvrir le Gestionnaire des tâches" },
        new() { Type = SystemControlType.OpenWindowsSettings, Name = "Paramètres Windows", Prefix = "winsettings", Icon = "⚙️", Category = "Système", Description = "Ouvrir les Paramètres Windows" },
        new() { Type = SystemControlType.OpenControlPanel, Name = "Panneau config.", Prefix = "control", Icon = "🎛️", Category = "Système", Description = "Ouvrir le Panneau de configuration" },
        new() { Type = SystemControlType.EmptyRecycleBin, Name = "Vider corbeille", Prefix = "emptybin", Icon = "🗑️", Category = "Système", Description = "Vider la corbeille" },
        new() { Type = SystemControlType.EmptyTemp, Name = "Vider Temp", Prefix = "emptytemp", Icon = "🧹", Category = "Système", Description = "Vider le dossier temporaire" },
        new() { Type = SystemControlType.OpenCmdAdmin, Name = "CMD Admin", Prefix = "cmd", Icon = "💻", Category = "Système", Description = "Ouvrir l'invite de commandes (admin)" },
        new() { Type = SystemControlType.OpenPowerShellAdmin, Name = "PowerShell Admin", Prefix = "powershell", Icon = "🔵", Category = "Système", Description = "Ouvrir PowerShell (admin)" },
        new() { Type = SystemControlType.RestartExplorer, Name = "Redém. Explorer", Prefix = "restartexplorer", Icon = "📁", Category = "Système", Description = "Redémarrer l'Explorateur Windows" },
        new() { Type = SystemControlType.OpenStartupFolder, Name = "Démarrage", Prefix = "startup", Icon = "🚀", Category = "Système", Description = "Ouvrir le dossier de démarrage Windows" },
        new() { Type = SystemControlType.OpenHostsFile, Name = "Fichier hosts", Prefix = "hosts", Icon = "📝", Category = "Système", Description = "Ouvrir le fichier hosts (admin)" }
    ];
}

// ══════════════════════════════════════════════════════════
//  DTO LEGACY — utilisé uniquement pour la migration
//  de l'ancien format JSON plat vers le nouveau.
// ══════════════════════════════════════════════════════════

internal sealed class LegacyAppSettings
{
    // Search
    public int MaxResults { get; set; } = Constants.DefaultMaxResults;
    public int SearchDepth { get; set; } = Constants.DefaultSearchDepth;
    public bool IndexHiddenFolders { get; set; }
    public bool IndexBrowserBookmarks { get; set; } = true;
    public bool EnableAliases { get; set; } = true;
    public int SystemSearchDepth { get; set; } = 5;
    public bool EnableSearchHistory { get; set; } = true;
    public int MaxSearchHistory { get; set; } = Constants.DefaultMaxSearchHistory;
    public List<HistoryItem>? SearchHistory { get; set; }
    public ScoringWeights? ScoringWeights { get; set; }
    public List<string>? IndexedFolders { get; set; }
    public List<string>? FileExtensions { get; set; }
    public List<PinnedItem>? PinnedItems { get; set; }
    public List<CustomScript>? Scripts { get; set; }
    public List<WebSearchEngine>? SearchEngines { get; set; }
    public bool EnableFileWatcher { get; set; } = true;
    public bool AutoReindexEnabled { get; set; }
    public AutoReindexMode AutoReindexMode { get; set; } = AutoReindexMode.Interval;
    public int AutoReindexIntervalMinutes { get; set; } = 60;
    public string? AutoReindexScheduledTime { get; set; }
    
    // Appearance
    public string? Theme { get; set; }
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;
    public string? AccentColor { get; set; }
    public double WindowOpacity { get; set; } = 1.0;
    public string? WindowPosition { get; set; }
    public double? LastWindowLeft { get; set; }
    public double? LastWindowTop { get; set; }
    public bool ShowInTaskbar { get; set; }
    public bool ShowSettingsButton { get; set; } = true;
    public bool ShowPreviewPanel { get; set; } = true;
    public bool ShowShortcutHints { get; set; } = true;
    public bool ShowCategoryBadges { get; set; } = true;
    public bool ShowIndexingStatus { get; set; } = true;
    public bool EnableAnimations { get; set; } = true;
    public int AnimationDurationMs { get; set; } = 140;
    public AnimationStyle AnimationStyle { get; set; } = AnimationStyle.FadeSlide;
    public int StaggerDelayMs { get; set; } = 30;
    public string? AutoThemeLightStart { get; set; }
    public string? AutoThemeDarkStart { get; set; }
    public string? LightThemeStartTime { get; set; }
    public string? DarkThemeStartTime { get; set; }
    
    // Integrations
    public string? WeatherCity { get; set; }
    public string? WeatherUnit { get; set; }
    public string? TranslateTargetLang { get; set; }
    public string? TranslateSourceLang { get; set; }
    public string? AiProvider { get; set; }
    public string? AiApiUrl { get; set; }
    public string? AiApiKey { get; set; }
    public string? AiModel { get; set; }
    public string? AiSystemPrompt { get; set; }
    public List<NoteWidgetInfo>? NoteWidgets { get; set; }
    public List<TimerWidgetInfo>? TimerWidgets { get; set; }
    public List<NoteItem>? Notes { get; set; }
    
    // Root
    public List<SystemControlCommand>? SystemCommands { get; set; }
    public HotkeySettings? Hotkey { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool CloseAfterLaunch { get; set; } = true;
    public bool MinimizeOnStartup { get; set; } = true;
    public bool SingleClickLaunch { get; set; }
}

// ══════════════════════════════════════════════════════════
//  TYPES SUPPORT (inchangés)
// ══════════════════════════════════════════════════════════

/// <summary>Configuration du raccourci clavier global.</summary>
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

public sealed class CustomScript
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool RunAsAdmin { get; set; }
    public string Keyword { get; set; } = string.Empty;
}

public sealed class WebSearchEngine
{
    public string Prefix { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UrlTemplate { get; set; } = string.Empty;
}

public enum SystemControlType
{
    Volume = 0, Mute = 1, Brightness = 2, Wifi = 3, Lock = 4, Sleep = 5,
    Hibernate = 6, Shutdown = 7, Restart = 8, Screenshot = 9,
    FlushDns = 10, Logoff = 11, EmptyRecycleBin = 12, OpenTaskManager = 13,
    OpenWindowsSettings = 14, OpenControlPanel = 15, EmptyTemp = 16,
    OpenCmdAdmin = 17, OpenPowerShellAdmin = 18, RestartExplorer = 19,
    SystemSearch = 20, Timer = 21, Timers = 22, Note = 23, Notes = 24,
    OpenStartupFolder = 25, OpenHostsFile = 26,
    Weather = 27, Translate = 28, AiChat = 29
}

public sealed class SystemControlCommand
{
    public SystemControlType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool RequiresArgument { get; set; }
    public string ArgumentHint { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    
    public SystemControlCommand Clone() => new()
    {
        Type = Type, Name = Name, Prefix = Prefix, Icon = Icon,
        Description = Description, IsEnabled = IsEnabled,
        RequiresArgument = RequiresArgument, ArgumentHint = ArgumentHint, Category = Category,
    };
}

public sealed class ScoringWeights
{
    public int ExactMatch { get; set; } = 1000;
    public int StartsWith { get; set; } = 800;
    public int Contains { get; set; } = 600;
    public int InitialsMatch { get; set; } = 500;
    public int SubsequenceMatch { get; set; } = 300;
    public int FuzzyMatchMultiplier { get; set; } = 250;
    public int MaxUsageBonus { get; set; } = 500;
    public int UsageBonusPerUse { get; set; } = 50;
    public int ExactWordBonus { get; set; } = 50;
    public double FuzzyMatchThreshold { get; set; } = 0.6;
    public int ConsecutiveMatchBonus { get; set; } = 10;
    public int WordBoundaryBonus { get; set; } = 20;
    public int FuzzyPerWordMultiplier { get; set; } = 500;
    public bool EnableRecencyBonus { get; set; } = true;
    public int MaxRecencyBonus { get; set; } = 150;
    public int RecencyDecayPerDay { get; set; } = 5;
    public bool EnablePathFuzzyMatch { get; set; } = true;
    public int PathExactSegmentMatch { get; set; } = 200;
    public int PathAllWordsMatchBonus { get; set; } = 100;
    
    public void ResetToDefaults()
    {
        var defaults = new ScoringWeights();
        ExactMatch = defaults.ExactMatch; StartsWith = defaults.StartsWith;
        Contains = defaults.Contains; InitialsMatch = defaults.InitialsMatch;
        SubsequenceMatch = defaults.SubsequenceMatch; FuzzyMatchMultiplier = defaults.FuzzyMatchMultiplier;
        MaxUsageBonus = defaults.MaxUsageBonus; UsageBonusPerUse = defaults.UsageBonusPerUse;
        ExactWordBonus = defaults.ExactWordBonus; FuzzyMatchThreshold = defaults.FuzzyMatchThreshold;
        ConsecutiveMatchBonus = defaults.ConsecutiveMatchBonus; WordBoundaryBonus = defaults.WordBoundaryBonus;
        FuzzyPerWordMultiplier = defaults.FuzzyPerWordMultiplier; EnableRecencyBonus = defaults.EnableRecencyBonus;
        MaxRecencyBonus = defaults.MaxRecencyBonus; RecencyDecayPerDay = defaults.RecencyDecayPerDay;
        EnablePathFuzzyMatch = defaults.EnablePathFuzzyMatch; PathExactSegmentMatch = defaults.PathExactSegmentMatch;
        PathAllWordsMatchBonus = defaults.PathAllWordsMatchBonus;
    }
    
    public ScoringWeights Clone() => new()
    {
        ExactMatch = ExactMatch, StartsWith = StartsWith, Contains = Contains,
        InitialsMatch = InitialsMatch, SubsequenceMatch = SubsequenceMatch,
        FuzzyMatchMultiplier = FuzzyMatchMultiplier, MaxUsageBonus = MaxUsageBonus,
        UsageBonusPerUse = UsageBonusPerUse, ExactWordBonus = ExactWordBonus,
        FuzzyMatchThreshold = FuzzyMatchThreshold, ConsecutiveMatchBonus = ConsecutiveMatchBonus,
        WordBoundaryBonus = WordBoundaryBonus, FuzzyPerWordMultiplier = FuzzyPerWordMultiplier,
        EnableRecencyBonus = EnableRecencyBonus, MaxRecencyBonus = MaxRecencyBonus,
        RecencyDecayPerDay = RecencyDecayPerDay, EnablePathFuzzyMatch = EnablePathFuzzyMatch,
        PathExactSegmentMatch = PathExactSegmentMatch, PathAllWordsMatchBonus = PathAllWordsMatchBonus
    };
}

public sealed class PinnedItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ResultType Type { get; set; }
    public string? Icon { get; set; }
    public DateTime PinnedAt { get; set; }
    public int Order { get; set; }
    
    public SearchResult ToSearchResult() => new()
    {
        Name = Name, Path = Path, Type = Type,
        Description = "⭐ Épinglé",
        DisplayIcon = Icon ?? GetDefaultIcon(),
        Score = 10000 + (1000 - Order)
    };
    
    private string GetDefaultIcon() => Type switch
    {
        ResultType.Application => "🚀", ResultType.StoreApp => "🪧",
        ResultType.File => "📄", ResultType.Folder => "📁",
        ResultType.Script => "⚡", ResultType.Bookmark => "⭐", _ => "📌"
    };
}

public sealed class NoteItem
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Preview => Content.Length > 50 ? Content[..47] + "..." : Content;
    public string DateFormatted => CreatedAt.ToString("dd/MM/yyyy HH:mm");
}

public sealed class HistoryItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ResultType Type { get; set; }
    public string? Icon { get; set; }
    public DateTime LastUsed { get; set; }
    
    public SearchResult ToSearchResult() => new()
    {
        Name = Name, Path = Path, Type = Type,
        Description = $"🕐 {LastUsed:dd/MM HH:mm}",
        DisplayIcon = Icon ?? (Type switch
        {
            ResultType.Application => "🚀", ResultType.StoreApp => "🪧",
            ResultType.File => "📄", ResultType.Folder => "📁",
            ResultType.Script => "⚡", ResultType.Bookmark => "⭐", _ => "📌"
        })
    };
}

public sealed class NoteWidgetInfo
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Top { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class TimerWidgetInfo
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public DateTime CreatedAt { get; set; }
}
