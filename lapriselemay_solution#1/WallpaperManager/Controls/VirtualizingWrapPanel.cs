using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using WpfSize = System.Windows.Size;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace WallpaperManager.Controls;

/// <summary>
/// WrapPanel virtualisé pour des performances optimales avec de grandes collections.
/// Utilise UI Virtualization pour ne créer que les éléments visibles.
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    #region Dependency Properties
    
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(180.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    
    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    
    public static readonly DependencyProperty ItemMarginProperty =
        DependencyProperty.Register(nameof(ItemMargin), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    
    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }
    
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }
    
    public double ItemMargin
    {
        get => (double)GetValue(ItemMarginProperty);
        set => SetValue(ItemMarginProperty, value);
    }
    
    #endregion
    
    #region Private Fields
    
    private WpfSize _extent = new(0, 0);
    private WpfSize _viewport = new(0, 0);
    private WpfPoint _offset = new(0, 0);
    private ScrollViewer? _scrollOwner;
    private int _itemsPerRow = 1;
    private int _firstVisibleIndex = -1;
    private int _lastVisibleIndex = -1;
    
    #endregion
    
    #region IScrollInfo Implementation
    
    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set => _scrollOwner = value;
    }
    
    public void LineUp() => SetVerticalOffset(_offset.Y - ItemTotalHeight);
    public void LineDown() => SetVerticalOffset(_offset.Y + ItemTotalHeight);
    public void LineLeft() => SetHorizontalOffset(_offset.X - ItemTotalWidth);
    public void LineRight() => SetHorizontalOffset(_offset.X + ItemTotalWidth);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width);
    public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width);
    
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - ItemTotalHeight * 3);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + ItemTotalHeight * 3);
    public void MouseWheelLeft() => SetHorizontalOffset(_offset.X - ItemTotalWidth * 3);
    public void MouseWheelRight() => SetHorizontalOffset(_offset.X + ItemTotalWidth * 3);
    
    public void SetHorizontalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, _extent.Width - _viewport.Width));
        if (Math.Abs(offset - _offset.X) > 0.001)
        {
            _offset.X = offset;
            _scrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }
    }
    
    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, _extent.Height - _viewport.Height));
        if (Math.Abs(offset - _offset.Y) > 0.001)
        {
            _offset.Y = offset;
            _scrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }
    }
    
    public WpfRect MakeVisible(System.Windows.Media.Visual visual, WpfRect rectangle)
    {
        if (visual is UIElement element)
        {
            var transform = element.TransformToAncestor(this);
            var elementRect = transform.TransformBounds(new WpfRect(element.RenderSize));
            
            if (elementRect.Bottom > _offset.Y + _viewport.Height)
                SetVerticalOffset(elementRect.Bottom - _viewport.Height);
            if (elementRect.Top < _offset.Y)
                SetVerticalOffset(elementRect.Top);
        }
        return rectangle;
    }
    
    #endregion
    
    #region Calculated Properties
    
    private double ItemTotalWidth => ItemWidth + ItemMargin * 2;
    private double ItemTotalHeight => ItemHeight + ItemMargin * 2;
    
    private int ItemCount
    {
        get
        {
            var itemsControl = ItemsControl.GetItemsOwner(this);
            return itemsControl?.Items.Count ?? 0;
        }
    }
    
    /// <summary>
    /// Indices des éléments actuellement visibles (pour le lazy loading).
    /// </summary>
    public (int First, int Last) VisibleRange => (_firstVisibleIndex, _lastVisibleIndex);
    
    /// <summary>
    /// Événement déclenché quand la plage visible change.
    /// </summary>
    public event EventHandler<VisibleRangeChangedEventArgs>? VisibleRangeChanged;
    
    #endregion
    
    #region Measure & Arrange
    
    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        if (double.IsInfinity(availableSize.Width))
            availableSize.Width = 1000;
        
        // Calculer le nombre d'items par ligne
        _itemsPerRow = Math.Max(1, (int)(availableSize.Width / ItemTotalWidth));
        
        var itemCount = ItemCount;
        if (itemCount == 0)
        {
            _extent = new WpfSize(0, 0);
            _viewport = availableSize;
            _scrollOwner?.InvalidateScrollInfo();
            return availableSize;
        }
        
        // Calculer l'étendue totale
        var rowCount = (int)Math.Ceiling((double)itemCount / _itemsPerRow);
        _extent = new WpfSize(availableSize.Width, rowCount * ItemTotalHeight);
        _viewport = availableSize;
        
        // Calculer les indices visibles
        var firstRow = (int)(_offset.Y / ItemTotalHeight);
        var lastRow = (int)((_offset.Y + _viewport.Height) / ItemTotalHeight);
        
        // Ajouter des lignes de buffer pour le préchargement (2 lignes avant/après)
        firstRow = Math.Max(0, firstRow - 2);
        lastRow = Math.Min(rowCount - 1, lastRow + 2);
        
        var newFirstVisible = firstRow * _itemsPerRow;
        var newLastVisible = Math.Min((lastRow + 1) * _itemsPerRow - 1, itemCount - 1);
        
        // Notifier si la plage a changé (sans le buffer pour le lazy loading)
        var actualFirstVisible = Math.Max(0, (int)(_offset.Y / ItemTotalHeight)) * _itemsPerRow;
        var actualLastVisible = Math.Min(
            ((int)((_offset.Y + _viewport.Height) / ItemTotalHeight) + 1) * _itemsPerRow - 1, 
            itemCount - 1);
        
        if (actualFirstVisible != _firstVisibleIndex || actualLastVisible != _lastVisibleIndex)
        {
            _firstVisibleIndex = actualFirstVisible;
            _lastVisibleIndex = actualLastVisible;
            
            VisibleRangeChanged?.Invoke(this, new VisibleRangeChangedEventArgs(
                _firstVisibleIndex, 
                _lastVisibleIndex,
                newFirstVisible, // avec buffer
                newLastVisible   // avec buffer
            ));
        }
        
        // Générer les conteneurs pour les éléments visibles
        var generator = ItemContainerGenerator;
        var startPos = generator.GeneratorPositionFromIndex(newFirstVisible);
        var childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
        
        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int i = newFirstVisible; i <= newLastVisible; i++)
            {
                var child = (UIElement?)generator.GenerateNext(out var isNewlyRealized);
                if (child == null) continue;
                
                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                        AddInternalChild(child);
                    else
                        InsertInternalChild(childIndex, child);
                    
                    generator.PrepareItemContainer(child);
                }
                
                child.Measure(new WpfSize(ItemWidth, ItemHeight));
                childIndex++;
            }
        }
        
        // Nettoyer les conteneurs hors de la plage visible
        CleanUpItems(newFirstVisible, newLastVisible);
        
        _scrollOwner?.InvalidateScrollInfo();
        return availableSize;
    }
    
    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        var generator = ItemContainerGenerator;
        
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            
            if (itemIndex < 0) continue;
            
            var row = itemIndex / _itemsPerRow;
            var col = itemIndex % _itemsPerRow;
            
            var x = col * ItemTotalWidth + ItemMargin;
            var y = row * ItemTotalHeight + ItemMargin - _offset.Y;
            
            child.Arrange(new WpfRect(x, y, ItemWidth, ItemHeight));
        }
        
        return finalSize;
    }
    
    #endregion
    
    #region Container Cleanup
    
    private void CleanUpItems(int firstVisible, int lastVisible)
    {
        var generator = ItemContainerGenerator;
        
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            
            if (itemIndex < firstVisible || itemIndex > lastVisible)
            {
                generator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }
    
    #endregion
    
    #region Item Changes
    
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                // Réinitialiser le scroll et remesurer
                _offset = new WpfPoint(0, 0);
                _scrollOwner?.InvalidateScrollInfo();
                break;
        }
        
        base.OnItemsChanged(sender, args);
    }
    
    #endregion
}

/// <summary>
/// Arguments de l'événement de changement de plage visible.
/// </summary>
public class VisibleRangeChangedEventArgs : EventArgs
{
    public int FirstVisibleIndex { get; }
    public int LastVisibleIndex { get; }
    public int FirstBufferedIndex { get; }
    public int LastBufferedIndex { get; }
    
    public VisibleRangeChangedEventArgs(int first, int last, int firstBuffered, int lastBuffered)
    {
        FirstVisibleIndex = first;
        LastVisibleIndex = last;
        FirstBufferedIndex = firstBuffered;
        LastBufferedIndex = lastBuffered;
    }
}
