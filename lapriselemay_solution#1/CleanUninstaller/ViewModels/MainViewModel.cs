using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using CleanUninstaller.Services.Interfaces;
using CleanUninstaller.Helpers;
using Shared.Logging;
using System.Collections.ObjectModel;
using Shared.Core.Extensions;
using Shared.Core.Helpers;

namespace CleanUninstaller.ViewModels;

/// <summary>
/// ViewModel principal de l'application.
/// Utilise l'injection de dépendances pour tous les services.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    #region Constants
    
    private const long Size10Mb = 10L * 1024 * 1024;
    private const long Size100Mb = 100L * 1024 * 1024;
    private const long Size1Gb = 1024L * 1024 * 1024;
    
    #endregion

    #region Dependencies
    
    private readonly IProgramScannerService _programScanner;
    private readonly IUninstallService _uninstallService;
    private readonly IResidualScannerService _residualScanner;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly Shared.Logging.ILoggerService _logger;
    
    #endregion

    #region State
    
    private List<InstalledProgram> _allPrograms = [];
    private CancellationTokenSource? _scanCts;
    
    #endregion

    /// <summary>
    /// Constructeur avec injection de dépendances
    /// </summary>
    public MainViewModel(
        IProgramScannerService programScanner,
        IUninstallService uninstallService,
        IResidualScannerService residualScanner,
        ISettingsService settingsService,
        IDialogService dialogService,
        Shared.Logging.ILoggerService logger)
    {
        _programScanner = programScanner ?? throw new ArgumentNullException(nameof(programScanner));
        _uninstallService = uninstallService ?? throw new ArgumentNullException(nameof(uninstallService));
        _residualScanner = residualScanner ?? throw new ArgumentNullException(nameof(residualScanner));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.Debug("MainViewModel initialisé via injection de dépendances");
    }

    #region Properties

    [ObservableProperty]
    private ObservableCollection<InstalledProgram> _programs = [];

    [ObservableProperty]
    private InstalledProgram? _selectedProgram;

    [ObservableProperty]
    private ObservableCollection<ResidualItem> _residuals = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _showSystemApps;

    [ObservableProperty]
    private bool _showWindowsApps = true;

    [ObservableProperty]
    private SortOption _sortBy = SortOption.Name;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private SizeFilter _sizeFilter = SizeFilter.All;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isUninstalling;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private string _statusMessage = "Prêt";

    [ObservableProperty]
    private int _totalProgramCount;

    public int FilteredProgramCount => Programs.Count;
    public int SelectedCount => Programs.Count(p => p.IsSelected);

    public string SelectedTotalSize
    {
        get
        {
            var total = Programs.Where(p => p.IsSelected).Sum(p => p.EstimatedSize);
            return SizeFormatter.FormatOrZero(total);
        }
    }

    public string ResidualsTotalSize
    {
        get
        {
            var total = Residuals.Where(r => r.IsSelected).Sum(r => r.Size);
            return SizeFormatter.FormatOrZero(total);
        }
    }

    public bool HasSelection => SelectedCount > 0;
    public bool HasResiduals => Residuals.Any();

    #endregion

    #region Commands

    [RelayCommand]
    private async Task ScanProgramsAsync()
    {
        if (IsScanning) return;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        IsScanning = true;
        Progress = 0;
        StatusMessage = "Scan en cours...";
        _logger.Info("Démarrage du scan des programmes");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            _allPrograms = await _programScanner.ScanAllProgramsAsync(progress, _scanCts.Token)
                .ConfigureAwait(true);
            TotalProgramCount = _allPrograms.Count;
            
            ApplyFilters();
            
            StatusMessage = $"{TotalProgramCount} programmes trouvés";
            _logger.Info($"Scan terminé: {TotalProgramCount} programmes trouvés");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan annulé";
            _logger.Info("Scan annulé par l'utilisateur");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error("Erreur lors du scan des programmes", ex);
        }
        finally
        {
            IsScanning = false;
            Progress = 100;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _logger.Debug("Annulation du scan demandée");
        _scanCts?.Cancel();
    }

    [RelayCommand]
    public async Task UninstallSelectedAsync()
    {
        if (SelectedProgram == null || IsUninstalling) return;

        IsUninstalling = true;
        var program = SelectedProgram;
        program.Status = ProgramStatus.Uninstalling;
        program.StatusMessage = "Désinstallation en cours...";
        _logger.Info($"Début de la désinstallation de: {program.DisplayName}");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            if (_settingsService.Settings.CreateRestorePointBeforeUninstall)
            {
                _logger.Info("Création du point de restauration...");
                StatusMessage = "Création du point de restauration...";
                var restorePointCreated = await _uninstallService.CreateRestorePointAsync(
                    $"Avant désinstallation de {program.DisplayName}").ConfigureAwait(true);
                
                if (!restorePointCreated)
                {
                    _logger.Warning("Impossible de créer le point de restauration");
                }
            }

            var result = await _uninstallService.UninstallProgramAsync(
                program,
                _settingsService.Settings.PreferQuietUninstall,
                scanResiduals: false,
                progress).ConfigureAwait(true);

            await Task.Delay(2000).ConfigureAwait(true);

            if (result.Success)
            {
                program.Status = ProgramStatus.Uninstalled;
                program.StatusMessage = "Désinstallé";
                _logger.Info($"Désinstallation réussie: {program.DisplayName}");

                _allPrograms.Remove(program);
                Programs.Remove(program);
                TotalProgramCount = _allPrograms.Count;
                SelectedProgram = null;

                if (_settingsService.Settings.ScanResidualsAfterUninstall)
                {
                    StatusMessage = "Désinstallé - Analyse des résidus...";
                    _logger.Debug("Lancement de l'analyse des résidus");
                    
                    var (deletionPerformed, residuals) = await _dialogService
                        .ShowResidualScanDialogAsync(program)
                        .ConfigureAwait(true);

                    if (deletionPerformed)
                    {
                        StatusMessage = "Désinstallation et nettoyage terminés";
                        _logger.Info("Nettoyage des résidus effectué");
                    }
                    else if (residuals.Count > 0)
                    {
                        Residuals.ReplaceWith(residuals);
                        OnPropertyChanged(nameof(HasResiduals));
                        OnPropertyChanged(nameof(ResidualsTotalSize));
                        StatusMessage = $"Désinstallé - {residuals.Count} résidu(s) restant(s)";
                        _logger.Info($"{residuals.Count} résidus trouvés");
                    }
                    else
                    {
                        StatusMessage = "Désinstallation complète - Aucun résidu";
                    }
                }
                else
                {
                    StatusMessage = "Désinstallation complète";
                }
            }
            else
            {
                program.Status = ProgramStatus.Error;
                program.StatusMessage = result.ErrorMessage ?? "Échec";
                StatusMessage = $"Erreur: {result.ErrorMessage}";
                _logger.Error($"Échec de la désinstallation de {program.DisplayName}: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            program.Status = ProgramStatus.Error;
            program.StatusMessage = ex.Message;
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error($"Exception lors de la désinstallation de {program.DisplayName}", ex);
        }
        finally
        {
            IsUninstalling = false;
        }
    }

    [RelayCommand]
    public async Task UninstallBatchAsync()
    {
        var selectedPrograms = Programs.Where(p => p.IsSelected).ToList();
        if (selectedPrograms.Count == 0 || IsUninstalling) return;

        IsUninstalling = true;
        _logger.Info($"Début de la désinstallation en lot de {selectedPrograms.Count} programmes");

        try
        {
            if (_settingsService.Settings.CreateRestorePointBeforeUninstall)
            {
                StatusMessage = "Création du point de restauration...";
                _logger.Info("Création du point de restauration avant désinstallation en lot");
                
                var restorePointCreated = await _uninstallService.CreateRestorePointAsync(
                    $"Avant désinstallation de {selectedPrograms.Count} programmes")
                    .ConfigureAwait(true);
                
                if (!restorePointCreated)
                {
                    _logger.Warning("Impossible de créer le point de restauration");
                }
            }

            var successCount = 0;
            var failCount = 0;
            var uninstalledPrograms = new List<InstalledProgram>();

            if (_settingsService.Settings.UseParallelBatchUninstall && selectedPrograms.Count > 1)
            {
                _logger.Info($"Mode parallèle activé (max {_settingsService.Settings.MaxParallelUninstalls} simultanés)");
                (successCount, failCount, uninstalledPrograms) = await UninstallBatchParallelAsync(selectedPrograms)
                    .ConfigureAwait(true);
            }
            else
            {
                _logger.Info("Mode séquentiel pour la désinstallation en lot");
                (successCount, failCount, uninstalledPrograms) = await UninstallBatchSequentialAsync(selectedPrograms)
                    .ConfigureAwait(true);
            }

            TotalProgramCount = _allPrograms.Count;
            StatusMessage = $"Terminé: {successCount} réussi(s), {failCount} échec(s)";
            _logger.Info($"Désinstallation en lot terminée: {successCount} réussi(s), {failCount} échec(s)");

            if (_settingsService.Settings.ScanResidualsAfterUninstall && uninstalledPrograms.Count > 0)
            {
                await ScanBatchResidualsAsync(uninstalledPrograms, successCount, failCount)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error("Erreur lors de la désinstallation en lot", ex);
        }
        finally
        {
            IsUninstalling = false;
            Progress = 100;
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    private async Task<(int success, int fail, List<InstalledProgram> uninstalled)> UninstallBatchSequentialAsync(
        List<InstalledProgram> programs)
    {
        var successCount = 0;
        var failCount = 0;
        var uninstalledPrograms = new List<InstalledProgram>();

        for (var i = 0; i < programs.Count; i++)
        {
            var program = programs[i];
            Progress = (i * 100) / programs.Count;
            StatusMessage = $"Désinstallation de {program.DisplayName} ({i + 1}/{programs.Count})...";

            program.Status = ProgramStatus.Uninstalling;
            _logger.Debug($"Désinstallation séquentielle: {program.DisplayName}");

            var result = await _uninstallService.UninstallProgramAsync(
                program,
                silent: true,
                scanResiduals: false).ConfigureAwait(false);

            if (result.Success)
            {
                successCount++;
                uninstalledPrograms.Add(program);
                _allPrograms.Remove(program);
                Programs.Remove(program);
                _logger.Debug($"Succès: {program.DisplayName}");
            }
            else
            {
                failCount++;
                program.Status = ProgramStatus.Error;
                program.StatusMessage = result.ErrorMessage ?? "Échec";
                _logger.Warning($"Échec: {program.DisplayName} - {result.ErrorMessage}");
            }
        }

        return (successCount, failCount, uninstalledPrograms);
    }

    private async Task<(int success, int fail, List<InstalledProgram> uninstalled)> UninstallBatchParallelAsync(
        List<InstalledProgram> programs)
    {
        var successCount = 0;
        var failCount = 0;
        var uninstalledPrograms = new List<InstalledProgram>();
        var lockObj = new object();
        var processedCount = 0;

        var semaphore = new SemaphoreSlim(_settingsService.Settings.MaxParallelUninstalls);

        var tasks = programs.Select(async program =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                program.Status = ProgramStatus.Uninstalling;
                _logger.Debug($"Désinstallation parallèle: {program.DisplayName}");

                var result = await _uninstallService.UninstallProgramAsync(
                    program,
                    silent: true,
                    scanResiduals: false).ConfigureAwait(false);

                lock (lockObj)
                {
                    processedCount++;
                    Progress = (processedCount * 100) / programs.Count;

                    if (result.Success)
                    {
                        successCount++;
                        uninstalledPrograms.Add(program);
                        _allPrograms.Remove(program);
                        _logger.Debug($"Succès parallèle: {program.DisplayName}");
                    }
                    else
                    {
                        failCount++;
                        program.Status = ProgramStatus.Error;
                        program.StatusMessage = result.ErrorMessage ?? "Échec";
                        _logger.Warning($"Échec parallèle: {program.DisplayName} - {result.ErrorMessage}");
                    }
                }

                return result;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var program in uninstalledPrograms)
        {
            Programs.Remove(program);
        }

        return (successCount, failCount, uninstalledPrograms);
    }

    private async Task ScanBatchResidualsAsync(List<InstalledProgram> uninstalledPrograms, int successCount, int failCount)
    {
        var allResiduals = new List<ResidualItem>();
        
        for (var i = 0; i < uninstalledPrograms.Count; i++)
        {
            var program = uninstalledPrograms[i];
            StatusMessage = $"Analyse des résidus de {program.DisplayName} ({i + 1}/{uninstalledPrograms.Count})...";
            _logger.Debug($"Scan des résidus pour: {program.DisplayName}");
            
            try
            {
                var residuals = await _residualScanner.ScanAsync(program).ConfigureAwait(false);
                allResiduals.AddRange(residuals);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Erreur lors du scan des résidus de {program.DisplayName}: {ex.Message}");
            }
        }

        if (allResiduals.Count > 0)
        {
            Residuals.ReplaceWith(allResiduals);
            OnPropertyChanged(nameof(HasResiduals));
            OnPropertyChanged(nameof(ResidualsTotalSize));
            
            StatusMessage = $"Terminé: {successCount} réussi(s), {failCount} échec(s), {allResiduals.Count} résidu(s) trouvé(s)";
            _logger.Info($"{allResiduals.Count} résidus trouvés au total");
        }
        else
        {
            StatusMessage = $"Terminé: {successCount} réussi(s), {failCount} échec(s) - Aucun résidu";
        }
    }

    [RelayCommand]
    public async Task CleanupResidualsAsync()
    {
        if (!HasResiduals || IsUninstalling) return;

        IsUninstalling = true;
        StatusMessage = "Nettoyage des résidus...";
        _logger.Info($"Début du nettoyage de {Residuals.Count(r => r.IsSelected)} résidus sélectionnés");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var result = await _uninstallService.CleanupResidualsAsync(Residuals, progress)
                .ConfigureAwait(true);

            Residuals.RemoveWhere(r => r.IsDeleted);

            StatusMessage = $"Nettoyage terminé: {result.DeletedCount} supprimé(s), {SizeFormatter.Format(result.SpaceFreed)} libéré(s)";
            _logger.Info($"Nettoyage terminé: {result.DeletedCount} supprimé(s), {result.SpaceFreed} octets libérés");
            
            if (result.FailedCount > 0)
            {
                StatusMessage += $" ({result.FailedCount} échec(s))";
                _logger.Warning($"{result.FailedCount} échecs lors du nettoyage");
                
                foreach (var error in result.Errors)
                {
                    _logger.Warning($"Échec: {error.Item?.Path} - {error.Message}");
                }
            }

            OnPropertyChanged(nameof(HasResiduals));
            OnPropertyChanged(nameof(ResidualsTotalSize));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error("Erreur lors du nettoyage des résidus", ex);
        }
        finally
        {
            IsUninstalling = false;
            Progress = 100;
        }
    }

    [RelayCommand]
    public async Task ScanResidualsAsync()
    {
        if (SelectedProgram == null || IsScanning) return;

        IsScanning = true;
        StatusMessage = $"Scan des résidus pour {SelectedProgram.DisplayName}...";
        _logger.Info($"Scan des résidus pour: {SelectedProgram.DisplayName}");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var residuals = await _residualScanner.ScanAsync(SelectedProgram, progress)
                .ConfigureAwait(true);
            Residuals.ReplaceWith(residuals);

            StatusMessage = $"{residuals.Count} résidus trouvés";
            _logger.Info($"{residuals.Count} résidus trouvés pour {SelectedProgram.DisplayName}");
            
            OnPropertyChanged(nameof(HasResiduals));
            OnPropertyChanged(nameof(ResidualsTotalSize));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            _logger.Error($"Erreur lors du scan des résidus de {SelectedProgram.DisplayName}", ex);
        }
        finally
        {
            IsScanning = false;
            Progress = 100;
        }
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        var newValue = !Programs.All(p => p.IsSelected);
        foreach (var program in Programs)
        {
            program.IsSelected = newValue;
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedTotalSize));
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void ToggleSelectAllResiduals()
    {
        var newValue = !Residuals.All(r => r.IsSelected);
        foreach (var residual in Residuals)
        {
            residual.IsSelected = newValue;
        }
        OnPropertyChanged(nameof(ResidualsTotalSize));
    }

    [RelayCommand]
    private void SelectHighConfidenceResiduals()
    {
        foreach (var residual in Residuals)
        {
            residual.IsSelected = residual.Confidence >= ConfidenceLevel.High;
        }
        OnPropertyChanged(nameof(ResidualsTotalSize));
    }

    #endregion

    #region Filter Methods

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnShowSystemAppsChanged(bool value) => ApplyFilters();
    partial void OnShowWindowsAppsChanged(bool value) => ApplyFilters();
    partial void OnSortByChanged(SortOption value) => ApplyFilters();
    partial void OnSortDescendingChanged(bool value) => ApplyFilters();
    partial void OnSizeFilterChanged(SizeFilter value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = _allPrograms.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(p => p.SearchName.Contains(search));
        }

        if (!ShowSystemApps)
        {
            filtered = filtered.Where(p => !p.IsSystemComponent);
        }

        if (!ShowWindowsApps)
        {
            filtered = filtered.Where(p => !p.IsWindowsApp);
        }

        filtered = SizeFilter switch
        {
            SizeFilter.Small => filtered.Where(p => p.EstimatedSize > 0 && p.EstimatedSize < Size10Mb),
            SizeFilter.Medium => filtered.Where(p => p.EstimatedSize >= Size10Mb && p.EstimatedSize < Size100Mb),
            SizeFilter.Large => filtered.Where(p => p.EstimatedSize >= Size100Mb && p.EstimatedSize < Size1Gb),
            SizeFilter.VeryLarge => filtered.Where(p => p.EstimatedSize >= Size1Gb),
            SizeFilter.Unknown => filtered.Where(p => p.EstimatedSize == 0),
            _ => filtered
        };

        filtered = SortBy switch
        {
            SortOption.Name => SortDescending 
                ? filtered.OrderByDescending(p => p.DisplayName) 
                : filtered.OrderBy(p => p.DisplayName),
            SortOption.Publisher => SortDescending 
                ? filtered.OrderByDescending(p => p.Publisher) 
                : filtered.OrderBy(p => p.Publisher),
            SortOption.Size => SortBySize(filtered),
            SortOption.InstallDate => SortDescending 
                ? filtered.OrderByDescending(p => p.InstallDate) 
                : filtered.OrderBy(p => p.InstallDate),
            _ => filtered.OrderBy(p => p.DisplayName)
        };

        Programs.ReplaceWith(filtered);
        OnPropertyChanged(nameof(FilteredProgramCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedTotalSize));
        OnPropertyChanged(nameof(HasSelection));
    }

    private IEnumerable<InstalledProgram> SortBySize(IEnumerable<InstalledProgram> programs)
    {
        var withSize = programs.Where(p => p.EstimatedSize > 0);
        var unknownSize = programs.Where(p => p.EstimatedSize == 0);

        var sortedWithSize = SortDescending
            ? withSize.OrderByDescending(p => p.EstimatedSize)
            : withSize.OrderBy(p => p.EstimatedSize);

        var sortedUnknown = unknownSize.OrderBy(p => p.DisplayName);

        return sortedWithSize.Concat(sortedUnknown);
    }

    #endregion

    #region Initialization

    public async Task InitializeAsync()
    {
        _logger.Info("Initialisation du MainViewModel");
        _settingsService.Load();
        await ScanProgramsAsync().ConfigureAwait(true);
    }

    #endregion
}
