using System;

namespace WallpaperManager.Services;

/// <summary>
/// Service de calcul des heures de lever/coucher du soleil
/// Basé sur l'algorithme NOAA (National Oceanic and Atmospheric Administration)
/// </summary>
public static class SunCalculatorService
{
    /// <summary>
    /// Résultat du calcul des heures solaires
    /// </summary>
    public record SunTimes(
        TimeSpan Dawn,           // Aube civile (soleil 6° sous l'horizon)
        TimeSpan Sunrise,        // Lever du soleil
        TimeSpan SolarNoon,      // Midi solaire
        TimeSpan Sunset,         // Coucher du soleil
        TimeSpan Dusk,           // Crépuscule civil
        TimeSpan GoldenHourAM,   // Début heure dorée matin
        TimeSpan GoldenHourPM,   // Début heure dorée soir
        bool IsPolarDay,         // Jour polaire (soleil ne se couche pas)
        bool IsPolarNight        // Nuit polaire (soleil ne se lève pas)
    );
    
    /// <summary>
    /// Calcule les heures de lever/coucher du soleil pour une position et une date
    /// </summary>
    public static SunTimes Calculate(double latitude, double longitude, DateTime date)
    {
        // Normaliser la latitude
        latitude = Math.Clamp(latitude, -89.99, 89.99);
        
        var jd = GetJulianDay(date);
        var t = GetJulianCentury(jd);
        
        var solarNoon = CalculateSolarNoon(longitude, t, date);
        
        // Calcul des angles
        var sunrise = CalculateSunrise(latitude, longitude, t, date, -0.833); // Lever/coucher standard
        var sunset = CalculateSunset(latitude, longitude, t, date, -0.833);
        var dawn = CalculateSunrise(latitude, longitude, t, date, -6.0);      // Aube civile
        var dusk = CalculateSunset(latitude, longitude, t, date, -6.0);       // Crépuscule civil
        var goldenAM = CalculateSunrise(latitude, longitude, t, date, 6.0);   // Heure dorée
        var goldenPM = CalculateSunset(latitude, longitude, t, date, 6.0);
        
        // Détection jour/nuit polaire
        bool isPolarDay = !sunrise.HasValue && latitude > 0;
        bool isPolarNight = !sunrise.HasValue && latitude < 0;
        
        // Valeurs par défaut pour les cas polaires
        if (isPolarDay)
        {
            return new SunTimes(
                TimeSpan.Zero,
                TimeSpan.Zero,
                solarNoon,
                TimeSpan.FromHours(24),
                TimeSpan.FromHours(24),
                TimeSpan.Zero,
                TimeSpan.FromHours(24),
                true,
                false
            );
        }
        
        if (isPolarNight)
        {
            return new SunTimes(
                TimeSpan.FromHours(12),
                TimeSpan.FromHours(12),
                solarNoon,
                TimeSpan.FromHours(12),
                TimeSpan.FromHours(12),
                TimeSpan.FromHours(12),
                TimeSpan.FromHours(12),
                false,
                true
            );
        }
        
        return new SunTimes(
            dawn ?? TimeSpan.FromHours(5),
            sunrise ?? TimeSpan.FromHours(6),
            solarNoon,
            sunset ?? TimeSpan.FromHours(18),
            dusk ?? TimeSpan.FromHours(19),
            goldenAM ?? TimeSpan.FromHours(6.5),
            goldenPM ?? TimeSpan.FromHours(17.5),
            false,
            false
        );
    }
    
    /// <summary>
    /// Calcule les heures solaires pour aujourd'hui
    /// </summary>
    public static SunTimes CalculateToday(double latitude, double longitude)
        => Calculate(latitude, longitude, DateTime.Today);
    
    /// <summary>
    /// Génère des variantes de temps basées sur le soleil
    /// </summary>
    public static (string Label, TimeSpan Time)[] GetSunBasedPresets(double latitude, double longitude, DateTime? date = null)
    {
        var sunTimes = Calculate(latitude, longitude, date ?? DateTime.Today);
        
        // Créer des presets basés sur les heures solaires réelles
        return
        [
            ("Aube", RoundToQuarter(sunTimes.Dawn)),
            ("Lever du soleil", RoundToQuarter(sunTimes.Sunrise)),
            ("Matin", RoundToQuarter(sunTimes.Sunrise.Add(TimeSpan.FromHours(2)))),
            ("Midi", RoundToQuarter(sunTimes.SolarNoon)),
            ("Après-midi", RoundToQuarter(sunTimes.SolarNoon.Add(TimeSpan.FromHours(3)))),
            ("Heure dorée", RoundToQuarter(sunTimes.GoldenHourPM)),
            ("Coucher du soleil", RoundToQuarter(sunTimes.Sunset)),
            ("Crépuscule", RoundToQuarter(sunTimes.Dusk)),
            ("Nuit", RoundToQuarter(sunTimes.Dusk.Add(TimeSpan.FromHours(1))))
        ];
    }
    
