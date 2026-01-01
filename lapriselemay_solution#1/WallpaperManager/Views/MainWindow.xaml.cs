using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using WallpaperManager.Services;
using WallpaperManager.ViewModels;

namespace WallpaperManager.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    
    public MainWindow()
    {
        InitializeComponent();
        
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        
        // S'abonner aux événements de rotation
        if (App.IsInitialized)
        {
            App.RotationService.WallpaperChanged += OnWallpaperChanged;
        }
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowIsOnScreen();
        
        // Mettre à jour le status initial
        var count = _viewModel.Wallpapers.Count;
        _viewModel.StatusMessage = count > 0 
            ? $"{count} fond(s) d'ecran dans la bibliotheque" 
            : "Ajoutez des fonds d'ecran avec le bouton + Ajouter";
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

    private void OnWallpaperChanged(object? sender, Models.Wallpaper e)
    {
        Dispatcher.Invoke(() =>
        {
            Title = $"Wallpaper Manager - {e.DisplayName}";
            _viewModel.StatusMessage = $"Fond d'ecran: {e.DisplayName}";
        });
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        
        // Minimiser dans le tray = fermer la fenêtre pour libérer la RAM
        if (WindowState == WindowState.Minimized && SettingsService.Current.MinimizeToTray)
        {
            Close(); // Ferme la fenêtre et libère la RAM, l'app continue dans le tray
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Si "minimiser dans le tray" est activé, fermer la fenêtre mais garder l'app
        if (SettingsService.Current.MinimizeToTray)
        {
            // Nettoyer les ressources pour libérer la RAM
            if (App.IsInitialized)
            {
                App.RotationService.WallpaperChanged -= OnWallpaperChanged;
            }
            _viewModel.Dispose();
            
            // Forcer le garbage collection pour libérer la mémoire
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            base.OnClosing(e);
            return;
        }
        
        // Sinon, fermer complètement l'application
        if (App.IsInitialized)
        {
            App.RotationService.WallpaperChanged -= OnWallpaperChanged;
        }
        _viewModel.Dispose();
        
        base.OnClosing(e);
        System.Windows.Application.Current.Shutdown();
    }
    
}
