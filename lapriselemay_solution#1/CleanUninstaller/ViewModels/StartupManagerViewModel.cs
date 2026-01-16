using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using CleanUninstaller.Services.Interfaces;
using Shared.Logging;

namespace CleanUninstaller.ViewModels;

/// <summary>
/// ViewModel pour le gestionnaire de programmes au démarrage
/// Utilise l'injection de dépendances pour les services
/// </summary>
public class StartupManagerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly StartupManagerService _startupService;
    private readonly Shared.Logging.ILoggerService _logger;
    private ObservableCollection<StartupProgram> _allPrograms = [];
    private ObservableCollection<StartupProgram> _filteredPrograms = [];
    private StartupProgram? _selectedProgram;
    private string _searchText = string.Empty;
    private bool _showDisabled = true;
    private bool _showRegistry = true;
    private bool _showStartupFolder = true;
    private bool _showScheduledTasks = true;
    private bool _isBusy;
    private string _statusMessage = "Prêt";
    private int _progress;
    
    // Tri
    private SortColumn _currentSortColumn = SortColumn.Impact;
    private bool _sortAscending = false;

    /// <summary>
    /// Constructeur avec injection de dépendances
    /// </summary>
    public StartupManagerViewModel(Shared.Logging.ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _startupService = new StartupManagerService();

        InitializeCommands();
        _logger.Debug("StartupManagerViewModel initialisé");
    }

    /// <summary>
    /// Constructeur par défaut pour compatibilité XAML
    /// </summary>
    public StartupManagerViewModel() : this(ServiceContainer.GetService<Shared.Logging.ILoggerService>())
    { }

    private void InitializeCommands()
    {
        // Initialiser les commandes
        RefreshCommand = new RelayCommand(async () => await ScanAsync());
        EnableSelectedCommand = new RelayCommand(async () => await SetSelectedEnabledAsync(true), () => SelectedProgram != null);
        DisableSelectedCommand = new RelayCommand(async () => await SetSelectedEnabledAsync(false), () => SelectedProgram != null);
        RemoveSelectedCommand = new RelayCommand(async () => await RemoveSelectedAsync(), () => SelectedProgram != null);
        EnableAllSelectedCommand = new RelayCommand(async () => await SetMultipleEnabledAsync(true));
        DisableAllSelectedCommand = new RelayCommand(async () => await SetMultipleEnabledAsync(false));
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
        DeselectAllCommand = new RelayCommand(() => SetAllSelected(false));
        OpenFileLocationCommand = new RelayCommand(() => OpenFileLocation(), () => SelectedProgram != null);
        
        // Commandes de tri
        SortByNameCommand = new RelayCommand(() => CurrentSortColumn = SortColumn.Name);
        SortByStatusCommand = new RelayCommand(() => CurrentSortColumn = SortColumn.Status);
        SortByTypeCommand = new RelayCommand(() => CurrentSortColumn = SortColumn.Type);
        SortByImpactCommand = new RelayCommand(() => CurrentSortColumn = SortColumn.Impact);
    }

    #region Properties

    public ObservableCollection<StartupProgram> Programs
    {
        get => _filteredPrograms;
        private set
        {
            _filteredPrograms = value;
            OnPropertyChanged(nameof(Programs));
        }
    }

    public StartupProgram? SelectedProgram
    {
        get => _selectedProgram;
        set
        {
            _selectedProgram = value;
            OnPropertyChanged(nameof(SelectedProgram));
            OnPropertyChanged(nameof(HasSelection));
            (EnableSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DisableSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenFileLocationCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged(nameof(SearchText));
            ApplyFilters();
        }
    }

    public bool ShowDisabled
    {
        get => _showDisabled;
        set { _showDisabled = value; OnPropertyChanged(nameof(ShowDisabled)); ApplyFilters(); }
    }

    public bool ShowRegistry
    {
        get => _showRegistry;
        set { _showRegistry = value; OnPropertyChanged(nameof(ShowRegistry)); ApplyFilters(); }
    }

    public bool ShowStartupFolder
    {
        get => _showStartupFolder;
        set { _showStartupFolder = value; OnPropertyChanged(nameof(ShowStartupFolder)); ApplyFilters(); }
    }

    public bool ShowScheduledTasks
    {
        get => _showScheduledTasks;
        set { _showScheduledTasks = value; OnPropertyChanged(nameof(ShowScheduledTasks)); ApplyFilters(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsNotBusy)); }
    }

    public bool IsNotBusy => !_isBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
    }

    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(nameof(Progress)); }
    }

    public bool HasSelection => SelectedProgram != null;

    public bool IsAdministrator => StartupManagerService.IsAdministrator();

    public SortColumn CurrentSortColumn
    {
        get => _currentSortColumn;
        set
        {
            if (_currentSortColumn != value)
            {
                _currentSortColumn = value;
                _sortAscending = true; // Reset to ascending when changing column
                OnPropertyChanged(nameof(CurrentSortColumn));
                UpdateSortIndicators();
                ApplyFilters();
            }
            else
            {
                // Toggle direction if same column
                _sortAscending = !_sortAscending;
                OnPropertyChanged(nameof(CurrentSortColumn));
                UpdateSortIndicators();
                ApplyFilters();
            }
        }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set { _sortAscending = value; OnPropertyChanged(nameof(SortAscending)); }
    }

    // Indicateurs de tri pour l'UI
    public string NameSortIndicator => GetSortIndicator(SortColumn.Name);
    public string StatusSortIndicator => GetSortIndicator(SortColumn.Status);
    public string TypeSortIndicator => GetSortIndicator(SortColumn.Type);
    public string ImpactSortIndicator => GetSortIndicator(SortColumn.Impact);

    private string GetSortIndicator(SortColumn column)
    {
        if (_currentSortColumn != column) return "";
        return _sortAscending ? " ↑" : " ↓";
    }

    private void UpdateSortIndicators()
    {
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(StatusSortIndicator));
        OnPropertyChanged(nameof(TypeSortIndicator));
        OnPropertyChanged(nameof(ImpactSortIndicator));
    }

    // Statistiques
    public int TotalCount => _allPrograms.Count;
    public int EnabledCount => _allPrograms.Count(p => p.IsEnabled);
    public int DisabledCount => _allPrograms.Count(p => !p.IsEnabled);
    public int HighImpactCount => _allPrograms.Count(p => p.IsEnabled && (p.Impact == StartupImpact.High || p.Impact == StartupImpact.Critical));

    public string TotalBootImpact
    {
        get
        {
            var totalMs = _startupService.CalculateTotalBootImpact(_allPrograms);
            if (totalMs < 1000)
                return $"{totalMs} ms";
            return $"{totalMs / 1000.0:F1} s";
        }
    }

    public string PotentialSavings
    {
        get
        {
            var highImpactMs = _allPrograms
                .Where(p => p.IsEnabled && (p.Impact == StartupImpact.High || p.Impact == StartupImpact.Critical))
                .Sum(p => p.EstimatedImpactMs);
            if (highImpactMs < 1000)
                return $"{highImpactMs} ms";
            return $"{highImpactMs / 1000.0:F1} s";
        }
    }

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;
    public ICommand EnableSelectedCommand { get; private set; } = null!;
    public ICommand DisableSelectedCommand { get; private set; } = null!;
    public ICommand RemoveSelectedCommand { get; private set; } = null!;
    public ICommand EnableAllSelectedCommand { get; private set; } = null!;
    public ICommand DisableAllSelectedCommand { get; private set; } = null!;
    public ICommand SelectAllCommand { get; private set; } = null!;
    public ICommand DeselectAllCommand { get; private set; } = null!;
    public ICommand OpenFileLocationCommand { get; private set; } = null!;
    
    // Commandes de tri
    public ICommand SortByNameCommand { get; private set; } = null!;
    public ICommand SortByStatusCommand { get; private set; } = null!;
    public ICommand SortByTypeCommand { get; private set; } = null!;
    public ICommand SortByImpactCommand { get; private set; } = null!;

    #endregion

    #region Methods

    public async Task ScanAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Scan des programmes au démarrage...";
        Progress = 0;
        _logger.Info("Démarrage du scan des programmes au démarrage");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var programs = await _startupService.ScanStartupProgramsAsync(progress);

            _allPrograms = new ObservableCollection<StartupProgram>(programs);
            ApplyFilters();

            StatusMessage = $"{programs.Count} programmes trouvés";
            _logger.Info($"Scan terminé: {programs.Count} programmes au démarrage trouvés");
            UpdateStatistics();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error("Erreur lors du scan des programmes au démarrage", ex);
        }
        finally
        {
            IsBusy = false;
            Progress = 100;
        }
    }

    private void ApplyFilters()
    {
        var filtered = _allPrograms.AsEnumerable();

        // Filtre par texte
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(p =>
                p.Name.ToLowerInvariant().Contains(search) ||
                p.Publisher.ToLowerInvariant().Contains(search) ||
                p.Command.ToLowerInvariant().Contains(search));
        }

        // Filtre par état
        if (!ShowDisabled)
        {
            filtered = filtered.Where(p => p.IsEnabled);
        }

        // Filtre par type
        filtered = filtered.Where(p =>
            (ShowRegistry && p.Type == Models.StartupType.Registry) ||
            (ShowStartupFolder && p.Type == Models.StartupType.StartupFolder) ||
            (ShowScheduledTasks && p.Type == Models.StartupType.ScheduledTask));

        // Appliquer le tri
        filtered = ApplySorting(filtered);

        Programs = new ObservableCollection<StartupProgram>(filtered);
        OnPropertyChanged(nameof(Programs));
    }

    private IEnumerable<StartupProgram> ApplySorting(IEnumerable<StartupProgram> programs)
    {
        return _currentSortColumn switch
        {
            SortColumn.Name => _sortAscending 
                ? programs.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                : programs.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            
            SortColumn.Status => _sortAscending
                ? programs.OrderBy(p => p.IsEnabled).ThenBy(p => p.Name)
                : programs.OrderByDescending(p => p.IsEnabled).ThenBy(p => p.Name),
            
            SortColumn.Type => _sortAscending
                ? programs.OrderBy(p => p.Type).ThenBy(p => p.Name)
                : programs.OrderByDescending(p => p.Type).ThenBy(p => p.Name),
            
            SortColumn.Impact => _sortAscending
                ? programs.OrderBy(p => p.Impact).ThenBy(p => p.Name)
                : programs.OrderByDescending(p => p.Impact).ThenBy(p => p.Name),
            
            _ => programs.OrderByDescending(p => p.Impact).ThenBy(p => p.Name)
        };
    }

    private async Task SetSelectedEnabledAsync(bool enabled)
    {
        if (SelectedProgram == null) return;

        IsBusy = true;
        StatusMessage = enabled ? "Activation..." : "Désactivation...";
        _logger.Info($"{(enabled ? "Activation" : "Désactivation")} de: {SelectedProgram.Name}");

        try
        {
            var success = await _startupService.SetStartupEnabledAsync(SelectedProgram, enabled);
            if (success)
            {
                StatusMessage = enabled ? "Programme activé" : "Programme désactivé";
                _logger.Info($"Programme {(enabled ? "activé" : "désactivé")}: {SelectedProgram.Name}");
                UpdateStatistics();
            }
            else
            {
                StatusMessage = "Erreur: Droits administrateur requis ?";
                _logger.Warning($"Échec de la modification de {SelectedProgram.Name} - Droits insuffisants ?");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error($"Erreur lors de la modification de {SelectedProgram.Name}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SetMultipleEnabledAsync(bool enabled)
    {
        var selected = _allPrograms.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Aucun programme sélectionné";
            return;
        }

        IsBusy = true;
        var successCount = 0;
        _logger.Info($"{(enabled ? "Activation" : "Désactivation")} de {selected.Count} programmes");

        foreach (var program in selected)
        {
            StatusMessage = $"{(enabled ? "Activation" : "Désactivation")} de {program.Name}...";
            try
            {
                if (await _startupService.SetStartupEnabledAsync(program, enabled))
                {
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Échec pour {program.Name}: {ex.Message}");
            }
        }

        StatusMessage = $"{successCount}/{selected.Count} programmes {(enabled ? "activés" : "désactivés")}";
        _logger.Info($"Opération en lot terminée: {successCount}/{selected.Count} réussis");
        UpdateStatistics();
        IsBusy = false;
    }

    private async Task RemoveSelectedAsync()
    {
        if (SelectedProgram == null) return;

        IsBusy = true;
        StatusMessage = "Suppression...";
        _logger.Info($"Suppression du programme au démarrage: {SelectedProgram.Name}");

        try
        {
            var success = await _startupService.RemoveFromStartupAsync(SelectedProgram);
            if (success)
            {
                var programName = SelectedProgram.Name;
                _allPrograms.Remove(SelectedProgram);
                ApplyFilters();
                StatusMessage = "Programme supprimé du démarrage";
                _logger.Info($"Programme supprimé du démarrage: {programName}");
                UpdateStatistics();
            }
            else
            {
                StatusMessage = "Erreur lors de la suppression";
                _logger.Warning($"Échec de la suppression de {SelectedProgram.Name}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error($"Erreur lors de la suppression de {SelectedProgram.Name}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var program in Programs)
        {
            program.IsSelected = selected;
        }
    }

    private void OpenFileLocation()
    {
        if (SelectedProgram == null || string.IsNullOrEmpty(SelectedProgram.Command)) return;

        try
        {
            var directory = Path.GetDirectoryName(SelectedProgram.Command);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{SelectedProgram.Command}\"",
                    UseShellExecute = true
                });
                _logger.Debug($"Ouverture de l'emplacement: {SelectedProgram.Command}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Impossible d'ouvrir l'emplacement: {ex.Message}");
        }
    }

    private void UpdateStatistics()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(HighImpactCount));
        OnPropertyChanged(nameof(TotalBootImpact));
        OnPropertyChanged(nameof(PotentialSavings));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

/// <summary>
/// Implémentation simple de ICommand
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Colonnes de tri disponibles
/// </summary>
public enum SortColumn
{
    Name,
    Status,
    Type,
    Impact
}
