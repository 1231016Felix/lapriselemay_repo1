using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.IO;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickLauncher.Models;
using QuickLauncher.Services;

namespace QuickLauncher.ViewModels;

/// <summary>
/// ViewModel pour la fenêtre principale avec commandes système optimisées.
/// </summary>
public sealed partial class LauncherViewModel : ObservableObject, IDisposable
{
    private readonly IndexingService _indexingService;
    private readonly FileWatcherService? _fileWatcherService;
    private readonly AliasService _aliasService;
    private readonly System.Timers.Timer _debounceTimer;
    private AppSettings _settings;
    private CancellationTokenSource? _searchCts;
    private bool _disposed;
    private string _pendingSearchText = string.Empty;
    
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
    
    [ObservableProperty]
    private bool _isSearching;
    
    [ObservableProperty]
    private FilePreview? _currentPreview;
    
    [ObservableProperty]
    private bool _showPreviewPanel;
    
    [ObservableProperty]
    private bool _showActionsPanel;
    
    [ObservableProperty]
    private int _selectedActionIndex;
    
    public ObservableCollection<SearchResult> Results { get; } = [];
    public ObservableCollection<FileAction> AvailableActions { get; } = [];
    
    public event EventHandler? RequestHide;
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;
    public event EventHandler<string>? RequestRename;
    public event EventHandler<(string Name, string Path)>? RequestCreateAlias;
    public event EventHandler<string>? ShowNotification;
    
    /// <summary>
    /// Déclenche l'événement RequestCreateAlias depuis l'extérieur de la classe.
    /// </summary>
    public void TriggerCreateAlias(string name, string path)
    {
        RequestCreateAlias?.Invoke(this, (name, path));
    }
    
    /// <summary>
    /// Service d'alias exposé pour l'UI.
    /// </summary>
    public AliasService AliasService => _aliasService;

