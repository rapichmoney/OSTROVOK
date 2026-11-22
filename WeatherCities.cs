using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly Dictionary<WeatherLocation, WeatherSnapshot> weatherSnapshots = new();
    private readonly HashSet<WeatherLocation> weatherFailures = [];
    private readonly Dictionary<WeatherLocation, DateTime> weatherAttempts = new();
    private bool weatherRefreshQueued, weatherForceQueued;
    private int weatherCitiesRevision;
    internal event Action? WeatherCitiesUpdated;
    internal Task RefreshSavedWeatherAsync(bool force = true) => LoadWeatherAsync(force);
    internal IReadOnlyList<WeatherLocation> SavedWeatherCities => settings.WeatherLocations;
    internal string WeatherCitySummary(WeatherLocation city) => weatherSnapshots.TryGetValue(city, out var snapshot)
        ? $"{snapshot.Temperature:0}° · {WeatherService.Describe(snapshot.Code)}" : weatherFailures.Contains(city) ? "Не удалось загрузить" : "Получаем прогноз…";
    private void RenderSavedWeatherCities()
    {
        SavedWeatherList.ItemsSource = SavedWeatherCities.Select((city, index) => new { Name = city.Name, Summary = WeatherCitySummary(city), Detail = city.Label, Priority = index == 0 ? "Основной" : "" }).ToArray();
        WeatherCitiesUpdated?.Invoke();
    }
    internal async Task AddWeatherCityAsync(WeatherLocation city)
    { if (WeatherCityList.Add(settings, city)) { ++weatherCitiesRevision; SaveSettings(); RenderSavedWeatherCities(); } await LoadWeatherAsync(); }
    internal async Task MoveWeatherCityAsync(WeatherLocation city, int offset)
    { if (!WeatherCityList.Move(settings, city, offset)) return; ++weatherCitiesRevision; SaveSettings(); RenderWeatherState(); RenderSavedWeatherCities(); await LoadWeatherAsync(); }
    internal async Task RemoveWeatherCityAsync(WeatherLocation city)
    { if (!WeatherCityList.Remove(settings, city)) return; ++weatherCitiesRevision; SaveSettings(); RenderWeatherState(); RenderSavedWeatherCities(); await LoadWeatherAsync(); }
    private void RenderWeatherState()
    {
        var city = settings.WeatherLocation;
        forecast = city != null && weatherSnapshots.TryGetValue(city, out var current) ? current : null;
        WeatherTemperature.Text = forecast != null ? $"{forecast.Temperature:0}°" : "—";
        WeatherCityLabel.Text = city?.Name ?? "Добавьте город в настройках";
        WeatherDescription.Text = forecast != null ? WeatherService.Describe(forecast.Code) : "";
        WeatherFeels.Text = forecast != null ? $"Ощущается как {forecast.FeelsLike:0}°" : "";
        WeatherHiLo.Text = forecast != null ? $"Макс. {forecast.High:0}° · мин. {forecast.Low:0}°" : "";
        WeatherWind.Text = forecast != null ? $"Ветер {forecast.Wind:0.#} м/с" : "";
        WeatherHumidity.Text = forecast != null ? $"Влажность {forecast.Humidity}%" : "";
        WeatherStatus.Text = forecast != null ? $"Open-Meteo · обновлено в {forecast.RetrievedAt:HH:mm}" : city == null ? "Выберите города в настройках погоды" : weatherFailures.Contains(city) ? "Не удалось загрузить прогноз. Повторите обновление позже." : "Получаем прогноз…";
        RenderForecast(); RefreshIdleContent();
    }
    private async Task LoadSavedWeatherAsync(bool force)
    {
        if (App.VerificationMode) { RenderSavedWeatherCities(); return; }
        if (weatherBusy) { weatherRefreshQueued = true; weatherForceQueued |= force; return; }
        weatherBusy = true;
        try
        {
            do
            {
                weatherRefreshQueued = false; force |= weatherForceQueued; weatherForceQueued = false;
                int revision = weatherCitiesRevision;
                RenderWeatherState(); RenderSavedWeatherCities();
                foreach (var city in SavedWeatherCities.ToArray())
                {
                    if (closing || revision != weatherCitiesRevision) break;
                    if (!force && weatherSnapshots.TryGetValue(city, out var cached) && DateTime.Now - cached.RetrievedAt < TimeSpan.FromMinutes(30)) continue;
                    if (!force && weatherAttempts.TryGetValue(city, out var attempted) && DateTime.Now - attempted < TimeSpan.FromMinutes(2)) continue;
                    weatherAttempts[city] = DateTime.Now;
                    try { weatherSnapshots[city] = await weatherService.FetchAsync(city); weatherFailures.Remove(city); }
                    catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                    { weatherFailures.Add(city); }
                    if (closing) return;
                    if (revision != weatherCitiesRevision) { weatherRefreshQueued = true; break; }
                    RenderWeatherState(); RenderSavedWeatherCities();
                }
                force = false;
            } while (weatherRefreshQueued && !closing);
        }
        finally { weatherBusy = false; }
    }
}
