namespace DesktopIsland.Services;

internal static class WeatherCityList
{
    internal static bool Same(WeatherLocation a, WeatherLocation b) => Math.Abs(a.Latitude - b.Latitude) < .0001 && Math.Abs(a.Longitude - b.Longitude) < .0001;
    internal static void Normalize(AppSettings settings)
    {
        settings.WeatherLocations ??= [];
        if (settings.WeatherLocations.Count == 0 && settings.WeatherLocation != null) settings.WeatherLocations.Add(settings.WeatherLocation);
        var unique = new List<WeatherLocation>();
        foreach (var city in settings.WeatherLocations)
            if (city != null && double.IsFinite(city.Latitude) && double.IsFinite(city.Longitude) && Math.Abs(city.Latitude) <= 90 && Math.Abs(city.Longitude) <= 180 && !unique.Any(x => Same(x, city))) unique.Add(city);
        settings.WeatherLocations = unique; Sync(settings);
    }
    private static void Sync(AppSettings settings) { settings.WeatherLocation = settings.WeatherLocations.FirstOrDefault(); settings.WeatherCity = settings.WeatherLocation?.Name ?? ""; }
    internal static bool Add(AppSettings settings, WeatherLocation city)
    {
        if (settings.WeatherLocations.Any(x => Same(x, city))) return false;
        settings.WeatherLocations.Add(city); Sync(settings); return true;
    }
    internal static bool Move(AppSettings settings, WeatherLocation city, int offset)
    {
        int index = settings.WeatherLocations.FindIndex(x => Same(x, city)), target = index + offset;
        if (index < 0 || target < 0 || target >= settings.WeatherLocations.Count) return false;
        settings.WeatherLocations.RemoveAt(index); settings.WeatherLocations.Insert(target, city); Sync(settings); return true;
    }
    internal static bool Remove(AppSettings settings, WeatherLocation city)
    {
        if (settings.WeatherLocations.RemoveAll(x => Same(x, city)) == 0) return false;
        Sync(settings); return true;
    }
}
