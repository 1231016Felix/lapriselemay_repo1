using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickLauncher.Models;
using QuickLauncher.Services;

namespace QuickLauncher.ViewModels;

/// <summary>
/// ViewModel pour la fenêtre principale avec commandes système optimisées.
/// </summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    private readonly IndexingService _indexingService;
    private AppSettings _settings;
    
    private static readonly FrozenDictionary<string, AppSystemCommand> AppCommands = 
        new Dictionary<string, AppSystemCommand>(StringComparer.OrdinalIgnoreCase)
        {
            // Commandes de navigation
            [":settings"] = new("⚙️", "Paramètres", "Ouvrir les paramètres", SystemAction.OpenSettings),
            ["settings"] = new("⚙️", "Paramètres", "Ouvrir les paramètres", SystemAction.OpenSettings),
            [":quit"] = new("🚪", "Quitter", "Fermer QuickLauncher", SystemAction.Quit),
            [":exit"] = new("🚪", "Quitter", "Fermer QuickLauncher", SystemAction.Quit),
            [":reload"] = new("🔄", "Réindexer", "Reconstruire l'index", SystemAction.Reindex),
            [":reindex"] = new("🔄", "Réindexer", "Reconstruire l'index", SystemAction.Reindex),
            [":history"] = new("📜", "Historique", "Afficher l'historique", SystemAction.ShowHistory),
            [":clear"] = new("🗑️", "Effacer", "Effacer l'historique", SystemAction.ClearHistory),
            [":help"] = new("❓", "Aide", "Commandes disponibles", SystemAction.ShowHelp),
            ["?"] = new("❓", "Aide", "Commandes disponibles", SystemAction.ShowHelp),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    
    [ObservableProperty]
    private string _searchText = string.Empty;
    
    [ObservableProperty]
    private int _selectedIndex;
    
    [ObservableProperty]
    private bool _hasResults;
    
    [ObservableProperty]
    private bool _isIndexing;
    
    public ObservableCollection<SearchResult> Results { get; } = [];
    
    public event EventHandler? RequestHide;
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;

    public LauncherViewModel(IndexingService indexingService)
    {
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _settings = AppSettings.Load();
        
        _indexingService.IndexingStarted += (_, _) => IsIndexing = true;
        _indexingService.IndexingCompleted += (_, _) => IsIndexing = false;
    }

    partial void OnSearchTextChanged(string value) => UpdateResults();
    
    private void UpdateResults()
    {
        Results.Clear();
        _settings = AppSettings.Load();
        
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ShowRecentHistory();
            return;
        }
        
        var query = SearchText.Trim();
        var queryLower = query.ToLowerInvariant();
        
        // Vérifier d'abord les commandes de contrôle système personnalisables
        if (IsSystemControlCommand(queryLower))
        {
            AddSystemControlSuggestions(queryLower);
            FinalizeResults();
            return;
        }
        
        // Commandes système correspondantes (settings, quit, etc.)
        AddMatchingAppCommands(query);
        
        // Si exactement une commande système, pas besoin d'autres résultats
        if (AppCommands.ContainsKey(query))
        {
            FinalizeResults();
            return;
        }
        
        // Résultats de recherche normaux
        var searchResults = _indexingService.Search(SearchText);
        foreach (var result in searchResults)
            Results.Add(result);
        
        FinalizeResults();
    }

    /// <summary>
    /// Vérifie si la requête correspond à une commande de contrôle système.
    /// </summary>
    private bool IsSystemControlCommand(string query)
    {
        // Obtenir les préfixes actifs depuis les paramètres
        var enabledCommands = _settings.SystemCommands.Where(c => c.IsEnabled).ToList();
        
        foreach (var cmd in enabledCommands)
        {
            var prefix = $":{cmd.Prefix}";
            if (query.StartsWith(prefix) || prefix.StartsWith(query))
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// Ajoute les suggestions de commandes de contrôle système basées sur les paramètres.
    /// </summary>
    private void AddSystemControlSuggestions(string query)
    {
        var enabledCommands = _settings.SystemCommands.Where(c => c.IsEnabled).ToList();
        
        // Ajouter les suggestions correspondantes
        foreach (var cmd in enabledCommands)
        {
            var prefix = $":{cmd.Prefix}";
            var displayName = cmd.RequiresArgument 
                ? $":{cmd.Prefix} {cmd.ArgumentHint}" 
                : $":{cmd.Prefix}";
            
            if (prefix.StartsWith(query) || displayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Results.Add(new SearchResult
                {
                    Name = displayName,
                    Description = cmd.Description,
                    Type = ResultType.SystemControl,
                    DisplayIcon = cmd.Icon,
                    Path = prefix
                });
            }
        }

        // Traitement des commandes avec arguments
        var parts = query.Split(' ', 2);
        if (parts.Length >= 1)
        {
            var cmdPrefix = parts[0].TrimStart(':');
            var arg = parts.Length > 1 ? parts[1] : null;
            
            var matchedCmd = enabledCommands.FirstOrDefault(c => 
                c.Prefix.Equals(cmdPrefix, StringComparison.OrdinalIgnoreCase));
            
            if (matchedCmd != null)
            {
                AddExecutableResult(matchedCmd, arg, query);
            }
        }
    }

    /// <summary>
    /// Ajoute un résultat exécutable pour une commande avec argument.
    /// </summary>
    private void AddExecutableResult(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        switch (cmd.Type)
        {
            case SystemControlType.Volume:
                var currentVol = SystemControlService.GetVolume();
                if (string.IsNullOrEmpty(arg))
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = $"Volume actuel: {currentVol}%",
                        Description = "Appuyez sur Entrée pour voir le volume",
                        Type = ResultType.SystemControl,
                        DisplayIcon = cmd.Icon,
                        Path = fullQuery
                    });
                }
                else if (int.TryParse(arg, out var volLevel))
                {
                    var clampedVol = Math.Clamp(volLevel, 0, 100);
                    Results.Insert(0, new SearchResult
                    {
                        Name = $"Régler le volume à {clampedVol}%",
                        Description = $"Volume actuel: {currentVol}%",
                        Type = ResultType.SystemControl,
                        DisplayIcon = clampedVol > 50 ? "🔊" : clampedVol > 0 ? "🔉" : "🔇",
                        Path = fullQuery
                    });
                }
                else if (arg is "up" or "down" or "+" or "-")
                {
                    var direction = arg is "up" or "+" ? "Augmenter" : "Diminuer";
                    Results.Insert(0, new SearchResult
                    {
                        Name = $"{direction} le volume de 10%",
                        Description = $"Volume actuel: {currentVol}%",
                        Type = ResultType.SystemControl,
                        DisplayIcon = cmd.Icon,
                        Path = fullQuery
                    });
                }
                break;
                
            case SystemControlType.Brightness:
                if (!string.IsNullOrEmpty(arg) && int.TryParse(arg, out var brightLevel))
                {
                    var clampedBright = Math.Clamp(brightLevel, 0, 100);
                    Results.Insert(0, new SearchResult
                    {
                        Name = $"Régler la luminosité à {clampedBright}%",
                        Description = "Appuyez sur Entrée pour appliquer",
                        Type = ResultType.SystemControl,
                        DisplayIcon = clampedBright > 50 ? "☀️" : "🌙",
                        Path = fullQuery
                    });
                }
                break;
                
            case SystemControlType.Wifi:
                if (arg == "on")
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = "Activer le WiFi",
                        Description = "Appuyez sur Entrée pour activer",
                        Type = ResultType.SystemControl,
                        DisplayIcon = "📶",
                        Path = fullQuery
                    });
                }
                else if (arg == "off")
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = "Désactiver le WiFi",
                        Description = "Appuyez sur Entrée pour désactiver",
                        Type = ResultType.SystemControl,
                        DisplayIcon = "📵",
                        Path = fullQuery
                    });
                }
                else if (arg == "status")
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = "Afficher l'état du WiFi",
                        Description = "Appuyez sur Entrée pour voir le statut",
                        Type = ResultType.SystemControl,
                        DisplayIcon = cmd.Icon,
                        Path = fullQuery
                    });
                }
                break;
                
            case SystemControlType.Screenshot:
                if (arg is "snip" or "region" or "select")
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = "Capture de région",
                        Description = "Ouvrir l'outil de capture Windows",
                        Type = ResultType.SystemControl,
                        DisplayIcon = "✂️",
                        Path = fullQuery
                    });
                }
                else if (arg is "primary" or "main")
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = "Capture écran principal",
                        Description = "Capturer uniquement l'écran principal",
                        Type = ResultType.SystemControl,
                        DisplayIcon = cmd.Icon,
                        Path = fullQuery
                    });
                }
                else
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = "Prendre une capture d'écran",
                        Description = "Sauvegarde dans Images/Screenshots",
                        Type = ResultType.SystemControl,
                        DisplayIcon = cmd.Icon,
                        Path = fullQuery
                    });
                }
                break;
                
            case SystemControlType.Lock:
            case SystemControlType.Sleep:
            case SystemControlType.Hibernate:
            case SystemControlType.Shutdown:
            case SystemControlType.Restart:
            case SystemControlType.Mute:
                // Ces commandes n'ont pas d'arguments, le résultat est déjà ajouté
                break;
        }
    }
    
    private void ShowRecentHistory()
    {
        if (!_settings.EnableSearchHistory || _settings.SearchHistory.Count == 0)
        {
            FinalizeResults();
            return;
        }
        
        foreach (var history in _settings.SearchHistory.Take(5))
        {
            Results.Add(new SearchResult
            {
                Name = history,
                Description = "Recherche récente",
                Type = ResultType.SearchHistory,
                DisplayIcon = "🕐"
            });
        }
        
        FinalizeResults();
    }
    
    private void AddMatchingAppCommands(string query)
    {
        var matchingCommands = AppCommands
            .Where(kv => kv.Key.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Select(kv => new SearchResult
            {
                Name = kv.Value.Name,
                Description = kv.Value.Description,
                Type = ResultType.SystemCommand,
                DisplayIcon = kv.Value.Icon,
                Path = kv.Key
            })
            .DistinctBy(r => r.Name)
            .Take(3);
        
        foreach (var cmd in matchingCommands)
            Results.Add(cmd);
    }
    
    private void FinalizeResults()
    {
        HasResults = Results.Count > 0;
        SelectedIndex = HasResults ? 0 : -1;
    }

    [RelayCommand]
    private void Execute()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) 
            return;
        
        var item = Results[SelectedIndex];
        
        switch (item.Type)
        {
            case ResultType.SystemCommand:
                ExecuteAppCommand(item.Path);
                break;
            
            case ResultType.SystemControl:
                ExecuteSystemControl(item.Path);
                break;
                
            case ResultType.SearchHistory:
                SearchText = item.Name;
                break;
                
            default:
                LaunchItem(item);
                break;
        }
    }
    
    private void LaunchItem(SearchResult item)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            if (_settings.EnableSearchHistory)
            {
                _settings.AddToSearchHistory(SearchText);
                _settings.Save();
            }
        }
        
        _indexingService.RecordUsage(item);
        LaunchService.Launch(item);
        RequestHide?.Invoke(this, EventArgs.Empty);
    }
    
    private void ExecuteAppCommand(string? command)
    {
        if (string.IsNullOrEmpty(command) || !AppCommands.TryGetValue(command, out var sysCmd))
            return;
        
        switch (sysCmd.Action)
        {
            case SystemAction.OpenSettings:
                RequestHide?.Invoke(this, EventArgs.Empty);
                RequestOpenSettings?.Invoke(this, EventArgs.Empty);
                break;
                
            case SystemAction.Quit:
                RequestQuit?.Invoke(this, EventArgs.Empty);
                break;
                
            case SystemAction.Reindex:
                RequestHide?.Invoke(this, EventArgs.Empty);
                RequestReindex?.Invoke(this, EventArgs.Empty);
                break;
                
            case SystemAction.ShowHistory:
                ShowSearchHistory();
                break;
                
            case SystemAction.ClearHistory:
                ClearHistory();
                break;
                
            case SystemAction.ShowHelp:
                ShowHelpCommands();
                break;
        }
    }

    private void ExecuteSystemControl(string? command)
    {
        if (string.IsNullOrEmpty(command))
            return;

        // Convertir le préfixe personnalisé vers le format attendu par SystemControlService
        var normalizedCommand = NormalizeSystemCommand(command);
        var result = SystemControlService.ExecuteCommand(normalizedCommand);
        
        if (result != null)
        {
            Results.Clear();
            Results.Add(new SearchResult
            {
                Name = result.Message,
                Description = result.Success ? "Commande exécutée" : "Erreur",
                Type = ResultType.SystemControl,
                DisplayIcon = result.Success ? "✅" : "❌"
            });

            if (result.Success && !string.IsNullOrEmpty(result.FilePath))
            {
                Results.Add(new SearchResult
                {
                    Name = "Ouvrir la capture",
                    Description = result.FilePath,
                    Type = ResultType.File,
                    Path = result.FilePath,
                    DisplayIcon = "📂"
                });
            }

            FinalizeResults();

            // Pour certaines commandes, fermer après exécution
            var commandLower = normalizedCommand.ToLowerInvariant();
            if (result.Success && (commandLower.Contains("sleep") || commandLower.Contains("lock") ||
                commandLower.Contains("shutdown") || commandLower.Contains("restart") || 
                commandLower.Contains("hibernate")))
            {
                RequestHide?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Convertit une commande avec préfixe personnalisé vers le format standard.
    /// </summary>
    private string NormalizeSystemCommand(string command)
    {
        var parts = command.TrimStart(':').Split(' ', 2);
        var prefix = parts[0];
        var arg = parts.Length > 1 ? parts[1] : null;
        
        // Trouver la commande correspondante dans les paramètres
        var matchedCmd = _settings.SystemCommands.FirstOrDefault(c => 
            c.IsEnabled && c.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase));
        
        if (matchedCmd == null)
            return command; // Retourner tel quel si non trouvé
        
        // Convertir vers le nom de commande standard utilisé par SystemControlService
        var standardCmd = matchedCmd.Type switch
        {
            SystemControlType.Volume => "volume",
            SystemControlType.Mute => "mute",
            SystemControlType.Brightness => "brightness",
            SystemControlType.Wifi => "wifi",
            SystemControlType.Lock => "lock",
            SystemControlType.Sleep => "sleep",
            SystemControlType.Hibernate => "hibernate",
            SystemControlType.Shutdown => "shutdown",
            SystemControlType.Restart => "restart",
            SystemControlType.Screenshot => "screenshot",
            _ => prefix
        };
        
        return string.IsNullOrEmpty(arg) ? $":{standardCmd}" : $":{standardCmd} {arg}";
    }
    
    private void ShowSearchHistory()
    {
        Results.Clear();
        
        if (_settings.SearchHistory.Count == 0)
        {
            Results.Add(new SearchResult
            {
                Name = "Aucun historique",
                Description = "Votre historique est vide",
                Type = ResultType.SystemCommand,
                DisplayIcon = "📭"
            });
        }
        else
        {
            foreach (var history in _settings.SearchHistory)
            {
                Results.Add(new SearchResult
                {
                    Name = history,
                    Description = "Recherche récente",
                    Type = ResultType.SearchHistory,
                    DisplayIcon = "🕐"
                });
            }
        }
        
        FinalizeResults();
    }
    
    private void ClearHistory()
    {
        _settings.ClearSearchHistory();
        _settings.Save();
        SearchText = string.Empty;
        RequestHide?.Invoke(this, EventArgs.Empty);
    }

    private void ShowHelpCommands()
    {
        Results.Clear();
        
        // Commandes de base de l'application
        Results.Add(new SearchResult 
        { 
            Name = ":settings", 
            Description = "Ouvrir les paramètres", 
            Type = ResultType.SystemCommand, 
            DisplayIcon = "⚙️", 
            Path = ":settings" 
        });
        Results.Add(new SearchResult 
        { 
            Name = ":reload", 
            Description = "Réindexer les fichiers", 
            Type = ResultType.SystemCommand, 
            DisplayIcon = "🔄", 
            Path = ":reload" 
        });
        Results.Add(new SearchResult 
        { 
            Name = ":history", 
            Description = "Voir l'historique", 
            Type = ResultType.SystemCommand, 
            DisplayIcon = "📜", 
            Path = ":history" 
        });
        Results.Add(new SearchResult 
        { 
            Name = ":clear", 
            Description = "Effacer l'historique", 
            Type = ResultType.SystemCommand, 
            DisplayIcon = "🗑️", 
            Path = ":clear" 
        });
        Results.Add(new SearchResult 
        { 
            Name = ":quit", 
            Description = "Fermer QuickLauncher", 
            Type = ResultType.SystemCommand, 
            DisplayIcon = "🚪", 
            Path = ":quit" 
        });
        
        // Commandes de contrôle système personnalisables (depuis les paramètres)
        foreach (var cmd in _settings.SystemCommands.Where(c => c.IsEnabled))
        {
            var displayName = cmd.RequiresArgument 
                ? $":{cmd.Prefix} {cmd.ArgumentHint}" 
                : $":{cmd.Prefix}";
            
            Results.Add(new SearchResult 
            { 
                Name = displayName, 
                Description = cmd.Description, 
                Type = ResultType.SystemControl, 
                DisplayIcon = cmd.Icon
            });
        }
        
        // Recherche web
        foreach (var engine in _settings.SearchEngines.Take(4))
        {
            Results.Add(new SearchResult 
            { 
                Name = $"{engine.Prefix} [recherche]", 
                Description = $"Recherche {engine.Name}", 
                Type = ResultType.SystemCommand, 
                DisplayIcon = "🌐" 
            });
        }
        
        FinalizeResults();
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        
        var newIndex = SelectedIndex + delta;
        
        if (newIndex < 0) 
            newIndex = Results.Count - 1;
        else if (newIndex >= Results.Count) 
            newIndex = 0;
        
        SelectedIndex = newIndex;
    }
    
    public void Reset()
    {
        SearchText = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
        HasResults = false;
    }
}

/// <summary>
/// Actions système disponibles via commandes de l'application.
/// </summary>
public enum SystemAction
{
    OpenSettings,
    Quit,
    Reindex,
    ShowHistory,
    ClearHistory,
    ShowHelp
}

/// <summary>
/// Définition d'une commande système de l'application.
/// </summary>
public readonly record struct AppSystemCommand(
    string Icon, 
    string Name, 
    string Description, 
    SystemAction Action);
