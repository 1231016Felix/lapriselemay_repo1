using System.Text.Json.Serialization;

namespace WallpaperManager.Models;

/// <summary>
/// Données météo actuelles.
/// </summary>
public class WeatherData
{
    public string City { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double FeelsLike { get; set; }
    public int Humidity { get; set; }
    public double WindSpeed { get; set; }
    public string WindDirection { get; set; } = string.Empty;
    public WeatherCondition Condition { get; set; }
    public string ConditionDescription { get; set; } = string.Empty;
    public string Icon { get; set; } = "☀️";
    public DateTime Sunrise { get; set; }
    public DateTime Sunset { get; set; }
    public DateTime LastUpdated { get; set; }
    public bool IsDay { get; set; }
    
    // Prévisions
    public List<WeatherForecast> Forecasts { get; set; } = [];
}

/// <summary>
/// Prévision météo pour un jour.
/// </summary>
public class WeatherForecast
{
    public DateTime Date { get; set; }
    public double TempMin { get; set; }
    public double TempMax { get; set; }
    public WeatherCondition Condition { get; set; }
    public string Icon { get; set; } = "☀️";
    public int PrecipitationProbability { get; set; }
}

/// <summary>
/// Conditions météo possibles.
/// </summary>
public enum WeatherCondition
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Overcast,
    Fog,
    Drizzle,
    Rain,
    HeavyRain,
    Snow,
    HeavySnow,
    Sleet,
    Thunderstorm,
    Unknown
}

/// <summary>
/// Réponse de l'API Open-Meteo.
/// </summary>
public class OpenMeteoResponse
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }
    
    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
    
    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = string.Empty;
    
    [JsonPropertyName("current")]
    public OpenMeteoCurrent? Current { get; set; }
    
    [JsonPropertyName("daily")]
    public OpenMeteoDaily? Daily { get; set; }
}

public class OpenMeteoCurrent
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;
    
    [JsonPropertyName("temperature_2m")]
    public double Temperature { get; set; }
    
    [JsonPropertyName("relative_humidity_2m")]
    public int Humidity { get; set; }
    
    [JsonPropertyName("apparent_temperature")]
    public double ApparentTemperature { get; set; }
    
    [JsonPropertyName("is_day")]
    public int IsDay { get; set; }
    
    [JsonPropertyName("weather_code")]
    public int WeatherCode { get; set; }
    
    [JsonPropertyName("wind_speed_10m")]
    public double WindSpeed { get; set; }
    
    [JsonPropertyName("wind_direction_10m")]
    public int WindDirection { get; set; }
}

public class OpenMeteoDaily
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; } = [];
    
    [JsonPropertyName("weather_code")]
    public List<int> WeatherCode { get; set; } = [];
    
    [JsonPropertyName("temperature_2m_max")]
    public List<double> TempMax { get; set; } = [];
    
    [JsonPropertyName("temperature_2m_min")]
    public List<double> TempMin { get; set; } = [];
    
    [JsonPropertyName("precipitation_probability_max")]
    public List<int>? PrecipitationProbability { get; set; }
    
    [JsonPropertyName("sunrise")]
    public List<string> Sunrise { get; set; } = [];
    
    [JsonPropertyName("sunset")]
    public List<string> Sunset { get; set; } = [];
}
