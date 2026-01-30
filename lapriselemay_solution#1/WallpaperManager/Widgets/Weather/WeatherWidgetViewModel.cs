using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using WallpaperManager.Models;
using WallpaperManager.Services;
using WallpaperManager.Widgets.Base;

namespace WallpaperManager.Widgets.Weather;

/// <summary>
/// ViewModel pour le widget Météo.
/// </summary>
public class WeatherWidgetViewModel : WidgetViewModelBase
{
    private readonly WeatherService _weatherService;
    private readonly bool _ownsWeatherService;
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperManager", "widget_debug.log");
    
    // Configuration
    private double _latitude = 45.5017;  // Montreal par défaut
    private double _longitude = -73.5673;
    private string _cityName = "Montreal";
    
    protected override int RefreshIntervalSeconds => 1800; // 30 minutes
    
    // Données actuelles
    private string _icon = "☀️";
    public string Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }
    
    private string _city = "Montreal";
    public string City
    {
        get => _city;
        private set => SetProperty(ref _city, value);
    }
    
    private double _temperature;
    public double Temperature
    {
        get => _temperature;
        private set => SetProperty(ref _temperature, value);
    }
    
    private double _feelsLike;
    public double FeelsLike
    {
        get => _feelsLike;
        private set => SetProperty(ref _feelsLike, value);
    }
    
    private string _condition = "";
    public string Condition
    {
        get => _condition;
        private set => SetProperty(ref _condition, value);
    }
    
    private int _humidity;
    public int Humidity
    {
        get => _humidity;
        private set => SetProperty(ref _humidity, value);
    }
    
    private double _windSpeed;
    public double WindSpeed
    {
        get => _windSpeed;
        private set => SetProperty(ref _windSpeed, value);
    }
    
    private string _windDirection = "";
    public string WindDirection
    {
        get => _windDirection;
        private set => SetProperty(ref _windDirection, value);
    }
    
    private DateTime _lastUpdated;
    public DateTime LastUpdated
    {
        get => _lastUpdated;
        private set => SetProperty(ref _lastUpdated, value);
    }
    
    private string _lastUpdatedFormatted = "--:--";
    public string LastUpdatedFormatted
    {
        get => _lastUpdatedFormatted;
        private set => SetProperty(ref _lastUpdatedFormatted, value);
    }
    
    // Prévisions
    public ObservableCollection<ForecastDay> Forecasts { get; } = [];
    
    public WeatherWidgetViewModel() : this(new WeatherService(), ownsService: true)
    {
    }
    
    public WeatherWidgetViewModel(WeatherService weatherService, bool ownsService = false)
    {
        _weatherService = weatherService;
        _ownsWeatherService = ownsService;
        IsLoading = true; // Commencer en mode chargement
        Log("WeatherWidgetViewModel créé");
    }
    
    private new static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] WEATHER: {message}\n");
        }
        catch { }
    }
    
    /// <summary>
    /// Configure la localisation pour la météo.
    /// </summary>
    public void SetLocation(double latitude, double longitude, string cityName)
    {
        Log($"SetLocation: {cityName} ({latitude}, {longitude})");
        _latitude = latitude;
        _longitude = longitude;
        _cityName = cityName;
        City = cityName;
        // Ne pas appeler RefreshAsync ici - Start() le fera
    }
    
    /// <summary>
    /// Configure la localisation à partir du nom de la ville.
    /// </summary>
    public async Task SetLocationByCity(string cityName)
    {
        var result = await _weatherService.GeocodeCity(cityName);
        if (result.HasValue)
        {
            SetLocation(result.Value.lat, result.Value.lon, result.Value.name);
        }
    }
    
    public override async Task RefreshAsync()
    {
        Log($"RefreshAsync START pour {_cityName}");
        IsLoading = true;
        ErrorMessage = null;
        
        try
        {
            var data = await _weatherService.GetWeatherAsync(_latitude, _longitude, _cityName, forceRefresh: true);
            
            if (data != null)
            {
                Log($"Données reçues: {data.Temperature}°C, {data.ConditionDescription}");
                
                Icon = data.Icon;
                City = data.City;
                Temperature = data.Temperature;
                FeelsLike = data.FeelsLike;
                Condition = data.ConditionDescription;
                Humidity = data.Humidity;
                WindSpeed = data.WindSpeed;
                WindDirection = data.WindDirection;
                LastUpdated = data.LastUpdated;
                LastUpdatedFormatted = data.LastUpdated.ToString("HH:mm");
                
                Log($"Propriétés mises à jour: Temp={Temperature}, Condition={Condition}");
                
                // Mettre à jour les prévisions sur le UI thread
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Forecasts.Clear();
                    foreach (var forecast in data.Forecasts.Skip(1).Take(4))
                    {
                        Forecasts.Add(new ForecastDay
                        {
                            DayName = forecast.Date.ToString("ddd"),
                            Icon = forecast.Icon,
                            TempMax = (int)forecast.TempMax,
                            TempMin = (int)forecast.TempMin,
                            PrecipProbability = forecast.PrecipitationProbability
                        });
                    }
                    Log($"Prévisions ajoutées: {Forecasts.Count}");
                });
            }
            else
            {
                Log("Données NULL!");
                ErrorMessage = "Impossible de récupérer la météo";
            }
        }
        catch (Exception ex)
        {
            Log($"ERREUR: {ex.Message}");
            ErrorMessage = $"Erreur: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            Log($"RefreshAsync END - IsLoading={IsLoading}");
        }
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsWeatherService)
        {
            _weatherService.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Représente un jour de prévision pour l'affichage.
/// </summary>
public class ForecastDay
{
    public string DayName { get; set; } = string.Empty;
    public string Icon { get; set; } = "☀️";
    public int TempMax { get; set; }
    public int TempMin { get; set; }
    public int PrecipProbability { get; set; }
}
