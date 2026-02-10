using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Services.CommandHandlers;

namespace QuickLauncher.ViewModels;

/// <summary>
/// ViewModel pour la fenêtre principale avec commandes système optimisées.
/// </summary>
public sealed partial class LauncherViewModel : ObservableObject, IDisposable
{
    private readonly IndexingService _indexingService;
    private readonly FileWatcherService? _fileWatcherService;
    private readonly AliasService _aliasService;
    private readonly ISettingsProvider _settingsProvider;
    private readonly NoteWidgetService _noteWidgetService;
    private readonly TimerWidgetService _timerWidgetService;
    private readonly NotesService _notesService;
    private readonly CommandRouter _commandRouter;
    private readonly ISystemControlExecutor _systemControlExecutor;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;
    
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
    
    [ObservableProperty]
    private string _ghostSuggestionText = string.Empty;
    
    [ObservableProperty]
    private bool _showCategoryBadges;
    
    [ObservableProperty]
    private bool _showSettingsButton;
    
    [ObservableProperty]
    private bool _showShortcutHints;
    
    public ObservableCollection<SearchResult> Results { get; } = [];
    public ObservableCollection<FileAction> AvailableActions { get; } = [];
    
    public event EventHandler? RequestHide;
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;
    public event EventHandler<string>? RequestRename;
    public event EventHandler<string>? ShowNotification;
    public event EventHandler? RequestCaretAtEnd;
    public event EventHandler<string?>? RequestScreenCapture;

    /// <summary>
    /// Accès rapide aux paramètres actuels (lecture seule, toujours à jour).
    /// Pour les mutations, utiliser _settingsProvider.Update() ou _settingsProvider.Save().
    /// </summary>
    private AppSettings _settings => _settingsProvider.Current;

    public LauncherViewModel(IndexingService indexingService, ISettingsProvider settingsProvider,
        AliasService aliasService, NoteWidgetService noteWidgetService, TimerWidgetService timerWidgetService,
        NotesService notesService, CommandRouter commandRouter, ISystemControlExecutor systemControlExecutor,
        FileWatcherService? fileWatcherService = null)
    {
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _aliasService = aliasService ?? throw new ArgumentNullException(nameof(aliasService));
        _noteWidgetService = noteWidgetService ?? throw new ArgumentNullException(nameof(noteWidgetService));
        _timerWidgetService = timerWidgetService ?? throw new ArgumentNullException(nameof(timerWidgetService));
        _notesService = notesService ?? throw new ArgumentNullException(nameof(notesService));
        _commandRouter = commandRouter ?? throw new ArgumentNullException(nameof(commandRouter));
        _systemControlExecutor = systemControlExecutor ?? throw new ArgumentNullException(nameof(systemControlExecutor));
        
        // Initialiser les propriétés d'apparence depuis les settings
        ShowCategoryBadges = _settings.ShowCategoryBadges;
        ShowSettingsButton = _settings.ShowSettingsButton;
        ShowShortcutHints = _settings.ShowShortcutHints;
        
        _indexingService.IndexingStarted += (_, _) => IsIndexing = true;
        _indexingService.IndexingCompleted += (_, _) => 
        {
            IsIndexing = false;
            // Démarrer le FileWatcher après l'indexation initiale
            _fileWatcherService?.Start();
        };
        
        // FileWatcher (injecté via DI, optionnel)
        _fileWatcherService = fileWatcherService;
        if (_fileWatcherService != null)
            _fileWatcherService.FilesChanged += OnFilesChanged;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Effacer le ghost immédiatement si le texte ne correspond plus
        // (évite un flash de suggestion périmée pendant le debounce)
        if (!string.IsNullOrEmpty(GhostSuggestionText)
            && !GhostSuggestionText.StartsWith(value, StringComparison.OrdinalIgnoreCase))
        {
            GhostSuggestionText = string.Empty;
        }
        
        // Annuler le debounce précédent et en démarrer un nouveau
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        _ = DebounceSearchAsync(value, _debounceCts.Token);
    }
    
