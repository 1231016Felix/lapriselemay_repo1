using System.Collections.ObjectModel;
using System.IO;
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
    private bool _disposed;
    
    [ObservableProperty]
    private ObservableCollection<Wallpaper> _wallpapers = [];
    
    [ObservableProperty]
    private ObservableCollection<WallpaperCollection> _collections = [];
    
    [ObservableProperty]
    private ObservableCollection<UnsplashPhoto> _unsplashPhotos = [];
    
    [ObservableProperty]
    private Wallpaper? _selectedWallpaper;
    
    [ObservableProperty]
    private WallpaperCollection? _selectedCollection;
    
    [ObservableProperty]
    private string _searchQuery = string.Empty;
    
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
    private int _selectedTabIndex;
    
    [ObservableProperty]
    private string _unsplashApiKey = string.Empty;
    
    [ObservableProperty]
    private bool _startWithWindows;
    
    [ObservableProperty]
    private bool _minimizeToTray = true;
    
    public string RotationStatusText => IsRotationEnabled ? "Active" : "Desactive";

    public MainViewModel()
    {
        _unsplashService = new UnsplashService();
        LoadData();
    }
    
    private void LoadData()
    {
        Wallpapers = new ObservableCollection<Wallpaper>(SettingsService.Wallpapers);
        Collections = new ObservableCollection<WallpaperCollection>(SettingsService.Collections);
        
        IsRotationEnabled = SettingsService.Current.RotationEnabled;
        RotationInterval = SettingsService.Current.RotationIntervalMinutes;
        UnsplashApiKey = SettingsService.Current.UnsplashApiKey ?? string.Empty;
        StartWithWindows = SettingsService.Current.StartWithWindows;
        MinimizeToTray = SettingsService.Current.MinimizeToTray;
        
        System.Diagnostics.Debug.WriteLine($"LoadData: IsRotationEnabled={IsRotationEnabled}");
    }
    
    [RelayCommand]
    private async Task AddWallpapersAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Videos|*.mp4;*.webm;*.avi|Tous|*.*",
            Title = "Selectionner des fonds d'ecran"
        };
        
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
            return;
        
        IsLoading = true;
        StatusMessage = "Ajout des fonds d'ecran...";
        
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        
        try
        {
            var addedCount = 0;
            foreach (var file in dialog.FileNames)
            {
                if (_cts.Token.IsCancellationRequested)
                    break;
                    
                var wallpaper = await CreateWallpaperFromFileAsync(file);
                if (wallpaper != null)
                {
                    SettingsService.AddWallpaper(wallpaper);
                    App.Current.Dispatcher.Invoke(() => Wallpapers.Add(wallpaper));
                    addedCount++;
                }
            }
            
            SettingsService.Save();
            StatusMessage = $"{addedCount} fond(s) d'ecran ajoute(s)";
            
            // Rafraichir la playlist du service de rotation
            if (App.IsInitialized)
            {
                App.RotationService.RefreshPlaylist();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation annulee";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private static Task<Wallpaper?> CreateWallpaperFromFileAsync(string filePath)
    {
        return Task.Run(() =>
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
            
            var fileInfo = new FileInfo(filePath);
            var wallpaper = new Wallpaper
            {
                FilePath = filePath,
                Name = Path.GetFileNameWithoutExtension(filePath),
                Type = type,
                FileSize = fileInfo.Length
            };
            
            if (type == WallpaperType.Static)
            {
                try
                {
                    using var stream = File.OpenRead(filePath);
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
        });
    }
    
    [RelayCommand]
    private void RemoveWallpaper()
    {
        if (SelectedWallpaper == null) return;
        
        var id = SelectedWallpaper.Id;
        SettingsService.RemoveWallpaper(id);
        Wallpapers.Remove(SelectedWallpaper);
        SelectedWallpaper = null;
        
        SettingsService.Save();
        StatusMessage = "Fond d'ecran supprime";
        
        if (App.IsInitialized)
        {
            App.RotationService.RefreshPlaylist();
        }
    }
    
    [RelayCommand]
    private void ApplyWallpaper()
    {
        if (SelectedWallpaper == null) return;
        
        if (!App.IsInitialized)
        {
            StatusMessage = "Service non initialise";
            return;
        }
        
        if (SelectedWallpaper.Type == WallpaperType.Static)
        {
            App.RotationService.ApplyWallpaper(SelectedWallpaper);
            StatusMessage = $"Fond d'ecran applique: {SelectedWallpaper.DisplayName}";
        }
        else
        {
            App.AnimatedService.Play(SelectedWallpaper);
            StatusMessage = "Fond d'ecran anime demarre";
        }
    }
    
    [RelayCommand]
    private void NextWallpaper()
    {
        if (!App.IsInitialized)
        {
            StatusMessage = "Service non initialise";
            return;
        }
        
        App.RotationService.RefreshPlaylist();
        
        if (Wallpapers.Count == 0)
        {
            StatusMessage = "Ajoutez des fonds d'ecran d'abord";
            return;
        }
        
        App.RotationService.Next();
        StatusMessage = "Fond d'ecran suivant applique";
    }
    
    [RelayCommand]
    private void PreviousWallpaper()
    {
        if (!App.IsInitialized)
        {
            StatusMessage = "Service non initialise";
            return;
        }
        
        App.RotationService.RefreshPlaylist();
        
        if (Wallpapers.Count == 0)
        {
            StatusMessage = "Ajoutez des fonds d'ecran d'abord";
            return;
        }
        
        App.RotationService.Previous();
        StatusMessage = "Fond d'ecran precedent applique";
    }
    
    partial void OnRotationIntervalChanged(int value)
    {
        SettingsService.Current.RotationIntervalMinutes = value;
        if (App.IsInitialized)
        {
            App.RotationService.SetInterval(value);
        }
        SettingsService.Save();
    }
    
    partial void OnIsRotationEnabledChanged(bool value)
    {
        System.Diagnostics.Debug.WriteLine($"OnIsRotationEnabledChanged: {value}");
        
        SettingsService.Current.RotationEnabled = value;
        
        if (App.IsInitialized)
        {
            if (value)
            {
                App.RotationService.RefreshPlaylist();
                App.RotationService.Start();
                StatusMessage = "Rotation automatique activee";
            }
            else
            {
                App.RotationService.Stop();
                StatusMessage = "Rotation automatique desactivee";
            }
        }
        
        SettingsService.Save();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _cts?.Cancel();
        _cts?.Dispose();
        _unsplashService.Dispose();
    }
}
