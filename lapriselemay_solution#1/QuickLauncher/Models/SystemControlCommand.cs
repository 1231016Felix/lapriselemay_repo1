using System.Text.Json.Serialization;

namespace QuickLauncher.Models;

/// <summary>
/// Types de commandes de contrôle système.
/// </summary>
public enum SystemControlType
{
    Volume = 0, Mute = 1, Brightness = 2, Wifi = 3, Lock = 4, Sleep = 5,
    Hibernate = 6, Shutdown = 7, Restart = 8, Screenshot = 9,
    FlushDns = 10, Logoff = 11, EmptyRecycleBin = 12, OpenTaskManager = 13,
    OpenWindowsSettings = 14, OpenControlPanel = 15, EmptyTemp = 16,
    OpenCmdAdmin = 17, OpenPowerShellAdmin = 18, RestartExplorer = 19,
    SystemSearch = 20, Timer = 21, Note = 23,
    // Timers (22) et Notes (24) supprimés — anciens sous-menus, purgés par migration.
    OpenStartupFolder = 25, OpenHostsFile = 26,
    Weather = 27, Translate = 28, AiChat = 29,
    ProcessKill = 30, DiskInfo = 31,
    // Commandes application
    AppSettings = 32, AppQuit = 33, AppReindex = 34,
    AppHistory = 35, AppClearHistory = 36, AppHelp = 37
}

/// <summary>
/// Définition d'une commande de contrôle système configurable.
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
    /// Indique si c'est une commande application (préfixe ::) vs système (préfixe :).
    /// </summary>
    [JsonIgnore]
    public bool IsAppCommand => Category == "Application";
    
    /// <summary>
    /// Retourne le préfixe complet avec : ou :: selon le type de commande.
    /// </summary>
    [JsonIgnore]
    public string FullPrefix => IsAppCommand ? $"::{Prefix}" : $":{Prefix}";
    
    public SystemControlCommand Clone() => new()
    {
        Type = Type, Name = Name, Prefix = Prefix, Icon = Icon,
        Description = Description, IsEnabled = IsEnabled,
        RequiresArgument = RequiresArgument, ArgumentHint = ArgumentHint, Category = Category,
    };
}