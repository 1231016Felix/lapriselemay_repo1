namespace WallpaperManager.Converters;

/// <summary>
/// Options statiques pour les sélecteurs d'heure
/// </summary>
public static class HourOptions
{
    public static int[] Values { get; } = Enumerable.Range(0, 24).ToArray();
}

/// <summary>
/// Options statiques pour les sélecteurs de minutes
/// </summary>
public static class MinuteOptions
{
    public static int[] Values { get; } = [0, 15, 30, 45];
}