    /// <summary>
    /// Debounce async : attend un court délai avant de lancer la recherche.
    /// Plus propre que System.Timers.Timer + Dispatcher.Invoke (pas de marshalling cross-thread).
    /// </summary>
    private async Task DebounceSearchAsync(string text, CancellationToken token)
    {
        try
        {
            await Task.Delay(Constants.SearchDebounceMs, token);
            // Vérifier que le texte n'a pas changé pendant le debounce
            if (!token.IsCancellationRequested && text == SearchText)
                UpdateResultsInternal();
        }
        catch (OperationCanceledException)
        {
            // Debounce annulé par une nouvelle frappe, c'est normal
        }
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
        
        // === Commandes asynchrones spécialisées ===
        // Le CommandRouter dispatche vers le bon handler (météo, traduction, IA, recherche système).
        var handler = _commandRouter.FindHandler(queryLower);
        if (handler != null)
        {
            _ = DispatchCommandAsync(handler, queryLower, _searchCts.Token);
            return;
        }
        
        // Suggestion pour :find (si activée, quand l'utilisateur tape le préfixe sans argument)
        var findCmd = _settings.SystemCommands.FirstOrDefault(c => c.Type == SystemControlType.SystemSearch);
        var findPrefix = findCmd?.Prefix ?? "find";
        var findEnabled = findCmd?.IsEnabled ?? true;
        
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
                
                // Ajouter les sous-commandes pour screenshot (snip doit apparaître même avec préfixe partiel)
                if (cmd.Type == SystemControlType.Screenshot && !query.Contains(" "))
                {
                    var snipName = $":{cmd.Prefix} snip";
                    Results.Add(new SearchResult
                    {
                        Name = snipName,
                        Description = "Sélectionner une zone à capturer (Outil Capture d'écran)",
                        Type = ResultType.SystemControl,
                        DisplayIcon = "✂️",
                        Path = snipName
                    });
                }
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
                        Name = "✂️ Capture de région",
                        Description = "Sélectionner une zone à capturer avec annotation",
                        Type = ResultType.SystemControl,
                        DisplayIcon = "✂️",
                        Path = fullQuery
                    });
                }
                else
                {
                    Results.Insert(0, new SearchResult
                    {
                        Name = "📸 Capture d'écran",
                        Description = "Ouvrir l'outil de capture Windows",
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
    
    /// <summary>
    /// Dispatche une commande asynchrone vers un handler spécialisé.
    /// Gère l'indicateur de chargement et l'affichage des résultats.
    /// Remplace les anciens blocs PerformXxxAsync monolithiques.
    /// </summary>
    private async Task DispatchCommandAsync(ICommandHandler handler, string query, CancellationToken token)
    {
        IsSearching = true;
        
        try
        {
            // Indicateur de chargement générique
            Results.Clear();
            Results.Add(new SearchResult
            {
                Name = "Chargement...",
                Description = "Requête en cours...",
                Type = ResultType.SystemControl,
                DisplayIcon = "⏳"
            });
            FinalizeResults();
            
            var result = await handler.ExecuteAsync(query, token);
            
            if (token.IsCancellationRequested) return;
            
            Results.Clear();
            foreach (var r in result.Results)
                Results.Add(r);
            FinalizeResults();
        }
        catch (OperationCanceledException)
        {
            // Recherche annulée par une nouvelle requête
        }
        catch (Exception ex)
        {
            Results.Clear();
            Results.Add(new SearchResult
            {
                Name = "Erreur",
                Description = ex.Message,
                Type = ResultType.SystemControl,
                DisplayIcon = "⚠️"
            });
            FinalizeResults();
        }
        finally
        {
            IsSearching = false;
        }
    }
    
    private void FinalizeResults()
    {
        HasResults = Results.Count > 0;
        SelectedIndex = HasResults ? 0 : -1;
        UpdateGhostSuggestion();
    }
    
    /// <summary>
    /// Met à jour la suggestion fantôme (ghost text) basée sur le premier résultat
    /// dont le nom commence par le texte saisi. Priorise les items les plus utilisés.
    /// </summary>
    private void UpdateGhostSuggestion()
    {
        if (!_settings.ShowGhostSuggestions || string.IsNullOrWhiteSpace(SearchText) || !HasResults)
        {
            GhostSuggestionText = string.Empty;
            return;
        }
        
        var query = SearchText.Trim();
        
        // Ne pas suggérer pour les commandes avec arguments (ex: ":note mon texte")
        // ni pour les préfixes de recherche web (ex: "g query")
        if (query.Contains(' '))
        {
            GhostSuggestionText = string.Empty;
            return;
        }
        
        // Chercher la meilleure suggestion parmi les résultats dont le nom commence par la query
        // Prioriser: les applications/fichiers fréquemment utilisés
        var bestMatch = Results
            .Where(r => r.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                        && r.Name.Length > query.Length
                        && r.Type is not (ResultType.Calculator or ResultType.WebSearch))
            .OrderByDescending(r => r.UseCount)
            .ThenByDescending(r => r.Score)
            .FirstOrDefault();
        
        if (bestMatch != null)
        {
            // Conserver la casse de l'utilisateur + complétion du résultat
            GhostSuggestionText = query + bestMatch.Name[query.Length..];
        }
        else
        {
            GhostSuggestionText = string.Empty;
        }
    }

    /// <summary>
    /// Accepte la suggestion fantôme : remplace le texte de recherche par la suggestion complète.
    /// </summary>
    /// <returns>true si une suggestion a été acceptée, false sinon.</returns>
    public bool AcceptGhostSuggestion()
    {
        if (string.IsNullOrEmpty(GhostSuggestionText))
            return false;
        
        SearchText = GhostSuggestionText;
        GhostSuggestionText = string.Empty;
        RequestCaretAtEnd?.Invoke(this, EventArgs.Empty);
        return true;
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
            _settingsProvider.Save();
            ShowNotification?.Invoke(this, "⭐ Épinglé");
            UpdateAvailableActions(result); // Rafraîchir les actions
            ShowActionsPanel = false;
            return;
        }
        
        // Cas spécial pour Unpin
        if (action.ActionType == FileActionType.Unpin)
        {
            _settings.UnpinItem(result.Path);
            _settingsProvider.Save();
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
        
        // Pour CopyName, passer le nom d'affichage plutôt que le path
        // (important pour les StoreApps où Path = package family name)
        var targetPath = action.ActionType == FileActionType.CopyName
            ? result.Name
            : result.Path;
        var success = action.Execute(targetPath);
        
        if (success)
        {
            // Notification de succès selon l'action
            var message = action.ActionType switch
            {
                FileActionType.CopyUrl => "🔗 URL copiée",
                FileActionType.CopyPath => "📋 Chemin copié",
                FileActionType.CopyName => "📋 Nom copié",
                FileActionType.Compress => "🗜️ Archive ZIP créée",
                FileActionType.SendByEmail => "📧 Email en cours...",
                FileActionType.Delete => "🗑️ Envoyé à la corbeille",
                _ => null
            };
            
            if (message != null)
                ShowNotification?.Invoke(this, message);
            
            // Fermer après les actions qui ouvrent quelque chose
            if (action.ActionType is FileActionType.Open 
                or FileActionType.RunAsAdmin 
                or FileActionType.OpenPrivate
                or FileActionType.OpenWith
                or FileActionType.OpenLocation
                or FileActionType.OpenInTerminal
                or FileActionType.OpenInExplorer
                or FileActionType.OpenInVSCode
                or FileActionType.EditInEditor
                or FileActionType.SendByEmail)
            {
                _indexingService.RecordUsage(result);
                RequestHide?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            if (action.ActionType == FileActionType.OpenInVSCode)
                ShowNotification?.Invoke(this, "❌ VS Code introuvable");
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
    /// Bascule l'affichage du panneau de prévisualisation.
    /// </summary>
    [RelayCommand]
    private void TogglePreview()
    {
        ShowPreviewPanel = !ShowPreviewPanel;
        _settings.ShowPreviewPanel = ShowPreviewPanel;
        _settingsProvider.Save();
        
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
            _settingsProvider.Save();
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
        
        var executionResult = _systemControlExecutor.Execute(command);
        
        if (!executionResult.Handled)
        {
            // Commande async non gérée par l'executor → re-router via CommandRouter
            ReRouteAsyncCommand(command);
            return;
        }
        
        // Appliquer le résultat déclaratif
        if (executionResult.AutoCompleteText != null)
        {
            SearchText = executionResult.AutoCompleteText;
            RequestCaretAtEnd?.Invoke(this, EventArgs.Empty);
            return;
        }
        
        if (executionResult.ResultsToShow != null)
        {
            Results.Clear();
            foreach (var r in executionResult.ResultsToShow)
                Results.Add(r);
            FinalizeResults();
        }
        
        if (executionResult.Notification != null)
            ShowNotification?.Invoke(this, executionResult.Notification);
        
        if (executionResult.ScreenCaptureMode != null)
        {
            RequestHide?.Invoke(this, EventArgs.Empty);
            _ = HandleScreenCaptureAsync(executionResult.ScreenCaptureMode);
            return;
        }
        
        if (executionResult.ShouldHide)
            RequestHide?.Invoke(this, EventArgs.Empty);
    }
    
    /// <summary>
    /// Re-route une commande async (météo, traduction, IA) vers le CommandRouter
    /// quand le SystemControlExecutor ne la gère pas (exécution via Enter sur un résultat).
    /// </summary>
    private void ReRouteAsyncCommand(string command)
    {
        var parts = command.TrimStart(':').Split(' ', 2);
        var cmdPrefix = parts[0];
        var arg = parts.Length > 1 ? parts[1] : null;
        
        var matchedCmd = _settings.SystemCommands.FirstOrDefault(c =>
            c.IsEnabled && c.Prefix.Equals(cmdPrefix, StringComparison.OrdinalIgnoreCase));
        
        if (matchedCmd == null) return;
        
        var fullQuery = $":{matchedCmd.Prefix}" + (arg != null ? $" {arg}" : "");
        var handler = _commandRouter.FindHandler(fullQuery);
        if (handler != null)
            _ = DispatchCommandAsync(handler, fullQuery, _searchCts?.Token ?? CancellationToken.None);
    }
    
    /// <summary>
    /// Gère la capture d'écran de manière asynchrone après avoir caché la fenêtre.
    /// </summary>
    private async Task HandleScreenCaptureAsync(string mode)
    {
        await Task.Delay(200);
        
        if (mode is "snip" or "region" or "select")
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                RequestScreenCapture?.Invoke(this, mode));
        }
        else
        {
            var path = SystemControlService.TakeScreenshot();
            if (path != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowNotification?.Invoke(this, "📸 Capture sauvegardée");
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                });
            }
        }
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
        _settingsProvider.Save();
        SearchText = string.Empty;
        RequestHide?.Invoke(this, EventArgs.Empty);
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
        _settingsProvider.Reload();
        ShowCategoryBadges = _settings.ShowCategoryBadges;
        ShowSettingsButton = _settings.ShowSettingsButton;
        ShowShortcutHints = _settings.ShowShortcutHints;
    }
    
    public void Reset()
    {
        SearchText = string.Empty;
        GhostSuggestionText = string.Empty;
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
    /// Annule tout debounce en cours et exécute la recherche immédiatement.
    /// </summary>
    public void ForceRefresh()
    {
        _debounceCts?.Cancel();
        UpdateResultsInternal();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
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
