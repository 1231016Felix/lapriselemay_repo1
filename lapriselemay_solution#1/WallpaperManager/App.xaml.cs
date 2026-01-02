using System.Threading;
using System.Windows;
using System.Drawing;
using Microsoft.Win32;
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
    private static SystemMonitorService? _systemMonitorService;
    private static bool _isInitialized;
    private static bool _mainWindowVisible;

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
            _systemMonitorService = new SystemMonitorService();
            _isInitialized = true;
            
            // Configurer le monitoring système (plein écran, batterie)
            _systemMonitorService.FullscreenStateChanged += OnFullscreenStateChanged;
            _systemMonitorService.BatteryStateChanged += OnBatteryStateChanged;
            _systemMonitorService.Start();
            
            // Créer l'icône dans le system tray
            CreateTrayIcon();
            
            // Créer et afficher la fenêtre principale seulement si nécessaire
            bool shouldStartMinimized = SettingsService.Current.StartMinimized || 
                                        SettingsService.Current.WasInTrayOnLastExit;
            
            if (!shouldStartMinimized)
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
                _mainWindowVisible = true;
            }
            else
            {
                _mainWindowVisible = false;
            }
            
            // Démarrer la rotation si activée
            if (SettingsService.Current.RotationEnabled)
            {
                _rotationService.Start();
            }
            
            // S'abonner aux événements de veille/réveil du système
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
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
            _mainWindowVisible = true;
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
        // Se désabonner des événements système
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        
        // Arrêter et disposer le monitoring système
        if (_systemMonitorService != null)
        {
            _systemMonitorService.FullscreenStateChanged -= OnFullscreenStateChanged;
            _systemMonitorService.BatteryStateChanged -= OnBatteryStateChanged;
            _systemMonitorService.Stop();
            _systemMonitorService.Dispose();
        }
        
        // Nettoyer le cache de thumbnails ancien
        ThumbnailService.Instance.CleanupOldCache();
        
        // Sauvegarder l'état de la fenêtre pour le prochain démarrage
        SettingsService.Current.WasInTrayOnLastExit = !_mainWindowVisible;
        
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
    
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                System.Diagnostics.Debug.WriteLine("Système en veille - Pause des services");
                _rotationService?.Pause();
                _animatedService?.Pause();
                break;
                
            case PowerModes.Resume:
                System.Diagnostics.Debug.WriteLine("Réveil du système - Reprise des services");
                if (SettingsService.Current.RotationEnabled)
                {
                    _rotationService?.Resume();
                }
                // Les fonds animés reprennent seulement si pas sur batterie/plein écran
                if (_systemMonitorService != null && !_systemMonitorService.ShouldPauseAnimated())
                {
                    // Note: Les fonds animés ne reprennent pas automatiquement après veille
                    // L'utilisateur doit les relancer manuellement
                }
                break;
        }
    }
    
    private void OnFullscreenStateChanged(object? sender, bool isFullscreen)
    {
        if (!SettingsService.Current.PauseOnFullscreen)
            return;
        
        Dispatcher.BeginInvoke(() =>
        {
            if (isFullscreen)
            {
                System.Diagnostics.Debug.WriteLine("App plein écran détectée - Pause du fond animé");
                _animatedService?.Pause();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Sortie du plein écran - Vérification reprise");
                // Reprendre seulement si pas d'autre raison de pause
                if (_systemMonitorService != null && !_systemMonitorService.ShouldPauseAnimated())
                {
                    _animatedService?.Resume();
                }
            }
        });
    }
    
    private void OnBatteryStateChanged(object? sender, bool isOnBattery)
    {
        if (!SettingsService.Current.PauseOnBattery)
            return;
        
        Dispatcher.BeginInvoke(() =>
        {
            if (isOnBattery)
            {
                System.Diagnostics.Debug.WriteLine("Sur batterie - Pause du fond animé");
                _animatedService?.Pause();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Sur secteur - Vérification reprise");
                // Reprendre seulement si pas d'autre raison de pause
                if (_systemMonitorService != null && !_systemMonitorService.ShouldPauseAnimated())
                {
                    _animatedService?.Resume();
                }
            }
        });
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
    
    public static void SetMainWindowVisible(bool visible) => _mainWindowVisible = visible;
}
