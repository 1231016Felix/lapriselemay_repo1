using System.Threading;
using System.Windows;
using Microsoft.Win32;
using WallpaperManager.Services;
using WallpaperManager.Views;
using Application = System.Windows.Application;

namespace WallpaperManager;

/// <summary>
/// Application principale avec gestion du cycle de vie et des services.
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "Global\\WallpaperManager_SingleInstance";
    
    private TrayIconService? _trayIconService;
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
            InitializeServices();
            ConfigureSystemMonitoring();
            InitializeTrayIcon();
            ShowMainWindowIfNeeded();
            StartRotationIfEnabled();
            
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

    private static void InitializeServices()
    {
        SettingsService.Load();
        
        _rotationService = new WallpaperRotationService();
        _animatedService = new AnimatedWallpaperService();
        _systemMonitorService = new SystemMonitorService();
        _isInitialized = true;
    }

    private static void ConfigureSystemMonitoring()
    {
        if (_systemMonitorService == null) return;
        
        _systemMonitorService.FullscreenStateChanged += OnFullscreenStateChanged;
        _systemMonitorService.BatteryStateChanged += OnBatteryStateChanged;
        _systemMonitorService.Start();
    }

    private void InitializeTrayIcon()
    {
        _trayIconService = new TrayIconService();
        _trayIconService.OpenRequested += (_, _) => ShowMainWindow();
        _trayIconService.NextWallpaperRequested += (_, _) => _rotationService?.Next();
        _trayIconService.PreviousWallpaperRequested += (_, _) => _rotationService?.Previous();
        _trayIconService.ExitRequested += (_, _) => ExitApplication();
        _trayIconService.Initialize();
    }

    private void ShowMainWindowIfNeeded()
    {
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
    }

    private static void StartRotationIfEnabled()
    {
        if (SettingsService.Current.RotationEnabled)
        {
            _rotationService?.Start();
        }
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
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
        _trayIconService?.Dispose();
        _trayIconService = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            DisposeServicesAsync();
        }
        finally
        {
            base.OnExit(e);
        }
    }

    private void DisposeServicesAsync()
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
            _systemMonitorService = null;
        }
        
        // Nettoyer le cache de thumbnails ancien
        ThumbnailService.Instance.CleanupOldCache();
        
        // Sauvegarder l'état de la fenêtre pour le prochain démarrage
        SettingsService.Current.WasInTrayOnLastExit = !_mainWindowVisible;
        
        try { SettingsService.Save(); } catch { }
        
        // Disposer les services de wallpaper
        if (_rotationService != null)
        {
            _rotationService.Stop();
            _rotationService.Dispose();
            _rotationService = null;
        }
        
        if (_animatedService != null)
        {
            _animatedService.Stop();
            _animatedService.Dispose();
            _animatedService = null;
        }
        
        // Disposer le tray icon
        _trayIconService?.Dispose();
        _trayIconService = null;
        
        // Libérer le mutex
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
        }
        catch { }
        
        _isInitialized = false;
    }
    
    #region Event Handlers
    
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Exception: {e.ExceptionObject}");
    }
    
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Dispatcher Exception: {e.Exception}");
        e.Handled = true;
    }
    
    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
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
                if (_systemMonitorService != null && !_systemMonitorService.ShouldPauseAnimated())
                {
                    // Note: Les fonds animés ne reprennent pas automatiquement après veille
                }
                break;
        }
    }
    
    private static void OnFullscreenStateChanged(object? sender, bool isFullscreen)
    {
        if (!SettingsService.Current.PauseOnFullscreen)
            return;
        
        Current.Dispatcher.BeginInvoke(() =>
        {
            if (isFullscreen)
            {
                System.Diagnostics.Debug.WriteLine("App plein écran détectée - Pause du fond animé");
                _animatedService?.Pause();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Sortie du plein écran - Vérification reprise");
                if (_systemMonitorService != null && !_systemMonitorService.ShouldPauseAnimated())
                {
                    _animatedService?.Resume();
                }
            }
        });
    }
    
    private static void OnBatteryStateChanged(object? sender, bool isOnBattery)
    {
        if (!SettingsService.Current.PauseOnBattery)
            return;
        
        Current.Dispatcher.BeginInvoke(() =>
        {
            if (isOnBattery)
            {
                System.Diagnostics.Debug.WriteLine("Sur batterie - Pause du fond animé");
                _animatedService?.Pause();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Sur secteur - Vérification reprise");
                if (_systemMonitorService != null && !_systemMonitorService.ShouldPauseAnimated())
                {
                    _animatedService?.Resume();
                }
            }
        });
    }
    
    #endregion
    
    #region Public Static Properties
    
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
    
    #endregion
}