    /// <summary>
    /// Génère un preset simplifié (4 périodes) basé sur le soleil
    /// </summary>
    public static (string Label, TimeSpan Time)[] GetSimpleSunPresets(double latitude, double longitude, DateTime? date = null)
    {
        var sunTimes = Calculate(latitude, longitude, date ?? DateTime.Today);
        
        return
        [
            ("Aube", RoundToQuarter(sunTimes.Dawn)),
            ("Jour", RoundToQuarter(sunTimes.SolarNoon.Subtract(TimeSpan.FromHours(2)))),
            ("Crépuscule", RoundToQuarter(sunTimes.GoldenHourPM)),
            ("Nuit", RoundToQuarter(sunTimes.Dusk.Add(TimeSpan.FromMinutes(30))))
        ];
    }
    
    private static TimeSpan RoundToQuarter(TimeSpan time)
    {
        var totalMinutes = (int)time.TotalMinutes;
        var roundedMinutes = (int)(Math.Round(totalMinutes / 15.0) * 15);
        return TimeSpan.FromMinutes(Math.Clamp(roundedMinutes, 0, 24 * 60 - 1));
    }
    
    #region NOAA Algorithm Implementation
    
    private static double GetJulianDay(DateTime date)
    {
        var y = date.Year;
        var m = date.Month;
        var d = date.Day;
        
        if (m <= 2)
        {
            y--;
            m += 12;
        }
        
        var a = (int)(y / 100.0);
        var b = 2 - a + (int)(a / 4.0);
        
        return (int)(365.25 * (y + 4716)) + (int)(30.6001 * (m + 1)) + d + b - 1524.5;
    }
    
    private static double GetJulianCentury(double jd)
        => (jd - 2451545.0) / 36525.0;
    
    private static double GetSunGeometricMeanLongitude(double t)
    {
        var l0 = 280.46646 + t * (36000.76983 + 0.0003032 * t);
        while (l0 > 360.0) l0 -= 360.0;
        while (l0 < 0.0) l0 += 360.0;
        return l0;
    }
    
    private static double GetSunGeometricMeanAnomaly(double t)
        => 357.52911 + t * (35999.05029 - 0.0001537 * t);
    
    private static double GetEarthOrbitEccentricity(double t)
        => 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
    
    private static double GetSunEquationOfCenter(double t)
    {
        var m = GetSunGeometricMeanAnomaly(t);
        var mrad = ToRadians(m);
        var sinm = Math.Sin(mrad);
        var sin2m = Math.Sin(mrad * 2);
        var sin3m = Math.Sin(mrad * 3);
        
        return sinm * (1.914602 - t * (0.004817 + 0.000014 * t)) +
               sin2m * (0.019993 - 0.000101 * t) +
               sin3m * 0.000289;
    }
    
    private static double GetSunTrueLongitude(double t)
        => GetSunGeometricMeanLongitude(t) + GetSunEquationOfCenter(t);
    
    private static double GetSunApparentLongitude(double t)
    {
        var o = GetSunTrueLongitude(t);
        var omega = 125.04 - 1934.136 * t;
        return o - 0.00569 - 0.00478 * Math.Sin(ToRadians(omega));
    }
    
    private static double GetMeanObliquityOfEcliptic(double t)
    {
        var seconds = 21.448 - t * (46.8150 + t * (0.00059 - t * 0.001813));
        return 23.0 + (26.0 + seconds / 60.0) / 60.0;
    }
    
    private static double GetObliquityCorrection(double t)
    {
        var e0 = GetMeanObliquityOfEcliptic(t);
        var omega = 125.04 - 1934.136 * t;
        return e0 + 0.00256 * Math.Cos(ToRadians(omega));
    }
    
