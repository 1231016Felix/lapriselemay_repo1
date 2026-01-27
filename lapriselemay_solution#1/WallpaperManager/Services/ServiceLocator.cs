using Microsoft.Extensions.DependencyInjection;
using WallpaperManager.Services.Messaging;
using WallpaperManager.ViewModels;

namespace WallpaperManager.Services;

/// <summary>
/// Conteneur d'injection de dépendances pour l'application.
/// Centralise la création et la gestion du cycle de vie des services.
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _serviceProvider;
    private static IServiceScope? _scope;
    
    /// <summary>
    /// Le fournisseur de services configuré.
    /// </summary>
    public static IServiceProvider Services => _serviceProvider 
        ?? throw new InvalidOperationException("ServiceLocator n'est pas initialisé. Appelez Configure() d'abord.");
    
    /// <summary>
    /// Indique si le ServiceLocator est initialisé.
    /// </summary>
    public static bool IsConfigured => _serviceProvider != null;
    
    /// <summary>
    /// Configure le conteneur de dépendances.
    /// </summary>
    public static void Configure(Action<IServiceCollection>? additionalConfiguration = null)
    {
        var services = new ServiceCollection();
        
        // Services singleton (une seule instance pour toute l'application)
        ConfigureSingletonServices(services);
        
        // Services transient (nouvelle instance à chaque demande)
        ConfigureTransientServices(services);
        
        // ViewModels
        ConfigureViewModels(services);
        
        // Configuration additionnelle
        additionalConfiguration?.Invoke(services);
        
        _serviceProvider = services.BuildServiceProvider();
        
        System.Diagnostics.Debug.WriteLine("ServiceLocator configuré");
    }
    
    private static void ConfigureSingletonServices(IServiceCollection services)
    {
        // Messenger pour la communication inter-composants
        services.AddSingleton(Messenger.Default);
        
        // Services de base qui existent pour toute la durée de l'application
        services.AddSingleton(ThumbnailService.Instance);
        
        // Services de wallpaper (créés à la demande, singleton pour l'app)
        services.AddSingleton<WallpaperRotationService>();
        services.AddSingleton<AnimatedWallpaperService>();
        services.AddSingleton<DynamicWallpaperService>();
        services.AddSingleton<TransitionService>();
        services.AddSingleton<HotkeyService>();
        
        // Services d'API (peuvent être lourds, une seule instance)
        services.AddSingleton<UnsplashService>();
        services.AddSingleton<PexelsService>();
        services.AddSingleton<PixabayService>();
    }
    
    private static void ConfigureTransientServices(IServiceCollection services)
    {
        // Pas de services transient pour l'instant
        // DuplicateDetectionService et SunCalculatorService sont statiques
    }
    
    private static void ConfigureViewModels(IServiceCollection services)
    {
        // ViewModel principal - singleton car une seule fenêtre principale
        services.AddSingleton<MainViewModel>();
    }
    
    /// <summary>
    /// Récupère un service du conteneur.
    /// </summary>
    public static T GetService<T>() where T : notnull
        => Services.GetRequiredService<T>();
    
    /// <summary>
    /// Tente de récupérer un service du conteneur.
    /// </summary>
    public static T? TryGetService<T>() where T : class
        => Services.GetService<T>();
    
    /// <summary>
    /// Crée un scope pour des services scoped.
    /// </summary>
    public static IServiceScope CreateScope()
        => Services.CreateScope();
    
    /// <summary>
    /// Libère les ressources du conteneur.
    /// </summary>
    public static void Dispose()
    {
        _scope?.Dispose();
        _scope = null;
        
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        _serviceProvider = null;
        
        System.Diagnostics.Debug.WriteLine("ServiceLocator disposé");
    }
}

/// <summary>
/// Extensions pour faciliter l'injection de dépendances.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Ajoute les services de wallpaper à la collection.
    /// </summary>
    public static IServiceCollection AddWallpaperServices(this IServiceCollection services)
    {
        services.AddSingleton<WallpaperRotationService>();
        services.AddSingleton<AnimatedWallpaperService>();
        services.AddSingleton<DynamicWallpaperService>();
        services.AddSingleton<TransitionService>();
        return services;
    }
    
    /// <summary>
    /// Ajoute les services d'API d'images à la collection.
    /// </summary>
    public static IServiceCollection AddImageApiServices(this IServiceCollection services)
    {
        services.AddSingleton<UnsplashService>();
        services.AddSingleton<PexelsService>();
        services.AddSingleton<PixabayService>();
        return services;
    }
}
