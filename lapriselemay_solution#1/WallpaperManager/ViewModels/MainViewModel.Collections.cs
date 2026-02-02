using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperManager.Models;
using WallpaperManager.Services;

namespace WallpaperManager.ViewModels;

public partial class MainViewModel
{
    // Collection spéciale "Favoris" (virtuelle, non stockée)
    private static readonly string FavoritesCollectionId = SystemCollectionIds.Favorites;
    
    /// <summary>
    /// Garde de réentrance : empêche OnSelectedCollectionChanged de se re-déclencher
    /// lors des mises à jour programmatiques de la liste des collections.
    /// </summary>
    private bool _suppressCollectionSelectionChanged;
    
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
    
    [ObservableProperty]
    private bool _isCollectionRotationActive;
    
    /// <summary>
    /// Indique si la collection sélectionnée est la collection spéciale "Favoris"
    /// </summary>
    public bool IsSelectedCollectionFavorites => SelectedCollection?.Id == FavoritesCollectionId;
    
    /// <summary>
    /// Indique si la collection sélectionnée est une collection système (non modifiable)
    /// </summary>
    public bool IsSelectedCollectionSystem => 
        SelectedCollection != null && SystemCollectionIds.IsSystemCollection(SelectedCollection.Id);
    
    /// <summary>
    /// Indique si la collection sélectionnée peut être modifiée
    /// </summary>
    public bool CanEditSelectedCollection => SelectedCollection != null && !IsSelectedCollectionSystem;
    
    private void LoadCollections()
    {
        // Mettre à jour le compteur des favoris
        UpdateFavoritesCount();
        
        // Mettre à jour les compteurs de luminosité
        UpdateBrightnessCounters();
        
        // Créer la liste avec les collections système en premier
        var allCollections = new List<Collection> 
        { 
            _favoritesCollection,
            _animatedCollection,
            _darkCollection,
            _lightCollection
        };
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
        // Garde de réentrance : ignorer les changements programmatiques
        if (_suppressCollectionSelectionChanged)
            return;
        
        try
        {
            // Désabonner de l'ancienne collection (sauf collections système)
            if (oldValue != null && !SystemCollectionIds.IsSystemCollection(oldValue.Id))
            {
                oldValue.PropertyChanged -= OnCollectionPropertyChanged;
            }
            
            // S'abonner à la nouvelle collection (sauf collections système)
            if (newValue != null && !SystemCollectionIds.IsSystemCollection(newValue.Id))
            {
                newValue.PropertyChanged += OnCollectionPropertyChanged;
            }
            
            // Charger les wallpapers de manière asynchrone pour ne pas geler l'UI
            _ = RefreshCollectionWallpapersAsync();
            
            // Notifier les propriétés dépendantes
            OnPropertyChanged(nameof(IsSelectedCollectionFavorites));
            OnPropertyChanged(nameof(IsSelectedCollectionSystem));
            OnPropertyChanged(nameof(IsSelectedCollectionBrightness));
            OnPropertyChanged(nameof(CanEditSelectedCollection));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur lors du changement de collection: {ex.Message}");
            StatusMessage = $"Erreur: {ex.Message}";
        }
    }
    
