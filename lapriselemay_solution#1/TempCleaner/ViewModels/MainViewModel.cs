using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempCleaner.Models;
using TempCleaner.Services;

namespace TempCleaner.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ScannerService _scannerService = new();
    private readonly CleanerService _cleanerService = new();
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private ObservableCollection<CleanerProfile> _profiles = [];

    [ObservableProperty]
    private ObservableCollection<TempFileInfo> _files = [];

    [ObservableProperty]
    private ObservableCollection<TempFileInfo> _filteredFiles = [];

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private string _statusMessage = "Prêt";

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private long _selectedSize;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _scanDuration = string.Empty;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private int _adminProfileCount;

    public string TotalSizeFormatted => FormatSize(TotalSize);
    public string SelectedSizeFormatted => FormatSize(SelectedSize);

    public bool IsWorking => IsScanning || IsCleaning;
    public bool CanClean => !IsScanning && !IsCleaning && SelectedCount > 0;
    public bool CanScan => !IsScanning && !IsCleaning;
    public bool CanCancel => IsScanning || IsCleaning;

    public MainViewModel()
    {
        IsAdmin = AdminService.IsRunningAsAdmin();
        LoadProfiles();
        AdminProfileCount = Profiles.Count(p => p.RequiresAdmin);
    }

    private void LoadProfiles()
    {
        Profiles = new ObservableCollection<CleanerProfile>(CleanerProfile.GetDefaultProfiles());
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (AdminService.RestartAsAdmin())
        {
            Application.Current.Shutdown();
        }
    }

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(IsWorking));
    partial void OnIsCleaningChanged(bool value) => OnPropertyChanged(nameof(IsWorking));

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(string message, int percent)>(p =>
            {
                StatusMessage = p.message;
                ProgressPercent = p.percent;
            });

            var result = await _scannerService.ScanAsync(
                Profiles,
                progress,
                _cancellationTokenSource.Token);

            Files = new ObservableCollection<TempFileInfo>(result.Files);
            TotalSize = result.TotalSize;
            TotalCount = result.TotalCount;
            ScanDuration = $"Durée: {result.ScanDuration.TotalSeconds:F1}s";

            ApplyFilter();
            UpdateSelectedStats();

            StatusMessage = $"Analyse terminée: {TotalCount} fichiers trouvés ({TotalSizeFormatted})";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Analyse annulée";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ProgressPercent = 0;
            NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selectedFiles = Files.Where(f => f.IsSelected && f.IsAccessible).ToList();

        var confirmResult = MessageBox.Show(
            $"Voulez-vous supprimer {selectedFiles.Count} fichiers ({FormatSize(selectedFiles.Sum(f => f.Size))}) ?\n\nCette action est irréversible.",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmResult != MessageBoxResult.Yes)
            return;

        IsCleaning = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(string message, int percent, long freedBytes)>(p =>
            {
                StatusMessage = p.message;
                ProgressPercent = p.percent;
            });

            var result = await _cleanerService.CleanAsync(
                selectedFiles,
                progress,
                _cancellationTokenSource.Token);

            // Retirer les fichiers supprimés de la liste
            var deletedPaths = selectedFiles
                .Where(f => !result.Errors.Any(e => e.FilePath == f.FullPath))
                .Select(f => f.FullPath)
                .ToHashSet();

            // Mettre à jour la collection principale
            var remainingFiles = Files.Where(f => !deletedPaths.Contains(f.FullPath)).ToList();
            Files = new ObservableCollection<TempFileInfo>(remainingFiles);

            // Mettre à jour les stats des profils
            UpdateProfileStats();

            ApplyFilter();
            UpdateSelectedStats();

            StatusMessage = $"Nettoyage terminé: {result.DeletedCount} fichiers supprimés ({result.FreedBytesFormatted} libérés)";

            if (result.FailedCount > 0)
            {
                MessageBox.Show(
                    $"{result.FailedCount} fichier(s) n'ont pas pu être supprimés.",
                    "Avertissement",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Nettoyage annulé";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsCleaning = false;
            ProgressPercent = 0;
            NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var file in FilteredFiles)
        {
            file.IsSelected = true;
        }
        UpdateSelectedStats();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var file in FilteredFiles)
        {
            file.IsSelected = false;
        }
        UpdateSelectedStats();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var file in FilteredFiles)
        {
            file.IsSelected = !file.IsSelected;
        }
        UpdateSelectedStats();
    }

    private void ApplyFilter()
    {
        // Afficher tous les fichiers (filtrage supprimé de l'interface)
        FilteredFiles = new ObservableCollection<TempFileInfo>(Files);
        TotalCount = FilteredFiles.Count;
        TotalSize = FilteredFiles.Sum(f => f.Size);
        OnPropertyChanged(nameof(TotalSizeFormatted));
    }

    private void UpdateProfileStats()
    {
        // Recalculer les stats de chaque profil basé sur les fichiers restants
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
        NotifyCanExecuteChanged();
    }

    private void NotifyCanExecuteChanged()
    {
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:N2} {suffixes[suffixIndex]}";
    }
}
