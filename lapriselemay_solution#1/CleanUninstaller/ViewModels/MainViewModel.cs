using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using CleanUninstaller.Services.Interfaces;
using CleanUninstaller.Helpers;
using System.Collections.ObjectModel;

namespace CleanUninstaller.ViewModels;

/// <summary>
/// ViewModel principal de l'application
/// Utilise l'injection de dépendances pour tous les services
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IProgramScannerService _programScanner;
    private readonly IUninstallService _uninstallService;
    private readonly IResidualScannerService _residualScanner;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;
    
    private List<InstalledProgram> _allPrograms = [];
    private CancellationTokenSource? _scanCts;

    /// <summary>
    /// Constructeur avec injection de dépendances (recommandé)
    /// </summary>
    public MainViewModel(
        IProgramScannerService programScanner,
        IUninstallService uninstallService,
        IResidualScannerService residualScanner,
        ISettingsService settingsService,
        ILoggerService logger)
    {
        _programScanner = programScanner ?? throw new ArgumentNullException(nameof(programScanner));
        _uninstallService = uninstallService ?? throw new ArgumentNullException(nameof(uninstallService));
        _residualScanner = residualScanner ?? throw new ArgumentNullException(nameof(residualScanner));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.Debug("MainViewModel initialisé via injection de dépendances");
    }

    /// <summary>
    /// Constructeur par défaut utilisant le ServiceContainer (pour compatibilité XAML)
    /// </summary>
    public MainViewModel() : this(
        ServiceContainer.GetService<IProgramScannerService>(),
        ServiceContainer.GetService<IUninstallService>(),
        ServiceContainer.GetService<IResidualScannerService>(),
        ServiceContainer.GetService<ISettingsService>(),
        ServiceContainer.GetService<ILoggerService>())
    { }

    #region Properties

    /// <summary>
    /// Liste des programmes affichés (filtrée)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<InstalledProgram> _programs = [];

    /// <summary>
    /// Programme sélectionné
    /// </summary>
    [ObservableProperty]
    private InstalledProgram? _selectedProgram;

    /// <summary>
    /// Résidus trouvés
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ResidualItem> _residuals = [];

    /// <summary>
    /// Texte de recherche
    /// </summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>
    /// Afficher les apps système
    /// </summary>
    [ObservableProperty]
    private bool _showSystemApps;

    /// <summary>
    /// Afficher les apps Windows Store
    /// </summary>
    [ObservableProperty]
    private bool _showWindowsApps = true;

    /// <summary>
    /// Option de tri actuelle
    /// </summary>
    [ObservableProperty]
    private SortOption _sortBy = SortOption.Name;

    /// <summary>
    /// Tri descendant
    /// </summary>
    [ObservableProperty]
    private bool _sortDescending;

    /// <summary>
    /// Filtre par taille
    /// </summary>
    [ObservableProperty]
    private SizeFilter _sizeFilter = SizeFilter.All;

    /// <summary>
    /// Indique si un scan est en cours
    /// </summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>
    /// Indique si une désinstallation est en cours
    /// </summary>
    [ObservableProperty]
    private bool _isUninstalling;

    /// <summary>
    /// Progression actuelle (0-100)
    /// </summary>
    [ObservableProperty]
    private int _progress;

    /// <summary>
    /// Message de statut
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Prêt";

    /// <summary>
    /// Nombre total de programmes
    /// </summary>
    [ObservableProperty]
    private int _totalProgramCount;

    /// <summary>
    /// Nombre de programmes affichés (filtrés)
    /// </summary>
    public int FilteredProgramCount => Programs.Count;

    /// <summary>
    /// Nombre de programmes sélectionnés
    /// </summary>
    public int SelectedCount => Programs.Count(p => p.IsSelected);

    /// <summary>
    /// Taille totale des programmes sélectionnés
    /// </summary>
    public string SelectedTotalSize
    {
        get
        {
            var total = Programs.Where(p => p.IsSelected).Sum(p => p.EstimatedSize);
            return CommonHelpers.FormatSizeOrZero(total);
        }
    }

    /// <summary>
    /// Taille totale des résidus sélectionnés
    /// </summary>
    public string ResidualsTotalSize
    {
        get
        {
            var total = Residuals.Where(r => r.IsSelected).Sum(r => r.Size);
            return CommonHelpers.FormatSizeOrZero(total);
        }
    }

    /// <summary>
    /// Indique si des programmes sont sélectionnés
    /// </summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>
    /// Indique si des résidus sont disponibles
    /// </summary>
    public bool HasResiduals => Residuals.Any();

    #endregion

    #region Commands

    /// <summary>
    /// Commande de scan des programmes
    /// </summary>
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

            _allPrograms = await _programScanner.ScanAllProgramsAsync(progress, _scanCts.Token);
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

    /// <summary>
    /// Commande d'annulation du scan
    /// </summary>
    [RelayCommand]
    private void CancelScan()
    {
        _logger.Debug("Annulation du scan demandée");
        _scanCts?.Cancel();
    }

    /// <summary>
    /// Référence à la fenêtre principale pour afficher les dialogues
    /// </summary>
    public Microsoft.UI.Xaml.XamlRoot? XamlRoot { get; set; }

    /// <summary>
    /// Commande de désinstallation du programme sélectionné
    /// </summary>
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

            // Créer un point de restauration si configuré
            if (_settingsService.Settings.CreateRestorePointBeforeUninstall)
            {
                _logger.Info("Création du point de restauration...");
                StatusMessage = "Création du point de restauration...";
                var restorePointCreated = await _uninstallService.CreateRestorePointAsync(
                    $"Avant désinstallation de {program.DisplayName}");
                
                if (!restorePointCreated)
                {
                    _logger.Warning("Impossible de créer le point de restauration");
                }
            }

            // Désinstaller sans scanner les résidus automatiquement
            var result = await _uninstallService.UninstallProgramAsync(
                program,
                _settingsService.Settings.PreferQuietUninstall,
                scanResiduals: false,
                progress);

            // Attendre un peu pour que le système mette à jour le registre
            await Task.Delay(2000);

            if (result.Success)
            {
                program.Status = ProgramStatus.Uninstalled;
                program.StatusMessage = "Désinstallé";
                _logger.Info($"Désinstallation réussie: {program.DisplayName}");

                // Retirer de la liste
                _allPrograms.Remove(program);
                Programs.Remove(program);
                TotalProgramCount = _allPrograms.Count;
                SelectedProgram = null;

                // Afficher le dialogue d'analyse des résidus si l'option est activée
                if (_settingsService.Settings.ScanResidualsAfterUninstall && XamlRoot != null)
                {
                    StatusMessage = "Désinstallé - Analyse des résidus...";
                    _logger.Debug("Lancement de l'analyse des résidus");
                    
                    var residualDialog = new Views.ResidualScanDialog(program)
                    {
                        XamlRoot = XamlRoot
                    };
                    
                    await residualDialog.ShowAsync();

                    // Mettre à jour le statut après le dialogue
                    if (residualDialog.DeletionPerformed)
                    {
                        StatusMessage = "Désinstallation et nettoyage terminés";
                        _logger.Info("Nettoyage des résidus effectué");
                    }
                    else if (residualDialog.Residuals.Count > 0)
                    {
                        Residuals = new ObservableCollection<ResidualItem>(residualDialog.Residuals);
                        OnPropertyChanged(nameof(HasResiduals));
                        OnPropertyChanged(nameof(ResidualsTotalSize));
                        StatusMessage = $"Désinstallé - {residualDialog.Residuals.Count} résidu(s) restant(s)";
                        _logger.Info($"{residualDialog.Residuals.Count} résidus trouvés");
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

    /// <summary>
    /// Commande de désinstallation en lot avec support parallèle
    /// </summary>
    [RelayCommand]
    public async Task UninstallBatchAsync()
    {
        var selectedPrograms = Programs.Where(p => p.IsSelected).ToList();
        if (selectedPrograms.Count == 0 || IsUninstalling) return;

        IsUninstalling = true;
        _logger.Info($"Début de la désinstallation en lot de {selectedPrograms.Count} programmes");

        try
        {
            // Créer un point de restauration si configuré
            if (_settingsService.Settings.CreateRestorePointBeforeUninstall)
            {
                StatusMessage = "Création du point de restauration...";
                _logger.Info("Création du point de restauration avant désinstallation en lot");
                
                var restorePointCreated = await _uninstallService.CreateRestorePointAsync(
                    $"Avant désinstallation de {selectedPrograms.Count} programmes");
                
                if (!restorePointCreated)
                {
                    _logger.Warning("Impossible de créer le point de restauration");
                }
            }

            var successCount = 0;
            var failCount = 0;
            var uninstalledPrograms = new List<InstalledProgram>();

            // Utiliser le traitement parallèle si configuré
            if (_settingsService.Settings.UseParallelBatchUninstall && selectedPrograms.Count > 1)
            {
                _logger.Info($"Mode parallèle activé (max {_settingsService.Settings.MaxParallelUninstalls} simultanés)");
                (successCount, failCount, uninstalledPrograms) = await UninstallBatchParallelAsync(selectedPrograms);
            }
            else
            {
                // Traitement séquentiel
                _logger.Info("Mode séquentiel pour la désinstallation en lot");
                (successCount, failCount, uninstalledPrograms) = await UninstallBatchSequentialAsync(selectedPrograms);
            }

            TotalProgramCount = _allPrograms.Count;
            StatusMessage = $"Terminé: {successCount} réussi(s), {failCount} échec(s)";
            _logger.Info($"Désinstallation en lot terminée: {successCount} réussi(s), {failCount} échec(s)");

            // Proposer l'analyse des résidus si l'option est activée et qu'il y a eu des succès
            if (_settingsService.Settings.ScanResidualsAfterUninstall && 
                uninstalledPrograms.Count > 0 && 
                XamlRoot != null)
            {
                await ScanBatchResidualsAsync(uninstalledPrograms, successCount, failCount);
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

    /// <summary>
    /// Désinstallation séquentielle (un programme à la fois)
    /// </summary>
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
                scanResiduals: false);

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

    /// <summary>
    /// Désinstallation parallèle avec throttling
    /// </summary>
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
            await semaphore.WaitAsync();
            try
            {
                program.Status = ProgramStatus.Uninstalling;
                _logger.Debug($"Désinstallation parallèle: {program.DisplayName}");

                var result = await _uninstallService.UninstallProgramAsync(
                    program,
                    silent: true,
                    scanResiduals: false);

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

        await Task.WhenAll(tasks);

        // Mettre à jour la collection sur le thread UI
        foreach (var program in uninstalledPrograms)
        {
            Programs.Remove(program);
        }

        return (successCount, failCount, uninstalledPrograms);
    }

    /// <summary>
    /// Scanne les résidus pour une liste de programmes désinstallés
    /// </summary>
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
                var residuals = await _residualScanner.ScanAsync(program);
                allResiduals.AddRange(residuals);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Erreur lors du scan des résidus de {program.DisplayName}: {ex.Message}");
            }
        }

        if (allResiduals.Count > 0)
        {
            Residuals = new ObservableCollection<ResidualItem>(allResiduals);
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

    /// <summary>
    /// Commande de nettoyage des résidus
    /// </summary>
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

            var result = await _uninstallService.CleanupResidualsAsync(Residuals, progress);

            // Retirer les éléments supprimés
            var deleted = Residuals.Where(r => r.IsDeleted).ToList();
            foreach (var item in deleted)
            {
                Residuals.Remove(item);
            }

            StatusMessage = $"Nettoyage terminé: {result.DeletedCount} supprimé(s), {FormatSize(result.SpaceFreed)} libéré(s)";
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

    /// <summary>
    /// Commande de scan des résidus pour le programme sélectionné
    /// </summary>
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

            var residuals = await _residualScanner.ScanAsync(SelectedProgram, progress);
            Residuals = new ObservableCollection<ResidualItem>(residuals);

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

    /// <summary>
    /// Sélectionne/désélectionne tous les programmes
    /// </summary>
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

    /// <summary>
    /// Sélectionne/désélectionne tous les résidus
    /// </summary>
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

    /// <summary>
    /// Sélectionne les résidus avec confiance élevée (High et VeryHigh uniquement)
    /// </summary>
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

    // Constantes pour les tailles (en octets)
    private const long SIZE_10_MB = 10L * 1024 * 1024;
    private const long SIZE_100_MB = 100L * 1024 * 1024;
    private const long SIZE_1_GB = 1024L * 1024 * 1024;

    private void ApplyFilters()
    {
        var filtered = _allPrograms.AsEnumerable();

        // Filtre de recherche
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(p => p.SearchName.Contains(search));
        }

        // Filtre apps système
        if (!ShowSystemApps)
        {
            filtered = filtered.Where(p => !p.IsSystemComponent);
        }

        // Filtre apps Windows Store
        if (!ShowWindowsApps)
        {
            filtered = filtered.Where(p => !p.IsWindowsApp);
        }

        // Filtre par taille
        filtered = SizeFilter switch
        {
            SizeFilter.Small => filtered.Where(p => p.EstimatedSize > 0 && p.EstimatedSize < SIZE_10_MB),
            SizeFilter.Medium => filtered.Where(p => p.EstimatedSize >= SIZE_10_MB && p.EstimatedSize < SIZE_100_MB),
            SizeFilter.Large => filtered.Where(p => p.EstimatedSize >= SIZE_100_MB && p.EstimatedSize < SIZE_1_GB),
            SizeFilter.VeryLarge => filtered.Where(p => p.EstimatedSize >= SIZE_1_GB),
            SizeFilter.Unknown => filtered.Where(p => p.EstimatedSize == 0),
            _ => filtered // SizeFilter.All
        };

        // Tri
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

        Programs = new ObservableCollection<InstalledProgram>(filtered);
        OnPropertyChanged(nameof(FilteredProgramCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedTotalSize));
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>
    /// Tri par taille avec les tailles inconnues à la fin
    /// </summary>
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

    #region Helpers

    /// <summary>
    /// Initialise le ViewModel (charger les settings et lancer le scan)
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.Info("Initialisation du MainViewModel");
        _settingsService.Load();
        await ScanProgramsAsync();
    }

    /// <summary>
    /// Formate une taille en octets en chaîne lisible
    /// </summary>
    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] suffixes = ["o", "Ko", "Mo", "Go", "To"];
        var i = 0;
        var size = (double)bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:N1} {suffixes[i]}";
    }

    #endregion
}