    public LauncherViewModel(IndexingService indexingService)
    {
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _settings = AppSettings.Load();
        _aliasService = new AliasService();
        
        _indexingService.IndexingStarted += (_, _) => IsIndexing = true;
        _indexingService.IndexingCompleted += (_, _) => 
        {
            IsIndexing = false;
            // Démarrer le FileWatcher après l'indexation initiale
            _fileWatcherService?.Start();
        };
        
        // Initialiser le timer de debouncing
        _debounceTimer = new System.Timers.Timer(Constants.SearchDebounceMs);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += OnDebounceTimerElapsed;
        
        // Initialiser le FileWatcher
        try
        {
            _fileWatcherService = new FileWatcherService();
            _fileWatcherService.FilesChanged += OnFilesChanged;
        }
        catch
        {
            // FileWatcher optionnel - continuer sans
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        // Stocker le texte en attente et redémarrer le timer de debouncing
        _pendingSearchText = value;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }
    
    private void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // Exécuter la mise à jour sur le thread UI
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Vérifier que le texte n'a pas changé pendant le debounce
            if (_pendingSearchText == SearchText)
            {
                UpdateResultsInternal();
            }
        });
    }
    
    partial void OnSelectedIndexChanged(int value)
    {
        // Mettre à jour la prévisualisation quand la sélection change
        if (value >= 0 && value < Results.Count && _settings.ShowPreviewPanel)
        {
            _ = UpdatePreviewAsync(Results[value]);
            UpdateAvailableActions(Results[value]);
        }
        else
        {
            CurrentPreview = null;
            AvailableActions.Clear();
        }
    }
    
    private async Task UpdatePreviewAsync(SearchResult result)
    {
        try
        {
            // Ne pas prévisualiser certains types
            if (result.Type is ResultType.WebSearch or ResultType.Calculator 
                or ResultType.SystemCommand or ResultType.SystemControl 
                or ResultType.SearchHistory)
            {
                CurrentPreview = null;
                return;
            }
            
            var preview = await FilePreviewService.GeneratePreviewAsync(result.Path);
            CurrentPreview = preview;
        }
        catch
        {
            CurrentPreview = null;
        }
    }
    
    private void UpdateAvailableActions(SearchResult result)
    {
        AvailableActions.Clear();
        var isPinned = _settings.IsPinned(result.Path);
        var actions = FileActionProvider.GetActionsForResult(result, isPinned);
        foreach (var action in actions)
            AvailableActions.Add(action);
        
        SelectedActionIndex = 0;
    }
    
    private void OnFilesChanged(object? sender, FileChangesEventArgs e)
    {
        // Mettre à jour l'index de manière incrémentale
        System.Diagnostics.Debug.WriteLine($"[FileWatcher] {e.Changes.Count} changements détectés");
        
        // Traiter les changements dans l'IndexingService
        _indexingService.ProcessFileChanges(e.Changes);
        
        // Rafraîchir les résultats si une recherche est en cours
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => UpdateResults());
        }
    }
    
    private void UpdateResults()
    {
        // Appel direct sans debouncing (utilisé pour les rafraîchissements forcés)
        UpdateResultsInternal();
    }
    
    private void UpdateResultsInternal()
    {
        // Annuler toute recherche précédente
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        
        Results.Clear();
        CurrentPreview = null;
        AvailableActions.Clear();
        // Note: Ne pas recharger _settings ici - utiliser l'instance en mémoire
        // Le rechargement causait des conditions de course avec Pin/Unpin
        
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ShowRecentHistory();
            return;
        }
        
        var query = SearchText.Trim();
        var queryLower = query.ToLowerInvariant();
        
        // Commande :find pour la recherche Windows Search (vérifier si activée)
        var findCmd = _settings.SystemCommands.FirstOrDefault(c => c.Type == SystemControlType.SystemSearch);
        var findPrefix = findCmd?.Prefix ?? "find";
        var findEnabled = findCmd?.IsEnabled ?? true;
        
        if (findEnabled && queryLower.StartsWith($":{findPrefix} ") && query.Length > findPrefix.Length + 2)
        {
            var searchQuery = query[(findPrefix.Length + 2)..].Trim();
            if (searchQuery.Length >= 2)
            {
                _ = PerformWindowsSearchAsync(searchQuery, _searchCts.Token);
                return;
            }
        }
        
        // Suggestion pour :find (si activée)
        if (findEnabled && (queryLower.StartsWith($":{findPrefix}") || $":{findPrefix}".StartsWith(queryLower)))
        {
            Results.Add(new SearchResult
            {
                Name = $":{findPrefix} <terme>",
                Description = findCmd?.Description ?? "Rechercher dans tout le système via Windows Search",
                Type = ResultType.SystemCommand,
                DisplayIcon = findCmd?.Icon ?? "🔍",
                Path = $":{findPrefix}"
            });
            
            if (queryLower == $":{findPrefix}")
            {
                FinalizeResults();
                return;
            }
        }
        
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
        
        // Recherche d'alias (priorité haute)
        if (_settings.EnableAliases)
        {
            var aliasResults = _aliasService.Search(query);
            foreach (var alias in aliasResults.Take(3))
            {
                alias.DisplayIcon = "⌨️";
                alias.Description = $"Alias → {alias.Path}";
                Results.Add(alias);
            }
        }
        
        // Résultats de recherche normaux
        var searchResults = _indexingService.Search(SearchText);
        foreach (var result in searchResults)
            Results.Add(result);
        
        FinalizeResults();
    }
    
    private async Task PerformWindowsSearchAsync(string query, CancellationToken token)
    {
        IsSearching = true;
        
        try
        {
            // Appliquer la profondeur de recherche depuis les paramètres
            UniversalSearchService.MaxSearchDepth = _settings.SystemSearchDepth;
            
            // Déterminer le moteur utilisé pour l'affichage
            var engineInfo = UniversalSearchService.GetEngineInfo();
            var engineName = engineInfo.Name;
            
            // Afficher un indicateur de recherche
            Results.Add(new SearchResult
            {
                Name = "Recherche en cours...",
                Description = $"Recherche de '{query}' via {engineName}",
                Type = ResultType.SystemCommand,
                DisplayIcon = "⏳"
            });
            FinalizeResults();
            
            var results = await UniversalSearchService.SearchAsync(query, null, token);
            
            if (token.IsCancellationRequested) return;
            
            Results.Clear();
            
            if (results.Count == 0)
            {
                Results.Add(new SearchResult
                {
                    Name = "Aucun résultat",
                    Description = $"Aucun fichier trouvé pour '{query}'",
                    Type = ResultType.SystemCommand,
                    DisplayIcon = "❌"
                });
            }
            else
            {
                foreach (var result in results)
                    Results.Add(result);
            }
            
            FinalizeResults();
        }
        catch (OperationCanceledException)
        {
            // Recherche annulée
        }
        catch (Exception ex)
        {
            Results.Clear();
            Results.Add(new SearchResult
            {
                Name = "Erreur de recherche",
                Description = ex.Message,
                Type = ResultType.SystemCommand,
                DisplayIcon = "⚠️"
            });
            FinalizeResults();
        }
        finally
        {
            IsSearching = false;
        }
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
    /// Vérifie si une commande système spécifique est activée.
    /// </summary>
    private bool IsSystemCommandEnabled(SystemControlType type)
    {
        return _settings.SystemCommands.Any(c => c.Type == type && c.IsEnabled);
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
            case SystemControlType.Logoff:
            case SystemControlType.EmptyRecycleBin:
            case SystemControlType.OpenTaskManager:
            case SystemControlType.OpenWindowsSettings:
            case SystemControlType.OpenControlPanel:
            case SystemControlType.EmptyTemp:
            case SystemControlType.OpenCmdAdmin:
            case SystemControlType.OpenPowerShellAdmin:
            case SystemControlType.RestartExplorer:
            case SystemControlType.FlushDns:
                // Ces commandes n'ont pas d'arguments, le résultat est déjà ajouté
                break;
                
            case SystemControlType.Timer:
                if (!string.IsNullOrEmpty(arg))
                {
                    var timerParts = arg.Split(' ', 2);
                    var duration = timerParts[0];
                    var label = timerParts.Length > 1 ? timerParts[1] : null;
                    var parsedDuration = TimerWidgetService.ParseDuration(duration);
                    
                    if (parsedDuration != null)
                    {
                        var durationText = TimerWidgetService.FormatDuration(parsedDuration.Value);
                        Results.Insert(0, new SearchResult
                        {
                            Name = $"⏱️ Créer minuterie: {durationText}",
                            Description = string.IsNullOrEmpty(label) ? "Appuyez sur Entrée pour démarrer" : $"Label: {label}",
                            Type = ResultType.SystemControl,
                            DisplayIcon = cmd.Icon,
                            Path = fullQuery
                        });
                    }
                    else
                    {
                        Results.Insert(0, new SearchResult
                        {
                            Name = "Format invalide",
                            Description = "Utilisez: 5m, 30s, 1h, 1h30m, etc.",
                            Type = ResultType.SystemControl,
                            DisplayIcon = "❌",
                            Path = ""
                        });
                    }
                }
                break;
                
            case SystemControlType.OpenStartupFolder:
                Results.Insert(0, new SearchResult
                {
                    Name = "Ouvrir le dossier de démarrage",
                    Description = "Appuyez sur Entrée pour ouvrir",
                    Type = ResultType.SystemControl,
                    DisplayIcon = cmd.Icon,
                    Path = fullQuery
                });
                break;
                
            case SystemControlType.OpenHostsFile:
                Results.Insert(0, new SearchResult
                {
                    Name = "Ouvrir le fichier hosts (admin)",
                    Description = "Appuyez sur Entrée pour ouvrir avec privilèges admin",
                    Type = ResultType.SystemControl,
                    DisplayIcon = cmd.Icon,
                    Path = fullQuery
                });
                break;
                
            case SystemControlType.Definition:
                if (!string.IsNullOrEmpty(arg))
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = $"📖 Définition de \"{arg}\"",
                        Description = "Appuyez sur Entrée pour chercher la définition",
                        Type = ResultType.SystemControl,
                        DisplayIcon = cmd.Icon,
                        Path = fullQuery
                    });
                }
                break;
                
            case SystemControlType.Translate:
                if (!string.IsNullOrEmpty(arg))
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = $"🌐 Traduire \"{arg}\"",
                        Description = "Appuyez sur Entrée pour traduire",
                        Type = ResultType.SystemControl,
                        DisplayIcon = cmd.Icon,
                        Path = fullQuery
                    });
                }
                break;
                
            case SystemControlType.Note:
                if (!string.IsNullOrEmpty(arg))
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = $"📝 Créer une note",
                        Description = arg.Length > 50 ? arg[..47] + "..." : arg,
                        Type = ResultType.SystemControl,
                        DisplayIcon = cmd.Icon,
                        Path = fullQuery
                    });
                }
                break;
        }
    }
    
    private void ShowRecentHistory()
    {
        // Afficher d'abord les items épinglés
        foreach (var pinned in _settings.PinnedItems.OrderBy(p => p.Order))
        {
            Results.Add(pinned.ToSearchResult());
        }
        
        // Puis l'historique si activé (items récemment utilisés)
        if (_settings.EnableSearchHistory && _settings.SearchHistory.Count > 0)
        {
            var maxHistory = Math.Max(0, 5 - _settings.PinnedItems.Count);
            foreach (var historyItem in _settings.SearchHistory.Take(maxHistory))
            {
                Results.Add(historyItem.ToSearchResult());
            }
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
                
            case ResultType.Note:
                // Les notes sont maintenant des widgets, pas d'action spéciale ici
                break;
                
            case ResultType.SearchHistory:
                // L'historique contient maintenant des items cliqués, on les relance
                LaunchItem(item);
                break;
                
            default:
                LaunchItem(item);
                break;
        }
    }
    
    /// <summary>
    /// Exécute l'action sélectionnée sur le résultat courant.
    /// </summary>
    [RelayCommand]
    private void ExecuteAction()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return;
        if (SelectedActionIndex < 0 || SelectedActionIndex >= AvailableActions.Count) return;
        
        var result = Results[SelectedIndex];
        var action = AvailableActions[SelectedActionIndex];
        
        ExecuteActionOnResult(action, result);
    }
    
    /// <summary>
    /// Exécute une action spécifique sur un résultat.
    /// </summary>
    public void ExecuteActionOnResult(FileAction action, SearchResult result)
    {
        // Demander confirmation si nécessaire
        if (action.RequiresConfirmation)
        {
            // La confirmation sera gérée par l'UI
            // Pour l'instant, on exécute directement
        }
        
        // Cas spécial pour Rename
        if (action.ActionType == FileActionType.Rename)
        {
            RequestRename?.Invoke(this, result.Path);
            return;
        }
        
        // Cas spécial pour Pin
        if (action.ActionType == FileActionType.Pin)
        {
            _settings.PinItem(result.Name, result.Path, result.Type, result.DisplayIcon);
            _settings.Save();
            ShowNotification?.Invoke(this, "⭐ Épinglé");
            UpdateAvailableActions(result); // Rafraîchir les actions
            ShowActionsPanel = false;
            return;
        }
        
        // Cas spécial pour Unpin
        if (action.ActionType == FileActionType.Unpin)
        {
            _settings.UnpinItem(result.Path);
            _settings.Save();
            ShowNotification?.Invoke(this, "📌 Désépinglé");
            UpdateAvailableActions(result); // Rafraîchir les actions
            // Si on était dans la vue des épingles, rafraîchir
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                Results.Clear();
                ShowRecentHistory();
            }
            ShowActionsPanel = false;
            return;
        }
        
        // Cas spécial pour CreateAlias
        if (action.ActionType == FileActionType.CreateAlias)
        {
            RequestCreateAlias?.Invoke(this, (result.Name, result.Path));
            ShowActionsPanel = false;
            return;
        }
        
        var success = action.Execute(result.Path);
        
        if (success)
        {
            // Notification de succès
            var message = action.ActionType switch
            {
                FileActionType.CopyPath => "Chemin copié",
                FileActionType.CopyName => "Nom copié",
                FileActionType.CopyUrl => "URL copiée",
                FileActionType.Delete => "Envoyé à la corbeille",
                _ => null
            };
            
            if (message != null)
                ShowNotification?.Invoke(this, message);
            
            // Fermer après certaines actions
            if (action.ActionType is FileActionType.Open 
                or FileActionType.RunAsAdmin 
                or FileActionType.OpenPrivate)
            {
                _indexingService.RecordUsage(result);
                RequestHide?.Invoke(this, EventArgs.Empty);
            }
        }
        
        ShowActionsPanel = false;
    }
    
    /// <summary>
    /// Exécute en mode administrateur.
    /// </summary>
    [RelayCommand]
    private void ExecuteAsAdmin()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return;
        
        var result = Results[SelectedIndex];
        if (result.Type is ResultType.Application or ResultType.Script or ResultType.File)
        {
            FileActionExecutor.Execute(FileActionType.RunAsAdmin, result.Path);
            _indexingService.RecordUsage(result);
            RequestHide?.Invoke(this, EventArgs.Empty);
        }
    }
    
    /// <summary>
    /// Ouvre l'emplacement du fichier.
    /// </summary>
    [RelayCommand]
    private void OpenLocation()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return;
        
        var result = Results[SelectedIndex];
        FileActionExecutor.Execute(FileActionType.OpenLocation, result.Path);
    }
    
    /// <summary>
    /// Copie le chemin dans le presse-papiers.
    /// </summary>
    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return;
        
        var result = Results[SelectedIndex];
        if (FileActionExecutor.Execute(FileActionType.CopyPath, result.Path))
        {
            ShowNotification?.Invoke(this, "Chemin copié");
        }
    }
    
    /// <summary>
    /// Bascule l'affichage du panneau de prévisualisation.
    /// </summary>
    [RelayCommand]
    private void TogglePreview()
    {
        ShowPreviewPanel = !ShowPreviewPanel;
        _settings.ShowPreviewPanel = ShowPreviewPanel;
        _settings.Save();
        
        if (ShowPreviewPanel && SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            _ = UpdatePreviewAsync(Results[SelectedIndex]);
        }
    }
    
    /// <summary>
    /// Bascule l'affichage du panneau d'actions.
    /// </summary>
    [RelayCommand]
    private void ToggleActions()
    {
        ShowActionsPanel = !ShowActionsPanel;
        
        if (ShowActionsPanel && SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            UpdateAvailableActions(Results[SelectedIndex]);
        }
    }
    
    private void LaunchItem(SearchResult item)
    {
        // Enregistrer l'item cliqué dans l'historique (au lieu de la requête de recherche)
        if (_settings.EnableSearchHistory && !string.IsNullOrWhiteSpace(item.Path))
        {
            var historyItem = new HistoryItem
            {
                Name = item.Name,
                Path = item.Path,
                Type = item.Type,
                Icon = item.DisplayIcon,
                LastUsed = DateTime.Now
            };
            _settings.AddToSearchHistory(historyItem);
            _settings.Save();
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
        
        // Gérer les commandes timer et note
        var parts = command.TrimStart(':').Split(' ', 2);
        var cmdPrefix = parts[0];
        var arg = parts.Length > 1 ? parts[1] : null;
        
        var matchedCmd = _settings.SystemCommands.FirstOrDefault(c => 
            c.IsEnabled && c.Prefix.Equals(cmdPrefix, StringComparison.OrdinalIgnoreCase));
        
        if (matchedCmd != null)
        {
            switch (matchedCmd.Type)
            {
                case SystemControlType.Timer:
                    if (!string.IsNullOrEmpty(arg))
                    {
                        var timerParts = arg.Split(' ', 2);
                        var duration = timerParts[0];
                        var label = timerParts.Length > 1 ? timerParts[1] : null;
                        
                        var timerWidget = TimerWidgetService.Instance.CreateWidget(duration, label);
                        if (timerWidget != null)
                        {
                            var durationText = TimerWidgetService.FormatDuration(TimeSpan.FromSeconds(timerWidget.DurationSeconds));
                            ShowResult($"⏱️ Minuterie créée: {durationText}", timerWidget.Label);
                            RequestHide?.Invoke(this, EventArgs.Empty);
                        }
                        else
                        {
                            ShowResult("❌ Format invalide", "Utilisez: 5m, 30s, 1h, 1h30m, etc.");
                        }
                    }
                    return;
                    
                case SystemControlType.Note:
                    if (!string.IsNullOrEmpty(arg))
                    {
                        var widgetInfo = NoteWidgetService.Instance.CreateWidget(arg);
                        ShowResult("📝 Note créée!", widgetInfo.Content.Length > 50 ? widgetInfo.Content[..47] + "..." : widgetInfo.Content);
                        RequestHide?.Invoke(this, EventArgs.Empty);
                    }
                    return;
                    
                case SystemControlType.Definition:
                    if (!string.IsNullOrEmpty(arg))
                        OpenDefinition(arg);
                    return;
                    
                case SystemControlType.Translate:
                    if (!string.IsNullOrEmpty(arg))
                        OpenTranslation(arg);
                    return;
            }
        }

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
                commandLower.Contains("hibernate") || commandLower.Contains("logoff") ||
                commandLower.Contains("restartexplorer")))
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
            SystemControlType.Logoff => "logoff",
            SystemControlType.EmptyRecycleBin => "emptybin",
            SystemControlType.OpenTaskManager => "taskmgr",
            SystemControlType.OpenWindowsSettings => "winsettings",
            SystemControlType.OpenControlPanel => "control",
            SystemControlType.EmptyTemp => "emptytemp",
            SystemControlType.OpenCmdAdmin => "cmd",
            SystemControlType.OpenPowerShellAdmin => "powershell",
            SystemControlType.RestartExplorer => "restartexplorer",
            SystemControlType.FlushDns => "flushdns",
            SystemControlType.OpenStartupFolder => "startup",
            SystemControlType.OpenHostsFile => "hosts",
            SystemControlType.Definition => "definition",
            SystemControlType.Translate => "translate",
            _ => prefix
        };
        
        return string.IsNullOrEmpty(arg) ? $":{standardCmd}" : $":{standardCmd} {arg}";
    }
    
    /// <summary>
    /// Affiche un résultat simple (message de confirmation).
    /// </summary>
    private void ShowResult(string name, string description)
    {
        Results.Clear();
        Results.Add(new SearchResult
        {
            Name = name,
            Description = description,
            Type = ResultType.SystemControl,
            DisplayIcon = name.Contains("✅") ? "✅" : name.Contains("❌") ? "❌" : "ℹ️"
        });
        FinalizeResults();
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
            foreach (var historyItem in _settings.SearchHistory)
            {
                Results.Add(historyItem.ToSearchResult());
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
    
    /// <summary>
    /// Ouvre la définition d'un mot dans le navigateur.
    /// </summary>
    private void OpenDefinition(string word)
    {
        try
        {
            // Utiliser Le Dictionnaire (fr) ou Wiktionary
            var encodedWord = Uri.EscapeDataString(word.Trim());
            var url = $"https://fr.wiktionary.org/wiki/{encodedWord}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            ShowResult($"📖 Définition de \"{word}\"", "Ouverture dans le navigateur...");
            RequestHide?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowResult("❌ Erreur", ex.Message);
        }
    }
    
    /// <summary>
    /// Ouvre Google Translate pour traduire le texte.
    /// </summary>
    private void OpenTranslation(string text)
    {
        try
        {
            // Parser le format ": texte en langue" ou juste "texte"
            var targetLang = "fr"; // Langue par défaut
            var textToTranslate = text;
            
            // Vérifier si le format contient " en " pour extraire la langue cible
            var enIndex = text.LastIndexOf(" en ", StringComparison.OrdinalIgnoreCase);
            if (enIndex > 0)
            {
                textToTranslate = text[..enIndex].Trim();
                var langPart = text[(enIndex + 4)..].Trim().ToLowerInvariant();
                
                // Mapper les noms de langues vers les codes ISO
                targetLang = langPart switch
                {
                    "français" or "french" or "fr" => "fr",
                    "anglais" or "english" or "en" => "en",
                    "espagnol" or "spanish" or "es" => "es",
                    "allemand" or "german" or "de" => "de",
                    "italien" or "italian" or "it" => "it",
                    "portugais" or "portuguese" or "pt" => "pt",
                    "chinois" or "chinese" or "zh" => "zh-CN",
                    "japonais" or "japanese" or "ja" => "ja",
                    "coréen" or "korean" or "ko" => "ko",
                    "russe" or "russian" or "ru" => "ru",
                    "arabe" or "arabic" or "ar" => "ar",
                    "néerlandais" or "dutch" or "nl" => "nl",
                    "polonais" or "polish" or "pl" => "pl",
                    _ => langPart.Length == 2 ? langPart : "fr"
                };
            }
            
            var encodedText = Uri.EscapeDataString(textToTranslate);
            var url = $"https://translate.google.com/?sl=auto&tl={targetLang}&text={encodedText}&op=translate";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            ShowResult($"🌐 Traduction vers {targetLang.ToUpperInvariant()}", "Ouverture dans le navigateur...");
            RequestHide?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowResult("❌ Erreur", ex.Message);
        }
    }
    
    private void ShowHelpCommands()
    {
        Results.Clear();
        
        // Commande de recherche système (si activée)
        var findCmd = _settings.SystemCommands.FirstOrDefault(c => c.Type == SystemControlType.SystemSearch);
        if (findCmd?.IsEnabled == true)
        {
            Results.Add(new SearchResult 
            { 
                Name = $":{findCmd.Prefix} <terme>", 
                Description = findCmd.Description, 
                Type = ResultType.SystemCommand, 
                DisplayIcon = findCmd.Icon,
                Path = $":{findCmd.Prefix}"
            });
        }
        
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
        
        // Raccourcis clavier
        Results.Add(new SearchResult 
        { 
            Name = "Raccourcis clavier", 
            Description = "Ctrl+Entrée: Admin • Ctrl+O: Emplacement • Ctrl+Maj+C: Copier chemin", 
            Type = ResultType.SystemCommand, 
            DisplayIcon = "⌨️" 
        });
        
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
    
    public void MoveActionSelection(int delta)
    {
        if (AvailableActions.Count == 0) return;
        
        var newIndex = SelectedActionIndex + delta;
        
        if (newIndex < 0) 
            newIndex = AvailableActions.Count - 1;
        else if (newIndex >= AvailableActions.Count) 
            newIndex = 0;
        
        SelectedActionIndex = newIndex;
    }
    
    /// <summary>
    /// Recharge les settings depuis le fichier pour synchroniser les changements
    /// (notamment les épingles modifiées depuis d'autres contextes).
    /// </summary>
    public void ReloadSettings()
    {
        _settings = AppSettings.Load();
    }
    
    public void Reset()
    {
        SearchText = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
        HasResults = false;
        CurrentPreview = null;
        ShowActionsPanel = false;
        AvailableActions.Clear();
        
        // Forcer l'affichage des épingles et historique
        // (OnSearchTextChanged ne se déclenche pas si SearchText était déjà vide)
        ShowRecentHistory();
    }
    
    /// <summary>
    /// Force le rafraîchissement des résultats de recherche.
    /// </summary>
    public void ForceRefresh()
    {
        _debounceTimer.Stop();
        UpdateResultsInternal();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _debounceTimer.Stop();
        _debounceTimer.Dispose();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _fileWatcherService?.Dispose();
        _aliasService?.Dispose();
        
        GC.SuppressFinalize(this);
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
