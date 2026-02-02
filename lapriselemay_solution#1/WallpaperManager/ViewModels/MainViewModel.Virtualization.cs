using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperManager.Controls;
using WallpaperManager.Models;
using WallpaperManager.Services;
using System.Collections.Concurrent;

namespace WallpaperManager.ViewModels;

/// <summary>
/// Extensions de MainViewModel pour la virtualisation et le lazy loading.
/// </summary>
public partial class MainViewModel
{
    private ICollectionView? _wallpapersView;
    private readonly object _viewLock = new();
    
    // Debounce pour OnThumbnailGenerated : empêche le spam de Refresh()
    private DispatcherTimer? _thumbnailRefreshTimer;
    private volatile bool _thumbnailRefreshPending;
    
    /// <summary>
    /// Vue filtrée et triée des wallpapers - utilise ICollectionView pour des performances optimales.
    /// À utiliser à la place de la propriété Wallpapers pour la liaison dans la ListBox.
    /// </summary>
    public ICollectionView WallpapersView
    {
        get
        {
            if (_wallpapersView == null)
            {
                InitializeCollectionView();
            }
            return _wallpapersView!;
        }
    }
    
    /// <summary>
    /// Initialise le timer pour le debounce du préchargement.
    /// Appelé depuis le constructeur.
    /// </summary>
    private void InitializeVirtualization()
    {
        // Le timer sera initialisé une seule fois
        // Note: Ce code suppose que InitializeVirtualization est appelé dans le constructeur
    }
    
    /// <summary>
    /// Initialise la CollectionView avec les filtres et le tri.
    /// </summary>
    private void InitializeCollectionView()
    {
        lock (_viewLock)
        {
            if (_wallpapersView != null) return;
            
            _wallpapersView = CollectionViewSource.GetDefaultView(_allWallpapers);
            
            // Configurer le filtrage
            _wallpapersView.Filter = WallpaperFilter;
            
            // Configurer le tri initial
            ApplySorting();
            
            // S'abonner aux changements de miniatures pour rafraîchir l'UI
            ThumbnailService.Instance.ThumbnailGenerated += OnThumbnailGenerated;
        }
    }
    
