using System.Net.Http;
using System.Text.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

/// <summary>
/// Service pour récupérer les données météo via Open-Meteo API.
/// API gratuite, illimitée, sans clé requise.
/// </summary>
public sealed class WeatherService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Lock _lock = new();
    private WeatherData? _cachedData;
    private DateTime _lastFetch = DateTime.MinValue;
    private bool _disposed;
    
    private const string BaseUrl = "https://api.open-meteo.com/v1/forecast";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    
    public event EventHandler<WeatherData>? WeatherUpdated;
    
    public WeatherData? CurrentWeather => _cachedData;
    
    public WeatherService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "WallpaperManager/2.0");
    }
    
    /// <summary>
    /// Récupère les données météo pour les coordonnées spécifiées.
    /// </summary>
    public async Task<WeatherData?> GetWeatherAsync(double latitude, double longitude, string city, bool forceRefresh = false)
    {
        if (_disposed) return null;
        
        // Vérifier le cache
        lock (_lock)
        {
            if (!forceRefresh && _cachedData != null && DateTime.Now - _lastFetch < CacheDuration)
            {
                return _cachedData;
            }
        }
        
        try
        {
            var url = BuildUrl(latitude, longitude);
            var response = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
            var apiResponse = JsonSerializer.Deserialize<OpenMeteoResponse>(response);
            
            if (apiResponse?.Current == null)
                return _cachedData;
            
            var weatherData = ParseResponse(apiResponse, city);
            
            lock (_lock)
            {
                _cachedData = weatherData;
                _lastFetch = DateTime.Now;
            }
            
            WeatherUpdated?.Invoke(this, weatherData);
            return weatherData;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur météo: {ex.Message}");
            return _cachedData;
        }
    }
    
    /// <summary>
    /// Recherche les coordonnées d'une ville via l'API de géocodage Open-Meteo.
    /// </summary>
    public async Task<(double lat, double lon, string name)?> GeocodeCity(string cityName)
    {
        if (string.IsNullOrWhiteSpace(cityName)) return null;
        
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityName)}&count=1&language=fr&format=json";
            var response = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
            
            using var doc = JsonDocument.Parse(response);
            var results = doc.RootElement.GetProperty("results");
            
            if (results.GetArrayLength() > 0)
            {
                var first = results[0];
                return (
                    first.GetProperty("latitude").GetDouble(),
                    first.GetProperty("longitude").GetDouble(),
                    first.GetProperty("name").GetString() ?? cityName
                );
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur géocodage: {ex.Message}");
        }
        
        return null;
    }
    
    private static string BuildUrl(double latitude, double longitude)
    {
        var parameters = new[]
        {
            $"latitude={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"longitude={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m,wind_direction_10m",
            "daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset",
            "timezone=auto",
            "forecast_days=5"
        };
        
        return $"{BaseUrl}?{string.Join("&", parameters)}";
    }
    
    private static WeatherData ParseResponse(OpenMeteoResponse response, string city)
    {
        var current = response.Current!;
        var daily = response.Daily;
        
        var condition = GetConditionFromCode(current.WeatherCode);
        var isDay = current.IsDay == 1;
        
        var data = new WeatherData
        {
            City = city,
            Temperature = Math.Round(current.Temperature, 1),
            FeelsLike = Math.Round(current.ApparentTemperature, 1),
            Humidity = current.Humidity,
            WindSpeed = Math.Round(current.WindSpeed, 1),
            WindDirection = GetWindDirection(current.WindDirection),
            Condition = condition,
            ConditionDescription = GetConditionDescription(condition),
            Icon = GetWeatherIcon(condition, isDay),
            IsDay = isDay,
            LastUpdated = DateTime.Now
        };
        
        // Sunrise/Sunset du jour
        if (daily?.Sunrise?.Count > 0)
        {
            if (DateTime.TryParse(daily.Sunrise[0], out var sunrise))
                data.Sunrise = sunrise;
            if (DateTime.TryParse(daily.Sunset[0], out var sunset))
                data.Sunset = sunset;
        }
        
        // Prévisions
        if (daily?.Time != null)
        {
            for (int i = 0; i < daily.Time.Count && i < 5; i++)
            {
                var forecastCondition = GetConditionFromCode(daily.WeatherCode[i]);
                data.Forecasts.Add(new WeatherForecast
                {
                    Date = DateTime.Parse(daily.Time[i]),
                    TempMin = Math.Round(daily.TempMin[i], 0),
                    TempMax = Math.Round(daily.TempMax[i], 0),
                    Condition = forecastCondition,
                    Icon = GetWeatherIcon(forecastCondition, true),
                    PrecipitationProbability = daily.PrecipitationProbability?[i] ?? 0
                });
            }
        }
        
        return data;
    }
    
    private static WeatherCondition GetConditionFromCode(int code)
    {
        return code switch
        {
            0 => WeatherCondition.Clear,
            1 or 2 => WeatherCondition.PartlyCloudy,
            3 => WeatherCondition.Overcast,
            45 or 48 => WeatherCondition.Fog,
            51 or 53 or 55 => WeatherCondition.Drizzle,
            56 or 57 => WeatherCondition.Sleet,
            61 or 63 or 80 or 81 => WeatherCondition.Rain,
            65 or 82 => WeatherCondition.HeavyRain,
            71 or 73 or 85 => WeatherCondition.Snow,
            75 or 86 => WeatherCondition.HeavySnow,
            77 => WeatherCondition.Sleet,
            95 or 96 or 99 => WeatherCondition.Thunderstorm,
            _ => WeatherCondition.Unknown
        };
    }
    
    private static string GetWeatherIcon(WeatherCondition condition, bool isDay)
    {
        return condition switch
        {
            WeatherCondition.Clear => isDay ? "☀️" : "🌙",
            WeatherCondition.PartlyCloudy => isDay ? "⛅" : "☁️",
            WeatherCondition.Cloudy or WeatherCondition.Overcast => "☁️",
            WeatherCondition.Fog => "🌫️",
            WeatherCondition.Drizzle => "🌧️",
            WeatherCondition.Rain => "🌧️",
            WeatherCondition.HeavyRain => "⛈️",
            WeatherCondition.Snow or WeatherCondition.HeavySnow => "❄️",
            WeatherCondition.Sleet => "🌨️",
            WeatherCondition.Thunderstorm => "⛈️",
            _ => "🌡️"
        };
    }
    
    private static string GetConditionDescription(WeatherCondition condition)
    {
        return condition switch
        {
            WeatherCondition.Clear => "Dégagé",
            WeatherCondition.PartlyCloudy => "Partiellement nuageux",
            WeatherCondition.Cloudy => "Nuageux",
            WeatherCondition.Overcast => "Couvert",
            WeatherCondition.Fog => "Brouillard",
            WeatherCondition.Drizzle => "Bruine",
            WeatherCondition.Rain => "Pluie",
            WeatherCondition.HeavyRain => "Forte pluie",
            WeatherCondition.Snow => "Neige",
            WeatherCondition.HeavySnow => "Forte neige",
            WeatherCondition.Sleet => "Grésil",
            WeatherCondition.Thunderstorm => "Orage",
            _ => "Inconnu"
        };
    }
    
    private static string GetWindDirection(int degrees)
    {
        var directions = new[] { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
        var index = (int)Math.Round(degrees / 45.0) % 8;
        return directions[index];
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