    private void OnCollectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Sauvegarder automatiquement les changements de nom ou d'icône
        if (e.PropertyName == nameof(Collection.Name) || e.PropertyName == nameof(Collection.Icon))
        {
            SettingsService.MarkDirty();
            SettingsService.Save();
            
            // Rafraîchir la liste pour mettre à jour l'affichage
            // On utilise la garde de réentrance pour éviter les cascades
            if (SelectedCollection != null)
            {
                var index = Collections.IndexOf(SelectedCollection);
                if (index >= 0)
                {
                    _suppressCollectionSelectionChanged = true;
                    try
                    {
                        var temp = SelectedCollection;
                        Collections.RemoveAt(index);
                        Collections.Insert(index, temp);
                        SelectedCollection = temp;
                    }
                    finally
                    {
                        _suppressCollectionSelectionChanged = false;
                    }
                }
            }
        }
    }
    
    private void RefreshCollectionWallpapers()
    {
        // Version synchrone qui délègue à la version async
        _ = RefreshCollectionWallpapersAsync();
    }
    
    /// <summary>
    /// Indicateur de chargement spécifique aux collections
    /// </summary>
    [ObservableProperty]
    private bool _isCollectionLoading;
    
    /// <summary>
    /// Texte de progression du chargement
    /// </summary>
    [ObservableProperty]
    private string _collectionLoadingText = "Chargement...";
    
    /// <summary>
    /// Rafraîchit les wallpapers de la collection sélectionnée de manière asynchrone et progressive.
    /// </summary>
    private async Task RefreshCollectionWallpapersAsync()
    {
        if (SelectedCollection == null)
        {
            CollectionWallpapers.Clear();
            return;
        }
        
        var collectionId = SelectedCollection.Id;
        
        try
        {
            // Afficher l'indicateur IMMÉDIATEMENT avant tout traitement
            IsCollectionLoading = true;
            CollectionLoadingText = "Chargement...";
            
            // Laisser le temps à l'UI de se mettre à jour (afficher le spinner)
            await Task.Delay(10).ConfigureAwait(true);
            
            // Effectuer les opérations de filtrage en arrière-plan
            var wallpapers = await Task.Run(() =>
            {
                // Vérifier que la collection n'a pas changé pendant l'attente
                if (SelectedCollection?.Id != collectionId)
                    return null;
                
                List<Wallpaper> result;
                
                if (collectionId == FavoritesCollectionId)
                {
                    result = _allWallpapers.Where(w => w.IsFavorite).ToList();
                }
                else if (collectionId == DarkCollectionId)
                {
                    result = _allWallpapers.Where(w => w.BrightnessCategory == BrightnessCategory.Dark).ToList();
                }
                else if (collectionId == LightCollectionId)
                {
                    result = _allWallpapers.Where(w => w.BrightnessCategory == BrightnessCategory.Light).ToList();
                }
                else if (collectionId == AnimatedCollectionId)
                {
                    result = _allWallpapers.Where(w => w.Type == WallpaperType.Animated || w.Type == WallpaperType.Video).ToList();
                }
                else
                {
                    result = SettingsService.GetWallpapersInCollection(collectionId);
                }
                
                return result;
            }).ConfigureAwait(true);
            
            // Vérifier que la collection n'a pas changé pendant l'attente
            if (wallpapers == null || SelectedCollection?.Id != collectionId)
                return;
            
            // Charger progressivement pour les grandes collections
            if (wallpapers.Count > 100)
            {
                CollectionLoadingText = $"Affichage de {wallpapers.Count} éléments...";
                
                // Vider d'abord pour éviter la mémoire excessive
                CollectionWallpapers = new ObservableCollection<Wallpaper>();
                
                // Ajouter par lots pour ne pas bloquer l'UI
                const int batchSize = 50;
                for (int i = 0; i < wallpapers.Count; i += batchSize)
                {
                    // Vérifier que la collection n'a pas changé
                    if (SelectedCollection?.Id != collectionId)
                        return;
                    
                    var batch = wallpapers.Skip(i).Take(batchSize);
                    foreach (var wp in batch)
                    {
                        CollectionWallpapers.Add(wp);
                    }
                    
                    CollectionLoadingText = $"Chargé {Math.Min(i + batchSize, wallpapers.Count)}/{wallpapers.Count}...";
                    
                    // Laisser l'UI respirer entre les lots
                    await Task.Delay(5).ConfigureAwait(true);
                }
            }
            else
            {
                // Pour les petites collections, charger tout d'un coup
                CollectionWallpapers = new ObservableCollection<Wallpaper>(wallpapers);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur lors du rafraîchissement des wallpapers de collection: {ex.Message}");
            CollectionWallpapers.Clear();
            StatusMessage = $"Erreur de chargement: {ex.Message}";
        }
        finally
        {
            IsCollectionLoading = false;
        }
    }
    
    /// <summary>
    /// Rafraîchit la collection Favoris (appelé quand un favori change)
    /// </summary>
    internal void RefreshFavoritesCollection()
    {
        UpdateFavoritesCount();
        
        // Notifier le changement de compteur sans casser la sélection
        _favoritesCollection.NotifyCountChanged();
        
        // Rafraîchir le contenu si Favoris est sélectionné (de manière async)
        if (IsSelectedCollectionFavorites)
        {
            _ = RefreshCollectionWallpapersAsync();
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
        
        // Forcer le rafraîchissement de l'UI avec garde de réentrance
        var index = Collections.IndexOf(SelectedCollection);
        if (index >= 0)
        {
            _suppressCollectionSelectionChanged = true;
            try
            {
                var temp = SelectedCollection;
                Collections.RemoveAt(index);
                Collections.Insert(index, temp);
                SelectedCollection = temp;
            }
            finally
            {
                _suppressCollectionSelectionChanged = false;
            }
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
        
        // Forcer le rafraîchissement avec garde de réentrance
        var index = Collections.IndexOf(SelectedCollection);
        if (index >= 0)
        {
            _suppressCollectionSelectionChanged = true;
            try
            {
                var temp = SelectedCollection;
                Collections.RemoveAt(index);
                Collections.Insert(index, temp);
                SelectedCollection = temp;
            }
            finally
            {
                _suppressCollectionSelectionChanged = false;
            }
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
        
        // Rafraîchir le compteur sans casser la sélection
        collection.NotifyCountChanged();
        
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
        else if (IsSelectedCollectionBrightness)
        {
            // Collections de luminosité : réanalyser l'image ou ignorer
            StatusMessage = "Les collections de luminosité sont gérées automatiquement par l'analyse";
        }
        else
        {
            SettingsService.RemoveWallpaperFromCollection(SelectedCollection.Id, wallpaper.Id);
            CollectionWallpapers.Remove(wallpaper);
            SettingsService.Save();
            
            // Rafraîchir le compteur sans casser la sélection
            SelectedCollection.NotifyCountChanged();
            
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
        else if (SelectedCollection.Id == DarkCollectionId)
        {
            wallpapers = _allWallpapers.Where(w => w.BrightnessCategory == BrightnessCategory.Dark).ToList();
        }
        else if (SelectedCollection.Id == LightCollectionId)
        {
            wallpapers = _allWallpapers.Where(w => w.BrightnessCategory == BrightnessCategory.Light).ToList();
        }
        else if (SelectedCollection.Id == AnimatedCollectionId)
        {
            wallpapers = _allWallpapers.Where(w => w.Type == WallpaperType.Animated || w.Type == WallpaperType.Video).ToList();
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
        
        // Désactiver la rotation intelligente si active
        if (SmartRotationEnabled)
        {
            SmartRotationEnabled = false;
        }
        
        // Configurer la rotation avec cette collection uniquement
        App.RotationService.SetPlaylist(wallpapers);
        App.RotationService.Start();
        
        // Appliquer immédiatement le premier wallpaper
        App.RotationService.Next();
        
        // Mettre à jour l'état : d'abord activer la rotation, puis verrouiller le toggle
        // Important: _isRotationEnabled est mis directement pour éviter que le handler
        // OnIsRotationEnabledChanged ne recharge la playlist par défaut
        _isRotationEnabled = true;
        SettingsService.Current.RotationEnabled = true;
        OnPropertyChanged(nameof(IsRotationEnabled));
        OnPropertyChanged(nameof(RotationStatusText));
        IsCollectionRotationActive = true;
        SettingsService.Save();
        StatusMessage = $"Rotation démarrée avec '{SelectedCollection.Name}' ({wallpapers.Count} fonds)";
    }
    
    [RelayCommand]
    private void StopCollectionRotation()
    {
        if (!App.IsInitialized) return;
        
        // Arrêter la rotation en cours
        App.RotationService.Stop();
        
        // Désactiver l'état de rotation de collection
        IsCollectionRotationActive = false;
        
        // Recharger la playlist par défaut (toute la bibliothèque) et relancer
        App.RotationService.RefreshPlaylist();
        App.RotationService.Start();
        
        // L'état reste "rotation activée" mais avec la bibliothèque complète
        _isRotationEnabled = true;
        SettingsService.Current.RotationEnabled = true;
        OnPropertyChanged(nameof(IsRotationEnabled));
        OnPropertyChanged(nameof(RotationStatusText));
        SettingsService.Save();
        
        StatusMessage = "Rotation de collection arrêtée — rotation de la bibliothèque rétablie";
    }
    
    partial void OnIsCollectionRotationActiveChanged(bool value)
    {
        // Notifier l'UI que l'état du toggle de rotation automatique peut avoir changé
        OnPropertyChanged(nameof(IsRotationToggleEnabled));
    }
}