    /// <summary>
    /// Prédicat de filtrage pour ICollectionView.
    /// </summary>
    private bool WallpaperFilter(object item)
    {
        if (item is not Wallpaper wallpaper)
            return false;
        
        // Filtre par recherche
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var query = SearchQuery;
            if (!wallpaper.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !wallpaper.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        
        // Filtre par type
        var matchesType = FilterType switch
        {
            "Statique" => wallpaper.Type == WallpaperType.Static,
            "Animé" => wallpaper.Type == WallpaperType.Animated,
            "Vidéo" => wallpaper.Type == WallpaperType.Video,
            _ => true
        };
        
        if (!matchesType) return false;
        
        // Filtre favoris
        if (FilterFavoritesOnly && !wallpaper.IsFavorite)
            return false;
        
        return true;
    }
    
    /// <summary>
    /// Applique le tri à la CollectionView.
    /// </summary>
    private void ApplySorting()
    {
        if (_wallpapersView == null) return;
        
        using (_wallpapersView.DeferRefresh())
        {
            _wallpapersView.SortDescriptions.Clear();
            
            var direction = SortDescending 
                ? ListSortDirection.Descending 
                : ListSortDirection.Ascending;
            
            var propertyName = SortBy switch
            {
                "Nom" => nameof(Wallpaper.DisplayName),
                "Taille" => nameof(Wallpaper.FileSize),
                "Résolution" => "PixelCount", // Propriété calculée, voir ci-dessous
                _ => nameof(Wallpaper.AddedDate)
            };
            
            // Pour la résolution, on utilise un tri personnalisé
            if (SortBy == "Résolution")
            {
                _wallpapersView.SortDescriptions.Add(new SortDescription(nameof(Wallpaper.Width), direction));
                _wallpapersView.SortDescriptions.Add(new SortDescription(nameof(Wallpaper.Height), direction));
            }
            else
            {
                _wallpapersView.SortDescriptions.Add(new SortDescription(propertyName, direction));
            }
        }
    }
    
    /// <summary>
    /// Rafraîchit la vue filtrée - beaucoup plus performant que recréer une collection.
    /// </summary>
    private void RefreshView()
    {
        if (_wallpapersView == null)
        {
            InitializeCollectionView();
        }
        else
        {
            // Rafraîchir le filtre de manière différée pour éviter les multiples rafraîchissements
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _wallpapersView?.Refresh();
                OnPropertyChanged(nameof(FilteredCount));
            }, DispatcherPriority.Background);
        }
    }
    
    /// <summary>
    /// Version optimisée de ApplyFiltersAndSort qui utilise ICollectionView.
    /// </summary>
    private void ApplyFiltersAndSortOptimized()
    {
        if (_wallpapersView == null)
        {
            InitializeCollectionView();
            return;
        }
        
        // Appliquer le nouveau tri si nécessaire
        ApplySorting();
        
        // Rafraîchir la vue (le filtre est déjà configuré)
        _wallpapersView.Refresh();
        
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(TotalCount));
    }
    
    /// <summary>
    /// Nombre d'éléments filtrés dans la vue.
    /// </summary>
    public int FilteredCountOptimized
    {
        get
        {
            if (_wallpapersView is ICollectionView view)
            {
                // Cast vers ListCollectionView pour accéder au Count
                if (view is ListCollectionView lcv)
                    return lcv.Count;
                
                // Fallback: compter manuellement (moins performant)
                var count = 0;
                foreach (var _ in view)
                    count++;
                return count;
            }
            return _allWallpapers.Count;
        }
    }
    
    /// <summary>
    /// Gère la notification de génération de miniature pour rafraîchir l'UI.
    /// Utilise un debounce pour éviter de spammer Refresh() à chaque miniature.
    /// </summary>
    private void OnThumbnailGenerated(object? sender, string filePath)
    {
        // Marquer qu'un rafraîchissement est nécessaire
        _thumbnailRefreshPending = true;
        
        // Programmer un rafraîchissement groupé via le dispatcher
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // Initialiser le timer de debounce si nécessaire
            if (_thumbnailRefreshTimer == null)
            {
                _thumbnailRefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _thumbnailRefreshTimer.Tick += (_, _) =>
                {
                    _thumbnailRefreshTimer.Stop();
                    if (_thumbnailRefreshPending)
                    {
                        _thumbnailRefreshPending = false;
                        _wallpapersView?.Refresh();
                        OnPropertyChanged(nameof(FilteredCount));
                        
                        // Rafraîchir aussi les wallpapers de la collection visible
                        RefreshCollectionWallpaperBindings();
                    }
                };
            }
            
            // (Re)démarrer le timer : on attend 300ms après le dernier thumbnail
            _thumbnailRefreshTimer.Stop();
            _thumbnailRefreshTimer.Start();
        }, DispatcherPriority.Background);
    }
    
    /// <summary>
    /// Force le rafraîchissement des bindings de miniatures dans la collection active.
    /// Ré-assigne la même collection pour déclencher la mise à jour de l'UI.
    /// </summary>
    private void RefreshCollectionWallpaperBindings()
    {
        if (SelectedCollection == null || CollectionWallpapers.Count == 0)
            return;
        
        // Notifier que la propriété CollectionWallpapers a "changé" pour forcer
        // la réévaluation des converters de miniatures sur les éléments visibles.
        OnPropertyChanged(nameof(CollectionWallpapers));
    }
    
    /// <summary>
    /// Gère le changement de plage visible du VirtualizingWrapPanel.
    /// Déclenche le préchargement des miniatures.
    /// </summary>
    public void OnVisibleRangeChanged(VisibleRangeChangedEventArgs e)
    {
        // Précharger les miniatures pour les éléments visibles et proches
        PreloadThumbnailsForRange(e.FirstVisibleIndex, e.LastVisibleIndex, e.FirstBufferedIndex, e.LastBufferedIndex);
    }
    
    /// <summary>
    /// Précharge les miniatures pour une plage d'indices.
    /// </summary>
    private void PreloadThumbnailsForRange(int firstVisible, int lastVisible, int firstBuffered, int lastBuffered)
    {
        if (_wallpapersView == null) return;
        
        var visiblePaths = new List<string>();
        var nearbyPaths = new List<string>();
        
        var index = 0;
        foreach (Wallpaper wallpaper in _wallpapersView)
        {
            if (index >= firstVisible && index <= lastVisible)
            {
                visiblePaths.Add(wallpaper.FilePath);
            }
            else if (index >= firstBuffered && index <= lastBuffered)
            {
                nearbyPaths.Add(wallpaper.FilePath);
            }
            
            index++;
            if (index > lastBuffered) break;
        }
        
        // Précharger via le ThumbnailService
        ThumbnailService.Instance.PreloadForVisibleRange(visiblePaths, nearbyPaths);
    }
    
    /// <summary>
    /// Ajoute un wallpaper à la collection source (optimisé).
    /// </summary>
    public void AddWallpaperOptimized(Wallpaper wallpaper)
    {
        _allWallpapers.Add(wallpaper);
        // La vue se met à jour automatiquement grâce à ObservableCollection
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
    }
    
    /// <summary>
    /// Supprime un wallpaper de la collection source (optimisé).
    /// </summary>
    public void RemoveWallpaperOptimized(Wallpaper wallpaper)
    {
        _allWallpapers.Remove(wallpaper);
        // La vue se met à jour automatiquement
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
    }
    
    /// <summary>
    /// Nettoie les ressources de virtualisation.
    /// </summary>
    private void CleanupVirtualization()
    {
        ThumbnailService.Instance.ThumbnailGenerated -= OnThumbnailGenerated;
        _thumbnailRefreshTimer?.Stop();
        _thumbnailRefreshTimer = null;
    }
}
