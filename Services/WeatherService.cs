using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace DesktopIsland.Services;

public sealed record WeatherPoint(string Label, double Temperature, string Description, double? Low = null, int Code = -1)
{
    public string Value => Low.HasValue ? $"{Temperature:0}° / {Low:0}°" : $"{Temperature:0}°";
}
public sealed record WeatherLocation(string Name, string Region, string Country, double Latitude, double Longitude)
{
    public string Label => string.Join(", ", new[] { Name, Region, Country }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
}
public sealed record WeatherSnapshot(string City, DateTime RetrievedAt, double Temperature, double FeelsLike, double Wind, int Humidity, int Code, double High, double Low, WeatherPoint[] Hours, WeatherPoint[] Days);

public sealed class WeatherService
{
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(18) };
    public async Task<WeatherLocation[]> SearchAsync(string city)
    {
        using var json = JsonDocument.Parse(await http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(city) + "&count=8&language=ru&format=json"));
        if (!json.RootElement.TryGetProperty("results", out var results)) return [];
        return results.EnumerateArray().Select(x => new WeatherLocation(x.GetProperty("name").GetString()!, x.TryGetProperty("admin1", out var region) ? region.GetString() ?? "" : "", x.TryGetProperty("country", out var country) ? country.GetString() ?? "" : "", x.GetProperty("latitude").GetDouble(), x.GetProperty("longitude").GetDouble())).ToArray();
    }
    public async Task<WeatherSnapshot> FetchAsync(WeatherLocation location)
    {
        string coords = "latitude=" + location.Latitude.ToString(CultureInfo.InvariantCulture) + "&longitude=" + location.Longitude.ToString(CultureInfo.InvariantCulture);
        var url = "https://api.open-meteo.com/v1/forecast?" + coords + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&hourly=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min,weather_code&wind_speed_unit=ms&timezone=auto&forecast_days=7";
        return Parse(location.Name, await http.GetStringAsync(url));
    }
    internal static WeatherSnapshot Parse(string city, string body)
    {
        using var json = JsonDocument.Parse(body); var root = json.RootElement;
        var current = root.GetProperty("current"); var hourly = root.GetProperty("hourly"); var daily = root.GetProperty("daily");
        var now = DateTime.Parse(current.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
        var hours = new List<WeatherPoint>(); var days = new List<WeatherPoint>();
        var hourTimes = hourly.GetProperty("time").EnumerateArray().Select(x => DateTime.Parse(x.GetString()!, CultureInfo.InvariantCulture)).ToArray();
        for (int i = 0; i < hourTimes.Length; i++)
            if (hourTimes[i] >= now.AddHours(-1) && hours.Count < 24) { int code = hourly.GetProperty("weather_code")[i].GetInt32(); hours.Add(new WeatherPoint(hourTimes[i].ToString("HH:mm"), hourly.GetProperty("temperature_2m")[i].GetDouble(), Describe(code), Code: code)); }
        int index = 0;
        foreach (var date in daily.GetProperty("time").EnumerateArray())
        {
            int code = daily.GetProperty("weather_code")[index].GetInt32();
            days.Add(new WeatherPoint(DateTime.Parse(date.GetString()!, CultureInfo.InvariantCulture).ToString("ddd, dd.MM"), daily.GetProperty("temperature_2m_max")[index].GetDouble(), Describe(code), daily.GetProperty("temperature_2m_min")[index].GetDouble(), code)); index++;
        }
        return new WeatherSnapshot(city, DateTime.Now, current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("apparent_temperature").GetDouble(), current.GetProperty("wind_speed_10m").GetDouble(), current.GetProperty("relative_humidity_2m").GetInt32(), current.GetProperty("weather_code").GetInt32(), days[0].Temperature, days[0].Low ?? 0, hours.ToArray(), days.ToArray());
    }
    public static string Describe(int code) => code switch { 0 => "Ясно", 1 or 2 => "Переменная облачность", 3 => "Пасмурно", 45 or 48 => "Туман", >= 51 and <= 57 => "Морось", >= 61 and <= 67 => "Дождь", >= 71 and <= 77 => "Снег", >= 80 and <= 82 => "Ливень", 85 or 86 => "Снегопад", >= 95 and <= 99 => "Гроза", _ => "Нет описания" };
}
