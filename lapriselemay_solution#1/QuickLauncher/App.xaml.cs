using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using QuickLauncher.Models;
using QuickLauncher.Services;
using QuickLauncher.Views;
using H.NotifyIcon;

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
    
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickLauncher", "app.log");

    private static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Debug.WriteLine(line);
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        SetupExceptionHandling();
        
        try
        {
            Log("=== Démarrage QuickLauncher ===");
            base.OnStartup(e);
            
            _settings = AppSettings.Load();
            
            Log("Synchronisation registre démarrage...");
            SettingsWindow.SyncStartupRegistry();
            
            Log("Création IndexingService...");
            _indexingService = new IndexingService();
            
            Log("Démarrage indexation async...");
            _ = _indexingService.StartIndexingAsync();
            
            Log("Création icône système...");
            CreateTrayIcon();
            
            Log("Enregistrement hotkey...");
            _hotkeyService = new HotkeyService();
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _hotkeyService.Register();
            
            Log("Configuration réindexation auto...");
            SetupAutoReindex();
            
            Log("Démarrage terminé!");
        }
        catch (Exception ex)
        {
            Log($"ERREUR STARTUP: {ex}");
            MessageBox.Show($"Erreur au démarrage:\n{ex.Message}", "QuickLauncher", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            Log($"ERREUR UI: {ex.Exception}");
            ex.Handled = true;
        };
        
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            Log($"ERREUR FATALE: {ex.ExceptionObject}");
        };
        
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log($"ERREUR TASK: {ex.Exception}");
            ex.SetObserved();
        };
    }

    private void CreateTrayIcon()
    {
        try
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = $"QuickLauncher - {_settings.Hotkey.DisplayText} pour ouvrir",
                Icon = GetAppIcon(),
                ContextMenu = CreateContextMenu(),
                Visibility = Visibility.Visible
            };
            
            _trayIcon.TrayMouseDoubleClick += (_, _) => ShowLauncher();
            _trayIcon.ForceCreate();
            
            Log("Icône système créée");
        }
        catch (Exception ex)
        {
            Log($"ERREUR TrayIcon: {ex}");
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
        catch (Exception ex)
        {
            Log($"Erreur chargement icône: {ex.Message}");
        }
        
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
        Log("Hotkey pressé");
        Dispatcher.Invoke(ShowLauncher);
    }

    public void ShowLauncher()
    {
        try
        {
            if (_indexingService == null)
            {
                Log("IndexingService null!");
                return;
            }
            
            if (_launcherWindow == null || !_launcherWindow.IsLoaded)
            {
                _launcherWindow = new LauncherWindow(_indexingService);
                _launcherWindow.Closed += (_, _) => _launcherWindow = null;
                _launcherWindow.RequestOpenSettings += (_, _) => Dispatcher.Invoke(ShowSettings);
                _launcherWindow.RequestQuit += (_, _) => Dispatcher.Invoke(ExitApplication);
                _launcherWindow.RequestReindex += async (_, _) => await Dispatcher.Invoke(async () => await ReindexAsync());
            }
            
            _launcherWindow.Show();
            _launcherWindow.Activate();
            _launcherWindow.FocusSearchBox();
        }
        catch (Exception ex)
        {
            Log($"ERREUR ShowLauncher: {ex}");
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
            
            if (_trayIcon != null)
                _trayIcon.ToolTipText = $"QuickLauncher - {_settings.Hotkey.DisplayText} pour ouvrir";
        }
        catch (Exception ex)
        {
            Log($"ERREUR Settings: {ex}");
        }
    }
    
    public void SetupAutoReindex()
    {
        _autoReindexTimer?.Stop();
        _settings = AppSettings.Load();
        
        if (!_settings.AutoReindexEnabled)
        {
            Log("Réindexation auto désactivée");
            return;
        }
        
        _autoReindexTimer = new DispatcherTimer();
        
        if (_settings.AutoReindexMode == AutoReindexMode.Interval)
        {
            // Mode intervalle
            _autoReindexTimer.Interval = TimeSpan.FromMinutes(_settings.AutoReindexIntervalMinutes);
            _autoReindexTimer.Tick += async (_, _) =>
            {
                Log($"Réindexation auto (intervalle {_settings.AutoReindexIntervalMinutes} min)");
                await ReindexAsync();
            };
            
            Log($"Timer réindexation: toutes les {_settings.AutoReindexIntervalMinutes} minutes");
        }
        else
        {
            // Mode heure programmée
            _autoReindexTimer.Interval = TimeSpan.FromMinutes(1);
            _autoReindexTimer.Tick += async (_, _) =>
            {
                var now = DateTime.Now;
                var scheduled = _settings.AutoReindexScheduledTime.Split(':');
                
                if (scheduled.Length == 2 &&
                    int.TryParse(scheduled[0], out var hour) &&
                    int.TryParse(scheduled[1], out var minute) &&
                    now.Hour == hour && now.Minute == minute)
                {
                    Log($"Réindexation auto (programmée {_settings.AutoReindexScheduledTime})");
                    await ReindexAsync();
                }
            };
            
            Log($"Timer réindexation: programmé à {_settings.AutoReindexScheduledTime}");
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
                Log("Réindexation terminée");
            }
        }
        catch (Exception ex)
        {
            Log($"ERREUR Reindex: {ex}");
        }
    }
    
    private void ShowHelp()
    {
        var helpText = $"""
            🚀 QuickLauncher - Aide

            📌 Raccourcis clavier:
            • {_settings.Hotkey.DisplayText} - Ouvrir/Fermer QuickLauncher
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
        
        MessageBox.Show(helpText, "QuickLauncher - Aide", 
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExitApplication()
    {
        Log("Fermeture application...");
        
        _autoReindexTimer?.Stop();
        _hotkeyService?.Unregister();
        _hotkeyService?.Dispose();
        _indexingService?.Dispose();
        _trayIcon?.Dispose();
        
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("OnExit");
        
        _autoReindexTimer?.Stop();
        _hotkeyService?.Unregister();
        _hotkeyService?.Dispose();
        _indexingService?.Dispose();
        _trayIcon?.Dispose();
        
        base.OnExit(e);
    }
}
