using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Views;
using QuickLauncher.Services.CommandHandlers;
using QuickLauncher.ViewModels;
using Shared.Logging;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace QuickLauncher;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private HotkeyService? _hotkeyService;
    private LauncherWindow? _launcherWindow;
    private DispatcherTimer? _autoReindexTimer;
    private DateTime? _lastScheduledReindex;
    private readonly ILogger _logger = new FileLogger(appName: Constants.AppName);

    /// <summary>
    /// Conteneur d'injection de dépendances.
    /// Centralise la création et la durée de vie des services.
    /// </summary>
    public static ServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        SetupExceptionHandling();
        
        try
        {
            _logger.Info("=== Démarrage QuickLauncher ===");
            base.OnStartup(e);
            
            // === Configuration DI ===
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();
            
            var settingsProvider = Services.GetRequiredService<ISettingsProvider>();
            var settings = settingsProvider.Current;
            
            _logger.Info("Initialisation du cache d'icônes persistant...");
            IconExtractorService.InitializePersistentCache();
            
            _logger.Info("Initialisation du thème...");
            var themeService = Services.GetRequiredService<ThemeService>();
            themeService.Initialize();
            themeService.ApplyTheme(settings.Appearance.Theme);
            themeService.ApplyAccentColor(settings.Appearance.AccentColor);
            
            _logger.Info("Synchronisation registre démarrage...");
            SettingsWindow.SyncStartupRegistry();
            
            var indexingService = Services.GetRequiredService<IndexingService>();
            
            _logger.Info("Démarrage indexation intelligente...");
            var fileWatcherService = Services.GetRequiredService<FileWatcherService>();
            _ = indexingService.SmartStartIndexingAsync().ContinueWith(_ =>
            {
                // Démarrer le FileWatcher après l'indexation, même si elle a échoué
                try { fileWatcherService.Start(); }
                catch (Exception ex) { _logger.Warning($"Erreur démarrage FileWatcher: {ex.Message}"); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
            
            _logger.Info("Restauration des widgets de notes et minuteries...");
            var noteWidgetService = Services.GetRequiredService<NoteWidgetService>();
            noteWidgetService.RestoreWidgets();
            
            var timerWidgetService = Services.GetRequiredService<TimerWidgetService>();
            timerWidgetService.RestoreWidgets();
            
            _logger.Info("Création icône système...");
            CreateTrayIcon(settings);
            
            _logger.Info("Enregistrement hotkey...");
            _hotkeyService = new HotkeyService(settings.Hotkey);
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            
            if (!_hotkeyService.Register())
                _logger.Warning($"Impossible d'enregistrer le raccourci {settings.Hotkey.DisplayText}");
            
            _logger.Info("Configuration réindexation auto...");
            SetupAutoReindex();
            
            _logger.Info("Démarrage terminé!");
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur au démarrage", ex);
            MessageBox.Show($"Erreur au démarrage:\n{ex.Message}", Constants.AppName, 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Configure les services dans le conteneur DI.
    /// </summary>
    private void ConfigureServices(IServiceCollection services)
    {
        // Logger partagé
        services.AddSingleton<ILogger>(_logger);
        
        // Settings centralisés (cache en mémoire, événement SettingsChanged)
        services.AddSingleton<ISettingsProvider, SettingsProvider>();
        
        // Services principaux
        services.AddSingleton<FolderFingerprintService>();
        services.AddSingleton<IndexingService>();
        services.AddSingleton<AliasService>();
        
        // FileWatcher (optionnel, enregistré mais peut échouer à l'init)
        services.AddSingleton<FileWatcherService>();
        
        // Services de widgets (migrés depuis singletons manuels)
        services.AddSingleton<NotesService>();
        services.AddSingleton<NoteWidgetService>();
        services.AddSingleton<TimerWidgetService>();
        
        // Recherche universelle (Everything / Windows Search / directe)
        services.AddSingleton<UniversalSearchService>();
        
        // Service de recherche (scoring, filtrage) — Amélioration #3
        services.AddSingleton<SearchService>();
        
        // Thème (gère Dark/Light/Auto/System) — Amélioration #4
        services.AddSingleton<ThemeService>();
        
        // Chargement d'icônes (Amélioration #1/#5)
        services.AddSingleton<IIconLoader, IconLoaderService>();
        
        // Intégrations web (météo, traduction)
        services.AddSingleton<WebIntegrationService>();
        
        // Assistant IA
        services.AddSingleton<AiChatService>();
        
        // === Command Handlers (chaque handler gère un type de commande :xxx) ===
        services.AddSingleton<ICommandHandler, WeatherCommandHandler>();
        services.AddSingleton<ICommandHandler, TranslationCommandHandler>();
        services.AddSingleton<ICommandHandler, AiCommandHandler>();
        services.AddSingleton<ICommandHandler, WindowsSearchCommandHandler>();
        services.AddSingleton<CommandRouter>();
        
        // === System Control Executor (exécution des commandes système via Entrée) ===
        services.AddSingleton<ISystemControlExecutor, SystemControlExecutor>();
        
        // === ViewModel et fenêtre principale (singletons réutilisés entre Show/Hide) ===
        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<LauncherWindow>();
        
        _logger.Info("Services DI configurés");
    }

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            _logger.Error("Erreur UI non gérée", ex.Exception);
            ex.Handled = true;
        };
        
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            _logger.Error("Erreur fatale", ex.ExceptionObject as Exception);
        };
        
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            _logger.Error("Erreur Task non observée", ex.Exception);
            ex.SetObserved();
        };
    }

    private void CreateTrayIcon(AppSettings settings)
    {
        try
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = $"{Constants.AppName} - {settings.Hotkey.DisplayText} pour ouvrir",
                Icon = GetAppIcon(),
                ContextMenu = CreateContextMenu(settings),
                Visibility = Visibility.Visible
            };
            
            _trayIcon.TrayMouseDoubleClick += (_, _) => ShowLauncher();
            _trayIcon.ForceCreate();
            
            _logger.Info("Icône système créée");
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur création TrayIcon", ex);
        }
    }
    
    private static Icon GetAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            var streamInfo = GetResourceStream(uri);
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                return new Icon(stream);
            }
        }
        catch { /* Utilise l'icône par défaut */ }
        
        return SystemIcons.Application;
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu(AppSettings settings)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        
        AddMenuItem(menu, $"Ouvrir ({settings.Hotkey.DisplayText})", ShowLauncher);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddMenuItem(menu, "⚙️ Paramètres...", ShowSettings);
        AddMenuItem(menu, "🔄 Réindexer", async () => await ReindexAsync());
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddMenuItem(menu, "❓ Aide", ShowHelp);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddMenuItem(menu, "🚪 Quitter", ExitApplication);
        
        return menu;
    }
    
    private static void AddMenuItem(System.Windows.Controls.ContextMenu menu, string header, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _logger.Info("Hotkey pressé");
        Dispatcher.Invoke(ShowLauncher);
    }

    public void ShowLauncher()
    {
        try
        {
            // La fenêtre est un singleton DI : réutilisée entre Show/Hide,
            // plus besoin de résoudre manuellement chaque service.
            if (_launcherWindow is not { IsLoaded: true })
            {
                _launcherWindow = Services.GetRequiredService<LauncherWindow>();
                _launcherWindow.Closed += (_, _) => _launcherWindow = null;
                _launcherWindow.RequestOpenSettings += (_, _) => Dispatcher.Invoke(ShowSettings);
                _launcherWindow.RequestQuit += (_, _) => Dispatcher.Invoke(ExitApplication);
                _launcherWindow.RequestReindex += async (_, _) => await Dispatcher.InvokeAsync(async () => await ReindexAsync());
            }
            
            _launcherWindow.Show();
            _launcherWindow.Activate();
            _launcherWindow.FocusSearchBox();
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur ShowLauncher", ex);
        }
    }

    private void ShowSettings()
    {
        try
        {
            var indexingService = Services.GetRequiredService<IndexingService>();
            var settingsProvider = Services.GetRequiredService<ISettingsProvider>();
            var themeService = Services.GetRequiredService<ThemeService>();
            var universalSearchService = Services.GetRequiredService<UniversalSearchService>();
            
            var settingsWindow = new SettingsWindow(indexingService, settingsProvider, themeService, universalSearchService);
            settingsWindow.ShowDialog();
            
            // Les paramètres sont déjà sauvegardés via le provider, mais on force un reload
            // pour être sûr de capter les modifications externes
            settingsProvider.Reload();
            var settings = settingsProvider.Current;
            
            // Réappliquer le thème et la couleur d'accent
            themeService.ApplyTheme(settings.Appearance.Theme);
            themeService.ApplyAccentColor(settings.Appearance.AccentColor);
            
            if (_trayIcon != null)
                _trayIcon.ToolTipText = $"{Constants.AppName} - {settings.Hotkey.DisplayText} pour ouvrir";
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur Settings", ex);
        }
    }
    
    public void SetupAutoReindex()
    {
        _autoReindexTimer?.Stop();
        var settingsProvider = Services.GetRequiredService<ISettingsProvider>();
        var settings = settingsProvider.Current;
        
        if (!settings.Search.AutoReindexEnabled)
        {
            _logger.Info("Réindexation auto désactivée");
            return;
        }
        
        _autoReindexTimer = new DispatcherTimer();
        
        if (settings.Search.AutoReindexMode == AutoReindexMode.Interval)
        {
            _autoReindexTimer.Interval = TimeSpan.FromMinutes(settings.Search.AutoReindexIntervalMinutes);
            _autoReindexTimer.Tick += async (_, _) =>
            {
                _logger.Info($"Réindexation auto (intervalle {settings.Search.AutoReindexIntervalMinutes} min)");
                await ReindexAsync();
            };
            
            _logger.Info($"Timer réindexation: toutes les {settings.Search.AutoReindexIntervalMinutes} minutes");
        }
        else
        {
            _autoReindexTimer.Interval = TimeSpan.FromMinutes(1);
            _autoReindexTimer.Tick += async (_, _) =>
            {
                var now = DateTime.Now;
                var parts = settings.Search.AutoReindexScheduledTime.Split(':');
                
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var hour) &&
                    int.TryParse(parts[1], out var minute) &&
                    now.Hour == hour && now.Minute == minute &&
                    _lastScheduledReindex?.Date != now.Date)
                {
                    _lastScheduledReindex = now;
                    _logger.Info($"Réindexation auto (programmée {settings.Search.AutoReindexScheduledTime})");
                    await ReindexAsync();
                }
            };
            
            _logger.Info($"Timer réindexation: programmé à {settings.Search.AutoReindexScheduledTime}");
        }
        
        _autoReindexTimer.Start();
    }

    private async Task ReindexAsync()
    {
        try
        {
            var indexingService = Services.GetRequiredService<IndexingService>();
            await indexingService.ReindexAsync();
            _logger.Info("Réindexation terminée");
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur Reindex", ex);
        }
    }
    
    private void ShowHelp()
    {
        var settingsProvider = Services.GetRequiredService<ISettingsProvider>();
        var settings = settingsProvider.Current;
        
        var helpText = $"""
            🚀 {Constants.AppName} - Aide

            📌 Raccourcis clavier:
            • {settings.Hotkey.DisplayText} - Ouvrir/Fermer {Constants.AppName}
            • Ctrl+, - Ouvrir les paramètres
            • Ctrl+R - Réindexer
            • Ctrl+Q - Quitter
            • Échap - Fermer la fenêtre

            📌 Commandes spéciales:
            • :settings - Ouvrir les paramètres
            • :reload - Réindexer les fichiers
            • :history - Voir l'historique
            • :clear - Effacer l'historique
            • :help ou ? - Afficher l'aide
            • :quit - Quitter l'application

            📌 Recherche web (préfixes):
            • g [texte] - Recherche Google
            • yt [texte] - Recherche YouTube
            • gh [texte] - Recherche GitHub
            • so [texte] - Recherche Stack Overflow
            """;
        
        MessageBox.Show(helpText, $"{Constants.AppName} - Aide", 
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExitApplication()
    {
        _logger.Info("Fermeture application...");
        Cleanup();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger.Info("OnExit");
        Cleanup();
        base.OnExit(e);
    }
    
    private void Cleanup()
    {
        _autoReindexTimer?.Stop();
        _hotkeyService?.Unregister();
        _hotkeyService?.Dispose();
        
        // Fermer les widgets avant le dispose DI (ils ont des fenêtres WPF à fermer)
        if (Services != null)
        {
            Services.GetService<NoteWidgetService>()?.CloseAll();
            Services.GetService<TimerWidgetService>()?.CloseAll();
            
            // ServiceProvider.Dispose() appelle automatiquement Dispose()
            // sur tous les singletons IDisposable (IndexingService, FileWatcherService,
            // AliasService, etc.) — pas besoin de les disposer individuellement.
            Services.Dispose();
        }
        
        _trayIcon?.Dispose();
        DesktopAttachHelper.Shutdown();
        // ThemeService.Shutdown() est appelé automatiquement via Dispose() par le conteneur DI
    }
}
