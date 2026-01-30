using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperManager.Models;
using WallpaperManager.Services;

namespace WallpaperManager.ViewModels;

/// <summary>
/// Partie du MainViewModel dédiée aux Widgets.
/// </summary>
public partial class MainViewModel
{
    private WidgetManagerService? _widgetManager;
    
    // Propriétés pour l'UI
    private bool _widgetsEnabled;
    public bool WidgetsEnabled
    {
        get => _widgetsEnabled;
        set
        {
            if (SetProperty(ref _widgetsEnabled, value) && _widgetManager != null)
            {
                _widgetManager.Settings.WidgetsEnabled = value;
                _widgetManager.SaveSettings();
                
                if (value)
                    _widgetManager.StartWidgets();
                else
                    _widgetManager.StopWidgets();
            }
        }
    }
    
    private bool _widgetsVisible;
    public bool WidgetsVisible
    {
        get => _widgetsVisible;
        set
        {
            if (SetProperty(ref _widgetsVisible, value) && _widgetManager != null)
            {
                if (value)
                    _widgetManager.ShowAllWidgets();
                else
                    _widgetManager.HideAllWidgets();
            }
        }
    }
    
    private string _weatherCity = "Montreal";
    public string WeatherCity
    {
        get => _weatherCity;
        set => SetProperty(ref _weatherCity, value);
    }
    
    private int _weatherRefreshInterval = 30;
    public int WeatherRefreshInterval
    {
        get => _weatherRefreshInterval;
        set
        {
            if (SetProperty(ref _weatherRefreshInterval, value) && _widgetManager != null)
            {
                _widgetManager.Settings.WeatherRefreshInterval = value;
                _widgetManager.SaveSettings();
            }
        }
    }
    
    private int _systemMonitorRefreshInterval = 2;
    public int SystemMonitorRefreshInterval
    {
        get => _systemMonitorRefreshInterval;
        set
        {
            if (SetProperty(ref _systemMonitorRefreshInterval, value) && _widgetManager != null)
            {
                _widgetManager.Settings.SystemMonitorRefreshInterval = value;
                _widgetManager.SaveSettings();
            }
        }
    }
    
    public ObservableCollection<WidgetConfigViewModel> WidgetConfigs { get; } = [];
    
    // Commands
    public ICommand ToggleWidgetsCommand => new RelayCommand(() => WidgetsVisible = !WidgetsVisible);
    public ICommand AddSystemMonitorWidgetCommand => new RelayCommand(AddSystemMonitorWidget);
    public ICommand AddWeatherWidgetCommand => new RelayCommand(AddWeatherWidget);
    public ICommand AddDiskStorageWidgetCommand => new RelayCommand(AddDiskStorageWidget);
    public ICommand AddBatteryWidgetCommand => new RelayCommand(AddBatteryWidget);
    public ICommand AddQuickNotesWidgetCommand => new RelayCommand(AddQuickNotesWidget);
    public ICommand RemoveWidgetCommand => new RelayCommand<WidgetConfigViewModel>(RemoveWidget);
    public ICommand UpdateWeatherCityCommand => new AsyncRelayCommand(UpdateWeatherCity);
    public ICommand RefreshWeatherCommand => new RelayCommand(RefreshWeather);
    
    /// <summary>
    /// Initialise le système de widgets.
    /// Utilise l'instance existante de App pour éviter les doublons.
    /// </summary>
    private void InitializeWidgets()
    {
        // Utiliser l'instance existante de App au lieu d'en créer une nouvelle
        if (App.IsInitialized)
        {
            _widgetManager = App.WidgetManagerService;
        }
        else
        {
            // Fallback si App n'est pas encore initialisée (ne devrait pas arriver)
            _widgetManager = new WidgetManagerService();
        }
        
        // Charger les paramètres
        _widgetsEnabled = _widgetManager.Settings.WidgetsEnabled;
        _widgetsVisible = _widgetManager.AreWidgetsVisible;
        _weatherCity = _widgetManager.Settings.WeatherCity;
        _weatherRefreshInterval = _widgetManager.Settings.WeatherRefreshInterval;
        _systemMonitorRefreshInterval = _widgetManager.Settings.SystemMonitorRefreshInterval;
        
        // Charger la liste des widgets
        RefreshWidgetList();
        
        OnPropertyChanged(nameof(WidgetsEnabled));
        OnPropertyChanged(nameof(WidgetsVisible));
        OnPropertyChanged(nameof(WeatherCity));
        OnPropertyChanged(nameof(WeatherRefreshInterval));
        OnPropertyChanged(nameof(SystemMonitorRefreshInterval));
    }
    
