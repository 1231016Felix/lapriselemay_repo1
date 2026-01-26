using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WallpaperManager.Services;

/// <summary>
/// Service qui surveille les éléments visibles dans une ListBox virtualisée
/// et déclenche le préchargement des miniatures.
/// </summary>
public sealed class VirtualizationHelper : IDisposable
{
    private readonly System.Windows.Controls.ListBox _listBox;
    private readonly Func<int, string?> _getFilePath;
    private readonly Func<int> _getItemCount;
    
    private ScrollViewer? _scrollViewer;
    private System.Timers.Timer? _debounceTimer;
    private volatile bool _disposed;
    
    // Plage visible actuelle
    private int _firstVisibleIndex = -1;
    private int _lastVisibleIndex = -1;
    
    // Nombre d'éléments à précharger avant/après la zone visible
    private const int PreloadBuffer = 10;
    
    public event EventHandler<(int First, int Last)>? VisibleRangeChanged;
    
    public VirtualizationHelper(
        System.Windows.Controls.ListBox listBox, 
        Func<int, string?> getFilePath,
        Func<int> getItemCount)
    {
        _listBox = listBox;
        _getFilePath = getFilePath;
        _getItemCount = getItemCount;
        
        // Attendre que la ListBox soit chargée
        if (_listBox.IsLoaded)
            Initialize();
        else
            _listBox.Loaded += OnListBoxLoaded;
    }
    
    private void OnListBoxLoaded(object sender, RoutedEventArgs e)
    {
        _listBox.Loaded -= OnListBoxLoaded;
        Initialize();
    }
    
    private void Initialize()
    {
        // Trouver le ScrollViewer
        _scrollViewer = FindScrollViewer(_listBox);
        
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
        
        // Timer de debounce pour éviter trop de mises à jour
        _debounceTimer = new System.Timers.Timer(100);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (s, e) => 
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(UpdateVisibleRange);
        };
        
        // Mise à jour initiale
        UpdateVisibleRange();
    }
    
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is ScrollViewer sv)
                return sv;
            
            var result = FindScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }
    
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Debounce pour éviter de surcharger pendant le scroll rapide
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }
    
    private void UpdateVisibleRange()
    {
        if (_disposed || _scrollViewer == null) return;
        
        var itemCount = _getItemCount();
        if (itemCount == 0) return;
        
        // Calculer la plage visible
        var (first, last) = CalculateVisibleRange();
        
        if (first == _firstVisibleIndex && last == _lastVisibleIndex)
            return;
        
        _firstVisibleIndex = first;
        _lastVisibleIndex = last;
        
        // Notifier le changement
        VisibleRangeChanged?.Invoke(this, (first, last));
        
        // Précharger les miniatures
        PreloadThumbnails(first, last, itemCount);
    }
    
    private (int First, int Last) CalculateVisibleRange()
    {
        if (_scrollViewer == null) return (0, 0);
        
        var itemCount = _getItemCount();
        if (itemCount == 0) return (0, 0);
        
        // Pour un WrapPanel, on doit calculer différemment
        // Estimation basée sur la taille des éléments (180x120 + margin)
        const double itemWidth = 188; // 180 + 8 margin
        const double itemHeight = 128; // 120 + 8 margin
        
        var viewportWidth = _scrollViewer.ViewportWidth;
        var viewportHeight = _scrollViewer.ViewportHeight;
        var verticalOffset = _scrollViewer.VerticalOffset;
        
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return (0, Math.Min(20, itemCount - 1));
        
        // Nombre d'éléments par ligne
        var itemsPerRow = Math.Max(1, (int)(viewportWidth / itemWidth));
        
        // Première ligne visible
        var firstRow = (int)(verticalOffset / itemHeight);
        var firstIndex = firstRow * itemsPerRow;
        
        // Nombre de lignes visibles
        var visibleRows = (int)Math.Ceiling(viewportHeight / itemHeight) + 1;
        var lastIndex = Math.Min((firstRow + visibleRows) * itemsPerRow, itemCount - 1);
        
        return (Math.Max(0, firstIndex), lastIndex);
    }
    
    private void PreloadThumbnails(int firstVisible, int lastVisible, int itemCount)
    {
        // Éléments visibles (priorité haute)
        var visiblePaths = new List<string>();
        for (int i = firstVisible; i <= lastVisible && i < itemCount; i++)
        {
            var path = _getFilePath(i);
            if (!string.IsNullOrEmpty(path))
                visiblePaths.Add(path);
        }
        
        // Éléments proches (préchargement)
        var nearbyPaths = new List<string>();
        
        // Avant la zone visible
        for (int i = Math.Max(0, firstVisible - PreloadBuffer); i < firstVisible; i++)
        {
            var path = _getFilePath(i);
            if (!string.IsNullOrEmpty(path))
                nearbyPaths.Add(path);
        }
        
        // Après la zone visible
        for (int i = lastVisible + 1; i <= Math.Min(lastVisible + PreloadBuffer, itemCount - 1); i++)
        {
            var path = _getFilePath(i);
            if (!string.IsNullOrEmpty(path))
                nearbyPaths.Add(path);
        }
        
        // Déclencher le préchargement
        ThumbnailService.Instance.PreloadForVisibleRange(visiblePaths, nearbyPaths);
    }
    
    /// <summary>
    /// Force une mise à jour de la plage visible.
    /// À appeler après un changement de filtre/tri.
    /// </summary>
    public void Refresh()
    {
        _firstVisibleIndex = -1;
        _lastVisibleIndex = -1;
        UpdateVisibleRange();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
    }
}
