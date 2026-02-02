using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WallpaperManager.Controls;
using WallpaperManager.Models;
using WallpaperManager.Services;
using WallpaperManager.ViewModels;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using ListBox = System.Windows.Controls.ListBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;

namespace WallpaperManager.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private VirtualizingWrapPanel? _virtualPanel;
    private readonly DispatcherTimer _scrollDebounceTimer;
    
    public MainWindow()
    {
        InitializeComponent();
        
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        
        // Timer pour le debounce du scroll (évite de précharger pendant le scroll rapide)
        _scrollDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _scrollDebounceTimer.Tick += OnScrollDebounceTimerTick;
        
        if (App.IsInitialized)
        {
            App.RotationService.WallpaperChanged += OnWallpaperChanged;
        }
        
        // S'abonner aux notifications de miniatures générées pour rafraîchir la liste
        ThumbnailService.Instance.ThumbnailGenerated += OnThumbnailGenerated;
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowIsOnScreen();
        
        // Essayer de trouver le VirtualizingWrapPanel
        FindVirtualPanel();
        
        // Lancer le nettoyage périodique du cache mémoire
        StartCacheMaintenanceTimer();
        
        var count = _viewModel.TotalCount;
        _viewModel.StatusMessage = count > 0 
            ? $"{count} fond(s) d'écran dans la bibliothèque" 
            : "Ajoutez des fonds d'écran avec + Ajouter ou glissez-déposez des fichiers";
        
        // Initialiser les widgets après le chargement de la fenêtre
        _viewModel.OnAppInitialized();
    }
    
    /// <summary>
    /// Trouve le VirtualizingWrapPanel dans l'arbre visuel.
    /// </summary>
    private void FindVirtualPanel()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _virtualPanel = FindVisualChild<VirtualizingWrapPanel>(WallpaperListBox);
            if (_virtualPanel != null)
            {
                _virtualPanel.VisibleRangeChanged += OnVisibleRangeChanged;
                System.Diagnostics.Debug.WriteLine("VirtualizingWrapPanel connecté pour le lazy loading");
            }
        }, DispatcherPriority.Loaded);
    }
    
    /// <summary>
    /// Timer de maintenance du cache mémoire.
    /// </summary>
    private void StartCacheMaintenanceTimer()
    {
        var maintenanceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        maintenanceTimer.Tick += (s, e) =>
        {
            // Nettoyer les miniatures non accédées depuis 5 minutes
            var evicted = ThumbnailService.Instance.TrimMemoryCache();
            if (evicted > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Cache maintenance: {evicted} miniatures évincées");
            }
        };
        maintenanceTimer.Start();
    }
    
    /// <summary>
    /// Gère le changement de plage visible pour le préchargement.
    /// </summary>
    private void OnVisibleRangeChanged(object? sender, VisibleRangeChangedEventArgs e)
    {
        // Debounce: réinitialiser le timer à chaque changement
        _scrollDebounceTimer.Stop();
        _scrollDebounceTimer.Tag = e; // Stocker les arguments
        _scrollDebounceTimer.Start();
    }
    
    /// <summary>
    /// Exécuté après le debounce du scroll.
    /// </summary>
    private void OnScrollDebounceTimerTick(object? sender, EventArgs e)
    {
        _scrollDebounceTimer.Stop();
        
        if (_scrollDebounceTimer.Tag is VisibleRangeChangedEventArgs args)
        {
            // Précharger les miniatures
            _viewModel.OnVisibleRangeChanged(args);
        }
    }
    
    /// <summary>
    /// Gère la notification qu'une miniature a été générée.
    /// </summary>
    private void OnThumbnailGenerated(object? sender, string filePath)
    {
        // Rafraîchir l'affichage de l'élément spécifique
        Dispatcher.BeginInvoke(() =>
        {
            // Force la mise à jour du binding pour cet élément
            // En WPF, cela peut être fait en trouvant le conteneur et en invalidant
            var container = WallpaperListBox.ItemContainerGenerator.ContainerFromItem(
                _viewModel.Wallpapers.FirstOrDefault(w => w.FilePath == filePath));
            
            if (container is ListBoxItem item)
            {
                // Trouver l'Image dans le template et forcer son rafraîchissement
                var image = FindVisualChild<System.Windows.Controls.Image>(item);
                if (image != null)
                {
                    // Obtenir la nouvelle miniature du cache
                    var thumbnail = ThumbnailService.Instance.GetThumbnailSync(filePath);
                    if (thumbnail != null)
                    {
                        image.Source = thumbnail;
                    }
                }
            }
        }, DispatcherPriority.Background);
    }
    
    /// <summary>
    /// Trouve un enfant visuel d'un type spécifique.
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        
        var childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            
            if (child is T found)
                return found;
            
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        
        return null;
    }
    
    private void EnsureWindowIsOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        
        if (Left < workArea.Left) Left = workArea.Left;
        if (Top < workArea.Top) Top = workArea.Top;
        if (Left + Width > workArea.Right) Left = workArea.Right - Width;
        if (Top + Height > workArea.Bottom) Top = workArea.Bottom - Height;
        if (Width > workArea.Width) Width = workArea.Width;
        if (Height > workArea.Height) Height = workArea.Height;
    }

    private void OnWallpaperChanged(object? sender, Wallpaper e)
    {
        Dispatcher.Invoke(() =>
        {
            Title = $"Wallpaper Manager - {e.DisplayName}";
            _viewModel.StatusMessage = $"Fond d'écran: {e.DisplayName}";
        });
    }
    
    // === DRAG & DROP ===
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }
    
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            await _viewModel.HandleDroppedFilesAsync(files);
        }
    }
    
    // === MULTI-SÉLECTION ===
    private void WallpaperListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            _viewModel.UpdateSelection(listBox.SelectedItems);
        }
    }
    
    // === DOUBLE-CLIC PRÉVISUALISATION ===
    private void WallpaperItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is Wallpaper wallpaper)
        {
            OpenPreview(wallpaper);
            e.Handled = true;
        }
    }
    
    private void PreviewImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedWallpaper != null)
        {
            OpenPreview(_viewModel.SelectedWallpaper);
        }
    }
    
    // === BOUTON AJOUTER À LA COLLECTION ===
    private void BtnAddToCollection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            button.ContextMenu.IsOpen = true;
        }
    }
    
    // === BOUTON INTERVALLE DE ROTATION ===
    private void BtnInterval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }
    
    private void OpenPreview(Wallpaper wallpaper)
    {
        var index = _viewModel.Wallpapers.IndexOf(wallpaper);
        var previewWindow = new PreviewWindow(_viewModel.Wallpapers, index >= 0 ? index : 0);
        previewWindow.ApplyRequested += (s, w) =>
        {
            if (App.IsInitialized)
            {
                if (w.Type == WallpaperType.Static)
                {
                    App.RotationService.ApplyWallpaper(w);
                }
                else
                {
                    App.AnimatedService.Play(w);
                }
                _viewModel.StatusMessage = $"Appliqué: {w.DisplayName}";
            }
        };
        previewWindow.Owner = this;
        previewWindow.ShowDialog();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Se désabonner des événements
        ThumbnailService.Instance.ThumbnailGenerated -= OnThumbnailGenerated;
        
        if (_virtualPanel != null)
        {
            _virtualPanel.VisibleRangeChanged -= OnVisibleRangeChanged;
        }
        
        _scrollDebounceTimer.Stop();
        
        if (SettingsService.Current.MinimizeToTray)
        {
            App.SetMainWindowVisible(false);
            
            if (App.IsInitialized)
            {
                App.RotationService.WallpaperChanged -= OnWallpaperChanged;
            }
            _viewModel.Dispose();
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            base.OnClosing(e);
            return;
        }
        
        if (App.IsInitialized)
        {
            App.RotationService.WallpaperChanged -= OnWallpaperChanged;
        }
        _viewModel.Dispose();
        
        base.OnClosing(e);
        System.Windows.Application.Current.Shutdown();
    }
}
