using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperManager.Models;
using WallpaperManager.Services;

namespace WallpaperManager.ViewModels;

public partial class MainViewModel
{
    // Collection spéciale "Favoris" (virtuelle, non stockée)
    private static readonly string FavoritesCollectionId = "__favorites__";
    
    private readonly Collection _favoritesCollection = new()
    {
        Id = FavoritesCollectionId,
        Name = "Favoris",
        Icon = "⭐"
    };
    
    [ObservableProperty]
    private ObservableCollection<Collection> _collections = [];
    
    [ObservableProperty]
    private Collection? _selectedCollection;
    
    [ObservableProperty]
    private ObservableCollection<Wallpaper> _collectionWallpapers = [];
    
    [ObservableProperty]
    private Wallpaper? _selectedCollectionWallpaper;
    
    /// <summary>
    /// Indique si la collection sélectionnée est la collection spéciale "Favoris"
    /// </summary>
    public bool IsSelectedCollectionFavorites => SelectedCollection?.Id == FavoritesCollectionId;
    
    /// <summary>
    /// Indique si la collection sélectionnée peut être modifiée
    /// </summary>
    public bool CanEditSelectedCollection => SelectedCollection != null && !IsSelectedCollectionFavorites;
    
    private void LoadCollections()
    {
        // Mettre à jour le compteur des favoris
        UpdateFavoritesCount();
        
        // Créer la liste avec Favoris en premier
        var allCollections = new List<Collection> { _favoritesCollection };
        allCollections.AddRange(SettingsService.Collections);
        
        Collections = new ObservableCollection<Collection>(allCollections);
    }
    
    private void UpdateFavoritesCount()
    {
        _favoritesCollection.WallpaperIds.Clear();
        foreach (var wallpaper in _allWallpapers.Where(w => w.IsFavorite))
        {
            _favoritesCollection.WallpaperIds.Add(wallpaper.Id);
        }
    }
    
    partial void OnSelectedCollectionChanged(Collection? oldValue, Collection? newValue)
    {
        // Désabonner de l'ancienne collection
        if (oldValue != null && oldValue.Id != FavoritesCollectionId)
        {
            oldValue.PropertyChanged -= OnCollectionPropertyChanged;
        }
        
        // S'abonner à la nouvelle collection (sauf Favoris)
        if (newValue != null && newValue.Id != FavoritesCollectionId)
        {
            newValue.PropertyChanged += OnCollectionPropertyChanged;
        }
        
        RefreshCollectionWallpapers();
        
        // Notifier les propriétés dépendantes
        OnPropertyChanged(nameof(IsSelectedCollectionFavorites));
        OnPropertyChanged(nameof(CanEditSelectedCollection));
    }
    