    private static double GetSunDeclination(double t)
    {
        var e = GetObliquityCorrection(t);
        var lambda = GetSunApparentLongitude(t);
        var sint = Math.Sin(ToRadians(e)) * Math.Sin(ToRadians(lambda));
        return ToDegrees(Math.Asin(sint));
    }
    
    private static double GetEquationOfTime(double t)
    {
        var e = GetObliquityCorrection(t);
        var l0 = GetSunGeometricMeanLongitude(t);
        var ecc = GetEarthOrbitEccentricity(t);
        var m = GetSunGeometricMeanAnomaly(t);
        
        var y = Math.Tan(ToRadians(e) / 2.0);
        y *= y;
        
        var sin2l0 = Math.Sin(2.0 * ToRadians(l0));
        var sinm = Math.Sin(ToRadians(m));
        var cos2l0 = Math.Cos(2.0 * ToRadians(l0));
        var sin4l0 = Math.Sin(4.0 * ToRadians(l0));
        var sin2m = Math.Sin(2.0 * ToRadians(m));
        
        var eqTime = y * sin2l0 - 2.0 * ecc * sinm + 4.0 * ecc * y * sinm * cos2l0 -
                     0.5 * y * y * sin4l0 - 1.25 * ecc * ecc * sin2m;
        
        return ToDegrees(eqTime) * 4.0;
    }
    
    private static double GetHourAngle(double latitude, double declination, double altitude)
    {
        var latRad = ToRadians(latitude);
        var decRad = ToRadians(declination);
        var altRad = ToRadians(altitude);
        
        var cosHA = (Math.Sin(altRad) - Math.Sin(latRad) * Math.Sin(decRad)) /
                    (Math.Cos(latRad) * Math.Cos(decRad));
        
        // Vérifier si le soleil se lève/couche à cette latitude
        if (cosHA > 1.0 || cosHA < -1.0) return double.NaN;
        
        return ToDegrees(Math.Acos(cosHA));
    }
    
    private static TimeSpan CalculateSolarNoon(double longitude, double t, DateTime date)
    {
        var eqTime = GetEquationOfTime(t);
        var solarNoonOffset = 720 - 4 * longitude - eqTime;
        var solarNoonMinutes = solarNoonOffset;
        
        // Ajuster pour le fuseau horaire local
        var utcOffset = TimeZoneInfo.Local.GetUtcOffset(date).TotalMinutes;
        solarNoonMinutes += utcOffset;
        
        while (solarNoonMinutes < 0) solarNoonMinutes += 1440;
        while (solarNoonMinutes >= 1440) solarNoonMinutes -= 1440;
        
        return TimeSpan.FromMinutes(solarNoonMinutes);
    }
    
    private static TimeSpan? CalculateSunrise(double latitude, double longitude, double t, DateTime date, double altitude)
    {
        var eqTime = GetEquationOfTime(t);
        var declination = GetSunDeclination(t);
        var hourAngle = GetHourAngle(latitude, declination, altitude);
        
        if (double.IsNaN(hourAngle)) return null;
        
        var sunriseOffset = 720 - 4 * (longitude + hourAngle) - eqTime;
        
        // Ajuster pour le fuseau horaire local
        var utcOffset = TimeZoneInfo.Local.GetUtcOffset(date).TotalMinutes;
        sunriseOffset += utcOffset;
        
        while (sunriseOffset < 0) sunriseOffset += 1440;
        while (sunriseOffset >= 1440) sunriseOffset -= 1440;
        
        return TimeSpan.FromMinutes(sunriseOffset);
    }
    
    private static TimeSpan? CalculateSunset(double latitude, double longitude, double t, DateTime date, double altitude)
    {
        var eqTime = GetEquationOfTime(t);
        var declination = GetSunDeclination(t);
        var hourAngle = GetHourAngle(latitude, declination, altitude);
        
        if (double.IsNaN(hourAngle)) return null;
        
        var sunsetOffset = 720 - 4 * (longitude - hourAngle) - eqTime;
        
        // Ajuster pour le fuseau horaire local
        var utcOffset = TimeZoneInfo.Local.GetUtcOffset(date).TotalMinutes;
        sunsetOffset += utcOffset;
        
        while (sunsetOffset < 0) sunsetOffset += 1440;
        while (sunsetOffset >= 1440) sunsetOffset -= 1440;
        
        return TimeSpan.FromMinutes(sunsetOffset);
    }
    
    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
    
    #endregion
}
