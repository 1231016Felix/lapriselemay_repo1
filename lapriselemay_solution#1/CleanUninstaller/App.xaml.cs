using Microsoft.UI.Xaml;
using CleanUninstaller.Services;
using CleanUninstaller.Views;

namespace CleanUninstaller;

/// <summary>
/// Application principale - Clean Uninstaller
/// Un désinstalleur intelligent avec détection avancée des résidus
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;

    // Services partagés (Singleton)
    public static RegistryService RegistryService { get; } = new();
    public static ProgramScannerService ProgramScanner { get; } = new();
    public static ResidualScannerService ResidualScanner { get; } = new();
    public static UninstallService UninstallService { get; } = new();
    public static SettingsService SettingsService { get; } = new();
    public static WindowsAppService WindowsAppService { get; } = new();
    public static AdvancedDetectionService AdvancedDetection { get; } = new();

    public App()
    {
        // Configuration requise pour PublishSingleFile avec Windows App SDK
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }

    /// <summary>
    /// Gestion des exceptions non gérées
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Logger l'erreur
        System.Diagnostics.Debug.WriteLine($"Exception non gérée: {e.Exception}");
        
        // Marquer comme géré pour éviter le crash
        e.Handled = true;
    }

    /// <summary>
    /// Obtient la fenêtre principale
    /// </summary>
    public static Window? MainWindow => (Current as App)?._mainWindow;
}
