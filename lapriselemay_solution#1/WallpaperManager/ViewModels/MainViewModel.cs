using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperManager.Models;
using WallpaperManager.Services;

namespace WallpaperManager.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly UnsplashService _unsplashService;
    private CancellationTokenSource? _cts;
    private readonly Lock _ctsLock = new();
    private volatile bool _disposed;
    
    // Collections source
    private ObservableCollection<Wallpaper> _allWallpapers = [];
    
    [ObservableProperty]
    private ObservableCollection<Wallpaper> _wallpapers = [];
    
    [ObservableProperty]
    private ObservableCollection<UnsplashPhoto> _unsplashPhotos = [];
    
    [ObservableProperty]
    private ObservableCollection<DynamicWallpaper> _dynamicWallpapers = [];
    
    [ObservableProperty]
    private Wallpaper? _selectedWallpaper;
    
    [ObservableProperty]
    private ObservableCollection<Wallpaper> _selectedWallpapers = [];
    
    [ObservableProperty]
    private DynamicWallpaper? _selectedDynamicWallpaper;
    
    // === FILTRES ET TRI ===
    [ObservableProperty]
    private string _searchQuery = string.Empty;
    
    [ObservableProperty]
    private string _filterType = "Tous"; // Tous, Statique, Animé, Vidéo
    
    [ObservableProperty]
    private bool _filterFavoritesOnly;
    
    [ObservableProperty]
    private string _sortBy = "Date"; // Date, Nom, Taille, Résolution
    
    [ObservableProperty]
    private bool _sortDescending = true;
    
    public string[] FilterTypes { get; } = ["Tous", "Statique", "Animé", "Vidéo"];
    public string[] SortOptions { get; } = ["Date", "Nom", "Taille", "Résolution"];
    
    // === AUTRES PROPRIÉTÉS ===
    [ObservableProperty]
    private string _unsplashSearchQuery = string.Empty;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationStatusText))]
    private bool _isRotationEnabled;
    
    [ObservableProperty]
    private int _rotationInterval = 30;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _statusMessage = string.Empty;
    
    [ObservableProperty]
    private ObservableCollection<DuplicateGroup> _duplicateGroups = [];
    
    [ObservableProperty]
    private bool _isDuplicateScanRunning;
    
    [ObservableProperty]
    private int _selectedTabIndex;
    
    [ObservableProperty]
    private string _unsplashApiKey = string.Empty;
    
    [ObservableProperty]
    private bool _startWithWindows;
    
    [ObservableProperty]
    private bool _minimizeToTray = true;
    
    [ObservableProperty]
    private bool _pauseOnBattery = true;
    
    [ObservableProperty]
    private bool _pauseOnFullscreen = true;
    
    public string RotationStatusText => IsRotationEnabled ? "Active" : "Desactive";
    
    public int SelectedCount => SelectedWallpapers.Count;
    public int TotalCount => _allWallpapers.Count;
    public int FilteredCount => Wallpapers.Count;

    public MainViewModel()
    {
        _unsplashService = new UnsplashService();
        SelectedWallpapers.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SelectedCount));
        LoadData();
    }
    
    private void LoadData()
    {
        _allWallpapers = new ObservableCollection<Wallpaper>(SettingsService.Wallpapers);
        DynamicWallpapers = new ObservableCollection<DynamicWallpaper>(SettingsService.DynamicWallpapers);
        LoadCollections();
        
        ApplyFiltersAndSort();
        
        IsRotationEnabled = SettingsService.Current.RotationEnabled;
        RotationInterval = SettingsService.Current.RotationIntervalMinutes;
        UnsplashApiKey = SettingsService.Current.UnsplashApiKey ?? string.Empty;
        StartWithWindows = SettingsService.Current.StartWithWindows;
        MinimizeToTray = SettingsService.Current.MinimizeToTray;
        PauseOnBattery = SettingsService.Current.PauseOnBattery;
        PauseOnFullscreen = SettingsService.Current.PauseOnFullscreen;
    }
    
    // === MÉTHODES DE FILTRE ET TRI ===
    partial void OnSearchQueryChanged(string value) => ApplyFiltersAndSort();
    partial void OnFilterTypeChanged(string value) => ApplyFiltersAndSort();
    partial void OnFilterFavoritesOnlyChanged(bool value) => ApplyFiltersAndSort();
    partial void OnSortByChanged(string value) => ApplyFiltersAndSort();
    partial void OnSortDescendingChanged(bool value) => ApplyFiltersAndSort();
    
    private void ApplyFiltersAndSort()
    {
        var filtered = _allWallpapers.AsEnumerable();
        
        // Filtre par recherche
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var query = SearchQuery.ToLowerInvariant();
            filtered = filtered.Where(w => 
                w.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                w.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        
        // Filtre par type
        filtered = FilterType switch
        {
            "Statique" => filtered.Where(w => w.Type == WallpaperType.Static),
            "Animé" => filtered.Where(w => w.Type == WallpaperType.Animated),
            "Vidéo" => filtered.Where(w => w.Type == WallpaperType.Video),
            _ => filtered
        };
        
        // Filtre favoris
        if (FilterFavoritesOnly)
            filtered = filtered.Where(w => w.IsFavorite);
        
        // Tri
        filtered = SortBy switch
        {
            "Nom" => SortDescending 
                ? filtered.OrderByDescending(w => w.DisplayName) 
                : filtered.OrderBy(w => w.DisplayName),
            "Taille" => SortDescending 
                ? filtered.OrderByDescending(w => w.FileSize) 
                : filtered.OrderBy(w => w.FileSize),
            "Résolution" => SortDescending 
                ? filtered.OrderByDescending(w => w.Width * w.Height) 
                : filtered.OrderBy(w => w.Width * w.Height),
            _ => SortDescending 
                ? filtered.OrderByDescending(w => w.AddedDate) 
                : filtered.OrderBy(w => w.AddedDate)
        };
        
        Wallpapers = new ObservableCollection<Wallpaper>(filtered);
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(TotalCount));
    }
    
    [RelayCommand]
    private void ClearFilters()
    {
        SearchQuery = string.Empty;
        FilterType = "Tous";
        FilterFavoritesOnly = false;
        SortBy = "Date";
        SortDescending = true;
    }
    
    // === MULTI-SÉLECTION ===
    public void UpdateSelection(System.Collections.IList selectedItems)
    {
        SelectedWallpapers.Clear();
        foreach (var item in selectedItems)
        {
            if (item is Wallpaper w)
                SelectedWallpapers.Add(w);
        }
        OnPropertyChanged(nameof(SelectedCount));
    }
    
    [RelayCommand]
    private void SelectAll()
    {
        SelectedWallpapers.Clear();
        foreach (var w in Wallpapers)
            SelectedWallpapers.Add(w);
        OnPropertyChanged(nameof(SelectedCount));
    }
    
    [RelayCommand]
    private void DeselectAll()
    {
        SelectedWallpapers.Clear();
        OnPropertyChanged(nameof(SelectedCount));
    }
    
    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedWallpapers.Count == 0) return;
        
        var toRemove = SelectedWallpapers.ToList();
        var count = toRemove.Count;
        
        foreach (var wallpaper in toRemove)
        {
            SettingsService.RemoveWallpaper(wallpaper.Id);
            _allWallpapers.Remove(wallpaper);
            Wallpapers.Remove(wallpaper);
        }
        
        SelectedWallpapers.Clear();
        SelectedWallpaper = null;
        SettingsService.Save();
        
        StatusMessage = $"{count} fond(s) d'écran supprimé(s)";
        
        if (App.IsInitialized)
            App.RotationService.RefreshPlaylist();
        
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
    }
    
    [RelayCommand]
    private void ToggleFavoriteSelected()
    {
        if (SelectedWallpapers.Count == 0) return;
        
        // Si au moins un n'est pas favori, on les met tous en favoris
        var shouldFavorite = SelectedWallpapers.Any(w => !w.IsFavorite);
        
        foreach (var wallpaper in SelectedWallpapers)
        {
            wallpaper.IsFavorite = shouldFavorite;
        }
        
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        // Rafraîchir la collection Favoris
        RefreshFavoritesCollection();
        
        StatusMessage = shouldFavorite 
            ? $"{SelectedWallpapers.Count} ajouté(s) aux favoris"
            : $"{SelectedWallpapers.Count} retiré(s) des favoris";
        
        ApplyFiltersAndSort();
    }
    
    // === PRÉVISUALISATION ===
    [RelayCommand]
    private void PreviewWallpaper(Wallpaper? wallpaper)
    {
        var target = wallpaper ?? SelectedWallpaper;
        if (target == null) return;
        
        var index = Wallpapers.IndexOf(target);
        var previewWindow = new Views.PreviewWindow(Wallpapers, index >= 0 ? index : 0);
        previewWindow.ApplyRequested += (s, w) => ApplyWallpaperDirect(w);
        previewWindow.ShowDialog();
    }
    
    private void ApplyWallpaperDirect(Wallpaper wallpaper)
    {
        if (!App.IsInitialized) return;
        
        if (wallpaper.Type == WallpaperType.Static)
        {
            App.RotationService.ApplyWallpaper(wallpaper);
            StatusMessage = $"Fond d'écran appliqué: {wallpaper.DisplayName}";
        }
        else
        {
            App.AnimatedService.Play(wallpaper);
            StatusMessage = "Fond d'écran animé démarré";
        }
    }
    
    // === DRAG & DROP ===
    public async Task HandleDroppedFilesAsync(string[] files)
    {
        if (files == null || files.Length == 0) return;
        
        IsLoading = true;
        StatusMessage = "Ajout des fichiers...";
        
        var cancellationToken = ResetCancellationToken();
        var addedCount = 0;
        
        try
        {
            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".mp4", ".webm", ".avi", ".mkv" };
            
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!validExtensions.Contains(ext)) continue;
                
                var wallpaper = await CreateWallpaperFromFileAsync(file).ConfigureAwait(true);
                if (wallpaper != null)
                {
                    SettingsService.AddWallpaper(wallpaper);
                    _allWallpapers.Add(wallpaper);
                    addedCount++;
                }
            }
            
            if (addedCount > 0)
            {
                ApplyFiltersAndSort();
                SettingsService.Save();
                
                if (App.IsInitialized)
                    App.RotationService.RefreshPlaylist();
            }
            
            StatusMessage = addedCount > 0 
                ? $"{addedCount} fond(s) d'écran ajouté(s) par glisser-déposer"
                : "Aucun fichier valide trouvé";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Opération annulée";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    // === WALLPAPERS DYNAMIQUES ===
    [RelayCommand]
    private void CreateDynamicWallpaper() => CreateDynamicWithPreset(4);
    
    [RelayCommand]
    private void CreateDynamic2Periods() => CreateDynamicWithPreset(2);
    
    [RelayCommand]
    private void CreateDynamic4Periods() => CreateDynamicWithPreset(4);
    
    [RelayCommand]
    private void CreateDynamic6Periods() => CreateDynamicWithPreset(6);
    
    private void CreateDynamicWithPreset(int periodCount)
    {
        var dynamic = new DynamicWallpaper
        {
            Name = $"Wallpaper Dynamique {DynamicWallpapers.Count + 1}"
        };
        
        var presets = periodCount switch
        {
            2 => TimePresets.TwoPeriods,
            6 => TimePresets.SixPeriods,
            _ => TimePresets.FourPeriods
        };
        
        foreach (var (label, time) in presets)
        {
            dynamic.Variants.Add(new TimeVariant
            {
                StartTime = time,
                Label = label
            });
        }
        
        SettingsService.AddDynamicWallpaper(dynamic);
        DynamicWallpapers.Add(dynamic);
        SelectedDynamicWallpaper = dynamic;
        SettingsService.Save();
        
        StatusMessage = $"Wallpaper dynamique créé ({periodCount} périodes) - Configurez les images";
    }
    
    [RelayCommand]
    private void DeleteDynamicWallpaper()
    {
        if (SelectedDynamicWallpaper == null) return;
        
        // Désactiver si c'est le wallpaper actif
        if (App.IsInitialized && App.DynamicService.ActiveWallpaper?.Id == SelectedDynamicWallpaper.Id)
            App.DynamicService.Stop();
        
        SettingsService.RemoveDynamicWallpaper(SelectedDynamicWallpaper.Id);
        DynamicWallpapers.Remove(SelectedDynamicWallpaper);
        SelectedDynamicWallpaper = null;
        SettingsService.Save();
        
        StatusMessage = "Wallpaper dynamique supprimé";
    }
    
    [RelayCommand]
    private void ActivateDynamicWallpaper()
    {
        if (SelectedDynamicWallpaper == null || !App.IsInitialized) return;
        
        // Vérifier qu'au moins une variante a un fichier
        if (!SelectedDynamicWallpaper.Variants.Any(v => v.Exists))
        {
            StatusMessage = "Configurez au moins une image avant d'activer";
            return;
        }
        
        // Désactiver la rotation normale
        if (IsRotationEnabled)
        {
            IsRotationEnabled = false;
        }
        
        App.DynamicService.Activate(SelectedDynamicWallpaper);
        StatusMessage = $"Wallpaper dynamique '{SelectedDynamicWallpaper.Name}' activé";
    }
    
    [RelayCommand]
    private void DeactivateDynamicWallpaper()
    {
        if (!App.IsInitialized) return;
        
        App.DynamicService.Stop();
        StatusMessage = "Wallpaper dynamique désactivé";
    }
    
    [RelayCommand]
    private async Task SetVariantImageAsync(TimeVariant? variant)
    {
        if (variant == null || SelectedDynamicWallpaper == null) return;
        
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp",
            Title = $"Sélectionner l'image pour {variant.Label ?? variant.StartTimeFormatted}"
        };
        
        if (dialog.ShowDialog() != true) return;
        
        // Trouver l'index et créer une nouvelle variante pour forcer le rafraîchissement
        var index = SelectedDynamicWallpaper.Variants.IndexOf(variant);
        if (index >= 0)
        {
            var newVariant = new TimeVariant
            {
                Id = variant.Id,
                StartTime = variant.StartTime,
                Label = variant.Label,
                FilePath = dialog.FileName
            };
            SelectedDynamicWallpaper.Variants[index] = newVariant;
        }
        
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        // Rafraîchir si c'est le wallpaper actif
        if (App.IsInitialized && App.DynamicService.ActiveWallpaper?.Id == SelectedDynamicWallpaper?.Id)
        {
            App.DynamicService.Refresh();
        }
        
        StatusMessage = $"Image configurée pour {variant.Label ?? variant.StartTimeFormatted}";
    }
    
    // === MÉTHODES EXISTANTES ===
    private CancellationToken ResetCancellationToken()
    {
        lock (_ctsLock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }
    
    [RelayCommand]
    private async Task AddWallpapersAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Videos|*.mp4;*.webm;*.avi|Tous|*.*",
            Title = "Sélectionner des fonds d'écran"
        };
        
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
            return;
        
        await HandleDroppedFilesAsync(dialog.FileNames);
    }
    
    internal static async Task<Wallpaper?> CreateWallpaperFromFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(filePath))
                return null;
            
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var type = extension switch
            {
                ".gif" => WallpaperType.Animated,
                ".mp4" or ".webm" or ".avi" or ".mkv" => WallpaperType.Video,
                _ => WallpaperType.Static
            };
            
            // Copier le fichier vers le dossier WallpaperManager si nécessaire
            var targetFolder = SettingsService.Current.WallpaperFolder;
            var finalPath = filePath;
            
            // Vérifier si le fichier n'est pas déjà dans le dossier cible
            if (!filePath.StartsWith(targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // Créer le dossier si nécessaire
                    if (!Directory.Exists(targetFolder))
                        Directory.CreateDirectory(targetFolder);
                    
                    var fileName = Path.GetFileName(filePath);
                    var targetPath = Path.Combine(targetFolder, fileName);
                    
                    // Gérer les conflits de nom
                    if (File.Exists(targetPath))
                    {
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        var counter = 1;
                        do
                        {
                            targetPath = Path.Combine(targetFolder, $"{nameWithoutExt}_{counter}{extension}");
                            counter++;
                        } while (File.Exists(targetPath));
                    }
                    
                    // Copier le fichier
                    File.Copy(filePath, targetPath, overwrite: false);
                    finalPath = targetPath;
                    
                    System.Diagnostics.Debug.WriteLine($"Fichier copié: {filePath} -> {targetPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur copie fichier: {ex.Message}");
                    // En cas d'erreur, utiliser le chemin original
                    finalPath = filePath;
                }
            }
            
            var fileInfo = new FileInfo(finalPath);
            var wallpaper = new Wallpaper
            {
                FilePath = finalPath,
                Name = Path.GetFileNameWithoutExtension(finalPath),
                Type = type,
                FileSize = fileInfo.Length
            };
            
            if (type == WallpaperType.Static)
            {
                try
                {
                    using var stream = File.OpenRead(finalPath);
                    var decoder = BitmapDecoder.Create(
                        stream, 
                        BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation,
                        BitmapCacheOption.None);
                    
                    if (decoder.Frames.Count > 0)
                    {
                        wallpaper.Width = decoder.Frames[0].PixelWidth;
                        wallpaper.Height = decoder.Frames[0].PixelHeight;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur lecture dimensions: {ex.Message}");
                }
            }
            
            return wallpaper;
        }).ConfigureAwait(false);
    }
    
    [RelayCommand]
    private void OpenInExplorer(Wallpaper? wallpaper)
    {
        var target = wallpaper ?? SelectedWallpaper;
        if (target == null || string.IsNullOrEmpty(target.FilePath)) return;
        
        if (!File.Exists(target.FilePath))
        {
            StatusMessage = "Fichier introuvable";
            return;
        }
        
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target.FilePath}\"");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void RemoveWallpaper()
    {
        if (SelectedWallpaper == null) return;
        
        SettingsService.RemoveWallpaper(SelectedWallpaper.Id);
        _allWallpapers.Remove(SelectedWallpaper);
        Wallpapers.Remove(SelectedWallpaper);
        SelectedWallpaper = null;
        
        SettingsService.Save();
        StatusMessage = "Fond d'écran supprimé";
        
        if (App.IsInitialized)
            App.RotationService.RefreshPlaylist();
        
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
    }
    
    [RelayCommand]
    private void ApplyWallpaper()
    {
        if (SelectedWallpaper == null) return;
        ApplyWallpaperDirect(SelectedWallpaper);
    }
    
    [RelayCommand]
    private void NextWallpaper()
    {
        if (!App.IsInitialized)
        {
            StatusMessage = "Service non initialisé";
            return;
        }
        
        App.RotationService.RefreshPlaylist();
        
        if (_allWallpapers.Count == 0)
        {
            StatusMessage = "Ajoutez des fonds d'écran d'abord";
            return;
        }
        
        App.RotationService.Next();
        StatusMessage = "Fond d'écran suivant appliqué";
    }
    
    [RelayCommand]
    private void PreviousWallpaper()
    {
        if (!App.IsInitialized)
        {
            StatusMessage = "Service non initialisé";
            return;
        }
        
        App.RotationService.RefreshPlaylist();
        
        if (_allWallpapers.Count == 0)
        {
            StatusMessage = "Ajoutez des fonds d'écran d'abord";
            return;
        }
        
        App.RotationService.Previous();
        StatusMessage = "Fond d'écran précédent appliqué";
    }
    
    partial void OnRotationIntervalChanged(int value)
    {
        SettingsService.Current.RotationIntervalMinutes = value;
        if (App.IsInitialized)
            App.RotationService.SetInterval(value);
        SettingsService.Save();
    }
    
    partial void OnIsRotationEnabledChanged(bool value)
    {
        SettingsService.Current.RotationEnabled = value;
        
        if (App.IsInitialized)
        {
            if (value)
            {
                // Désactiver le wallpaper dynamique si actif
                App.DynamicService.Stop();
                
                App.RotationService.RefreshPlaylist();
                App.RotationService.Start();
                StatusMessage = "Rotation automatique activée";
            }
            else
            {
                App.RotationService.Stop();
                StatusMessage = "Rotation automatique désactivée";
            }
        }
        
        SettingsService.Save();
    }
    
    partial void OnPauseOnBatteryChanged(bool value)
    {
        SettingsService.Current.PauseOnBattery = value;
        SettingsService.Save();
    }
    
    partial void OnPauseOnFullscreenChanged(bool value)
    {
        SettingsService.Current.PauseOnFullscreen = value;
        SettingsService.Save();
    }
    
    [RelayCommand]
    private async Task ScanDuplicatesAsync()
    {
        if (IsDuplicateScanRunning) return;
        
        IsDuplicateScanRunning = true;
        DuplicateGroups.Clear();
        
        var cancellationToken = ResetCancellationToken();
        
        var progress = new Progress<(int current, int total, string status)>(p =>
        {
            StatusMessage = p.status;
        });
        
        try
        {
            var groups = await DuplicateDetectionService.FindDuplicatesAsync(
                _allWallpapers, 
                progress, 
                cancellationToken).ConfigureAwait(true);
            
            DuplicateGroups = new ObservableCollection<DuplicateGroup>(groups);
            
            if (groups.Count == 0)
            {
                StatusMessage = "Aucun doublon trouvé";
            }
            else
            {
                var totalDuplicates = groups.Sum(g => g.Wallpapers.Count - 1);
                var recoverableSpace = DuplicateDetectionService.CalculateRecoverableSpace(groups);
                StatusMessage = $"{totalDuplicates} doublon(s) trouvé(s) - {DuplicateDetectionService.FormatSize(recoverableSpace)} récupérables";
            }
            
            SettingsService.Save();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Analyse annulée";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
        }
        finally
        {
            IsDuplicateScanRunning = false;
        }
    }
    
    [RelayCommand]
    private void RemoveDuplicate(Wallpaper? wallpaper)
    {
        if (wallpaper == null) return;
        
        SettingsService.RemoveWallpaper(wallpaper.Id);
        _allWallpapers.Remove(wallpaper);
        Wallpapers.Remove(wallpaper);
        
        foreach (var group in DuplicateGroups.ToList())
        {
            group.Wallpapers.Remove(wallpaper);
            if (group.Wallpapers.Count <= 1)
                DuplicateGroups.Remove(group);
        }
        
        SettingsService.Save();
        
        var remaining = DuplicateGroups.Sum(g => g.Wallpapers.Count - 1);
        StatusMessage = remaining > 0 
            ? $"Doublon retiré - {remaining} restant(s)" 
            : "Tous les doublons ont été traités";
        
        if (App.IsInitialized)
            App.RotationService.RefreshPlaylist();
        
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
    }
    
    [RelayCommand]
    private void ClearThumbnailCache()
    {
        ThumbnailService.Instance.ClearAllCache();
        StatusMessage = "Cache des miniatures vidé";
        ApplyFiltersAndSort();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        lock (_ctsLock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        
        _unsplashService.Dispose();
    }
}
