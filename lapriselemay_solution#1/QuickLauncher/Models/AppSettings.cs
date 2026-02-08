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
    
    // === Commandes de contrôle système ===
    public List<SystemControlCommand> SystemCommands { get; set; } = GetDefaultSystemCommands();

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
    public string Theme { get; set; } = "Dark";
    public bool ShowSettingsButton { get; set; } = true;
    public bool ShowPreviewPanel { get; set; } = true;
    public bool ShowShortcutHints { get; set; } = true;
    
    // === Mode thème automatique ===
    public string LightThemeStartTime { get; set; } = "07:00";
    public string DarkThemeStartTime { get; set; } = "19:00";
    
    // === Indicateurs de catégorie (badges colorés) ===
    public bool ShowCategoryBadges { get; set; } = true;
    
    // === Animations ===
    public bool EnableAnimations { get; set; } = true;
    public int AnimationDurationMs { get; set; } = 140;
    public AnimationStyle AnimationStyle { get; set; } = AnimationStyle.FadeSlide;
    public int StaggerDelayMs { get; set; } = 30;
    
    // === Thème automatique (jour/nuit) ===
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;
    public string AutoThemeLightStart { get; set; } = "07:00";  // Début mode clair
    public string AutoThemeDarkStart { get; set; } = "19:00";   // Début mode sombre
    
    // === Surveillance fichiers ===
    public bool EnableFileWatcher { get; set; } = true;
    
    // === Position fenêtre ===
    public string WindowPosition { get; set; } = "Center";
    public double? LastWindowLeft { get; set; }
    public double? LastWindowTop { get; set; }
    
    // === Raccourci clavier ===
    public HotkeySettings Hotkey { get; set; } = new();
    
    // === Indexation ===
    public int SearchDepth { get; set; } = Constants.DefaultSearchDepth;
    public bool IndexHiddenFolders { get; set; }
    public bool IndexBrowserBookmarks { get; set; } = true;
    
    // === Recherche système (:find) ===
    public int SystemSearchDepth { get; set; } = 5;
    
    // === Réindexation automatique ===
    public bool AutoReindexEnabled { get; set; }
    public AutoReindexMode AutoReindexMode { get; set; } = AutoReindexMode.Interval;
    public int AutoReindexIntervalMinutes { get; set; } = 60;
    public string AutoReindexScheduledTime { get; set; } = "03:00";
    
    // === Historique de recherche (items cliqués) ===
    public List<HistoryItem> SearchHistory { get; set; } = [];
    
    // === Widgets de notes sur le bureau ===
    public List<NoteWidgetInfo> NoteWidgets { get; set; } = [];
    
    // === Widgets de minuteries sur le bureau ===
    public List<TimerWidgetInfo> TimerWidgets { get; set; } = [];
    
    // === Items épinglés ===
    public List<PinnedItem> PinnedItems { get; set; } = [];
    
    // === Alias activés ===
    public bool EnableAliases { get; set; } = true;
    
    // === Poids de scoring configurables ===
    public ScoringWeights ScoringWeights { get; set; } = new();
    
    // === Notes rapides ===
    public List<NoteItem> Notes { get; set; } = [];
    
    // === Intégrations web ===
    public string WeatherCity { get; set; } = "Montreal";
    public string WeatherUnit { get; set; } = "celsius";  // "celsius" ou "fahrenheit"
    public string TranslateTargetLang { get; set; } = "en";
    public string TranslateSourceLang { get; set; } = "auto";
    
    // === Intégration IA ===
    public string AiProvider { get; set; } = "chatgpt";  // "ollama", "chatgpt" ou "custom"
    public string AiApiUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string AiApiKey { get; set; } = string.Empty;  // Requis pour ChatGPT, vide pour Ollama
    public string AiModel { get; set; } = "gpt-4o-mini";  // Modèle par défaut
    public string AiSystemPrompt { get; set; } = "Tu es un assistant concis intégré dans un lanceur d'applications. Réponds de manière courte et directe (2-3 phrases max). Pas de markdown. Langue: français.";

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
    
    private static List<SystemControlCommand> GetDefaultSystemCommands() =>
    [
        // ═══════════════════════════════════════════════════════════════
        // 📝 PRODUCTIVITÉ
        // ═══════════════════════════════════════════════════════════════
        new() { Type = SystemControlType.Timer, Name = "Minuterie", Prefix = "timer", Icon = "⏱️", Category = "Productivité",
                Description = "Créer une minuterie (ex: :timer 5m Pause café)", RequiresArgument = true, ArgumentHint = "[durée] [label]" },
        new() { Type = SystemControlType.Note, Name = "Nouvelle note", Prefix = "note", Icon = "📝", Category = "Productivité",
                Description = "Créer une note sur le bureau", RequiresArgument = true, ArgumentHint = "[contenu]" },
        new() { Type = SystemControlType.SystemSearch, Name = "Recherche système", Prefix = "find", Icon = "🔎", Category = "Productivité",
                Description = "Rechercher des fichiers sur tout le système", RequiresArgument = true, ArgumentHint = "[terme]" },
        new() { Type = SystemControlType.Screenshot, Name = "Capture d'écran", Prefix = "screenshot", Icon = "📸", Category = "Productivité",
                Description = "Prendre une capture d'écran", ArgumentHint = "[snip|primary]" },

        // ═══════════════════════════════════════════════════════════════
        // 🌐 INTÉGRATIONS WEB
        // ═══════════════════════════════════════════════════════════════
        new() { Type = SystemControlType.Weather, Name = "Météo", Prefix = "weather", Icon = "🌤️", Category = "Intégrations web",
                Description = "Afficher la météo actuelle (ex: :weather ou :weather Paris)", ArgumentHint = "[ville]" },
        new() { Type = SystemControlType.Translate, Name = "Traduction", Prefix = "translate", Icon = "🌐", Category = "Intégrations web",
                Description = "Traduire du texte (ex: :translate hello ou :translate fr bonjour)", RequiresArgument = true, ArgumentHint = "[lang] <texte>" },
        new() { Type = SystemControlType.AiChat, Name = "Assistant IA", Prefix = "ai", Icon = "🤖", Category = "Intégrations web",
                Description = "Poser une question à l'IA (ex: :ai qu'est-ce qu'une API REST?)", RequiresArgument = true, ArgumentHint = "<question>" },

        // ═══════════════════════════════════════════════════════════════
        // 🔊 MULTIMÉDIA
        // ═══════════════════════════════════════════════════════════════
        new() { Type = SystemControlType.Volume, Name = "Volume", Prefix = "volume", Icon = "🔊", Category = "Multimédia",
                Description = "Régler le volume (0-100, up, down)", RequiresArgument = true, ArgumentHint = "[0-100|up|down]" },
        new() { Type = SystemControlType.Mute, Name = "Muet", Prefix = "mute", Icon = "🔇", Category = "Multimédia",
                Description = "Basculer le mode muet" },
        new() { Type = SystemControlType.Brightness, Name = "Luminosité", Prefix = "brightness", Icon = "☀️", Category = "Multimédia",
                Description = "Régler la luminosité (0-100)", RequiresArgument = true, ArgumentHint = "[0-100]" },

        // ═══════════════════════════════════════════════════════════════
        // 🌐 RÉSEAU
        // ═══════════════════════════════════════════════════════════════
        new() { Type = SystemControlType.Wifi, Name = "WiFi", Prefix = "wifi", Icon = "📶", Category = "Réseau",
                Description = "Contrôler le WiFi", RequiresArgument = true, ArgumentHint = "[on|off|status]" },
        new() { Type = SystemControlType.FlushDns, Name = "Vider DNS", Prefix = "flushdns", Icon = "🌐", Category = "Réseau",
                Description = "Vider le cache DNS" },

        // ═══════════════════════════════════════════════════════════════
        // ⚡ SESSION
        // ═══════════════════════════════════════════════════════════════
        new() { Type = SystemControlType.Lock, Name = "Verrouiller", Prefix = "lock", Icon = "🔒", Category = "Session",
                Description = "Verrouiller la session" },
        new() { Type = SystemControlType.Logoff, Name = "Déconnexion", Prefix = "logoff", Icon = "🚪", Category = "Session",
                Description = "Déconnecter la session" },
        new() { Type = SystemControlType.Sleep, Name = "Veille", Prefix = "sleep", Icon = "😴", Category = "Session",
                Description = "Mettre en veille" },
        new() { Type = SystemControlType.Hibernate, Name = "Hibernation", Prefix = "hibernate", Icon = "💤", Category = "Session",
                Description = "Mettre en hibernation" },
        new() { Type = SystemControlType.Shutdown, Name = "Éteindre", Prefix = "shutdown", Icon = "🔌", Category = "Session",
                Description = "Éteindre l'ordinateur" },
        new() { Type = SystemControlType.Restart, Name = "Redémarrer", Prefix = "restart", Icon = "🔄", Category = "Session",
                Description = "Redémarrer l'ordinateur" },

        // ═══════════════════════════════════════════════════════════════
        // 🔧 SYSTÈME
        // ═══════════════════════════════════════════════════════════════
        new() { Type = SystemControlType.OpenTaskManager, Name = "Gestionnaire tâches", Prefix = "taskmgr", Icon = "📊", Category = "Système",
                Description = "Ouvrir le Gestionnaire des tâches" },
        new() { Type = SystemControlType.OpenWindowsSettings, Name = "Paramètres Windows", Prefix = "winsettings", Icon = "⚙️", Category = "Système",
                Description = "Ouvrir les Paramètres Windows" },
        new() { Type = SystemControlType.OpenControlPanel, Name = "Panneau config.", Prefix = "control", Icon = "🎛️", Category = "Système",
                Description = "Ouvrir le Panneau de configuration" },
        new() { Type = SystemControlType.EmptyRecycleBin, Name = "Vider corbeille", Prefix = "emptybin", Icon = "🗑️", Category = "Système",
                Description = "Vider la corbeille" },
        new() { Type = SystemControlType.EmptyTemp, Name = "Vider Temp", Prefix = "emptytemp", Icon = "🧹", Category = "Système",
                Description = "Vider le dossier temporaire" },
        new() { Type = SystemControlType.OpenCmdAdmin, Name = "CMD Admin", Prefix = "cmd", Icon = "💻", Category = "Système",
                Description = "Ouvrir l'invite de commandes (admin)" },
        new() { Type = SystemControlType.OpenPowerShellAdmin, Name = "PowerShell Admin", Prefix = "powershell", Icon = "🔵", Category = "Système",
                Description = "Ouvrir PowerShell (admin)" },
        new() { Type = SystemControlType.RestartExplorer, Name = "Redém. Explorer", Prefix = "restartexplorer", Icon = "📁", Category = "Système",
                Description = "Redémarrer l'Explorateur Windows" },
        new() { Type = SystemControlType.OpenStartupFolder, Name = "Démarrage", Prefix = "startup", Icon = "🚀", Category = "Système",
                Description = "Ouvrir le dossier de démarrage Windows" },
        new() { Type = SystemControlType.OpenHostsFile, Name = "Fichier hosts", Prefix = "hosts", Icon = "📝", Category = "Système",
                Description = "Ouvrir le fichier hosts (admin)" }
    ];
    
    /// <summary>
    /// Réinitialise les commandes système aux valeurs par défaut.
    /// </summary>
    public void ResetSystemCommands() => SystemCommands = GetDefaultSystemCommands();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    // Migration: ajouter les nouvelles commandes système manquantes
                    settings.MigrateSystemCommands();
                    System.Diagnostics.Debug.WriteLine($"[Settings] Chargé avec {settings.PinnedItems.Count} épingles");
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Erreur chargement: {ex.Message}");
        }
        
        return new AppSettings();
    }

    /// <summary>
    /// Ajoute les commandes système manquantes et met à jour les catégories (migration).
    /// </summary>
    private void MigrateSystemCommands()
    {
        var defaultCommands = GetDefaultSystemCommands();
        var existingTypes = SystemCommands.Select(c => c.Type).ToHashSet();
        
        // Supprimer les commandes obsolètes (Notes et Timers ont été remplacés par des widgets)
        SystemCommands.RemoveAll(c => c.Type == SystemControlType.Notes || c.Type == SystemControlType.Timers);
        
        // Ajouter les commandes manquantes
        foreach (var cmd in defaultCommands)
        {
            if (!existingTypes.Contains(cmd.Type))
            {
                SystemCommands.Add(cmd);
            }
        }
        
        // Mettre à jour les catégories des commandes existantes (si vides)
        foreach (var existingCmd in SystemCommands)
        {
            if (string.IsNullOrEmpty(existingCmd.Category))
            {
                var defaultCmd = defaultCommands.FirstOrDefault(d => d.Type == existingCmd.Type);
                if (defaultCmd != null)
                {
                    existingCmd.Category = defaultCmd.Category;
                }
            }
        }
        
        // Réorganiser les commandes selon l'ordre par défaut (par catégorie)
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
    
    public void AddToSearchHistory(HistoryItem item)
    {
        if (!EnableSearchHistory || item == null || string.IsNullOrWhiteSpace(item.Path)) return;
        
        // Supprimer les doublons basés sur le chemin
        SearchHistory.RemoveAll(h => h.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase));
        SearchHistory.Insert(0, item);
        
        if (SearchHistory.Count > MaxSearchHistory)
            SearchHistory.RemoveRange(MaxSearchHistory, SearchHistory.Count - MaxSearchHistory);
    }
    
    public void ClearSearchHistory() => SearchHistory.Clear();
    
    // === Gestion des items épinglés ===
    
    /// <summary>
    /// Épingle un item.
    /// </summary>
    public void PinItem(string name, string path, ResultType type, string? icon = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        
        // Vérifier si déjà épinglé
        if (PinnedItems.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;
        
        PinnedItems.Add(new PinnedItem
        {
            Name = name,
            Path = path,
            Type = type,
            Icon = icon,
            PinnedAt = DateTime.Now,
            Order = PinnedItems.Count
        });
    }
    
    /// <summary>
    /// Désépingle un item.
    /// </summary>
    public bool UnpinItem(string path)
    {
        var item = PinnedItems.FirstOrDefault(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            PinnedItems.Remove(item);
            // Réordonner
            for (int i = 0; i < PinnedItems.Count; i++)
                PinnedItems[i].Order = i;
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Vérifie si un item est épinglé.
    /// </summary>
    public bool IsPinned(string path)
    {
        return PinnedItems.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Déplace un item épinglé vers le haut.
    /// </summary>
    public void MovePinnedItemUp(string path)
    {
        var index = PinnedItems.FindIndex(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (index > 0)
        {
            (PinnedItems[index], PinnedItems[index - 1]) = (PinnedItems[index - 1], PinnedItems[index]);
            PinnedItems[index].Order = index;
            PinnedItems[index - 1].Order = index - 1;
        }
    }
    
    /// <summary>
    /// Déplace un item épinglé vers le bas.
    /// </summary>
    public void MovePinnedItemDown(string path)
    {
        var index = PinnedItems.FindIndex(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index < PinnedItems.Count - 1)
        {
            (PinnedItems[index], PinnedItems[index + 1]) = (PinnedItems[index + 1], PinnedItems[index]);
            PinnedItems[index].Order = index;
            PinnedItems[index + 1].Order = index + 1;
        }
    }
    
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

/// <summary>
/// Types d'actions de contrôle système.
/// IMPORTANT: Ne pas réorganiser les valeurs existantes pour la compatibilité JSON!
/// </summary>
public enum SystemControlType
{
    // Valeurs originales (ne pas modifier l'ordre)
    Volume = 0,
    Mute = 1,
    Brightness = 2,
    Wifi = 3,
    Lock = 4,
    Sleep = 5,
    Hibernate = 6,
    Shutdown = 7,
    Restart = 8,
    Screenshot = 9,
    
    // Nouvelles valeurs (ajouter à la fin uniquement)
    FlushDns = 10,
    Logoff = 11,
    EmptyRecycleBin = 12,
    OpenTaskManager = 13,
    OpenWindowsSettings = 14,
    OpenControlPanel = 15,
    EmptyTemp = 16,
    OpenCmdAdmin = 17,
    OpenPowerShellAdmin = 18,
    RestartExplorer = 19,
    SystemSearch = 20,
    Timer = 21,
    Timers = 22,
    Note = 23,
    Notes = 24,
    
    // Commandes supplémentaires
    OpenStartupFolder = 25,
    OpenHostsFile = 26,
    
    // Intégrations web (APIs directes)
    Weather = 27,
    Translate = 28,
    AiChat = 29
}

/// <summary>
/// Configuration d'une commande de contrôle système personnalisable.
/// </summary>
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
    
    /// <summary>
    /// Crée une copie de la commande.
    /// </summary>
    public SystemControlCommand Clone() => new()
    {
        Type = Type,
        Name = Name,
        Prefix = Prefix,
        Icon = Icon,
        Description = Description,
        IsEnabled = IsEnabled,
        RequiresArgument = RequiresArgument,
        ArgumentHint = ArgumentHint,
        Category = Category,
    };
}

/// <summary>
/// Poids configurables pour l'algorithme de scoring de recherche.
/// Permet aux utilisateurs de personnaliser l'importance relative des différents types de correspondance.
/// </summary>
public sealed class ScoringWeights
{
    /// <summary>
    /// Score pour une correspondance exacte (nom == requête).
    /// </summary>
    public int ExactMatch { get; set; } = 1000;
    
    /// <summary>
    /// Score de base lorsque le nom commence par la requête.
    /// </summary>
    public int StartsWith { get; set; } = 800;
    
    /// <summary>
    /// Score de base lorsque le nom contient la requête.
    /// </summary>
    public int Contains { get; set; } = 600;
    
    /// <summary>
    /// Score pour une correspondance par initiales (ex: "vs" -> "Visual Studio").
    /// </summary>
    public int InitialsMatch { get; set; } = 500;
    
    /// <summary>
    /// Score de base pour une correspondance par sous-séquence.
    /// </summary>
    public int SubsequenceMatch { get; set; } = 300;
    
    /// <summary>
    /// Multiplicateur maximum pour la similarité de Levenshtein (fuzzy matching).
    /// Le score final est: similarity * FuzzyMatchMultiplier
    /// </summary>
    public int FuzzyMatchMultiplier { get; set; } = 250;
    
    /// <summary>
    /// Score maximum ajouté pour la fréquence d'utilisation.
    /// </summary>
    public int MaxUsageBonus { get; set; } = 500;
    
    /// <summary>
    /// Points ajoutés par utilisation (plafonné par MaxUsageBonus).
    /// </summary>
    public int UsageBonusPerUse { get; set; } = 50;
    
    /// <summary>
    /// Bonus pour un mot de la requête correspondant exactement à un mot du résultat.
    /// </summary>
    public int ExactWordBonus { get; set; } = 50;
    
    /// <summary>
    /// Seuil minimum de similarité pour le fuzzy matching (0.0 à 1.0).
    /// </summary>
    public double FuzzyMatchThreshold { get; set; } = 0.6;
    
    /// <summary>
    /// Bonus pour les correspondances de caractères consécutifs dans les sous-séquences.
    /// </summary>
    public int ConsecutiveMatchBonus { get; set; } = 10;
    
    /// <summary>
    /// Bonus lorsqu'une correspondance est au début d'un mot.
    /// </summary>
    public int WordBoundaryBonus { get; set; } = 20;
    
    // === Fuzzy Per-Word (tolérance aux typos mot par mot) ===
    
    /// <summary>
    /// Multiplicateur pour le scoring fuzzy per-word.
    /// Le score final est: similaritéMoyenne * FuzzyPerWordMultiplier.
    /// Ex: "firfox" vs "Firefox" → similarité 0.86 × 500 = 430
    /// </summary>
    public int FuzzyPerWordMultiplier { get; set; } = 500;
    
    // === Recency Decay (bonus pour les items récemment utilisés) ===
    
    /// <summary>
    /// Active/désactive le bonus de recency (items récemment utilisés).
    /// </summary>
    public bool EnableRecencyBonus { get; set; } = true;
    
    /// <summary>
    /// Bonus maximum pour un item utilisé aujourd'hui.
    /// </summary>
    public int MaxRecencyBonus { get; set; } = 150;
    
    /// <summary>
    /// Nombre de points perdus par jour depuis la dernière utilisation.
    /// Exemple: avec DecayPerDay=5 et MaxRecencyBonus=150, le bonus atteint 0 après 30 jours.
    /// </summary>
    public int RecencyDecayPerDay { get; set; } = 5;
    
    // === Path Fuzzy Matching (recherche multi-mots sur le chemin complet) ===
    
    /// <summary>
    /// Active/désactive le fuzzy matching sur les chemins complets.
    /// Permet de trouver "proj quick" → C:\Projects\QuickLauncher
    /// </summary>
    public bool EnablePathFuzzyMatch { get; set; } = true;
    
    /// <summary>
    /// Score pour une correspondance exacte d'un segment de chemin.
    /// </summary>
    public int PathExactSegmentMatch { get; set; } = 200;
    
    /// <summary>
    /// Bonus additionnel quand tous les mots de la requête matchent des segments.
    /// </summary>
    public int PathAllWordsMatchBonus { get; set; } = 100;
    
    /// <summary>
    /// Réinitialise tous les poids aux valeurs par défaut.
    /// </summary>
    public void ResetToDefaults()
    {
        ExactMatch = 1000;
        StartsWith = 800;
        Contains = 600;
        InitialsMatch = 500;
        SubsequenceMatch = 300;
        FuzzyMatchMultiplier = 250;
        MaxUsageBonus = 500;
        UsageBonusPerUse = 50;
        ExactWordBonus = 50;
        FuzzyMatchThreshold = 0.6;
        ConsecutiveMatchBonus = 10;
        WordBoundaryBonus = 20;
        FuzzyPerWordMultiplier = 500;
        EnableRecencyBonus = true;
        MaxRecencyBonus = 150;
        RecencyDecayPerDay = 5;
        EnablePathFuzzyMatch = true;
        PathExactSegmentMatch = 200;
        PathAllWordsMatchBonus = 100;
    }
    
    /// <summary>
    /// Crée une copie des poids.
    /// </summary>
    public ScoringWeights Clone() => new()
    {
        ExactMatch = ExactMatch,
        StartsWith = StartsWith,
        Contains = Contains,
        InitialsMatch = InitialsMatch,
        SubsequenceMatch = SubsequenceMatch,
        FuzzyMatchMultiplier = FuzzyMatchMultiplier,
        MaxUsageBonus = MaxUsageBonus,
        UsageBonusPerUse = UsageBonusPerUse,
        ExactWordBonus = ExactWordBonus,
        FuzzyMatchThreshold = FuzzyMatchThreshold,
        ConsecutiveMatchBonus = ConsecutiveMatchBonus,
        WordBoundaryBonus = WordBoundaryBonus,
        FuzzyPerWordMultiplier = FuzzyPerWordMultiplier,
        EnableRecencyBonus = EnableRecencyBonus,
        MaxRecencyBonus = MaxRecencyBonus,
        RecencyDecayPerDay = RecencyDecayPerDay,
        EnablePathFuzzyMatch = EnablePathFuzzyMatch,
        PathExactSegmentMatch = PathExactSegmentMatch,
        PathAllWordsMatchBonus = PathAllWordsMatchBonus
    };
}

/// <summary>
/// Item épinglé par l'utilisateur pour accès rapide.
/// </summary>
public sealed class PinnedItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ResultType Type { get; set; }
    public string? Icon { get; set; }
    public DateTime PinnedAt { get; set; }
    public int Order { get; set; }
    
    /// <summary>
    /// Convertit en SearchResult pour l'affichage.
    /// </summary>
    public SearchResult ToSearchResult() => new()
    {
        Name = Name,
        Path = Path,
        Type = Type,
        Description = "⭐ Épinglé",
        DisplayIcon = Icon ?? GetDefaultIcon(),
        Score = 10000 + (1000 - Order) // Score très élevé pour apparaître en premier
    };
    
    private string GetDefaultIcon() => Type switch
    {
        ResultType.Application => "🚀",
        ResultType.StoreApp => "🪧",
        ResultType.File => "📄",
        ResultType.Folder => "📁",
        ResultType.Script => "⚡",
        ResultType.Bookmark => "⭐",
        _ => "📌"
    };
}

/// <summary>
/// Note rapide de l'utilisateur.
/// </summary>
public sealed class NoteItem
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Retourne un aperçu tronqué de la note.
    /// </summary>
    public string Preview => Content.Length > 50 ? Content[..47] + "..." : Content;
    
    /// <summary>
    /// Retourne la date formatée.
    /// </summary>
    public string DateFormatted => CreatedAt.ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// Item de l'historique (programme/fichier cliqué).
/// </summary>
public sealed class HistoryItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ResultType Type { get; set; }
    public string? Icon { get; set; }
    public DateTime LastUsed { get; set; }
    
    /// <summary>
    /// Convertit en SearchResult pour l'affichage.
    /// </summary>
    public SearchResult ToSearchResult() => new()
    {
        Name = Name,
        Path = Path,
        Type = Type,
        Description = $"🕐 {LastUsed:dd/MM HH:mm}",
        DisplayIcon = Icon ?? GetDefaultIcon()
    };
    
    private string GetDefaultIcon() => Type switch
    {
        ResultType.Application => "🚀",
        ResultType.StoreApp => "🪧",
        ResultType.File => "📄",
        ResultType.Folder => "📁",
        ResultType.Script => "⚡",
        ResultType.Bookmark => "⭐",
        _ => "📌"
    };
}

/// <summary>
/// Information d'un widget de note sur le bureau.
/// </summary>
public sealed class NoteWidgetInfo
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Top { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Information d'un widget de minuterie sur le bureau.
/// </summary>
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
