using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using CleanUninstaller.Services.Interfaces;
using Shared.Logging;
using System.Collections.ObjectModel;

namespace CleanUninstaller.ViewModels;

/// <summary>
/// ViewModel amélioré pour la page de monitoring d'installation
/// Avec support backup/restore et statistiques temps réel avancées
/// Utilise l'injection de dépendances pour les services
/// </summary>
public partial class EnhancedInstallationMonitorViewModel : ObservableObject, IDisposable
{
    private readonly EnhancedInstallationMonitorService _monitorService;
    private readonly BackupService _backupService;
    private readonly Shared.Logging.ILoggerService _logger;
    private bool _isDisposed;
    private System.Timers.Timer? _durationTimer;

    /// <summary>
    /// Constructeur avec injection de dépendances
    /// </summary>
    public EnhancedInstallationMonitorViewModel(Shared.Logging.ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _monitorService = new EnhancedInstallationMonitorService();
        _backupService = new BackupService();
        
        _monitorService.RealTimeChangeDetected += OnRealTimeChangeDetected;
        _monitorService.StatusChanged += OnStatusChanged;
        _monitorService.InstallerProcessDetected += OnInstallerProcessDetected;
        _monitorService.InstallerProcessExited += OnInstallerProcessExited;
        _monitorService.StatisticsUpdated += OnStatisticsUpdated;
        
        _logger.Debug("EnhancedInstallationMonitorViewModel initialisé");
    }

    /// <summary>
    /// Constructeur par défaut pour compatibilité XAML
    /// </summary>
    public EnhancedInstallationMonitorViewModel() : this(ServiceContainer.GetService<Shared.Logging.ILoggerService>())
    { }

