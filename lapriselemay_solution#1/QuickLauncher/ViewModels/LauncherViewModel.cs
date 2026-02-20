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
    private readonly SearchService _searchService;
    private readonly IIconLoader _iconLoader;
    private readonly SystemControlSuggestionBuilder _suggestionBuilder = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;
    
    /// <summary>
    /// Compteur de génération pour invalider les résultats async périmés.
    /// Incrémenté à chaque nouvelle recherche pour que DispatchCommandAsync
    /// ne puisse pas écraser les résultats d'une recherche plus récente.
    /// </summary>
    private int _searchGeneration;
    
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
    public event EventHandler<(string Name, string Path)>? RequestCreateAlias;


    /// <summary>
    /// Accès rapide aux paramètres actuels (lecture seule, toujours à jour).
    /// Pour les mutations, utiliser _settingsProvider.Update() ou _settingsProvider.Save().
    /// </summary>
    private AppSettings _settings => _settingsProvider.Current;

    public LauncherViewModel(IndexingService indexingService, ISettingsProvider settingsProvider,
        AliasService aliasService, NoteWidgetService noteWidgetService, TimerWidgetService timerWidgetService,
        NotesService notesService, CommandRouter commandRouter, ISystemControlExecutor systemControlExecutor,
        SearchService searchService, IIconLoader iconLoader, FileWatcherService? fileWatcherService = null)
    {
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _aliasService = aliasService ?? throw new ArgumentNullException(nameof(aliasService));
        _noteWidgetService = noteWidgetService ?? throw new ArgumentNullException(nameof(noteWidgetService));
        _timerWidgetService = timerWidgetService ?? throw new ArgumentNullException(nameof(timerWidgetService));
        _notesService = notesService ?? throw new ArgumentNullException(nameof(notesService));
        _commandRouter = commandRouter ?? throw new ArgumentNullException(nameof(commandRouter));
        _systemControlExecutor = systemControlExecutor ?? throw new ArgumentNullException(nameof(systemControlExecutor));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _iconLoader = iconLoader ?? throw new ArgumentNullException(nameof(iconLoader));
        
        // Initialiser les propriétés d'apparence depuis les settings
        ShowCategoryBadges = _settings.Appearance.ShowCategoryBadges;
        ShowSettingsButton = _settings.Appearance.ShowSettingsButton;
        ShowShortcutHints = _settings.Appearance.ShowShortcutHints;
        
        _indexingService.IndexingStarted += (_, _) => IsIndexing = true;
        _indexingService.IndexingCompleted += (_, _) => IsIndexing = false;
        
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
    /// Utilise un délai allongé pour les commandes IA afin de laisser
    /// l'utilisateur finir sa question avant d'envoyer la requête.
    /// </summary>
    private async Task DebounceSearchAsync(string text, CancellationToken token)
    {
        try
        {
            var delayMs = Constants.SearchDebounceMs;
            
            // Délai allongé pour les commandes IA (configurable 1-4s)
            if (IsAiQuery(text))
            {
                var seconds = Math.Clamp(
                    _settings.Integrations.AiDebounceSeconds,
                    Constants.AiDebounceSecondsMin,
                    Constants.AiDebounceSecondsMax);
                delayMs = seconds * 1000;
                
                // Afficher un indicateur d'attente immédiat pour que l'utilisateur
                // sache que le mode IA est actif et attend la fin de la saisie
                ShowAiWaitingIndicator(text);
            }
            
            await Task.Delay(delayMs, token);
            // Vérifier que le texte n'a pas changé pendant le debounce
            if (!token.IsCancellationRequested && text == SearchText)
                UpdateResultsInternal();
        }
        catch (OperationCanceledException)
        {
            // Debounce annulé par une nouvelle frappe, c'est normal
        }
    }
    
    /// <summary>
    /// Vérifie si la requête correspond à une commande IA avec un argument.
    /// Ex: ":ai qu'est-ce qu'une API?" → true, ":ai" seul → false.
    /// </summary>
    private bool IsAiQuery(string text)
    {
        var cmd = _settings.SystemCommands.FirstOrDefault(c => c.Type == SystemControlType.AiChat);
        if (cmd is not { IsEnabled: true }) return false;
        var prefix = $":{cmd.Prefix} ";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length + 1;
    }
    
    /// <summary>
    /// Affiche un indicateur visuel pendant le délai d'attente IA.
    /// L'utilisateur voit qu'il est en mode IA et que le système
    /// attend la fin de sa saisie avant d'envoyer la requête.
    /// </summary>
    private void ShowAiWaitingIndicator(string text)
    {
        var delaySeconds = Math.Clamp(
            _settings.Integrations.AiDebounceSeconds,
            Constants.AiDebounceSecondsMin,
            Constants.AiDebounceSecondsMax);
        
        Results.Clear();
        Results.Add(new SearchResult
        {
            Name = "🤖 En attente de la fin de la saisie...",
            Description = $"La requête sera envoyée {delaySeconds}s après la dernière touche",
            Type = ResultType.SystemControl,
            DisplayIcon = "✍️",
            IsInfoBlock = true
        });
        FinalizeResults();
    }
    
    partial void OnSelectedIndexChanged(int value)
    {
        // Mettre à jour la prévisualisation quand la sélection change
        if (value >= 0 && value < Results.Count && _settings.Appearance.ShowPreviewPanel)
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
                or ResultType.AppControl or ResultType.SearchHistory)
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
        var isPinned = _settings.Search.IsPinned(result.Path);
        var hasAlias = _aliasService.GetAliasByTargetPath(result.Path) != null;
        var actions = FileActionProvider.GetActionsForResult(result, isPinned, hasAlias);
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
        // Annuler toute recherche précédente et invalider les résultats async en cours
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _searchGeneration);
        
        CurrentPreview = null;
        AvailableActions.Clear();
        // Amélioration #4 : capturer la référence settings une seule fois (cache mémoire pur)
        var settings = _settings;
        
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
            // Les commandes async gèrent leur propre cycle Results.Clear → affichage
            Results.Clear();
            _ = DispatchCommandAsync(handler, queryLower, generation, _searchCts.Token);
            return;
        }
        
        // Amélioration #3 : construire les résultats dans une liste temporaire
        // pour éviter le flash visuel causé par Results.Clear() suivi d'ajouts individuels.
        var tempResults = new List<SearchResult>();
        
        // Suggestion pour :find (si activée, quand l'utilisateur tape le préfixe sans argument)
        var findCmd = settings.SystemCommands.FirstOrDefault(c => c.Type == SystemControlType.SystemSearch);
        var findPrefix = findCmd?.Prefix ?? "find";
        var findEnabled = findCmd?.IsEnabled ?? true;
        
        if (findEnabled && (queryLower.StartsWith($":{findPrefix}") || $":{findPrefix}".StartsWith(queryLower)))
        {
            tempResults.Add(new SearchResult
            {
                Name = $":{findPrefix} <terme>",
                Description = findCmd?.Description ?? "Rechercher dans tout le système via Windows Search",
                Type = ResultType.SystemCommand,
                DisplayIcon = findCmd?.Icon ?? "🔍",
                Path = $":{findPrefix}"
            });
            
            if (queryLower == $":{findPrefix}")
            {
                SwapResults(tempResults);
                return;
            }
        }
        
        // Vérifier les commandes de contrôle système (inclut les commandes applicatives)
        if (IsSystemControlCommand(queryLower))
        {
            Results.Clear();
            AddSystemControlSuggestions(queryLower);
            FinalizeResults();
            return;
        }
        
        // Recherche d'alias (priorité haute)
        if (settings.Search.EnableAliases)
        {
            var aliasResults = _aliasService.Search(query);
            foreach (var alias in aliasResults.Take(3))
            {
                alias.DisplayIcon = "⌨️";
                alias.Description = $"Alias → {alias.Path}";
                tempResults.Add(alias);
            }
        }
        
        // Résultats de recherche normaux
        var searchResults = _searchService.Search(SearchText);
        foreach (var result in searchResults)
            tempResults.Add(result);
        
        SwapResults(tempResults);
    }
    
    /// <summary>
    /// Amélioration #3 : remplace le contenu de Results d'un coup pour éviter le flash visuel.
    /// Minimise les notifications de changement de collection en faisant un diff minimal.
    /// </summary>
    private void SwapResults(List<SearchResult> newResults)
    {
        // Stratégie simple et efficace : clear + bulk add
        // (un RangeObservableCollection serait encore mieux, mais ceci élimine déjà le flash
        //  car les résultats sont prêts avant le premier Clear)
        Results.Clear();
        foreach (var result in newResults)
            Results.Add(result);
        FinalizeResults();
        
        // Charger les icônes natives en arrière-plan
        if (newResults.Count > 0)
            _ = _iconLoader.LoadIconsAsync(newResults, _searchCts?.Token ?? CancellationToken.None);
    }
    

    /// <summary>
    /// Vérifie si la requête correspond à une commande de contrôle système.
    /// Distingue le préfixe ":" (commandes système) de "::" (commandes application).
    /// </summary>
    private bool IsSystemControlCommand(string query)
    {
        if (!query.StartsWith(':'))
            return false;
        
        var enabledCommands = _settings.SystemCommands.Where(c => c.IsEnabled).ToList();
        
        // Déterminer la portée du préfixe :
        // ":"  seul (1 car) → afficher tout (l'utilisateur n'a pas encore choisi : vs ::)
        // "::" → uniquement les commandes application
        // ":x" (x != ':') → uniquement les commandes système
        var isDoubleColon = query.StartsWith("::");
        var isSingleColonOnly = query.Length >= 2 && query[1] != ':';
        
        foreach (var cmd in enabledCommands)
        {
            if (isSingleColonOnly && cmd.IsAppCommand) continue;
            if (isDoubleColon && !cmd.IsAppCommand) continue;
            
            var prefix = cmd.FullPrefix;
            if (query.StartsWith(prefix) || prefix.StartsWith(query))
                return true;
        }
        
        return false;
    }
    

    /// <summary>
    /// Ajoute les suggestions de commandes de contrôle système basées sur les paramètres.
    /// Filtre selon la portée du préfixe : ":" (système) vs "::" (application).
    /// </summary>
    private void AddSystemControlSuggestions(string query)
    {
        var enabledCommands = _settings.SystemCommands.Where(c => c.IsEnabled).ToList();
        
        // Filtrer selon la portée :: vs :
        var isDoubleColon = query.StartsWith("::");
        var isSingleColonOnly = query.Length >= 2 && query[1] != ':';
        
        if (isSingleColonOnly)
            enabledCommands = enabledCommands.Where(c => !c.IsAppCommand).ToList();
        else if (isDoubleColon)
            enabledCommands = enabledCommands.Where(c => c.IsAppCommand).ToList();
        
        // Déterminer si la requête contient un argument (ex: ":sc snip" → arg = "snip")
        var parts = query.Split(' ', 2);
        var cmdPrefix = parts[0].TrimStart(':');
        var arg = parts.Length > 1 ? parts[1] : null;
        
        // Trouver la commande exacte correspondant au préfixe
        var matchedCmd = enabledCommands.FirstOrDefault(c => 
            c.Prefix.Equals(cmdPrefix, StringComparison.OrdinalIgnoreCase));
        
        // Si on a une commande exacte avec argument, ne générer QUE le résultat exécutable
        // (pas de suggestions de préfixe ni de sous-commandes, on sait ce que l'utilisateur veut)
        if (matchedCmd != null && arg != null)
        {
            AddExecutableResult(matchedCmd, arg, query);
            return;
        }
        
        // Ensemble des types déjà ajoutés via le builder, pour éviter les doublons
        var typesFromBuilder = new HashSet<SystemControlType>();
        
        // Si on a une commande exacte SANS argument, ajouter le résultat exécutable en premier
        if (matchedCmd != null)
        {
            var suggestions = _suggestionBuilder.Build(matchedCmd, null, query);
            if (suggestions is { Count: > 0 })
            {
                foreach (var s in suggestions)
                    Results.Add(s);
                typesFromBuilder.Add(matchedCmd.Type);
            }
            
            // Ajouter les sous-commandes pour screenshot (snip) même quand le préfixe est exact
            if (matchedCmd.Type == SystemControlType.Screenshot)
            {
                var snipName = $":{matchedCmd.Prefix} snip";
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
        
        // Ajouter les suggestions de préfixe partiel (pour les commandes dont le préfixe
        // commence par la saisie, ex: ":s" → ":sleep", ":screenshot", "::s" → "::settings", ...)
        foreach (var cmd in enabledCommands)
        {
            // Ne pas dupliquer la commande déjà traitée par le builder
            if (typesFromBuilder.Contains(cmd.Type))
                continue;
            
            var prefix = cmd.FullPrefix;
            var displayName = cmd.RequiresArgument 
                ? $"{cmd.FullPrefix} {cmd.ArgumentHint}" 
                : cmd.FullPrefix;
            
            if (prefix.StartsWith(query) || displayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Results.Add(new SearchResult
                {
                    Name = displayName,
                    Description = cmd.Description,
                    Type = cmd.IsAppCommand ? ResultType.AppControl : ResultType.SystemControl,
                    DisplayIcon = cmd.Icon,
                    Path = prefix
                });
                
                // Ajouter les sous-commandes pour screenshot (snip doit apparaître même avec préfixe partiel)
                if (cmd.Type == SystemControlType.Screenshot && !query.Contains(" "))
                {
                    var snipName = $"{cmd.FullPrefix} snip";
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
    }

    /// <summary>
    /// Ajoute un résultat exécutable pour une commande avec argument.
    /// Amélioration #1 : délègue au SystemControlSuggestionBuilder déclaratif.
    /// </summary>
    private void AddExecutableResult(SystemControlCommand cmd, string? arg, string fullQuery)
    {
        var suggestions = _suggestionBuilder.Build(cmd, arg, fullQuery);
        if (suggestions == null || suggestions.Count == 0)
            return;
        
        // Insérer en tête des résultats (le builder retourne déjà dans le bon ordre)
        for (var i = suggestions.Count - 1; i >= 0; i--)
            Results.Insert(0, suggestions[i]);
    }
    
    private void ShowRecentHistory()
    {
        Results.Clear();
        
        // Afficher d'abord les items épinglés
        foreach (var pinned in _settings.Search.PinnedItems.OrderBy(p => p.Order))
        {
            Results.Add(pinned.ToSearchResult());
        }
        
        // Puis l'historique si activé (items récemment utilisés)
        if (_settings.Search.EnableSearchHistory && _settings.Search.SearchHistory.Count > 0)
        {
            var maxHistory = Math.Max(0, 5 - _settings.Search.PinnedItems.Count);
            foreach (var historyItem in _settings.Search.SearchHistory.Take(maxHistory))
            {
                Results.Add(historyItem.ToSearchResult());
            }
        }
        
        FinalizeResults();
        
        // Charger les icônes natives en arrière-plan
        if (Results.Count > 0)
            _ = _iconLoader.LoadIconsAsync(Results.ToList(), _searchCts?.Token ?? CancellationToken.None);
    }
    
    /// <summary>
    /// Réordonne un item épinglé par drag & drop et rafraîchit l'affichage.
    /// </summary>
    public void ReorderPinnedItem(int fromIndex, int toIndex)
    {
        _settings.Search.ReorderPinnedItem(fromIndex, toIndex);
        _settingsProvider.Save();
        
        // Rafraîchir l'affichage si on est dans la vue des épingles
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            Results.Clear();
            ShowRecentHistory();
        }
    }
    
    /// <summary>
    /// Nombre d'items épinglés actuels.
    /// </summary>
    public int PinnedItemCount => _settings.Search.PinnedItems.Count;
    
    /// <summary>
    /// Vérifie si un résultat à l'index donné est un item épinglé.
    /// </summary>
    public bool IsResultPinned(int resultIndex)
    {
        if (resultIndex < 0 || resultIndex >= Results.Count) return false;
        return resultIndex < _settings.Search.PinnedItems.Count 
               && Results[resultIndex].Description == "⭐ Épinglé";
    }
    
    /// <summary>
    /// Exécute une action applicative retournée par le SystemControlExecutor.
    /// </summary>
    private void HandleAppAction(AppAction action)
    {
        switch (action)
        {
            case AppAction.OpenSettings:
                RequestHide?.Invoke(this, EventArgs.Empty);
                RequestOpenSettings?.Invoke(this, EventArgs.Empty);
                break;
            case AppAction.Quit:
                RequestQuit?.Invoke(this, EventArgs.Empty);
                break;
            case AppAction.Reindex:
                RequestHide?.Invoke(this, EventArgs.Empty);
                RequestReindex?.Invoke(this, EventArgs.Empty);
                break;
            case AppAction.ShowHistory:
                ShowSearchHistory();
                break;
            case AppAction.ClearHistory:
                ClearHistory();
                break;
            case AppAction.ShowHelp:
                ShowHelpCommands();
                break;
        }
    }
    
    /// <summary>
    /// Dispatche une commande asynchrone vers un handler spécialisé.
    /// Gère l'indicateur de chargement et l'affichage des résultats.
    /// Remplace les anciens blocs PerformXxxAsync monolithiques.
    /// </summary>
    private async Task DispatchCommandAsync(ICommandHandler handler, string query, int generation, CancellationToken token)
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
            
            // Vérifier que cette recherche est toujours la plus récente
            if (token.IsCancellationRequested || generation != _searchGeneration) return;
            
            Results.Clear();
            foreach (var r in result.Results)
                Results.Add(r);
            FinalizeResults();
            
            // Charger les icônes natives en arrière-plan
            if (result.Results.Count > 0)
                _ = _iconLoader.LoadIconsAsync(result.Results, token);
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
        if (!_settings.Appearance.ShowGhostSuggestions || string.IsNullOrWhiteSpace(SearchText) || !HasResults)
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
        
        // 1. Commandes système/application (ex: ":w" → ":weather", "::s" → "::settings")
        if (query.StartsWith(':') && query.Length >= 2)
        {
            // Distinguer :: (commandes application) de : (commandes système)
            if (query.StartsWith("::"))
            {
                // Préfixe "::" → ne suggérer que les commandes application
                var appQuery = query[2..]; // sans le "::"
                if (appQuery.Length == 0)
                {
                    GhostSuggestionText = string.Empty;
                    return;
                }
                var bestAppCmd = _settings.SystemCommands
                    .Where(c => c.IsEnabled && c.IsAppCommand
                                && c.Prefix.StartsWith(appQuery, StringComparison.OrdinalIgnoreCase)
                                && c.Prefix.Length > appQuery.Length)
                    .OrderBy(c => c.Prefix.Length)
                    .FirstOrDefault();
                
                if (bestAppCmd != null)
                {
                    GhostSuggestionText = $"::{bestAppCmd.Prefix}";
                    return;
                }
            }
            else
            {
                // Préfixe ":" → ne suggérer que les commandes système (pas application)
                var cmdQuery = query[1..]; // sans le ":"
                var bestCmd = _settings.SystemCommands
                    .Where(c => c.IsEnabled && !c.IsAppCommand
                                && c.Prefix.StartsWith(cmdQuery, StringComparison.OrdinalIgnoreCase)
                                && c.Prefix.Length > cmdQuery.Length)
                    .OrderBy(c => c.Prefix.Length)
                    .FirstOrDefault();
                
                if (bestCmd != null)
                {
                    GhostSuggestionText = $":{bestCmd.Prefix}";
                    return;
                }
            }
        }
        
        // 2. Préfixes de recherche web (ex: "g" → "g ", "yt" → "yt ")
        var matchingEngine = _settings.Search.SearchEngines
            .FirstOrDefault(e => e.Prefix.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                                 && e.Prefix.Length >= query.Length);
        if (matchingEngine != null && matchingEngine.Prefix.Length == query.Length)
        {
            // Le préfixe est complet, suggérer l'espace
            GhostSuggestionText = $"{matchingEngine.Prefix} ";
            return;
        }
        
        // 3. Résultats de recherche (applications, fichiers, historique)
        var bestMatch = Results
            .Where(r => r.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                        && r.Name.Length > query.Length
                        && r.Type is not (ResultType.Calculator or ResultType.WebSearch
                            or ResultType.SystemControl or ResultType.AppControl or ResultType.SystemCommand))
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
            case ResultType.SystemControl:
            case ResultType.AppControl:
                // Les items SystemControl issus de l'index (ms-settings:, control|, .msc, etc.)
                // doivent être lancés directement via LaunchService, pas via l'executor de commandes.
                // Seules les commandes préfixées par ':' sont gérées par ExecuteSystemControl.
                if (!string.IsNullOrEmpty(item.Path) && !item.Path.StartsWith(':'))
                    LaunchItem(item);
                else
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
    /// Enregistre un alias via l'AliasService.
    /// </summary>
    public void SaveAlias(string alias, string targetPath)
    {
        _aliasService.SetAlias(alias, targetPath);
        ShowNotification?.Invoke(this, $"⌨️ Alias '{alias}' créé");
    }
    
    /// <summary>
    /// Supprime l'alias associé à un chemin cible.
    /// </summary>
    public void DeleteAlias(string targetPath)
    {
        var entry = _aliasService.GetAliasByTargetPath(targetPath);
        if (entry != null)
        {
            _aliasService.RemoveAlias(entry.Alias);
            ShowNotification?.Invoke(this, $"⌨️ Alias '{entry.Alias}' supprimé");
        }
    }
    
    /// <summary>
    /// Vérifie si un chemin cible possède un alias.
    /// </summary>
    public bool HasAlias(string targetPath) => _aliasService.GetAliasByTargetPath(targetPath) != null;
    
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
        
        // Cas spécial pour CreateAlias
        if (action.ActionType == FileActionType.CreateAlias)
        {
            RequestCreateAlias?.Invoke(this, (result.Name, result.Path));
            return;
        }
        
        // Cas spécial pour DeleteAlias
        if (action.ActionType == FileActionType.DeleteAlias)
        {
            DeleteAlias(result.Path);
            UpdateAvailableActions(result);
            ShowActionsPanel = false;
            return;
        }
        
        // Cas spécial pour Pin
        if (action.ActionType == FileActionType.Pin)
        {
            _settings.Search.PinItem(result.Name, result.Path, result.Type, result.DisplayIcon);
            _settingsProvider.Save();
            ShowNotification?.Invoke(this, "⭐ Épinglé");
            UpdateAvailableActions(result); // Rafraîchir les actions
            ShowActionsPanel = false;
            return;
        }
        
        // Cas spécial pour Unpin
        if (action.ActionType == FileActionType.Unpin)
        {
            _settings.Search.UnpinItem(result.Path);
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
        _settings.Appearance.ShowPreviewPanel = ShowPreviewPanel;
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
        if (_settings.Search.EnableSearchHistory && !string.IsNullOrWhiteSpace(item.Path))
        {
            var historyItem = new HistoryItem
            {
                Name = item.Name,
                Path = item.Path,
                Type = item.Type,
                Icon = item.DisplayIcon,
                LastUsed = DateTime.Now
            };
            _settings.Search.AddToSearchHistory(historyItem);
            _settingsProvider.Save();
        }
        
        _indexingService.RecordUsage(item);
        LaunchService.Launch(item);
        RequestHide?.Invoke(this, EventArgs.Empty);
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
        // Actions applicatives
        if (executionResult.AppAction != null)
        {
            HandleAppAction(executionResult.AppAction.Value);
            return;
        }
        
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
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                RequestScreenCapture?.Invoke(this, executionResult.ScreenCaptureMode));
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
        
        var fullQuery = matchedCmd.FullPrefix + (arg != null ? $" {arg}" : "");
        var handler = _commandRouter.FindHandler(fullQuery);
        if (handler != null)
            _ = DispatchCommandAsync(handler, fullQuery, _searchGeneration, _searchCts?.Token ?? CancellationToken.None);
    }
    
    
    private void ShowSearchHistory()
    {
        Results.Clear();
        
        if (_settings.Search.SearchHistory.Count == 0)
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
            foreach (var historyItem in _settings.Search.SearchHistory)
            {
                Results.Add(historyItem.ToSearchResult());
            }
        }
        
        FinalizeResults();
    }
    
    private void ClearHistory()
    {
        _settings.Search.ClearSearchHistory();
        _settingsProvider.Save();
        SearchText = string.Empty;
        RequestHide?.Invoke(this, EventArgs.Empty);
    }
    
    private void ShowHelpCommands()
    {
        Results.Clear();
        
        // Toutes les commandes système (inclut les commandes applicatives)
        foreach (var cmd in _settings.SystemCommands.Where(c => c.IsEnabled))
        {
            var displayName = cmd.RequiresArgument 
                ? $"{cmd.FullPrefix} {cmd.ArgumentHint}" 
                : cmd.FullPrefix;
            
            Results.Add(new SearchResult 
            { 
                Name = displayName, 
                Description = cmd.Description, 
                Type = cmd.IsAppCommand ? ResultType.AppControl : ResultType.SystemControl, 
                DisplayIcon = cmd.Icon,
                Path = cmd.FullPrefix
            });
        }
        
        // Recherche web
        foreach (var engine in _settings.Search.SearchEngines.Take(4))
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
        ShowCategoryBadges = _settings.Appearance.ShowCategoryBadges;
        ShowSettingsButton = _settings.Appearance.ShowSettingsButton;
        ShowShortcutHints = _settings.Appearance.ShowShortcutHints;
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


