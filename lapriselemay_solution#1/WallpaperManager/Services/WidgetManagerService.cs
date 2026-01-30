using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using WallpaperManager.Models;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfApplication = System.Windows.Application;
using WallpaperManager.Widgets.Base;
using WallpaperManager.Widgets.SystemMonitor;
using WallpaperManager.Widgets.Weather;
using WallpaperManager.Widgets.DiskStorage;
using WallpaperManager.Widgets.Battery;
using WallpaperManager.Widgets.QuickNotes;

namespace WallpaperManager.Services;

/// <summary>
/// Service de gestion des widgets sur le bureau.
/// Gère le cycle de vie, la persistance et l'affichage des widgets.
/// </summary>
public sealed class WidgetManagerService : IDisposable
{
    #region Native APIs pour détecter l'état des fenêtres
    
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);
    
    #endregion
    
    private readonly string _configPath;
    private readonly Dictionary<string, WidgetWindow> _activeWindows = [];
    private readonly Lock _lock = new();
    private readonly WeatherService _weatherService;
    private readonly DispatcherTimer _visibilityMonitor;
    private bool _disposed;
    
    public WidgetsSettings Settings { get; private set; } = new();
    
    public event EventHandler? SettingsChanged;
    
    public bool AreWidgetsVisible { get; private set; } = false;
    
    public WidgetManagerService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallpaperManager");
        
        Directory.CreateDirectory(appData);
        _configPath = Path.Combine(appData, "widgets.json");
        
        _weatherService = new WeatherService();
        
        // Timer pour surveiller la visibilité des widgets (détecte "Afficher le bureau")
        _visibilityMonitor = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _visibilityMonitor.Tick += OnVisibilityMonitorTick;
        
        LoadSettings();
    }
    
    /// <summary>
    /// Vérifie si les widgets ont été cachés par "Afficher le bureau" et les réaffiche.
    /// </summary>
    private void OnVisibilityMonitorTick(object? sender, EventArgs e)
    {
        if (_disposed || !AreWidgetsVisible) return;
        
        lock (_lock)
        {
            foreach (var window in _activeWindows.Values)
            {
                try
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd != IntPtr.Zero && !IsWindowVisible(hwnd))
                    {
                        // La fenêtre a été cachée (probablement par "Afficher le bureau")
                        WpfApplication.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            window.Show();
                            window.Activate();
                        });
                    }
                }
                catch
                {
                    // Ignorer les erreurs
                }
            }
        }
    }
    
    /// <summary>
    /// Charge les paramètres depuis le fichier.
    /// </summary>
    public void LoadSettings()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                Settings = JsonSerializer.Deserialize<WidgetsSettings>(json) ?? new WidgetsSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur chargement widgets: {ex.Message}");
            Settings = new WidgetsSettings();
        }
        
        // Ne plus créer de widgets par défaut
        // L'utilisateur les ajoutera manuellement via l'interface
    }
    
    /// <summary>
    /// Sauvegarde les paramètres dans le fichier.
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde widgets: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Crée les widgets par défaut.
    /// </summary>
    private void CreateDefaultWidgets()
    {
        // Widget System Monitor
        Settings.Widgets.Add(new WidgetConfig
        {
            Type = WidgetType.SystemMonitor,
            IsEnabled = true,
            IsVisible = true,
            Left = 50,
            Top = 50,
            Width = 280,
            Height = 200,
            BackgroundOpacity = 0.85
        });
        
        // Widget Météo
        Settings.Widgets.Add(new WidgetConfig
        {
            Type = WidgetType.Weather,
            IsEnabled = true,
            IsVisible = true,
            Left = 50,
            Top = 280,
            Width = 300,
            Height = 250,
            BackgroundOpacity = 0.85
        });
        
        SaveSettings();
    }
    
    /// <summary>
    /// Démarre tous les widgets activés.
    /// </summary>
    public void StartWidgets()
    {
        // Éviter les doubles démarrages (vérifier si des fenêtres sont déjà actives)
        lock (_lock)
        {
            if (_activeWindows.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"StartWidgets: {_activeWindows.Count} widgets déjà actifs, ignoré");
                return;
            }
        }
        
        System.Diagnostics.Debug.WriteLine($"StartWidgets appelé. WidgetsEnabled={Settings.WidgetsEnabled}, Count={Settings.Widgets.Count}");
        
        if (!Settings.WidgetsEnabled) return;
        
        foreach (var config in Settings.Widgets.Where(w => w.IsEnabled))
        {
            System.Diagnostics.Debug.WriteLine($"Démarrage widget: {config.Type} ({config.Id})");
            ShowWidget(config);
        }
        
        AreWidgetsVisible = true;
        
        // Démarrer le moniteur de visibilité
        _visibilityMonitor.Start();
    }
    
    /// <summary>
    /// Arrête tous les widgets.
    /// </summary>
    public void StopWidgets()
    {
        // Arrêter le moniteur de visibilité
        _visibilityMonitor.Stop();
        
        lock (_lock)
        {
            foreach (var window in _activeWindows.Values.ToList())
            {
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    window.Close();
                });
            }
            _activeWindows.Clear();
        }
        
        AreWidgetsVisible = false;
    }
    
    /// <summary>
    /// Bascule la visibilité de tous les widgets.
    /// </summary>
    public void ToggleWidgetsVisibility()
    {
        if (AreWidgetsVisible)
        {
            HideAllWidgets();
        }
        else
        {
            ShowAllWidgets();
        }
    }
    
    /// <summary>
    /// Masque tous les widgets sans les fermer.
    /// </summary>
    public void HideAllWidgets()
    {
        // Arrêter le moniteur pour ne pas réafficher automatiquement
        _visibilityMonitor.Stop();
        
        lock (_lock)
        {
            foreach (var window in _activeWindows.Values)
            {
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    window.AllowHide = true; // Permettre le masquage volontaire
                    window.Hide();
                });
            }
        }
        AreWidgetsVisible = false;
    }
    
    /// <summary>
    /// Affiche tous les widgets masqués.
    /// </summary>
    public void ShowAllWidgets()
    {
        lock (_lock)
        {
            foreach (var window in _activeWindows.Values)
            {
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    window.AllowHide = false; // Réactiver la protection
                    window.Show();
                });
            }
        }
        AreWidgetsVisible = true;
        
        // Redémarrer le moniteur
        _visibilityMonitor.Start();
    }
    
    /// <summary>
    /// Affiche un widget spécifique.
    /// </summary>
    public void ShowWidget(WidgetConfig config)
    {
        if (_disposed) return;
        
        System.Diagnostics.Debug.WriteLine($"ShowWidget: {config.Type} - Début");
        
        WpfApplication.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                lock (_lock)
                {
                    // Fermer la fenêtre existante si présente
                    if (_activeWindows.TryGetValue(config.Id, out var existing))
                    {
                        existing.Close();
                        _activeWindows.Remove(config.Id);
                    }
                    
                    // Créer le contrôle et le ViewModel appropriés
                    var (control, viewModel) = CreateWidgetContent(config);
                    
                    if (control == null || viewModel == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"ShowWidget: {config.Type} - Control ou ViewModel null!");
                        return;
                    }
                    
                    // Créer la fenêtre
                    var window = new WidgetWindow();
                    window.SetWidget(control, viewModel, config);
                    window.Closed += (s, e) =>
                    {
                        lock (_lock)
                        {
                            _activeWindows.Remove(config.Id);
                        }
                        // Sauvegarder la position
                        config.Left = window.Left;
                        config.Top = window.Top;
                        SaveSettings();
                    };
                    
                    _activeWindows[config.Id] = window;
                    window.Show();
                    System.Diagnostics.Debug.WriteLine($"ShowWidget: {config.Type} - Fenêtre affichée");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowWidget ERREUR: {ex}");
            }
        });
    }
    
    /// <summary>
    /// Masque un widget spécifique.
    /// </summary>
    public void HideWidget(string widgetId)
    {
        lock (_lock)
        {
            if (_activeWindows.TryGetValue(widgetId, out var window))
            {
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    window.Close();
                });
                _activeWindows.Remove(widgetId);
            }
        }
        
        // Mettre à jour la config
        var config = Settings.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (config != null)
        {
            config.IsVisible = false;
            SaveSettings();
        }
    }
    
    /// <summary>
    /// Crée le contenu d'un widget selon son type.
    /// </summary>
    private (WpfUserControl? control, WidgetViewModelBase? viewModel) CreateWidgetContent(WidgetConfig config)
    {
        return config.Type switch
        {
            WidgetType.SystemMonitor => CreateSystemMonitorWidget(config),
            WidgetType.Weather => CreateWeatherWidget(config),
            WidgetType.DiskStorage => CreateDiskStorageWidget(config),
            WidgetType.Battery => CreateBatteryWidget(config),
            WidgetType.Notes => CreateQuickNotesWidget(config),
            WidgetType.QuickNotes => CreateQuickNotesWidget(config),
            _ => (null, null)
        };
    }
    
    private (WpfUserControl, WidgetViewModelBase) CreateSystemMonitorWidget(WidgetConfig config)
    {
        var viewModel = new SystemMonitorWidgetViewModel();
        
        // Appliquer les paramètres personnalisés
        if (config.Settings.TryGetValue("RefreshInterval", out var interval))
        {
            viewModel.SetRefreshInterval(Convert.ToInt32(interval));
        }
        else
        {
            viewModel.SetRefreshInterval(Settings.SystemMonitorRefreshInterval);
        }
        
        var control = new SystemMonitorWidget();
        return (control, viewModel);
    }
    
    private (WpfUserControl, WidgetViewModelBase) CreateWeatherWidget(WidgetConfig config)
    {
        var viewModel = new WeatherWidgetViewModel(_weatherService);
        
        // Appliquer la localisation
        viewModel.SetLocation(
            Settings.WeatherLatitude,
            Settings.WeatherLongitude,
            Settings.WeatherCity);
        
        viewModel.SetRefreshInterval(Settings.WeatherRefreshInterval * 60);
        
        var control = new WeatherWidget();
        return (control, viewModel);
    }
    
    private (WpfUserControl, WidgetViewModelBase) CreateDiskStorageWidget(WidgetConfig config)
    {
        var viewModel = new DiskStorageWidgetViewModel();
        var control = new DiskStorageWidget();
        return (control, viewModel);
    }
    
    private (WpfUserControl, WidgetViewModelBase) CreateBatteryWidget(WidgetConfig config)
    {
        var viewModel = new BatteryWidgetViewModel();
        var control = new BatteryWidget();
        return (control, viewModel);
    }
    
    private (WpfUserControl, WidgetViewModelBase) CreateQuickNotesWidget(WidgetConfig config)
    {
        var viewModel = new QuickNotesWidgetViewModel();
        var control = new QuickNotesWidget();
        return (control, viewModel);
    }
    
    /// <summary>
    /// Ajoute un nouveau widget.
    /// </summary>
    public WidgetConfig AddWidget(WidgetType type)
    {
        var (width, height) = type switch
        {
            WidgetType.Weather => (300, 250),
            WidgetType.DiskStorage => (260, 180),
            WidgetType.Battery => (220, 140),
            WidgetType.Notes => (280, 300),
            WidgetType.QuickNotes => (280, 300),
            _ => (280, 200)
        };
        
        var config = new WidgetConfig
        {
            Type = type,
            IsEnabled = true,
            IsVisible = true,
            Left = 100 + (Settings.Widgets.Count * 50),
            Top = 100 + (Settings.Widgets.Count * 50),
            Width = width,
            Height = height,
            BackgroundOpacity = 0.85
        };
        
        Settings.Widgets.Add(config);
        SaveSettings();
        
        if (Settings.WidgetsEnabled)
        {
            ShowWidget(config);
        }
        
        return config;
    }
    
    /// <summary>
    /// Supprime un widget.
    /// </summary>
    public void RemoveWidget(string widgetId)
    {
        HideWidget(widgetId);
        
        var config = Settings.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (config != null)
        {
            Settings.Widgets.Remove(config);
            SaveSettings();
        }
    }
    
    /// <summary>
    /// Met à jour la configuration météo.
    /// </summary>
    public async Task UpdateWeatherLocation(string cityName)
    {
        var result = await _weatherService.GeocodeCity(cityName);
        if (result.HasValue)
        {
            Settings.WeatherCity = result.Value.name;
            Settings.WeatherLatitude = result.Value.lat;
            Settings.WeatherLongitude = result.Value.lon;
            SaveSettings();
            
            // Mettre à jour les widgets météo actifs
            RefreshWeatherWidgets();
        }
    }
    
    /// <summary>
    /// Rafraîchit tous les widgets météo.
    /// </summary>
    public void RefreshWeatherWidgets()
    {
        lock (_lock)
        {
            foreach (var config in Settings.Widgets.Where(w => w.Type == WidgetType.Weather))
            {
                if (_activeWindows.TryGetValue(config.Id, out var window))
                {
                    // Fermer et rouvrir pour appliquer les nouveaux paramètres
                    WpfApplication.Current?.Dispatcher.Invoke(() =>
                    {
                        window.Close();
                    });
                    _activeWindows.Remove(config.Id);
                    ShowWidget(config);
                }
            }
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _visibilityMonitor.Stop();
        StopWidgets();
        _weatherService.Dispose();
    }
}
