using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Shared.Logging;
using CleanUninstaller.ViewModels;

namespace CleanUninstaller.Services;

/// <summary>
/// Configuration et accès au conteneur d'injection de dépendances
/// Centralise la configuration de tous les services de l'application
/// </summary>
public static class ServiceContainer
{
    private static IServiceProvider? _serviceProvider;
    private static readonly object _lock = new();
    private static Func<XamlRoot?>? _xamlRootProvider;

    /// <summary>
    /// Configure le provider de XamlRoot (doit être appelé avant d'utiliser les services UI)
    /// </summary>
    public static void SetXamlRootProvider(Func<XamlRoot?> provider)
    {
        _xamlRootProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Obtient le fournisseur de services (lazy initialization thread-safe)
    /// </summary>
    public static IServiceProvider Services
    {
        get
        {
            if (_serviceProvider == null)
            {
                lock (_lock)
                {
                    _serviceProvider ??= ConfigureServices();
                }
            }
            return _serviceProvider;
        }
    }

    /// <summary>
    /// Configure tous les services de l'application
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // ==========================================
        // Services Singleton (une seule instance)
        // ==========================================
        
        // Logger en premier - utilisé par tous les autres services
        // Enregistré comme ILoggerService (de Shared.Logging) pour compatibilité
        services.AddSingleton<ILoggerService, LoggerService>();
        
        // Services de configuration
        services.AddSingleton<Interfaces.ISettingsService, SettingsService>();
        
        // Services de registre et système
        services.AddSingleton<Interfaces.IRegistryService, RegistryService>();
        services.AddSingleton<Interfaces.IWindowsAppService, WindowsAppService>();
        
        // Moniteur d'installation (conserve l'état)
        services.AddSingleton<Interfaces.IInstallationMonitorService, InstallationMonitorService>();
        
        // Service de dialogues (singleton car utilise le même XamlRoot)
        services.AddSingleton<Interfaces.IDialogService>(sp => 
            new DialogService(_xamlRootProvider ?? (() => null)));

        // ==========================================
        // Services Transient (nouvelle instance à chaque demande)
        // ==========================================
        
        // Scanners - peuvent être utilisés en parallèle
        services.AddTransient<Interfaces.IProgramScannerService, ProgramScannerService>();
        services.AddTransient<Interfaces.IResidualScannerService, ResidualScannerService>();
        services.AddTransient<Interfaces.IAdvancedDetectionService, AdvancedDetectionService>();
        
        // Service de désinstallation
        services.AddTransient<Interfaces.IUninstallService, UninstallService>();

        // ==========================================
        // ViewModels
        // ==========================================
        
        // ViewModels principaux
        services.AddTransient<MainViewModel>();
        services.AddTransient<InstallationMonitorViewModel>();
        services.AddTransient<StartupManagerViewModel>();
        services.AddTransient<EnhancedInstallationMonitorViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Obtient un service du conteneur (lève une exception si non trouvé)
    /// </summary>
    /// <typeparam name="T">Type du service</typeparam>
    /// <returns>Instance du service</returns>
    /// <exception cref="InvalidOperationException">Si le service n'est pas enregistré</exception>
    public static T GetService<T>() where T : class
    {
        return Services.GetRequiredService<T>();
    }

    /// <summary>
    /// Essaie d'obtenir un service du conteneur (retourne null si non trouvé)
    /// </summary>
    /// <typeparam name="T">Type du service</typeparam>
    /// <returns>Instance du service ou null</returns>
    public static T? TryGetService<T>() where T : class
    {
        return Services.GetService<T>();
    }

    /// <summary>
    /// Crée un scope pour les services scoped (si nécessaire à l'avenir)
    /// </summary>
    public static IServiceScope CreateScope()
    {
        return Services.CreateScope();
    }

    /// <summary>
    /// Libère les ressources du conteneur
    /// Appelé lors de la fermeture de l'application
    /// </summary>
    public static void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _serviceProvider = null;
        _xamlRootProvider = null;
    }
}

/// <summary>
/// Extensions pour faciliter l'accès aux services
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Résout un service depuis n'importe quel objet
    /// Usage: this.Resolve&lt;ILoggerService&gt;()
    /// </summary>
    public static T Resolve<T>(this object _) where T : class
        => ServiceContainer.GetService<T>();
    
    /// <summary>
    /// Essaie de résoudre un service depuis n'importe quel objet
    /// Usage: this.TryResolve&lt;ILoggerService&gt;()
    /// </summary>
    public static T? TryResolve<T>(this object _) where T : class
        => ServiceContainer.TryGetService<T>();
}
