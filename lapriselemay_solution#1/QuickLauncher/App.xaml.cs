using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
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
            var icon = GetOrCreateAppIcon();
            
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
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
    
    private static Icon GetOrCreateAppIcon()
    {
        try
        {
            // Chemin du fichier ICO dans AppData
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QuickLauncher");
            Directory.CreateDirectory(appDataPath);
            var icoPath = Path.Combine(appDataPath, "app.ico");
            
            // Si le fichier existe et est valide, le charger
            if (File.Exists(icoPath))
            {
                try
                {
                    return new Icon(icoPath);
                }
                catch
                {
                    // Fichier corrompu, le recréer
                    File.Delete(icoPath);
                }
            }
            
            // Créer le fichier ICO
            CreateIcoFile(icoPath);
            
            // Charger l'icône créée
            if (File.Exists(icoPath))
            {
                return new Icon(icoPath);
            }
        }
        catch (Exception ex)
        {
            Log($"Erreur création icône: {ex.Message}");
        }
        
        return SystemIcons.Application;
    }
    
    private static void CreateIcoFile(string path)
    {
        // Créer un bitmap 32x32
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        
        // Cercle bleu
        using var blueBrush = new SolidBrush(System.Drawing.Color.FromArgb(255, 0, 120, 212));
        g.FillEllipse(blueBrush, 1, 1, 29, 29);
        
        // Loupe blanche
        using var whitePen = new Pen(System.Drawing.Color.White, 2.5f);
        whitePen.StartCap = LineCap.Round;
        whitePen.EndCap = LineCap.Round;
        g.DrawEllipse(whitePen, 8, 6, 12, 12);
        g.DrawLine(whitePen, 18, 16, 24, 22);
        
        // Sauvegarder comme fichier ICO
        using var fs = new FileStream(path, FileMode.Create);
        
        // Header ICO (6 bytes)
        fs.Write(new byte[] { 0, 0, 1, 0, 1, 0 }, 0, 6);
        
        // Convertir bitmap en données PNG
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        var pngData = pngStream.ToArray();
        
        // Directory entry (16 bytes)
        fs.WriteByte(32);  // Width
        fs.WriteByte(32);  // Height
        fs.WriteByte(0);   // Color palette
        fs.WriteByte(0);   // Reserved
        fs.Write(BitConverter.GetBytes((ushort)1), 0, 2);  // Color planes
        fs.Write(BitConverter.GetBytes((ushort)32), 0, 2); // Bits per pixel
        fs.Write(BitConverter.GetBytes(pngData.Length), 0, 4); // Size of image data
        fs.Write(BitConverter.GetBytes(22), 0, 4); // Offset to image data (6 + 16 = 22)
        
        // Image data (PNG)
        fs.Write(pngData, 0, pngData.Length);
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