    /// <summary>
    /// Synchronise l'état des widgets avec l'UI (appelé au démarrage de l'app).
    /// Note: Les widgets sont déjà démarrés par App.StartRotationIfEnabled()
    /// </summary>
    private void StartWidgetsIfEnabled()
    {
        // Ne plus démarrer ici - c'est déjà fait par App
        // Juste synchroniser l'état de l'UI
        if (_widgetManager != null)
        {
            _widgetsVisible = _widgetManager.AreWidgetsVisible;
            OnPropertyChanged(nameof(WidgetsVisible));
        }
    }
    
    /// <summary>
    /// Rafraîchit la liste des widgets pour l'UI.
    /// </summary>
    private void RefreshWidgetList()
    {
        WidgetConfigs.Clear();
        
        if (_widgetManager == null) return;
        
        foreach (var config in _widgetManager.Settings.Widgets)
        {
            WidgetConfigs.Add(new WidgetConfigViewModel(config));
        }
    }
    
    private void AddSystemMonitorWidget()
    {
        var config = _widgetManager?.AddWidget(WidgetType.SystemMonitor);
        if (config != null)
        {
            WidgetConfigs.Add(new WidgetConfigViewModel(config));
        }
    }
    
    private void AddWeatherWidget()
    {
        var config = _widgetManager?.AddWidget(WidgetType.Weather);
        if (config != null)
        {
            WidgetConfigs.Add(new WidgetConfigViewModel(config));
        }
    }
    
    private void AddDiskStorageWidget()
    {
        var config = _widgetManager?.AddWidget(WidgetType.DiskStorage);
        if (config != null)
        {
            WidgetConfigs.Add(new WidgetConfigViewModel(config));
        }
    }
    
    private void AddBatteryWidget()
    {
        var config = _widgetManager?.AddWidget(WidgetType.Battery);
        if (config != null)
        {
            WidgetConfigs.Add(new WidgetConfigViewModel(config));
        }
    }
    
    private void AddQuickNotesWidget()
    {
        var config = _widgetManager?.AddWidget(WidgetType.Notes);
        if (config != null)
        {
            WidgetConfigs.Add(new WidgetConfigViewModel(config));
        }
    }
    
    private void RemoveWidget(WidgetConfigViewModel? vm)
    {
        if (vm == null) return;
        
        _widgetManager?.RemoveWidget(vm.Id);
        WidgetConfigs.Remove(vm);
    }
    
    private async Task UpdateWeatherCity()
    {
        if (_widgetManager == null || string.IsNullOrWhiteSpace(WeatherCity)) return;
        
        await _widgetManager.UpdateWeatherLocation(WeatherCity);
        WeatherCity = _widgetManager.Settings.WeatherCity;
    }
    
    private void RefreshWeather()
    {
        _widgetManager?.RefreshWeatherWidgets();
    }
    
    /// <summary>
    /// Nettoie les widgets (appelé à la fermeture de l'app).
    /// Note: Ne pas appeler Dispose() ici car c'est App qui gère le cycle de vie du service.
    /// </summary>
    private void CleanupWidgets()
    {
        // Le Dispose est géré par App.DisposeServicesAsync()
        _widgetManager = null;
    }
}

/// <summary>
/// ViewModel pour la configuration d'un widget dans l'UI.
/// </summary>
public class WidgetConfigViewModel : ObservableObject
{
    private readonly WidgetConfig _config;
    
    public string Id => _config.Id;
    public WidgetType Type => _config.Type;
    
    public string TypeName => Type switch
    {
        WidgetType.SystemMonitor => "📊 System Monitor",
        WidgetType.Weather => "🌤️ Météo",
        WidgetType.Clock => "🕐 Horloge",
        WidgetType.Calendar => "📅 Calendrier",
        WidgetType.Notes => "📝 Notes",
        WidgetType.QuickNotes => "📝 Quick Notes",
        WidgetType.MediaPlayer => "🎵 Média",
        WidgetType.Shortcuts => "⚡ Raccourcis",
        WidgetType.Quote => "💬 Citation",
        WidgetType.RssFeed => "📰 RSS",
        WidgetType.DiskStorage => "💾 Stockage",
        WidgetType.Battery => "🔋 Batterie",
        _ => "Widget"
    };
    
    public bool IsEnabled
    {
        get => _config.IsEnabled;
        set
        {
            _config.IsEnabled = value;
            OnPropertyChanged();
        }
    }
    
    public bool IsVisible
    {
        get => _config.IsVisible;
        set
        {
            _config.IsVisible = value;
            OnPropertyChanged();
        }
    }
    
    public double BackgroundOpacity
    {
        get => _config.BackgroundOpacity;
        set
        {
            _config.BackgroundOpacity = Math.Clamp(value, 0.1, 1.0);
            OnPropertyChanged();
        }
    }
    
    public WidgetConfigViewModel(WidgetConfig config)
    {
        _config = config;
    }
}
