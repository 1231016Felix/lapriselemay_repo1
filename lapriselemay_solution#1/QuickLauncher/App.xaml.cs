using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Views;
using Shared.Logging;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace QuickLauncher;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private HotkeyService? _hotkeyService;
    private LauncherWindow? _launcherWindow;
    private IndexingService? _indexingService;
    private DispatcherTimer? _autoReindexTimer;
    private AppSettings _settings = null!;
    private readonly ILogger _logger = new FileLogger(appName: Constants.AppName);

    protected override async void OnStartup(StartupEventArgs e)
    {
        SetupExceptionHandling();
        
        try
        {
            _logger.Info("=== Démarrage QuickLauncher ===");
            base.OnStartup(e);
            
            _settings = AppSettings.Load();
            
            _logger.Info("Initialisation du cache d'icônes persistant...");
            IconExtractorService.InitializePersistentCache();
            
            _logger.Info("Initialisation du thème...");
            ThemeService.Initialize();
            ThemeService.ApplyTheme(_settings.Theme);
            ThemeService.ApplyAccentColor(_settings.AccentColor);
            
            _logger.Info("Synchronisation registre démarrage...");
            SettingsWindow.SyncStartupRegistry();
            
            _logger.Info("Création IndexingService...");
            _indexingService = new IndexingService(_logger);
            
            _logger.Info("Démarrage indexation async...");
            _ = _indexingService.StartIndexingAsync();
            
            _logger.Info("Création icône système...");
            CreateTrayIcon();
            
            _logger.Info("Enregistrement hotkey...");
            _hotkeyService = new HotkeyService(_settings.Hotkey);
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            
            if (!_hotkeyService.Register())
                _logger.Warning($"Impossible d'enregistrer le raccourci {_settings.Hotkey.DisplayText}");
            
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

    private void CreateTrayIcon()
    {
        try
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = $"{Constants.AppName} - {_settings.Hotkey.DisplayText} pour ouvrir",
                Icon = GetAppIcon(),
                ContextMenu = CreateContextMenu(),
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

    private System.Windows.Controls.ContextMenu CreateContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        
        AddMenuItem(menu, $"Ouvrir ({_settings.Hotkey.DisplayText})", ShowLauncher);
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
            if (_indexingService == null)
            {
                _logger.Warning("IndexingService est null");
                return;
            }
            
            if (_launcherWindow is not { IsLoaded: true })
            {
                _launcherWindow = new LauncherWindow(_indexingService);
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
            var settingsWindow = new SettingsWindow(_indexingService);
            settingsWindow.ShowDialog();
            
            // Recharger les paramètres
            _settings = AppSettings.Load();
            
            // Réappliquer le thème et la couleur d'accent
            ThemeService.ApplyTheme(_settings.Theme);
            ThemeService.ApplyAccentColor(_settings.AccentColor);
            
            if (_trayIcon != null)
                _trayIcon.ToolTipText = $"{Constants.AppName} - {_settings.Hotkey.DisplayText} pour ouvrir";
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur Settings", ex);
        }
    }
    
    public void SetupAutoReindex()
    {
        _autoReindexTimer?.Stop();
        _settings = AppSettings.Load();
        
        if (!_settings.AutoReindexEnabled)
        {
            _logger.Info("Réindexation auto désactivée");
            return;
        }
        
        _autoReindexTimer = new DispatcherTimer();
        
        if (_settings.AutoReindexMode == AutoReindexMode.Interval)
        {
            _autoReindexTimer.Interval = TimeSpan.FromMinutes(_settings.AutoReindexIntervalMinutes);
            _autoReindexTimer.Tick += async (_, _) =>
            {
                _logger.Info($"Réindexation auto (intervalle {_settings.AutoReindexIntervalMinutes} min)");
                await ReindexAsync();
            };
            
            _logger.Info($"Timer réindexation: toutes les {_settings.AutoReindexIntervalMinutes} minutes");
        }
        else
        {
            _autoReindexTimer.Interval = TimeSpan.FromMinutes(1);
            _autoReindexTimer.Tick += async (_, _) =>
            {
                var now = DateTime.Now;
                var parts = _settings.AutoReindexScheduledTime.Split(':');
                
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var hour) &&
                    int.TryParse(parts[1], out var minute) &&
                    now.Hour == hour && now.Minute == minute)
                {
                    _logger.Info($"Réindexation auto (programmée {_settings.AutoReindexScheduledTime})");
                    await ReindexAsync();
                }
            };
            
            _logger.Info($"Timer réindexation: programmé à {_settings.AutoReindexScheduledTime}");
        }
        
        _autoReindexTimer.Start();
    }

    private async Task ReindexAsync()
    {
        try
        {
            if (_indexingService != null)
            {
                await _indexingService.ReindexAsync();
                _logger.Info("Réindexation terminée");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur Reindex", ex);
        }
    }
    
    private void ShowHelp()
    {
        var helpText = $"""
            🚀 {Constants.AppName} - Aide

            📌 Raccourcis clavier:
            • {_settings.Hotkey.DisplayText} - Ouvrir/Fermer {Constants.AppName}
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
        _indexingService?.Dispose();
        _trayIcon?.Dispose();
        ThemeService.Shutdown();
    }
}
