using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempCleaner.Helpers;
using TempCleaner.Models;
using TempCleaner.Services;

namespace TempCleaner.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ScannerService _scannerService = new();
    private readonly CleanerService _cleanerService = new();
    private readonly SystemCleanerService _systemCleaner = new();
    private readonly SettingsService _settingsService = new();
    private CancellationTokenSource? _cts;

    #region Observable Properties

    [ObservableProperty] private ObservableCollection<CleanerProfile> _profiles = [];
    [ObservableProperty] private ObservableCollection<TempFileInfo> _files = [];
    [ObservableProperty] private ObservableCollection<TempFileInfo> _filteredFiles = [];
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private string _statusMessage = "Prêt";
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private long _totalSize;
    [ObservableProperty] private long _selectedSize;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _scanDuration = string.Empty;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private int _adminProfileCount;
    [ObservableProperty] private long _recycleBinSize;
    [ObservableProperty] private int _recycleBinCount;

    #endregion

    #region Computed Properties

    public string TotalSizeFormatted => FileSizeHelper.Format(TotalSize);
    public string SelectedSizeFormatted => FileSizeHelper.Format(SelectedSize);
    public string RecycleBinSizeFormatted => FileSizeHelper.Format(RecycleBinSize);
    public bool IsWorking => IsScanning || IsCleaning;
    public bool CanClean => !IsWorking && SelectedCount > 0;
    public bool CanScan => !IsWorking;
    public bool CanCancel => IsWorking;

    #endregion

    #region Constructor

    public MainViewModel()
    {
        IsAdmin = AdminService.IsRunningAsAdmin();
        Profiles = new ObservableCollection<CleanerProfile>(CleanerProfile.GetDefaultProfiles());
        _settingsService.ApplyToProfiles(Profiles);
        AdminProfileCount = Profiles.Count(p => p.RequiresAdmin);
        UpdateRecycleBinStats();
    }

    public void SaveSettings() => _settingsService.SaveProfiles(Profiles);

    #endregion

    #region Profile Warning

    public bool ShowProfileWarning(CleanerProfile profile)
    {
        var warning = profile.GetDetailedWarning();
        var result = MessageBox.Show(
            $"{warning}\n\nVoulez-vous activer cette catégorie ?",
            $"⚠️ Avertissement - {profile.Name}",
            MessageBoxButton.YesNo,
            profile.IsSafe ? MessageBoxImage.Information : MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    #endregion

    #region Property Changed Handlers

    partial void OnIsScanningChanged(bool value) => NotifyStateChanged();
    partial void OnIsCleaningChanged(bool value) => NotifyStateChanged();

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CanCancel));
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Scan Command

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(string message, int percent)>(p =>
            {
                StatusMessage = p.message;
                ProgressPercent = p.percent;
            });

            var result = await _scannerService.ScanAsync(Profiles, progress, _cts.Token);

            Files = new ObservableCollection<TempFileInfo>(result.Files);
            TotalSize = result.TotalSize;
            TotalCount = result.TotalCount;
            ScanDuration = $"Durée: {result.ScanDuration.TotalSeconds:F1}s";

            ApplyFilter();
            UpdateSelectedStats();
            UpdateRecycleBinStats();

            StatusMessage = $"Analyse: {TotalCount} fichiers ({TotalSizeFormatted})";
        }
        catch (OperationCanceledException) { StatusMessage = "Analyse annulée"; }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ProgressPercent = 0;
        }
    }

    #endregion

    #region Clean Command

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selectedFiles = Files.Where(f => f.IsSelected && f.IsAccessible).ToList();

        var confirm = MessageBox.Show(
            $"Supprimer {selectedFiles.Count} fichiers ({FileSizeHelper.Format(selectedFiles.Sum(f => f.Size))}) ?\n\nCette action est irréversible.",
            "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsCleaning = true;
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(string message, int percent, long freedBytes)>(p =>
            {
                StatusMessage = p.message;
                ProgressPercent = p.percent;
            });

            var result = await _cleanerService.CleanAsync(selectedFiles, progress, _cts.Token);

            var deletedPaths = selectedFiles
                .Where(f => !result.Errors.Any(e => e.FilePath == f.FullPath))
                .Select(f => f.FullPath)
                .ToHashSet();

            Files = new ObservableCollection<TempFileInfo>(Files.Where(f => !deletedPaths.Contains(f.FullPath)));

            UpdateProfileStats();
            ApplyFilter();
            UpdateSelectedStats();

            StatusMessage = $"Nettoyage: {result.DeletedCount} fichiers ({result.FreedBytesFormatted} libérés)";

            if (result.FailedCount > 0)
            {
                MessageBox.Show(
                    $"{result.FailedCount} fichier(s) non supprimés sur {result.DeletedCount + result.FailedCount}.\n{result.FreedBytesFormatted} libérés.",
                    "Avertissement", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) { StatusMessage = "Nettoyage annulé"; }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsCleaning = false;
            ProgressPercent = 0;
        }
    }

    #endregion


    #region System Cleaning Commands

    [RelayCommand]
    private async Task EmptyRecycleBinAsync()
    {
        if (RecycleBinCount == 0)
        {
            MessageBox.Show("La corbeille est déjà vide.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Vider la corbeille ?\n\n{RecycleBinCount} éléments ({RecycleBinSizeFormatted})",
            "Corbeille", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = "Vidage de la corbeille...";
        var (success, message) = await _systemCleaner.EmptyRecycleBinAsync();
        UpdateRecycleBinStats();
        StatusMessage = message;
        if (!success) MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    [RelayCommand]
    private async Task FlushDnsCacheAsync()
    {
        StatusMessage = "Vidage du cache DNS...";
        var (success, message) = await _systemCleaner.FlushDnsCacheAsync();
        StatusMessage = message;
        MessageBox.Show(message, success ? "Succès" : "Erreur", MessageBoxButton.OK, 
            success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    [RelayCommand]
    private void ClearClipboard()
    {
        var (_, message) = _systemCleaner.ClearClipboard();
        StatusMessage = message;
    }

    [RelayCommand]
    private async Task ClearRecentDocumentsAsync()
    {
        var confirm = MessageBox.Show("Effacer la liste des documents récents ?", 
            "Documents récents", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = "Effacement des documents récents...";
        var (success, message, _) = await _systemCleaner.ClearRecentDocumentsAsync();
        StatusMessage = message;
        if (!success) MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    #endregion

    #region Memory Purge Commands

    [RelayCommand]
    private async Task PurgeWorkingSetAsync()
    {
        StatusMessage = "Purge du Working Set...";
        var result = await _systemCleaner.PurgeWorkingSetAsync();
        StatusMessage = result.Message;
        ShowMemoryPurgeResult(result, "Working Set");
    }

    [RelayCommand]
    private async Task PurgeStandbyListAsync()
    {
        if (!IsAdmin) { ShowAdminRequired(); return; }
        StatusMessage = "Purge du Standby List...";
        var result = await _systemCleaner.PurgeStandbyListAsync();
        StatusMessage = result.Message;
        ShowMemoryPurgeResult(result, "Standby List");
    }

    [RelayCommand]
    private async Task PurgeAllMemoryAsync()
    {
        StatusMessage = "Purge de la mémoire...";
        var result = await _systemCleaner.PurgeAllMemoryAsync();
        StatusMessage = result.Message;
        ShowMemoryPurgeResult(result, "Mémoire");
    }

    [RelayCommand]
    private async Task PurgeMaxMemoryAsync()
    {
        StatusMessage = "Purge MAXIMALE de la mémoire...";
        var result = await _systemCleaner.PurgeMaxMemoryAsync();
        StatusMessage = result.Message;
        ShowMemoryPurgeResult(result, "Purge Maximale");
    }

    [RelayCommand]
    private async Task PurgeModifiedPageListAsync()
    {
        if (!IsAdmin) { ShowAdminRequired(); return; }
        StatusMessage = "Purge du Modified Page List...";
        var result = await _systemCleaner.PurgeModifiedPageListAsync();
        StatusMessage = result.Message;
        ShowMemoryPurgeResult(result, "Modified Page List");
    }

    [RelayCommand]
    private async Task PurgeLowPriorityStandbyAsync()
    {
        if (!IsAdmin) { ShowAdminRequired(); return; }
        StatusMessage = "Purge du Low-Priority Standby...";
        var result = await _systemCleaner.PurgeLowPriorityStandbyListAsync();
        StatusMessage = result.Message;
        ShowMemoryPurgeResult(result, "Low-Priority Standby");
    }

    private static void ShowAdminRequired() => 
        MessageBox.Show("Droits administrateur requis.", "Droits insuffisants", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static void ShowMemoryPurgeResult(SystemCleanerService.MemoryPurgeResult result, string title)
    {
        if (!result.Success && result.TotalMemory == 0)
        {
            MessageBox.Show(result.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var message = $"""
            ══════════════════════════════════
                      STATISTIQUES MÉMOIRE
            ══════════════════════════════════

            📊 Mémoire totale:     {FileSizeHelper.Format(result.TotalMemory)}

            ▬▬▬ AVANT LA PURGE ▬▬▬
            💾 Utilisée:           {FileSizeHelper.Format(result.UsedBefore)}
            ✅ Disponible:         {FileSizeHelper.Format(result.AvailableBefore)}
            📈 Charge:             {result.MemoryLoadBefore:F0}%

            ▬▬▬ APRÈS LA PURGE ▬▬▬
            💾 Utilisée:           {FileSizeHelper.Format(result.UsedAfter)}
            ✅ Disponible:         {FileSizeHelper.Format(result.AvailableAfter)}
            📈 Charge:             {result.MemoryLoadAfter:F0}%

            ══════════════════════════════════
            🎯 MÉMOIRE LIBÉRÉE:    {FileSizeHelper.Format(result.Freed)}
            ══════════════════════════════════
            """;

        if (result.ProcessCount > 0)
            message += $"\n\n🔧 Processus traités: {result.SuccessCount}/{result.ProcessCount}";

        MessageBox.Show(message, $"Purge {title} - {(result.Success ? "Succès" : "Partiel")}",
            MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    #endregion


    #region System Commands

    [RelayCommand]
    private async Task CleanupWinSxSAsync()
    {
        if (!IsAdmin) { ShowAdminRequired(); return; }

        var confirm = MessageBox.Show(
            "Nettoyer les composants Windows obsolètes (WinSxS) ?\n\n⚠️ Cette opération peut prendre plusieurs minutes et ne peut pas être annulée.",
            "Nettoyage WinSxS", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = "Nettoyage WinSxS en cours (peut prendre plusieurs minutes)...";
        var (success, message) = await _systemCleaner.CleanupWinSxSAsync();
        StatusMessage = message;
        MessageBox.Show(message, success ? "Succès" : "Erreur", MessageBoxButton.OK, 
            success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    [RelayCommand]
    private async Task ToggleHibernationAsync()
    {
        if (!IsAdmin) { ShowAdminRequired(); return; }

        bool isEnabled = _systemCleaner.IsHibernationEnabled();

        if (isEnabled)
        {
            var confirm = MessageBox.Show(
                "Désactiver l'hibernation ?\n\n💾 Cela supprimera hiberfil.sys et libérera plusieurs GB d'espace.\n\n⚠️ Vous ne pourrez plus utiliser l'hibernation.",
                "Désactiver l'hibernation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            StatusMessage = "Désactivation de l'hibernation...";
            var (success, message, _) = await _systemCleaner.DisableHibernationAsync();
            StatusMessage = message;
            MessageBox.Show(message, success ? "Succès" : "Erreur", MessageBoxButton.OK, 
                success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        else
        {
            var confirm = MessageBox.Show("Réactiver l'hibernation ?", "Activer l'hibernation", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            StatusMessage = "Activation de l'hibernation...";
            var (success, message) = await _systemCleaner.EnableHibernationAsync();
            StatusMessage = message;
            MessageBox.Show(message, success ? "Succès" : "Erreur", MessageBoxButton.OK, 
                success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ClearWindowsStoreCacheAsync()
    {
        StatusMessage = "Nettoyage du cache Windows Store...";
        var (success, message) = await _systemCleaner.ClearWindowsStoreCacheAsync();
        StatusMessage = message;
        MessageBox.Show(message, success ? "Succès" : "Erreur", MessageBoxButton.OK, 
            success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    [RelayCommand]
    private async Task RunDiskCleanupAsync()
    {
        if (!IsAdmin) { ShowAdminRequired(); return; }
        StatusMessage = "Lancement du nettoyage de disque Windows...";
        var (_, message) = await _systemCleaner.RunDiskCleanupAsync();
        StatusMessage = message;
    }

    #endregion

    #region Selection Commands

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var file in FilteredFiles) file.IsSelected = true;
        UpdateSelectedStats();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var file in FilteredFiles) file.IsSelected = false;
        UpdateSelectedStats();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var file in FilteredFiles) file.IsSelected = !file.IsSelected;
        UpdateSelectedStats();
    }

    [RelayCommand]
    private void SelectPrivacyItems()
    {
        foreach (var profile in Profiles.Where(p => p.IsPrivacy))
            profile.IsEnabled = true;
    }

    [RelayCommand]
    private void DeselectPrivacyItems()
    {
        foreach (var profile in Profiles.Where(p => p.IsPrivacy))
            profile.IsEnabled = false;
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (AdminService.RestartAsAdmin())
            Application.Current.Shutdown();
    }

    #endregion

    #region Helper Methods

    private void UpdateRecycleBinStats()
    {
        var (size, count) = _systemCleaner.GetRecycleBinInfo();
        RecycleBinSize = size;
        RecycleBinCount = count;
        OnPropertyChanged(nameof(RecycleBinSizeFormatted));
    }

    private void ApplyFilter()
    {
        FilteredFiles = new ObservableCollection<TempFileInfo>(Files);
        TotalCount = FilteredFiles.Count;
        TotalSize = FilteredFiles.Sum(f => f.Size);
        OnPropertyChanged(nameof(TotalSizeFormatted));
    }

    private void UpdateProfileStats()
    {
        foreach (var profile in Profiles)
        {
            var profileFiles = Files.Where(f => f.Category == profile.Name).ToList();
            profile.FileCount = profileFiles.Count;
            profile.TotalSize = profileFiles.Sum(f => f.Size);
        }
    }

    public void UpdateSelectedStats()
    {
        var selected = Files.Where(f => f.IsSelected && f.IsAccessible);
        SelectedCount = selected.Count();
        SelectedSize = selected.Sum(f => f.Size);
        OnPropertyChanged(nameof(SelectedSizeFormatted));
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.NotifyCanExecuteChanged();
    }

    #endregion
}
