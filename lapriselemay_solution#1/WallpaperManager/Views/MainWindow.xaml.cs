using System.ComponentModel;
using System.Windows;
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
    
    public MainWindow()
    {
        InitializeComponent();
        
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        
        if (App.IsInitialized)
        {
            App.RotationService.WallpaperChanged += OnWallpaperChanged;
        }
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowIsOnScreen();
        
        var count = _viewModel.TotalCount;
        _viewModel.StatusMessage = count > 0 
            ? $"{count} fond(s) d'écran dans la bibliothèque" 
            : "Ajoutez des fonds d'écran avec + Ajouter ou glissez-déposez des fichiers";
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