    private void OnCollectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Sauvegarder automatiquement les changements de nom ou d'icône
        if (e.PropertyName == nameof(Collection.Name) || e.PropertyName == nameof(Collection.Icon))
        {
            SettingsService.MarkDirty();
            SettingsService.Save();
            
            // Rafraîchir la liste pour mettre à jour l'affichage
            if (SelectedCollection != null)
            {
                var index = Collections.IndexOf(SelectedCollection);
                if (index >= 0)
                {
                    var temp = SelectedCollection;
                    Collections.RemoveAt(index);
                    Collections.Insert(index, temp);
                    SelectedCollection = temp;
                }
            }
        }
    }
    
    private void RefreshCollectionWallpapers()
    {
        if (SelectedCollection == null)
        {
            CollectionWallpapers.Clear();
            return;
        }
        
        List<Wallpaper> wallpapers;
        
        if (SelectedCollection.Id == FavoritesCollectionId)
        {
            // Collection Favoris : récupérer tous les favoris
            wallpapers = _allWallpapers.Where(w => w.IsFavorite).ToList();
        }
        else
        {
            // Collection normale
            wallpapers = SettingsService.GetWallpapersInCollection(SelectedCollection.Id);
        }
        
        CollectionWallpapers = new ObservableCollection<Wallpaper>(wallpapers);
    }
    
    /// <summary>
    /// Rafraîchit la collection Favoris (appelé quand un favori change)
    /// </summary>
    internal void RefreshFavoritesCollection()
    {
        UpdateFavoritesCount();
        
        // Rafraîchir l'affichage du compteur dans la liste
        var index = Collections.IndexOf(_favoritesCollection);
        if (index >= 0)
        {
            Collections.RemoveAt(index);
            Collections.Insert(index, _favoritesCollection);
        }
        
        // Rafraîchir le contenu si Favoris est sélectionné
        if (IsSelectedCollectionFavorites)
        {
            RefreshCollectionWallpapers();
        }
    }
    
    [RelayCommand]
    private void CreateCollection()
    {
        var collection = new Collection
        {
            Name = $"Collection {Collections.Count + 1}",
            Icon = GetNextIcon()
        };
        
        SettingsService.AddCollection(collection);
        Collections.Add(collection);
        SelectedCollection = collection;
        SettingsService.Save();
        
        StatusMessage = $"Collection '{collection.Name}' créée";
    }
    
    private string GetNextIcon()
    {
        var icons = new[] { "📁", "🎨", "🌙", "☀️", "🎮", "🏞️", "🌊", "🔥", "❄️", "🌸", "🍂", "⭐" };
        return icons[Collections.Count % icons.Length];
    }
    
    [RelayCommand]
    private void DeleteCollection()
    {
        if (SelectedCollection == null) return;
        
        // Empêcher la suppression de la collection Favoris
        if (IsSelectedCollectionFavorites)
        {
            StatusMessage = "La collection Favoris ne peut pas être supprimée";
            return;
        }
        
        var name = SelectedCollection.Name;
        SettingsService.RemoveCollection(SelectedCollection.Id);
        Collections.Remove(SelectedCollection);
        SelectedCollection = null;
        CollectionWallpapers.Clear();
        SettingsService.Save();
        
        StatusMessage = $"Collection '{name}' supprimée";
    }
    
    [RelayCommand]
    private void RenameCollection(string? newName)
    {
        if (SelectedCollection == null || string.IsNullOrWhiteSpace(newName)) return;
        
        SelectedCollection.Name = newName.Trim();
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        // Forcer le rafraîchissement de l'UI
        var index = Collections.IndexOf(SelectedCollection);
        if (index >= 0)
        {
            var temp = SelectedCollection;
            Collections.RemoveAt(index);
            Collections.Insert(index, temp);
            SelectedCollection = temp;
        }
        
        StatusMessage = $"Collection renommée en '{newName}'";
    }
    
    [RelayCommand]
    private void SetCollectionIcon(string? icon)
    {
        if (SelectedCollection == null || string.IsNullOrEmpty(icon)) return;
        
        SelectedCollection.Icon = icon;
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        // Forcer le rafraîchissement
        var index = Collections.IndexOf(SelectedCollection);
        if (index >= 0)
        {
            var temp = SelectedCollection;
            Collections.RemoveAt(index);
            Collections.Insert(index, temp);
            SelectedCollection = temp;
        }
    }
    
    [RelayCommand]
    private void AddToCollection(Collection? collection)
    {
        if (collection == null) return;
        
        var wallpapersToAdd = SelectedWallpapers.Count > 0 
            ? SelectedWallpapers.ToList() 
            : (SelectedWallpaper != null ? [SelectedWallpaper] : []);
        
        if (wallpapersToAdd.Count == 0) return;
        
        var addedCount = 0;
        foreach (var wallpaper in wallpapersToAdd)
        {
            if (!collection.WallpaperIds.Contains(wallpaper.Id))
            {
                SettingsService.AddWallpaperToCollection(collection.Id, wallpaper.Id);
                addedCount++;
            }
        }
        
        SettingsService.Save();
        
        // Rafraîchir si c'est la collection sélectionnée
        if (SelectedCollection?.Id == collection.Id)
            RefreshCollectionWallpapers();
        
        // Rafraîchir le compteur dans la liste
        var index = Collections.IndexOf(collection);
        if (index >= 0)
        {
            Collections.RemoveAt(index);
            Collections.Insert(index, collection);
        }
        
        StatusMessage = addedCount > 0 
            ? $"{addedCount} fond(s) ajouté(s) à '{collection.Name}'"
            : "Les fonds d'écran sont déjà dans cette collection";
    }
    
    [RelayCommand]
    private void RemoveFromCollection(Wallpaper? wallpaper)
    {
        if (SelectedCollection == null || wallpaper == null) return;
        
        if (IsSelectedCollectionFavorites)
        {
            // Dans la collection Favoris, retirer = défavoriser
            wallpaper.IsFavorite = false;
            SettingsService.MarkDirty();
            SettingsService.Save();
            
            // Mettre à jour la collection Favoris
            RefreshFavoritesCollection();
            
            // Mettre à jour l'affichage dans la bibliothèque
            ApplyFiltersAndSort();
            
            StatusMessage = $"'{wallpaper.DisplayName}' retiré des favoris";
        }
        else
        {
            SettingsService.RemoveWallpaperFromCollection(SelectedCollection.Id, wallpaper.Id);
            CollectionWallpapers.Remove(wallpaper);
            SettingsService.Save();
            
            // Rafraîchir le compteur
            var index = Collections.IndexOf(SelectedCollection);
            if (index >= 0)
            {
                var temp = SelectedCollection;
                Collections.RemoveAt(index);
                Collections.Insert(index, temp);
                SelectedCollection = temp;
            }
            
            StatusMessage = $"'{wallpaper.DisplayName}' retiré de la collection";
        }
    }
    
    [RelayCommand]
    private void ApplyCollectionWallpaper()
    {
        if (SelectedCollectionWallpaper == null) return;
        ApplyWallpaperDirect(SelectedCollectionWallpaper);
    }
    
    [RelayCommand]
    private void PreviewCollectionWallpaper(Wallpaper? wallpaper)
    {
        var target = wallpaper ?? SelectedCollectionWallpaper;
        if (target == null) return;
        
        var index = CollectionWallpapers.IndexOf(target);
        var previewWindow = new Views.PreviewWindow(CollectionWallpapers, index >= 0 ? index : 0);
        previewWindow.ApplyRequested += (s, w) => ApplyWallpaperDirect(w);
        previewWindow.ShowDialog();
    }
    
    [RelayCommand]
    private void StartCollectionRotation()
    {
        if (SelectedCollection == null || !App.IsInitialized) return;
        
        // Récupérer les wallpapers selon le type de collection
        List<Wallpaper> wallpapers;
        if (SelectedCollection.Id == FavoritesCollectionId)
        {
            // Collection Favoris : récupérer tous les favoris
            wallpapers = _allWallpapers.Where(w => w.IsFavorite).ToList();
        }
        else
        {
            // Collection normale
            wallpapers = SettingsService.GetWallpapersInCollection(SelectedCollection.Id);
        }
        
        if (wallpapers.Count == 0)
        {
            StatusMessage = "La collection est vide";
            return;
        }
        
        // Désactiver le wallpaper dynamique
        App.DynamicService.Stop();
        
        // Configurer la rotation avec cette collection uniquement
        App.RotationService.SetPlaylist(wallpapers);
        App.RotationService.Start();
        
        IsRotationEnabled = true;
        StatusMessage = $"Rotation démarrée avec '{SelectedCollection.Name}' ({wallpapers.Count} fonds)";
    }
}
