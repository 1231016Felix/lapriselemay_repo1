using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickLauncher.Models;
using QuickLauncher.Services;
using System.Collections.ObjectModel;

namespace QuickLauncher.ViewModels;

public partial class LauncherViewModel : ObservableObject
{
    private readonly IndexingService _indexingService;
    private readonly AppSettings _settings;
    
    // Commandes système intégrées
    private static readonly Dictionary<string, SystemCommand> SystemCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        { ":settings", new("⚙️", "Paramètres", "Ouvrir les paramètres de QuickLauncher", SystemAction.OpenSettings) },
        { "settings", new("⚙️", "Paramètres", "Ouvrir les paramètres de QuickLauncher", SystemAction.OpenSettings) },
        { ":quit", new("🚪", "Quitter", "Fermer QuickLauncher", SystemAction.Quit) },
        { ":exit", new("🚪", "Quitter", "Fermer QuickLauncher", SystemAction.Quit) },
        { ":reload", new("🔄", "Réindexer", "Reconstruire l'index de recherche", SystemAction.Reindex) },
        { ":reindex", new("🔄", "Réindexer", "Reconstruire l'index de recherche", SystemAction.Reindex) },
        { ":history", new("📜", "Historique", "Afficher l'historique de recherche", SystemAction.ShowHistory) },
        { ":clear", new("🗑️", "Effacer historique", "Effacer l'historique de recherche", SystemAction.ClearHistory) },
        { ":help", new("❓", "Aide", "Afficher les commandes disponibles", SystemAction.ShowHelp) },
        { "?", new("❓", "Aide", "Afficher les commandes disponibles", SystemAction.ShowHelp) },
    };
    
    [ObservableProperty]
    private string _searchText = string.Empty;
    
    [ObservableProperty]
    private int _selectedIndex;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private bool _hasResults;
    
    public ObservableCollection<SearchResult> Results { get; } = new();
    
    public event EventHandler? RequestHide;
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;
    
    public LauncherViewModel(IndexingService indexingService)
    {
        _indexingService = indexingService;
        _settings = AppSettings.Load();
    }

    partial void OnSearchTextChanged(string value)
    {
        UpdateResults();
    }
    
    private void UpdateResults()
    {
        Results.Clear();
        
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            // Afficher l'historique si activé et vide
            if (_settings.EnableSearchHistory && _settings.SearchHistory.Count > 0)
            {
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
            }
            HasResults = Results.Count > 0;
            if (HasResults) SelectedIndex = 0;
            return;
        }
        
        // Vérifier si c'est une commande système
        var trimmedSearch = SearchText.Trim();
        
        // Chercher les commandes système qui commencent par le texte saisi
        var matchingCommands = SystemCommands
            .Where(kv => kv.Key.StartsWith(trimmedSearch, StringComparison.OrdinalIgnoreCase) ||
                         (trimmedSearch.StartsWith(":") && kv.Key.StartsWith(trimmedSearch, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => new SearchResult
            {
                Name = kv.Value.Name,
                Description = kv.Value.Description,
                Type = ResultType.SystemCommand,
                DisplayIcon = kv.Value.Icon,
                Path = kv.Key // Stocker la commande dans Path
            })
            .DistinctBy(r => r.Name)
            .Take(3);
        
        foreach (var cmd in matchingCommands)
        {
            Results.Add(cmd);
        }
        
        // Si c'est exactement une commande système, ne pas chercher d'autres résultats
        if (SystemCommands.ContainsKey(trimmedSearch))
        {
            HasResults = Results.Count > 0;
            if (HasResults) SelectedIndex = 0;
            return;
        }
        
        // Résultats de recherche normaux
        var results = _indexingService.Search(SearchText);
        foreach (var result in results)
        {
            Results.Add(result);
        }
        
        HasResults = Results.Count > 0;
        
        if (HasResults)
            SelectedIndex = 0;
    }
    
    [RelayCommand]
    private void Execute()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            var item = Results[SelectedIndex];
            
            // Gérer les différents types de résultats
            switch (item.Type)
            {
                case ResultType.SystemCommand:
                    ExecuteSystemCommand(item.Path);
                    break;
                    
                case ResultType.SearchHistory:
                    // Réutiliser une recherche de l'historique
                    SearchText = item.Name;
                    break;
                    
                default:
                    // Ajouter à l'historique
                    if (!string.IsNullOrWhiteSpace(SearchText) && _settings.EnableSearchHistory)
                    {
                        _settings.AddToSearchHistory(SearchText);
                        _settings.Save();
                    }
                    
                    _indexingService.RecordUsage(item);
                    LaunchService.Launch(item);
                    RequestHide?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
    }
    
    private void ExecuteSystemCommand(string? command)
    {
        if (string.IsNullOrEmpty(command)) return;
        
        if (SystemCommands.TryGetValue(command, out var sysCmd))
        {
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
                    _settings.ClearSearchHistory();
                    _settings.Save();
                    SearchText = string.Empty;
                    RequestHide?.Invoke(this, EventArgs.Empty);
                    break;
                    
                case SystemAction.ShowHelp:
                    ShowHelpCommands();
                    break;
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
                Description = "Votre historique de recherche est vide",
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
        
        HasResults = Results.Count > 0;
        SelectedIndex = 0;
    }
    
    private void ShowHelpCommands()
    {
        Results.Clear();
        
        var helpItems = new[]
        {
            new SearchResult { Name = ":settings", Description = "Ouvrir les paramètres", Type = ResultType.SystemCommand, DisplayIcon = "⚙️", Path = ":settings" },
            new SearchResult { Name = ":reload", Description = "Réindexer les fichiers", Type = ResultType.SystemCommand, DisplayIcon = "🔄", Path = ":reload" },
            new SearchResult { Name = ":history", Description = "Voir l'historique de recherche", Type = ResultType.SystemCommand, DisplayIcon = "📜", Path = ":history" },
            new SearchResult { Name = ":clear", Description = "Effacer l'historique", Type = ResultType.SystemCommand, DisplayIcon = "🗑️", Path = ":clear" },
            new SearchResult { Name = ":quit", Description = "Fermer QuickLauncher", Type = ResultType.SystemCommand, DisplayIcon = "🚪", Path = ":quit" },
            new SearchResult { Name = "g [recherche]", Description = "Recherche Google", Type = ResultType.SystemCommand, DisplayIcon = "🌐" },
            new SearchResult { Name = "yt [recherche]", Description = "Recherche YouTube", Type = ResultType.SystemCommand, DisplayIcon = "📺" },
        };
        
        foreach (var item in helpItems)
        {
            Results.Add(item);
        }
        
        HasResults = true;
        SelectedIndex = 0;
    }
    
    [RelayCommand]
    private void ExecuteSelected(SearchResult? item)
    {
        if (item != null)
        {
            if (item.Type == ResultType.SystemCommand)
            {
                ExecuteSystemCommand(item.Path);
                return;
            }
            
            if (item.Type == ResultType.SearchHistory)
            {
                SearchText = item.Name;
                return;
            }
            
            _indexingService.RecordUsage(item);
            LaunchService.Launch(item);
            RequestHide?.Invoke(this, EventArgs.Empty);
        }
    }
    
    [RelayCommand]
    private void OpenFolder(SearchResult? item)
    {
        if (item != null)
        {
            LaunchService.OpenContainingFolder(item);
            RequestHide?.Invoke(this, EventArgs.Empty);
        }
    }
    
    [RelayCommand]
    private void RunAsAdmin(SearchResult? item)
    {
        if (item != null && item.Type == ResultType.Application)
        {
            _indexingService.RecordUsage(item);
            LaunchService.RunAsAdmin(item);
            RequestHide?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        
        var newIndex = SelectedIndex + delta;
        if (newIndex < 0) newIndex = Results.Count - 1;
        if (newIndex >= Results.Count) newIndex = 0;
        
        SelectedIndex = newIndex;
    }
    
    [RelayCommand]
    private void Cancel()
    {
        SearchText = string.Empty;
        Results.Clear();
        RequestHide?.Invoke(this, EventArgs.Empty);
    }
    
    public void Reset()
    {
        SearchText = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
        HasResults = false;
    }
    
    public void TriggerOpenSettings()
    {
        RequestOpenSettings?.Invoke(this, EventArgs.Empty);
    }
}

// Classes utilitaires pour les commandes système
public enum SystemAction
{
    OpenSettings,
    Quit,
    Reindex,
    ShowHistory,
    ClearHistory,
    ShowHelp
}

public record SystemCommand(string Icon, string Name, string Description, SystemAction Action);
