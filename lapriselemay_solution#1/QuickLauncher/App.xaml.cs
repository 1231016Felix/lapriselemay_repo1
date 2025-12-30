using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using QuickLauncher.Services;
using QuickLauncher.Views;
using H.NotifyIcon;

namespace QuickLauncher;

public partial class App : System.Windows.Application
{
    private TaskbarIcon? _trayIcon;
    private HotkeyService? _hotkeyService;
    private LauncherWindow? _launcherWindow;
    private IndexingService? _indexingService;
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
        // Gestion globale des erreurs
        DispatcherUnhandledException += (s, ex) =>
        {
            Log($"ERREUR UI: {ex.Exception}");
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            Log($"ERREUR FATALE: {ex.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            Log($"ERREUR TASK: {ex.Exception}");
            ex.SetObserved();
        };

        try
        {
            Log("=== Démarrage QuickLauncher ===");
            base.OnStartup(e);
            
            Log("Synchronisation registre démarrage...");
            Views.SettingsWindow.SyncStartupRegistry();
            
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
            
            Log("Démarrage terminé avec succès!");
        }
        catch (Exception ex)
        {
            Log($"ERREUR STARTUP: {ex}");
            System.Windows.MessageBox.Show($"Erreur au démarrage:\n{ex.Message}", "QuickLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateTrayIcon()
    {
        try
        {
            var settings = Models.AppSettings.Load();
            
            // Créer/charger l'icône
            var icon = GetAppIcon();
            
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = $"QuickLauncher - {settings.Hotkey.DisplayText} pour ouvrir",
                Icon = icon,
                ContextMenu = CreateContextMenu(),
                Visibility = Visibility.Visible
            };
            _trayIcon.TrayMouseDoubleClick += (_, _) => ShowLauncher();
            
            // Forcer l'affichage
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
            // Charger l'icône intégrée dans les ressources
            var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Log($"Erreur chargement icône ressource: {ex.Message}");
        }
        
        return SystemIcons.Application;
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var settings = Models.AppSettings.Load();
        
        var openItem = new System.Windows.Controls.MenuItem { Header = $"Ouvrir ({settings.Hotkey.DisplayText})" };
        openItem.Click += (_, _) => ShowLauncher();
        
        var settingsItem = new System.Windows.Controls.MenuItem { Header = "⚙️ Paramètres..." };
        settingsItem.Click += (_, _) => ShowSettings();
        
        var reindexItem = new System.Windows.Controls.MenuItem { Header = "🔄 Réindexer" };
        reindexItem.Click += async (_, _) => await ReindexAsync();
        
        var separator = new System.Windows.Controls.Separator();
        
        var helpItem = new System.Windows.Controls.MenuItem { Header = "❓ Aide" };
        helpItem.Click += (_, _) => ShowHelp();
        
        var separator2 = new System.Windows.Controls.Separator();
        
        var exitItem = new System.Windows.Controls.MenuItem { Header = "🚪 Quitter" };
        exitItem.Click += (_, _) => ExitApplication();
        
        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(reindexItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(helpItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exitItem);
        
        return menu;
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Log("Hotkey pressé!");
        Dispatcher.Invoke(ShowLauncher);
    }

    public void ShowLauncher()
    {
        try
        {
            Log("ShowLauncher appelé");
            if (_indexingService == null)
            {
                Log("IndexingService est null!");
                return;
            }
            
            if (_launcherWindow == null || !_launcherWindow.IsLoaded)
            {
                Log("Création nouvelle fenêtre...");
                _launcherWindow = new LauncherWindow(_indexingService);
                _launcherWindow.Closed += (_, _) => _launcherWindow = null;
                
                // Connecter les événements de la fenêtre
                _launcherWindow.RequestOpenSettings += (_, _) => Dispatcher.Invoke(ShowSettings);
                _launcherWindow.RequestQuit += (_, _) => Dispatcher.Invoke(ExitApplication);
                _launcherWindow.RequestReindex += async (_, _) => await Dispatcher.Invoke(async () => await ReindexAsync());
            }
            
            Log("Affichage fenêtre...");
            _launcherWindow.Show();
            _launcherWindow.Activate();
            _launcherWindow.FocusSearchBox();
            Log("Fenêtre affichée");
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
            Log("Ouverture paramètres...");
            var settingsWindow = new SettingsWindow(_indexingService);
            settingsWindow.ShowDialog();
            
            // Recharger les paramètres après fermeture
            var settings = Models.AppSettings.Load();
            
            // Mettre à jour le tooltip de l'icône système
            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = $"QuickLauncher - {settings.Hotkey.DisplayText} pour ouvrir";
            }
            
            Log("Paramètres fermés");
        }
        catch (Exception ex)
        {
            Log($"ERREUR Settings: {ex}");
        }
    }
    
    private async Task ReindexAsync()
    {
        try
        {
            Log("Début réindexation...");
            if (_indexingService != null)
            {
                await _indexingService.ReindexAsync();
                Log("Réindexation terminée!");
            }
        }
        catch (Exception ex)
        {
            Log($"ERREUR Reindex: {ex}");
        }
    }
    
    private void ShowHelp()
    {
        var settings = Models.AppSettings.Load();
        var helpText = $@"🚀 QuickLauncher - Aide

📌 Raccourcis clavier:
• {settings.Hotkey.DisplayText} - Ouvrir/Fermer QuickLauncher
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
• so [texte] - Recherche Stack Overflow";
        
        System.Windows.MessageBox.Show(helpText, "QuickLauncher - Aide", 
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExitApplication()
    {
        Log("Fermeture application...");
        _hotkeyService?.Unregister();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("OnExit");
        _hotkeyService?.Unregister();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
