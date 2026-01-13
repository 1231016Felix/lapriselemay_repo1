using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using CleanUninstaller.Services.Interfaces;
using System.Collections.ObjectModel;

namespace CleanUninstaller.ViewModels;

/// <summary>
/// ViewModel pour la page de monitoring d'installation
/// Utilise l'injection de dépendances pour tous les services
/// </summary>
public partial class InstallationMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IInstallationMonitorService _monitorService;
    private readonly ILoggerService _logger;
    private bool _isDisposed;

    /// <summary>
    /// Constructeur avec injection de dépendances (recommandé)
    /// </summary>
    public InstallationMonitorViewModel(
        IInstallationMonitorService monitorService,
        ILoggerService logger)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Abonner aux événements si le service les supporte
        if (_monitorService is InstallationMonitorService concreteService)
        {
            concreteService.RealTimeChangeDetected += OnRealTimeChangeDetected;
            concreteService.StatusChanged += OnStatusChanged;
        }
        
        _logger.Debug("InstallationMonitorViewModel initialisé");
    }

    /// <summary>
    /// Constructeur par défaut utilisant le ServiceContainer (pour compatibilité XAML)
    /// </summary>
    public InstallationMonitorViewModel() : this(
        ServiceContainer.GetService<IInstallationMonitorService>(),
        ServiceContainer.GetService<ILoggerService>())
    { }

    #region Properties

    /// <summary>
    /// Installation en cours de monitoring
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentMonitoring))]
    [NotifyPropertyChangedFor(nameof(IsMonitoring))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    private MonitoredInstallation? _currentMonitoring;

    /// <summary>
    /// Liste des changements détectés en temps réel
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SystemChange> _realTimeChanges = [];

    /// <summary>
    /// Liste des installations surveillées sauvegardées
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MonitoredInstallation> _savedInstallations = [];

    /// <summary>
    /// Installation sélectionnée dans la liste
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedInstallation))]
    [NotifyPropertyChangedFor(nameof(CanPerfectUninstall))]
    private MonitoredInstallation? _selectedInstallation;

    /// <summary>
    /// Nom de l'installation (saisi par l'utilisateur)
    /// </summary>
    [ObservableProperty]
    private string _installationName = "";

    /// <summary>
    /// Chemin de l'installeur (optionnel)
    /// </summary>
    [ObservableProperty]
    private string _installerPath = "";

    /// <summary>
    /// Indique si une opération est en cours
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    private bool _isBusy;

    /// <summary>
    /// Progression actuelle
    /// </summary>
    [ObservableProperty]
    private int _progress;

    /// <summary>
    /// Message de statut
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Prêt à surveiller une installation";

    /// <summary>
    /// Filtre de catégorie pour les changements
    /// </summary>
    [ObservableProperty]
    private SystemChangeCategory? _categoryFilter;

    /// <summary>
    /// Filtre de type pour les changements
    /// </summary>
    [ObservableProperty]
    private ChangeType? _changeTypeFilter;

    // Propriétés calculées

    public bool HasCurrentMonitoring => CurrentMonitoring != null;
    public bool IsMonitoring => CurrentMonitoring?.Status == MonitoringStatus.Monitoring;
    public bool IsPaused => CurrentMonitoring?.Status == MonitoringStatus.Paused;
    public bool HasSelectedInstallation => SelectedInstallation != null;

    public bool CanStart => !IsBusy && CurrentMonitoring == null;
    public bool CanStop => !IsBusy && (IsMonitoring || IsPaused);
    public bool CanPause => !IsBusy && IsMonitoring;
    public bool CanResume => !IsBusy && IsPaused;
    public bool CanPerfectUninstall => SelectedInstallation != null && 
                                        !SelectedInstallation.IsUninstalled &&
                                        SelectedInstallation.Changes.Count > 0;

    /// <summary>
    /// Nombre de changements en temps réel
    /// </summary>
    public int RealTimeChangeCount => RealTimeChanges.Count;

    /// <summary>
    /// Statistiques du monitoring actuel
    /// </summary>
    public string CurrentStats
    {
        get
        {
            if (CurrentMonitoring == null) return "";
            
            var stats = CurrentMonitoring.Statistics;
            return $"{stats.FilesCreated} fichiers, {stats.FoldersCreated} dossiers, " +
                   $"{stats.RegistryKeysCreated + stats.RegistryValuesCreated} entrées registre";
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Démarre le monitoring
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartMonitoringAsync()
    {
        if (!CanStart) return;

        IsBusy = true;
        RealTimeChanges.Clear();
        _logger.Info($"Démarrage du monitoring pour: {InstallationName}");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var name = string.IsNullOrWhiteSpace(InstallationName) ? null : InstallationName;
            var path = string.IsNullOrWhiteSpace(InstallerPath) ? null : InstallerPath;

            if (_monitorService is InstallationMonitorService concreteService)
            {
                CurrentMonitoring = await concreteService.StartMonitoringAsync(name, path, progress);
            }
            
            StatusMessage = "🔴 Surveillance active - Lancez votre installation maintenant";
            _logger.Info("Monitoring démarré avec succès");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error("Erreur lors du démarrage du monitoring", ex);
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    /// <summary>
    /// Arrête le monitoring et analyse les changements
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopMonitoringAsync()
    {
        if (!CanStop) return;

        IsBusy = true;
        _logger.Info("Arrêt du monitoring demandé");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            if (_monitorService is InstallationMonitorService concreteService)
            {
                var result = await concreteService.StopMonitoringAsync(progress);

                if (result != null)
                {
                    SavedInstallations.Insert(0, result);
                    StatusMessage = $"✅ Analyse terminée: {result.Statistics.TotalChanges} changements détectés";
                    _logger.Info($"Monitoring terminé: {result.Statistics.TotalChanges} changements");
                }
            }

            CurrentMonitoring = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error("Erreur lors de l'arrêt du monitoring", ex);
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    /// <summary>
    /// Met en pause le monitoring
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    private void PauseMonitoring()
    {
        if (_monitorService is InstallationMonitorService concreteService)
        {
            concreteService.PauseMonitoring();
        }
        StatusMessage = "⏸️ Surveillance en pause";
        _logger.Debug("Monitoring mis en pause");
        UpdateCommands();
    }

    /// <summary>
    /// Reprend le monitoring
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResume))]
    private void ResumeMonitoring()
    {
        if (_monitorService is InstallationMonitorService concreteService)
        {
            concreteService.ResumeMonitoring();
        }
        StatusMessage = "🔴 Surveillance reprise";
        _logger.Debug("Monitoring repris");
        UpdateCommands();
    }

    /// <summary>
    /// Annule le monitoring
    /// </summary>
    [RelayCommand]
    private void CancelMonitoring()
    {
        if (_monitorService is InstallationMonitorService concreteService)
        {
            concreteService.CancelMonitoring();
        }
        CurrentMonitoring = null;
        RealTimeChanges.Clear();
        StatusMessage = "Monitoring annulé";
        _logger.Info("Monitoring annulé par l'utilisateur");
        UpdateCommands();
    }

    /// <summary>
    /// Effectue une désinstallation parfaite
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPerfectUninstall))]
    private async Task PerfectUninstallAsync()
    {
        if (SelectedInstallation == null || !CanPerfectUninstall) return;

        IsBusy = true;
        _logger.Info($"Désinstallation parfaite demandée pour: {SelectedInstallation.Name}");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            if (_monitorService is InstallationMonitorService concreteService)
            {
                var result = await concreteService.PerfectUninstallAsync(
                    SelectedInstallation,
                    removeSelectedOnly: true,
                    progress);

                StatusMessage = $"✅ Désinstallation terminée: {result.DeletedCount} éléments supprimés, " +
                               $"{FormatSize(result.SpaceFreed)} libérés";
                
                _logger.Info($"Désinstallation parfaite terminée: {result.DeletedCount} supprimés, {result.SpaceFreed} octets libérés");

                if (result.FailedCount > 0)
                {
                    StatusMessage += $" ({result.FailedCount} échecs)";
                    _logger.Warning($"{result.FailedCount} échecs lors de la désinstallation parfaite");
                }
            }

            // Rafraîchir la liste
            OnPropertyChanged(nameof(CanPerfectUninstall));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error("Erreur lors de la désinstallation parfaite", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Charge les installations sauvegardées
    /// </summary>
    [RelayCommand]
    private async Task LoadSavedInstallationsAsync()
    {
        _logger.Debug("Chargement des installations sauvegardées");
        
        try
        {
            if (_monitorService is InstallationMonitorService concreteService)
            {
                var installations = await concreteService.LoadAllMonitoredInstallationsAsync();
                SavedInstallations = new ObservableCollection<MonitoredInstallation>(installations);
                _logger.Info($"{installations.Count} installations chargées");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur chargement: {ex.Message}";
            _logger.Error("Erreur lors du chargement des installations", ex);
        }
    }

    /// <summary>
    /// Supprime une installation sauvegardée
    /// </summary>
    [RelayCommand]
    private void DeleteSavedInstallation(MonitoredInstallation installation)
    {
        if (_monitorService is InstallationMonitorService concreteService)
        {
            concreteService.DeleteMonitoredInstallation(installation.Id);
        }
        SavedInstallations.Remove(installation);
        _logger.Info($"Installation supprimée: {installation.Name}");

        if (SelectedInstallation == installation)
        {
            SelectedInstallation = null;
        }
    }

    /// <summary>
    /// Sélectionne tous les changements
    /// </summary>
    [RelayCommand]
    private void SelectAllChanges()
    {
        if (SelectedInstallation == null) return;

        foreach (var change in SelectedInstallation.Changes)
        {
            change.IsSelected = true;
        }
    }

    /// <summary>
    /// Désélectionne tous les changements
    /// </summary>
    [RelayCommand]
    private void DeselectAllChanges()
    {
        if (SelectedInstallation == null) return;

        foreach (var change in SelectedInstallation.Changes)
        {
            change.IsSelected = false;
        }
    }

    /// <summary>
    /// Sélectionne uniquement les fichiers et dossiers
    /// </summary>
    [RelayCommand]
    private void SelectFilesOnly()
    {
        if (SelectedInstallation == null) return;

        foreach (var change in SelectedInstallation.Changes)
        {
            change.IsSelected = change.Category is SystemChangeCategory.File 
                                or SystemChangeCategory.Folder;
        }
    }

    /// <summary>
    /// Sélectionne uniquement le registre
    /// </summary>
    [RelayCommand]
    private void SelectRegistryOnly()
    {
        if (SelectedInstallation == null) return;

        foreach (var change in SelectedInstallation.Changes)
        {
            change.IsSelected = change.Category is SystemChangeCategory.RegistryKey 
                                or SystemChangeCategory.RegistryValue;
        }
    }

    /// <summary>
    /// Inverse la sélection
    /// </summary>
    [RelayCommand]
    private void InvertSelection()
    {
        if (SelectedInstallation == null) return;

        foreach (var change in SelectedInstallation.Changes)
        {
            change.IsSelected = !change.IsSelected;
        }
    }

    /// <summary>
    /// Exporte les changements détectés en JSON
    /// </summary>
    [RelayCommand]
    private async Task ExportChangesAsync()
    {
        if (SelectedInstallation == null) return;

        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            
            // Obtenir le handle de la fenêtre
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = $"{SelectedInstallation.Name}_changes";
            picker.FileTypeChoices.Add("JSON", [".json"]);
            picker.FileTypeChoices.Add("CSV", [".csv"]);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var extension = Path.GetExtension(file.Path).ToLowerInvariant();

            if (extension == ".csv")
            {
                await ExportToCsvAsync(file.Path);
            }
            else
            {
                await ExportToJsonAsync(file.Path);
            }

            StatusMessage = $"Exporté vers {file.Name}";
            _logger.Info($"Changements exportés vers: {file.Path}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur export: {ex.Message}";
            _logger.Error("Erreur lors de l'export des changements", ex);
        }
    }

    private async Task ExportToJsonAsync(string path)
    {
        if (SelectedInstallation == null) return;

        var json = System.Text.Json.JsonSerializer.Serialize(SelectedInstallation, 
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private async Task ExportToCsvAsync(string path)
    {
        if (SelectedInstallation == null) return;

        var lines = new List<string>
        {
            "Type,Catégorie,Chemin,Taille,Horodatage,Description"
        };

        foreach (var change in SelectedInstallation.Changes)
        {
            var line = $"\"{change.ChangeTypeName}\",\"{change.CategoryName}\"," +
                      $"\"{change.Path.Replace("\"", "\"\"")}\",{change.Size}," +
                      $"\"{change.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{change.Description.Replace("\"", "\"\"")}\"";
            lines.Add(line);
        }

        await File.WriteAllLinesAsync(path, lines);
    }

    #endregion

    #region Event Handlers

    private void OnRealTimeChangeDetected(object? sender, SystemChange change)
    {
        // Dispatcher pour la mise à jour UI depuis un autre thread
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            RealTimeChanges.Insert(0, change);
            
            // Limiter le nombre d'éléments affichés pour les performances
            while (RealTimeChanges.Count > 500)
            {
                RealTimeChanges.RemoveAt(RealTimeChanges.Count - 1);
            }

            OnPropertyChanged(nameof(RealTimeChangeCount));
            OnPropertyChanged(nameof(CurrentStats));
        });
    }

    private void OnStatusChanged(object? sender, MonitoringStatus status)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            OnPropertyChanged(nameof(IsMonitoring));
            OnPropertyChanged(nameof(IsPaused));
            UpdateCommands();
        });
    }

    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(HasCurrentMonitoring));
        StartMonitoringCommand.NotifyCanExecuteChanged();
        StopMonitoringCommand.NotifyCanExecuteChanged();
        PauseMonitoringCommand.NotifyCanExecuteChanged();
        ResumeMonitoringCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Helpers

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 o";

        string[] suffixes = ["o", "Ko", "Mo", "Go"];
        var i = 0;
        double size = bytes;

        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size:N1} {suffixes[i]}";
    }

    /// <summary>
    /// Initialise le ViewModel
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.Debug("Initialisation du InstallationMonitorViewModel");
        await LoadSavedInstallationsAsync();
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        if (_monitorService is InstallationMonitorService concreteService)
        {
            concreteService.RealTimeChangeDetected -= OnRealTimeChangeDetected;
            concreteService.StatusChanged -= OnStatusChanged;
            concreteService.Dispose();
        }
        
        _isDisposed = true;
        _logger.Debug("InstallationMonitorViewModel disposé");

        GC.SuppressFinalize(this);
    }

    #endregion
}
