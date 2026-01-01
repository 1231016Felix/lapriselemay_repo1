using System.Threading;
using System.Windows;
using System.Drawing;
using WallpaperManager.Services;
using WallpaperManager.Views;
using H.NotifyIcon;
using Application = System.Windows.Application;

namespace WallpaperManager;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "Global\\WallpaperManager_SingleInstance";
    
    private TaskbarIcon? _trayIcon;
    private static WallpaperRotationService? _rotationService;
    private static AnimatedWallpaperService? _animatedService;
    private static bool _isInitialized;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Vérifier si une instance existe déjà
        bool createdNew;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch
        {
            createdNew = false;
        }
        
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Wallpaper Manager est déjà en cours d'exécution.\n\nVérifiez l'icône dans la barre des tâches (zone de notification près de l'horloge).",
                "Wallpaper Manager - Déjà ouvert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            
            Environment.Exit(0);
            return;
        }
        
        base.OnStartup(e);
        
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        
        try
        {
            // Charger les paramètres
            SettingsService.Load();
            
            // Initialiser les services
            _rotationService = new WallpaperRotationService();
            _animatedService = new AnimatedWallpaperService();
            _isInitialized = true;
            
            // Créer l'icône dans le system tray
            CreateTrayIcon();
            
            // Créer et afficher la fenêtre principale
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            
            // Démarrer la rotation si activée
            if (SettingsService.Current.RotationEnabled)
            {
                _rotationService.Start();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur démarrage: {ex}");
            System.Windows.MessageBox.Show(
                $"Erreur au démarrage:\n{ex.Message}", 
                "Erreur", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
        }
    }

    private void CreateTrayIcon()
    {
        try
        {
            // Créer le menu contextuel
            var contextMenu = new System.Windows.Controls.ContextMenu();
            
            var openItem = new System.Windows.Controls.MenuItem { Header = "📂 Ouvrir Wallpaper Manager" };
            openItem.Click += (_, _) => ShowMainWindow();
            contextMenu.Items.Add(openItem);
            
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            
            var nextItem = new System.Windows.Controls.MenuItem { Header = "▶ Fond suivant" };
            nextItem.Click += (_, _) => _rotationService?.Next();
            contextMenu.Items.Add(nextItem);
            
            var prevItem = new System.Windows.Controls.MenuItem { Header = "◀ Fond précédent" };
            prevItem.Click += (_, _) => _rotationService?.Previous();
            contextMenu.Items.Add(prevItem);
            
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            
            var exitItem = new System.Windows.Controls.MenuItem { Header = "❌ Quitter complètement" };
            exitItem.Click += (_, _) => ExitApplication();
            contextMenu.Items.Add(exitItem);
            
            // Créer l'icône
            _trayIcon = new TaskbarIcon
            {
                Icon = GetTrayIcon(),
                ToolTipText = "Wallpaper Manager - Clic droit pour le menu",
                ContextMenu = contextMenu,
                Visibility = Visibility.Visible
            };
            
            // Double-clic pour ouvrir
            _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
            
            // Forcer la création de l'icône
            _trayIcon.ForceCreate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur création tray icon: {ex}");
        }
    }

    private static Icon GetTrayIcon()
    {
        try
        {
            // Charger l'icône depuis les ressources
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur chargement icône: {ex}");
        }
        
        // Fallback sur l'icône système
        return SystemIcons.Application;
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            // Toujours créer une nouvelle fenêtre (l'ancienne a été fermée pour libérer la RAM)
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
            mainWindow.Focus();
        });
    }
    
    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { SettingsService.Save(); } catch { }
        
        _rotationService?.Stop();
        _rotationService?.Dispose();
        
        _animatedService?.Stop();
        _animatedService?.Dispose();
        
        _trayIcon?.Dispose();
        
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { }
        
        base.OnExit(e);
    }
    
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Exception: {e.ExceptionObject}");
    }
    
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Dispatcher Exception: {e.Exception}");
        e.Handled = true;
    }

    public static WallpaperRotationService RotationService
    {
        get
        {
            if (!_isInitialized || _rotationService == null)
                throw new InvalidOperationException("App non initialisée");
            return _rotationService;
        }
    }
    
    public static AnimatedWallpaperService AnimatedService
    {
        get
        {
            if (!_isInitialized || _animatedService == null)
                throw new InvalidOperationException("App non initialisée");
            return _animatedService;
        }
    }
    
    public static bool IsInitialized => _isInitialized;
}
