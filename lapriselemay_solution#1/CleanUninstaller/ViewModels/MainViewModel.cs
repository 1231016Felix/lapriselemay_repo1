using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CleanUninstaller.Models;
using CleanUninstaller.Services;
using CleanUninstaller.Helpers;
using System.Collections.ObjectModel;

namespace CleanUninstaller.ViewModels;

/// <summary>
/// ViewModel principal de l'application
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ProgramScannerService _programScanner;
    private readonly UninstallService _uninstallService;
    private readonly ResidualScannerService _residualScanner;
    private readonly SettingsService _settingsService;
    
    private List<InstalledProgram> _allPrograms = [];
    private CancellationTokenSource? _scanCts;

    public MainViewModel()
    {
        _programScanner = App.ProgramScanner;
        _uninstallService = App.UninstallService;
        _residualScanner = App.ResidualScanner;
        _settingsService = App.SettingsService;
    }

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
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan annulé";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
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

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            // Créer un backup si configuré
            await _settingsService.CreateRegistryBackupAsync(program.DisplayName);

            // Désinstaller sans scanner les résidus automatiquement
            // (on le fera via le dialogue si l'option est activée)
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

                // Retirer de la liste
                _allPrograms.Remove(program);
                Programs.Remove(program);
                TotalProgramCount = _allPrograms.Count;
                SelectedProgram = null;

                // Afficher le dialogue d'analyse des résidus si l'option est activée
                if (_settingsService.Settings.ThoroughAnalysisEnabled && XamlRoot != null)
                {
                    StatusMessage = "Désinstallé - Analyse des résidus...";
                    
                    var residualDialog = new Views.ResidualScanDialog(program)
                    {
                        XamlRoot = XamlRoot
                    };
                    
                    await residualDialog.ShowAsync();

                    // Mettre à jour le statut après le dialogue
                    if (residualDialog.DeletionPerformed)
                    {
                        StatusMessage = "Désinstallation et nettoyage terminés";
                    }
                    else if (residualDialog.Residuals.Count > 0)
                    {
                        // Des résidus restent
                        Residuals = new ObservableCollection<ResidualItem>(residualDialog.Residuals);
                        OnPropertyChanged(nameof(HasResiduals));
                        OnPropertyChanged(nameof(ResidualsTotalSize));
                        StatusMessage = $"Désinstallé - {residualDialog.Residuals.Count} résidu(s) restant(s)";
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
            }
        }
        catch (Exception ex)
        {
            program.Status = ProgramStatus.Error;
            program.StatusMessage = ex.Message;
            StatusMessage = $"Erreur: {ex.Message}";
        }
        finally
        {
            IsUninstalling = false;
        }
    }

    /// <summary>
    /// Commande de désinstallation en lot
    /// </summary>
    [RelayCommand]
    public async Task UninstallBatchAsync()
    {
        var selectedPrograms = Programs.Where(p => p.IsSelected).ToList();
        if (selectedPrograms.Count == 0 || IsUninstalling) return;

        IsUninstalling = true;

        try
        {
            // Créer un point de restauration si configuré
            if (_settingsService.Settings.CreateRestorePoint && selectedPrograms.Count > 1)
            {
                StatusMessage = "Création du point de restauration...";
                await _uninstallService.CreateRestorePointAsync(
                    $"Avant désinstallation de {selectedPrograms.Count} programmes");
            }

            var successCount = 0;
            var failCount = 0;
            var uninstalledPrograms = new List<InstalledProgram>();

            for (var i = 0; i < selectedPrograms.Count; i++)
            {
                var program = selectedPrograms[i];
                Progress = (i * 100) / selectedPrograms.Count;
                StatusMessage = $"Désinstallation de {program.DisplayName} ({i + 1}/{selectedPrograms.Count})...";

                program.Status = ProgramStatus.Uninstalling;

                var result = await _uninstallService.UninstallProgramAsync(
                    program,
                    _settingsService.Settings.PreferQuietUninstall,
                    scanResiduals: false);

                if (result.Success)
                {
                    successCount++;
                    uninstalledPrograms.Add(program);
                    _allPrograms.Remove(program);
                    Programs.Remove(program);
                }
                else
                {
                    failCount++;
                    program.Status = ProgramStatus.Error;
                    program.StatusMessage = result.ErrorMessage ?? "Échec";
                }
            }

            TotalProgramCount = _allPrograms.Count;
            StatusMessage = $"Terminé: {successCount} réussi(s), {failCount} échec(s)";

            // Proposer l'analyse des résidus si l'option est activée et qu'il y a eu des succès
            if (_settingsService.Settings.ThoroughAnalysisEnabled && 
                uninstalledPrograms.Count > 0 && 
                XamlRoot != null)
            {
                // Scanner les résidus de tous les programmes désinstallés
                var allResiduals = new List<ResidualItem>();
                
                for (var i = 0; i < uninstalledPrograms.Count; i++)
                {
                    var program = uninstalledPrograms[i];
                    StatusMessage = $"Analyse des résidus de {program.DisplayName} ({i + 1}/{uninstalledPrograms.Count})...";
                    
                    var residuals = await _residualScanner.ScanResidualsAsync(program);
                    allResiduals.AddRange(residuals);
                }

                if (allResiduals.Count > 0)
                {
                    // Afficher le dialogue avec tous les résidus
                    Residuals = new ObservableCollection<ResidualItem>(allResiduals);
                    OnPropertyChanged(nameof(HasResiduals));
                    OnPropertyChanged(nameof(ResidualsTotalSize));
                    
                    StatusMessage = $"Terminé: {successCount} réussi(s), {failCount} échec(s), {allResiduals.Count} résidu(s) trouvé(s)";
                }
                else
                {
                    StatusMessage = $"Terminé: {successCount} réussi(s), {failCount} échec(s) - Aucun résidu";
                }
            }
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
    /// Commande de nettoyage des résidus
    /// </summary>
    [RelayCommand]
    public async Task CleanupResidualsAsync()
    {
        if (!HasResiduals || IsUninstalling) return;

        IsUninstalling = true;
        StatusMessage = "Nettoyage des résidus...";

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
            
            if (result.FailedCount > 0)
            {
                StatusMessage += $" ({result.FailedCount} échec(s))";
            }

            OnPropertyChanged(nameof(HasResiduals));
            OnPropertyChanged(nameof(ResidualsTotalSize));
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

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                StatusMessage = p.StatusMessage;
            });

            var residuals = await _residualScanner.ScanResidualsAsync(SelectedProgram, progress);
            Residuals = new ObservableCollection<ResidualItem>(residuals);

            StatusMessage = $"{residuals.Count} résidus trouvés";
            OnPropertyChanged(nameof(HasResiduals));
            OnPropertyChanged(nameof(ResidualsTotalSize));
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
            // Sélectionner uniquement les éléments sûrs (High et VeryHigh)
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
        // Séparer les programmes avec taille connue et inconnue
        var withSize = programs.Where(p => p.EstimatedSize > 0);
        var unknownSize = programs.Where(p => p.EstimatedSize == 0);

        // Trier les programmes avec taille connue
        var sortedWithSize = SortDescending
            ? withSize.OrderByDescending(p => p.EstimatedSize)
            : withSize.OrderBy(p => p.EstimatedSize);

        // Les tailles inconnues sont toujours à la fin, triées par nom
        var sortedUnknown = unknownSize.OrderBy(p => p.DisplayName);

        // Concaténer: tailles connues d'abord, puis inconnues
        return sortedWithSize.Concat(sortedUnknown);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Initialise le ViewModel (charger les settings et lancer le scan)
    /// </summary>
    public async Task InitializeAsync()
    {
        await _settingsService.LoadAsync();
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
