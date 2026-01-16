using Microsoft.UI.Xaml;
using CleanUninstaller.Services;
using CleanUninstaller.Services.Interfaces;
using CleanUninstaller.Views;
using Shared.Logging;

namespace CleanUninstaller;

/// <summary>
/// Application principale - Clean Uninstaller
/// Un désinstalleur intelligent avec détection avancée des résidus
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;
    private readonly Shared.Logging.ILoggerService _logger;

    /// <summary>
    /// Indique si l'application est complètement initialisée
    /// </summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// Obtient la fenêtre principale
    /// </summary>
    public static Window? MainWindow => (Current as App)?._mainWindow;

    #region Service Accessors (pour compatibilité avec le code existant)
    // Ces propriétés permettent un accès simplifié aux services principaux
    // Elles utilisent le ServiceContainer sous le capot
    
    /// <summary>
    /// Service de registre
    /// </summary>
    public static IRegistryService RegistryService => ServiceContainer.GetService<IRegistryService>();
    
    /// <summary>
    /// Scanner de programmes
    /// </summary>
    public static IProgramScannerService ProgramScanner => ServiceContainer.GetService<IProgramScannerService>();
    
    /// <summary>
    /// Scanner de résidus
    /// </summary>
    public static IResidualScannerService ResidualScanner => ServiceContainer.GetService<IResidualScannerService>();
    
    /// <summary>
    /// Service de désinstallation
    /// </summary>
    public static IUninstallService UninstallService => ServiceContainer.GetService<IUninstallService>();
    
    /// <summary>
    /// Service de paramètres
    /// </summary>
    public static ISettingsService SettingsService => ServiceContainer.GetService<ISettingsService>();
    
    /// <summary>
    /// Service des applications Windows Store
    /// </summary>
    public static IWindowsAppService WindowsAppService => ServiceContainer.GetService<IWindowsAppService>();
    
    /// <summary>
    /// Service de détection avancée
    /// </summary>
    public static IAdvancedDetectionService AdvancedDetection => ServiceContainer.GetService<IAdvancedDetectionService>();
    
    /// <summary>
    /// Moniteur d'installation
    /// </summary>
    public static IInstallationMonitorService InstallationMonitor => ServiceContainer.GetService<IInstallationMonitorService>();
    
    /// <summary>
    /// Service de logging
    /// </summary>
    public static Shared.Logging.ILoggerService Logger => ServiceContainer.GetService<Shared.Logging.ILoggerService>();
    #endregion

    public App()
    {
        // Configuration requise pour PublishSingleFile avec Windows App SDK
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        
        InitializeComponent();
        
        // Initialiser le logger en premier
        _logger = ServiceContainer.GetService<Shared.Logging.ILoggerService>();
        _logger.Info("Application démarrée");
        
        // Gestionnaires d'exceptions globaux
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.Info("OnLaunched - Création de la fenêtre principale");
        
        try
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += OnMainWindowClosed;
            _mainWindow.Activate();
            
            IsInitialized = true;
            _logger.Info("Application initialisée avec succès");
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur lors du lancement de l'application", ex);
            throw;
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _logger.Info("Fermeture de l'application");
        
        // Libérer les ressources du conteneur de services
        ServiceContainer.Dispose();
    }

    /// <summary>
    /// Gestion des exceptions non gérées UI
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger.Error("Exception UI non gérée", e.Exception);
        
        // Marquer comme géré pour éviter le crash si possible
        e.Handled = true;
    }

    /// <summary>
    /// Gestion des exceptions de tâches non observées
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.Error("Exception de tâche non observée", e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Gestion des exceptions du domaine d'application
    /// </summary>
    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger.Error($"Exception domaine non gérée (IsTerminating: {e.IsTerminating})", ex);
        }
    }
}
