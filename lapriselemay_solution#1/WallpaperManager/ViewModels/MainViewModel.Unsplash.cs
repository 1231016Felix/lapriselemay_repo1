using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperManager.Models;
using WallpaperManager.Services;

namespace WallpaperManager.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task SearchUnsplash()
    {
        if (string.IsNullOrWhiteSpace(UnsplashApiKey))
        {
            StatusMessage = "Veuillez configurer votre clé API Unsplash";
            return;
        }
        
        IsLoading = true;
        StatusMessage = "Recherche sur Unsplash...";
        
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        
        try
        {
            var query = string.IsNullOrWhiteSpace(UnsplashSearchQuery) 
                ? SettingsService.Current.UnsplashDefaultQuery 
                : UnsplashSearchQuery;
            
            var photos = await _unsplashService.SearchPhotosAsync(query, cancellationToken: _cts.Token);
            
            UnsplashPhotos.Clear();
            foreach (var photo in photos)
            {
                UnsplashPhotos.Add(photo);
            }
            
            StatusMessage = $"{photos.Count} résultat(s) trouvé(s)";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Recherche annulée";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task LoadRandomUnsplash()
    {
        if (string.IsNullOrWhiteSpace(UnsplashApiKey))
        {
            StatusMessage = "Veuillez configurer votre clé API Unsplash";
            return;
        }
        
        IsLoading = true;
        StatusMessage = "Chargement de photos aléatoires...";
        
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        
        try
        {
            var photos = await _unsplashService.GetRandomPhotosAsync(
                10, 
                string.IsNullOrWhiteSpace(UnsplashSearchQuery) ? null : UnsplashSearchQuery,
                _cts.Token);
            
            UnsplashPhotos.Clear();
            foreach (var photo in photos)
            {
                UnsplashPhotos.Add(photo);
            }
            
            StatusMessage = $"{photos.Count} photo(s) chargée(s)";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Chargement annulé";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task DownloadUnsplashPhoto(UnsplashPhoto? photo)
    {
        if (photo == null) return;
        
        IsLoading = true;
        StatusMessage = "Téléchargement en cours...";
        
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        
        try
        {
            var progress = new Progress<int>(p => StatusMessage = $"Téléchargement: {p}%");
            var localPath = await _unsplashService.DownloadPhotoAsync(photo, progress, _cts.Token);
            
            if (localPath != null)
            {
                // Générer le thumbnail AVANT d'ajouter à la collection
                StatusMessage = "Génération de la miniature...";
                await ThumbnailService.Instance.GetThumbnailAsync(localPath);
                
                var wallpaper = UnsplashService.CreateWallpaperFromPhoto(photo, localPath);
                SettingsService.AddWallpaper(wallpaper);
                Wallpapers.Add(wallpaper);
                SettingsService.Save();
                StatusMessage = "Photo téléchargée et ajoutée à la collection";
            }
            else
            {
                StatusMessage = "Erreur lors du téléchargement";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Téléchargement annulé";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task DownloadAndApplyUnsplashPhoto(UnsplashPhoto? photo)
    {
        if (photo == null) return;
        
        await DownloadUnsplashPhoto(photo);
        
        var wallpaper = Wallpapers.LastOrDefault();
        if (wallpaper != null)
        {
            App.RotationService.ApplyWallpaper(wallpaper);
            StatusMessage = "Photo téléchargée et appliquée";
        }
    }
    
    partial void OnUnsplashApiKeyChanged(string value)
    {
        SettingsService.Current.UnsplashApiKey = value;
        SettingsService.Save();
    }
    
    partial void OnStartWithWindowsChanged(bool value)
    {
        SettingsService.Current.StartWithWindows = value;
        SetStartup(value);
        SettingsService.Save();
    }
    
    partial void OnMinimizeToTrayChanged(bool value)
    {
        SettingsService.Current.MinimizeToTray = value;
        SettingsService.Save();
    }
    
    private static void SetStartup(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            
            if (key != null)
            {
                if (enable)
                {
                    var exePath = Environment.ProcessPath;
                    if (exePath != null)
                        key.SetValue("WallpaperManager", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("WallpaperManager", false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur startup: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private void CreateCollection()
    {
        var collection = new WallpaperCollection
        {
            Name = $"Collection {Collections.Count + 1}"
        };
        SettingsService.AddCollection(collection);
        Collections.Add(collection);
        SelectedCollection = collection;
        SettingsService.Save();
    }
    
    [RelayCommand]
    private void DeleteCollection()
    {
        if (SelectedCollection == null) return;
        
        SettingsService.RemoveCollection(SelectedCollection.Id);
        Collections.Remove(SelectedCollection);
        SelectedCollection = null;
        SettingsService.Save();
    }
    
    [RelayCommand]
    private void AddToCollection()
    {
        if (SelectedWallpaper == null || SelectedCollection == null) return;
        
        if (!SelectedCollection.WallpaperIds.Contains(SelectedWallpaper.Id))
        {
            SelectedCollection.WallpaperIds.Add(SelectedWallpaper.Id);
            SettingsService.MarkDirty();
            SettingsService.Save();
            StatusMessage = "Fond d'écran ajouté à la collection";
        }
    }
    
    [RelayCommand]
    private void SetActiveCollection()
    {
        if (SelectedCollection == null) return;
        
        SettingsService.Current.ActiveCollectionId = SelectedCollection.Id;
        App.RotationService.RefreshPlaylist();
        SettingsService.Save();
        StatusMessage = $"Collection '{SelectedCollection.Name}' activée pour la rotation";
    }
    
    [RelayCommand]
    private void StopAnimatedWallpaper()
    {
        App.AnimatedService.Stop();
        // Libère LibVLC pour économiser ~50-100 MB de RAM
        App.AnimatedService.ReleaseLibVLC();
        StatusMessage = "Fond d'écran animé arrêté (mémoire libérée)";
    }
    
    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedWallpaper == null) return;
        
        SelectedWallpaper.IsFavorite = !SelectedWallpaper.IsFavorite;
        SettingsService.MarkDirty();
        SettingsService.Save();
    }
}