    #region Observable Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentMonitoring))]
    [NotifyPropertyChangedFor(nameof(IsMonitoring))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    private MonitoredInstallation? _currentMonitoring;

    [ObservableProperty]
    private ObservableCollection<SystemChange> _realTimeChanges = [];

    [ObservableProperty]
    private ObservableCollection<MonitoredInstallation> _savedInstallations = [];

    [ObservableProperty]
    private ObservableCollection<ProcessInfo> _activeProcesses = [];

    [ObservableProperty]
    private ObservableCollection<UninstallBackup> _availableBackups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedInstallation))]
    [NotifyPropertyChangedFor(nameof(CanPerfectUninstall))]
    [NotifyPropertyChangedFor(nameof(SelectedChanges))]
    private MonitoredInstallation? _selectedInstallation;

    [ObservableProperty]
    private UninstallBackup? _selectedBackup;

    [ObservableProperty]
    private string _installationName = "";

    [ObservableProperty]
    private string _installerPath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    private bool _isBusy;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private string _statusMessage = "Prêt à surveiller une installation";

    [ObservableProperty]
    private bool _createBackupBeforeUninstall = true;

    [ObservableProperty]
    private string _monitoringDuration = "0:00";

    // Statistiques temps réel
    [ObservableProperty]
    private int _filesDetectedCount;

    [ObservableProperty]
    private int _foldersDetectedCount;

    [ObservableProperty]
    private int _registryDetectedCount;

    [ObservableProperty]
    private int _processesDetectedCount;

    // Filtres
    [ObservableProperty]
    private SystemChangeCategory? _categoryFilter;

    [ObservableProperty]
    private ChangeType? _changeTypeFilter;

    [ObservableProperty]
    private string _searchFilter = "";

    #endregion

    #region Computed Properties

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

    public int RealTimeChangeCount => RealTimeChanges.Count;
    public int TotalStatisticsCount => FilesDetectedCount + FoldersDetectedCount + 
                                       RegistryDetectedCount + ProcessesDetectedCount;

    public IEnumerable<SystemChange> SelectedChanges => 
        SelectedInstallation?.Changes.Where(c => c.IsSelected) ?? [];

    public int SelectedChangesCount => SelectedChanges.Count();

    public string FormattedSelectedSize
    {
        get
        {
            var size = SelectedChanges.Sum(c => c.Size);
            return FormatSize(size);
        }
    }

    #endregion

    #region Commands

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartMonitoringAsync()
    {
        if (!CanStart) return;

        IsBusy = true;
        RealTimeChanges.Clear();
        ActiveProcesses.Clear();
        ResetStatistics();

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var name = string.IsNullOrWhiteSpace(InstallationName) ? null : InstallationName;
            var path = string.IsNullOrWhiteSpace(InstallerPath) ? null : InstallerPath;

            CurrentMonitoring = await _monitorService.StartMonitoringAsync(name, path, progress);
            
            StartDurationTimer();
            StatusMessage = "🔴 Surveillance active - Lancez votre installation maintenant";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Erreur: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopMonitoringAsync()
    {
        if (!CanStop) return;

        IsBusy = true;
        StopDurationTimer();

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var result = await _monitorService.StopMonitoringAsync(progress);

            if (result != null)
            {
                SavedInstallations.Insert(0, result);
                StatusMessage = $"✅ Analyse terminée: {result.Statistics.TotalChanges} changements détectés";
            }

            CurrentMonitoring = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Erreur: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            UpdateCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void PauseMonitoring()
    {
        _monitorService.PauseMonitoring();
        StopDurationTimer();
        StatusMessage = "⏸️ Surveillance en pause";
        UpdateCommands();
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void ResumeMonitoring()
    {
        _monitorService.ResumeMonitoring();
        StartDurationTimer();
        StatusMessage = "🔴 Surveillance reprise";
        UpdateCommands();
    }

    [RelayCommand]
    private void CancelMonitoring()
    {
        _monitorService.CancelMonitoring();
        StopDurationTimer();
        CurrentMonitoring = null;
        RealTimeChanges.Clear();
        ActiveProcesses.Clear();
        StatusMessage = "❌ Monitoring annulé";
        UpdateCommands();
    }

    [RelayCommand(CanExecute = nameof(CanPerfectUninstall))]
    private async Task PerfectUninstallAsync()
    {
        if (SelectedInstallation == null || !CanPerfectUninstall) return;

        IsBusy = true;

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var result = await _monitorService.PerfectUninstallAsync(
                SelectedInstallation,
                createBackup: CreateBackupBeforeUninstall,
                removeSelectedOnly: true,
                progress);

            var message = $"✅ Désinstallation terminée: {result.DeletedCount} éléments supprimés, " +
                         $"{FormatSize(result.SpaceFreed)} libérés";

            if (result.FailedCount > 0)
            {
                message += $" ({result.FailedCount} échecs)";
            }

            if (!string.IsNullOrEmpty(result.BackupId))
            {
                message += " • Sauvegarde créée";
                await LoadBackupsAsync();
            }

            StatusMessage = message;
            OnPropertyChanged(nameof(CanPerfectUninstall));
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Erreur: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup == null) return;

        IsBusy = true;

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var result = await _monitorService.RestoreInstallationAsync(
                SelectedBackup.Id,
                progress);

            StatusMessage = $"✅ Restauration terminée: {result.RestoredCount} éléments restaurés";

            if (result.FailedCount > 0)
            {
                StatusMessage += $" ({result.FailedCount} échecs)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Erreur: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadSavedInstallationsAsync()
    {
        try
        {
            var installations = await _monitorService.LoadAllMonitoredInstallationsAsync();
            SavedInstallations = new ObservableCollection<MonitoredInstallation>(installations);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Erreur chargement: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadBackupsAsync()
    {
        try
        {
            var backups = await _backupService.GetBackupsAsync();
            AvailableBackups = new ObservableCollection<UninstallBackup>(backups);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Erreur chargement backups: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DeleteSavedInstallation(MonitoredInstallation installation)
    {
        _monitorService.DeleteMonitoredInstallation(installation.Id);
        SavedInstallations.Remove(installation);

        if (SelectedInstallation == installation)
        {
            SelectedInstallation = null;
        }
    }

    [RelayCommand]
    private void DeleteBackup(UninstallBackup backup)
    {
        _backupService.DeleteBackup(backup);
        AvailableBackups.Remove(backup);

        if (SelectedBackup == backup)
        {
            SelectedBackup = null;
        }
    }

    #region Selection Commands

    [RelayCommand]
    private void SelectAllChanges()
    {
        if (SelectedInstallation == null) return;
        foreach (var change in SelectedInstallation.Changes)
            change.IsSelected = true;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void DeselectAllChanges()
    {
        if (SelectedInstallation == null) return;
        foreach (var change in SelectedInstallation.Changes)
            change.IsSelected = false;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void SelectFilesOnly()
    {
        if (SelectedInstallation == null) return;
        foreach (var change in SelectedInstallation.Changes)
            change.IsSelected = change.Category is SystemChangeCategory.File or SystemChangeCategory.Folder;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void SelectRegistryOnly()
    {
        if (SelectedInstallation == null) return;
        foreach (var change in SelectedInstallation.Changes)
            change.IsSelected = change.Category is SystemChangeCategory.RegistryKey or SystemChangeCategory.RegistryValue;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void SelectByCategory(SystemChangeCategory category)
    {
        if (SelectedInstallation == null) return;
        foreach (var change in SelectedInstallation.Changes)
            change.IsSelected = change.Category == category;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        if (SelectedInstallation == null) return;
        foreach (var change in SelectedInstallation.Changes)
            change.IsSelected = !change.IsSelected;
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedChanges));
        OnPropertyChanged(nameof(SelectedChangesCount));
        OnPropertyChanged(nameof(FormattedSelectedSize));
    }

    #endregion

    [RelayCommand]
    private async Task ExportChangesAsync()
    {
        if (SelectedInstallation == null) return;

        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
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

            StatusMessage = $"✅ Exporté vers {file.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Erreur export: {ex.Message}";
        }
    }

    #endregion

    #region Event Handlers

    private void OnRealTimeChangeDetected(object? sender, SystemChange change)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            RealTimeChanges.Insert(0, change);

            // Limiter pour les performances
            while (RealTimeChanges.Count > 1000)
            {
                RealTimeChanges.RemoveAt(RealTimeChanges.Count - 1);
            }

            OnPropertyChanged(nameof(RealTimeChangeCount));
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

    private void OnInstallerProcessDetected(object? sender, ProcessInfo info)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            ActiveProcesses.Add(info);
            ProcessesDetectedCount++;
            OnPropertyChanged(nameof(TotalStatisticsCount));
        });
    }

    private void OnInstallerProcessExited(object? sender, ProcessInfo info)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var existing = ActiveProcesses.FirstOrDefault(p => p.ProcessId == info.ProcessId);
            if (existing != null)
            {
                ActiveProcesses.Remove(existing);
            }
        });
    }

    private void OnStatisticsUpdated(object? sender, MonitoringStatistics stats)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            FilesDetectedCount = stats.FilesDetected;
            FoldersDetectedCount = stats.FoldersDetected;
            RegistryDetectedCount = stats.RegistryChangesDetected;
            ProcessesDetectedCount = stats.ProcessesDetected;
            OnPropertyChanged(nameof(TotalStatisticsCount));
        });
    }

    #endregion

    #region Private Helpers

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

    private void ResetStatistics()
    {
        FilesDetectedCount = 0;
        FoldersDetectedCount = 0;
        RegistryDetectedCount = 0;
        ProcessesDetectedCount = 0;
    }

    private void StartDurationTimer()
    {
        _durationTimer?.Stop();
        _durationTimer = new System.Timers.Timer(1000);
        _durationTimer.Elapsed += (s, e) =>
        {
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (CurrentMonitoring != null)
                {
                    MonitoringDuration = CurrentMonitoring.FormattedDuration;
                }
            });
        };
        _durationTimer.Start();
    }

    private void StopDurationTimer()
    {
        _durationTimer?.Stop();
        _durationTimer?.Dispose();
        _durationTimer = null;
    }

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
            "Type,Catégorie,Chemin,Taille,Horodatage,Description,Sélectionné"
        };

        foreach (var change in SelectedInstallation.Changes)
        {
            var line = $"\"{change.ChangeTypeName}\",\"{change.CategoryName}\"," +
                      $"\"{change.Path.Replace("\"", "\"\"")}\",{change.Size}," +
                      $"\"{change.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                      $"\"{change.Description.Replace("\"", "\"\"")}\",{change.IsSelected}";
            lines.Add(line);
        }

        await File.WriteAllLinesAsync(path, lines);
    }

    public async Task InitializeAsync()
    {
        await LoadSavedInstallationsAsync();
        await LoadBackupsAsync();
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _durationTimer?.Stop();
        _durationTimer?.Dispose();

        _monitorService.RealTimeChangeDetected -= OnRealTimeChangeDetected;
        _monitorService.StatusChanged -= OnStatusChanged;
        _monitorService.InstallerProcessDetected -= OnInstallerProcessDetected;
        _monitorService.InstallerProcessExited -= OnInstallerProcessExited;
        _monitorService.StatisticsUpdated -= OnStatisticsUpdated;
        _monitorService.Dispose();
        _isDisposed = true;

        GC.SuppressFinalize(this);
    }

    #endregion
}
