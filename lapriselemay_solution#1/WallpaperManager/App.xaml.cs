using System.Threading;
using System.Windows;
using Microsoft.Win32;
using WallpaperManager.Models;
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
    private static DynamicWallpaperService? _dynamicService;
    private static SystemMonitorService? _systemMonitorService;
    private static HotkeyService? _hotkeyService;
    private static TransitionService? _transitionService;
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
        _dynamicService = new DynamicWallpaperService();
        _systemMonitorService = new SystemMonitorService();
        _hotkeyService = new HotkeyService();
        _transitionService = new TransitionService
        {
            CurrentEffect = SettingsService.Current.TransitionEffect,
            TransitionDuration = TimeSpan.FromMilliseconds(SettingsService.Current.TransitionDurationMs)
        };
        
        // Connecter le service de transition au service de rotation
        _rotationService.SetTransitionService(_transitionService);
        
        // Connecter l'événement pour les wallpapers animés/vidéo dans la rotation
        _rotationService.AnimatedWallpaperRequested += OnAnimatedWallpaperRequested;
        
        // Connecter les raccourcis clavier globaux
        _hotkeyService.NextWallpaperRequested += (_, _) => _rotationService?.Next();
        _hotkeyService.PreviousWallpaperRequested += (_, _) => _rotationService?.Previous();
        _hotkeyService.ToggleFavoriteRequested += OnToggleFavoriteHotkey;
        _hotkeyService.TogglePauseRequested += OnTogglePauseHotkey;
        
        _isInitialized = true;
    }
    
    private static void OnAnimatedWallpaperRequested(object? sender, Wallpaper wallpaper)
    {
        System.Diagnostics.Debug.WriteLine($"Animation demandée par rotation: {wallpaper.DisplayName}");
        _animatedService?.Play(wallpaper);
    }
    
    private static void OnToggleFavoriteHotkey(object? sender, EventArgs e)
    {
        var currentWallpaper = _rotationService?.CurrentWallpaper;
        if (currentWallpaper == null) return;
        
        currentWallpaper.IsFavorite = !currentWallpaper.IsFavorite;
        SettingsService.MarkDirty();
        SettingsService.Save();
        
        System.Diagnostics.Debug.WriteLine($"Hotkey Favoris: {currentWallpaper.DisplayName} = {currentWallpaper.IsFavorite}");
    }
    
    private static void OnTogglePauseHotkey(object? sender, EventArgs e)
    {
        if (_rotationService == null) return;
        
        if (_rotationService.IsRunning)
        {
            _rotationService.Pause();
            System.Diagnostics.Debug.WriteLine("Hotkey: Rotation en pause");
        }
        else
        {
            _rotationService.Resume();
            System.Diagnostics.Debug.WriteLine("Hotkey: Rotation reprise");
        }
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
            
            // Initialiser les raccourcis clavier avec la fenêtre
            _hotkeyService?.Initialize(mainWindow);
        }
        else
        {
            _mainWindowVisible = false;
            
            // Créer une fenêtre cachée pour les raccourcis clavier
            var hiddenWindow = new Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            hiddenWindow.Show();
            hiddenWindow.Hide();
            _hotkeyService?.Initialize(hiddenWindow);
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
        
        // Disposer le service de raccourcis clavier
        if (_hotkeyService != null)
        {
            _hotkeyService.Dispose();
            _hotkeyService = null;
        }
        
        // Disposer le service de transition
        if (_transitionService != null)
        {
            _transitionService.Dispose();
            _transitionService = null;
        }
        
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
            _rotationService.AnimatedWallpaperRequested -= OnAnimatedWallpaperRequested;
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
        
        if (_dynamicService != null)
        {
            _dynamicService.Stop();
            _dynamicService.Dispose();
            _dynamicService = null;
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
                // Rafraîchir le wallpaper dynamique si actif
                _dynamicService?.Refresh();
                
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
    
    public static DynamicWallpaperService DynamicService
    {
        get
        {
            if (!_isInitialized || _dynamicService == null)
                throw new InvalidOperationException("App non initialisée");
            return _dynamicService;
        }
    }
    
    public static HotkeyService HotkeyService
    {
        get
        {
            if (!_isInitialized || _hotkeyService == null)
                throw new InvalidOperationException("App non initialisée");
            return _hotkeyService;
        }
    }
    
    public static TransitionService TransitionService
    {
        get
        {
            if (!_isInitialized || _transitionService == null)
                throw new InvalidOperationException("App non initialisée");
            return _transitionService;
        }
    }
    
    public static bool IsInitialized => _isInitialized;
    
    public static void SetMainWindowVisible(bool visible) => _mainWindowVisible = visible;
    
    #endregion
}
