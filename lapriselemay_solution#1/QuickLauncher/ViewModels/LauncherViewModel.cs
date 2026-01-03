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
    
    private static readonly Dictionary<string, SystemCommand> SystemCommands = new(StringComparer.OrdinalIgnoreCase)
    {
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
    };
    
    [ObservableProperty]
    private string _searchText = string.Empty;
    
    [ObservableProperty]
    private int _selectedIndex;
    
    [ObservableProperty]
    private bool _hasResults;
    
    public ObservableCollection<SearchResult> Results { get; } = [];
    
    public event EventHandler? RequestHide;
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestQuit;
    public event EventHandler? RequestReindex;
    
    public LauncherViewModel(IndexingService indexingService)
    {
        _indexingService = indexingService;
        _settings = AppSettings.Load();
    }

    partial void OnSearchTextChanged(string value) => UpdateResults();
    
    private void UpdateResults()
    {
        Results.Clear();
        
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ShowRecentHistory();
            return;
        }
        
        var query = SearchText.Trim();
        
        // Commandes système correspondantes
        AddMatchingSystemCommands(query);
        
        // Si exactement une commande système, pas besoin d'autres résultats
        if (SystemCommands.ContainsKey(query))
        {
            FinalizeResults();
            return;
        }
        
        // Résultats de recherche normaux
        foreach (var result in _indexingService.Search(SearchText))
            Results.Add(result);
        
        FinalizeResults();
    }
    
    private void ShowRecentHistory()
    {
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
        FinalizeResults();
    }
    
    private void AddMatchingSystemCommands(string query)
    {
        var commands = SystemCommands
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
        
        foreach (var cmd in commands)
            Results.Add(cmd);
    }
    
    private void FinalizeResults()
    {
        HasResults = Results.Count > 0;
        if (HasResults) SelectedIndex = 0;
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
                ExecuteSystemCommand(item.Path);
                break;
                
            case ResultType.SearchHistory:
                SearchText = item.Name;
                break;
                
            default:
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
    
    private void ExecuteSystemCommand(string? command)
    {
        if (string.IsNullOrEmpty(command) || !SystemCommands.TryGetValue(command, out var sysCmd))
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
    
    private void ShowHelpCommands()
    {
        Results.Clear();
        
        Results.Add(new SearchResult { Name = ":settings", Description = "Ouvrir les paramètres", Type = ResultType.SystemCommand, DisplayIcon = "⚙️", Path = ":settings" });
        Results.Add(new SearchResult { Name = ":reload", Description = "Réindexer les fichiers", Type = ResultType.SystemCommand, DisplayIcon = "🔄", Path = ":reload" });
        Results.Add(new SearchResult { Name = ":history", Description = "Voir l'historique", Type = ResultType.SystemCommand, DisplayIcon = "📜", Path = ":history" });
        Results.Add(new SearchResult { Name = ":clear", Description = "Effacer l'historique", Type = ResultType.SystemCommand, DisplayIcon = "🗑️", Path = ":clear" });
        Results.Add(new SearchResult { Name = ":quit", Description = "Fermer QuickLauncher", Type = ResultType.SystemCommand, DisplayIcon = "🚪", Path = ":quit" });
        Results.Add(new SearchResult { Name = "g [recherche]", Description = "Recherche Google", Type = ResultType.SystemCommand, DisplayIcon = "🌐" });
        Results.Add(new SearchResult { Name = "yt [recherche]", Description = "Recherche YouTube", Type = ResultType.SystemCommand, DisplayIcon = "📺" });
        
        FinalizeResults();
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        
        var newIndex = SelectedIndex + delta;
        if (newIndex < 0) newIndex = Results.Count - 1;
        if (newIndex >= Results.Count) newIndex = 0;
        
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
