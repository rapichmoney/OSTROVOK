using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private WeatherPoint[] WeatherSceneFixture() => new[] { 0, 3, 61, 71, 2, 85, 45, 95, 80 }
        .Select((code, i) => new WeatherPoint(DateTime.Today.AddDays(i).ToString("ddd, dd.MM", CultureInfo.GetCultureInfo("ru-RU")), 2 + i % 4, WeatherService.Describe(code), -i % 3, code)).ToArray();
    private async Task VerifyWeatherScenesAsync(Action<string, bool, string?> check, string output)
    {
        int oldTab = Navigation.SelectedIndex; var oldSource = ForecastList.ItemsSource;
        var cityVisibility = SavedWeatherList.Visibility; bool reduced = Motion.Reduced;
        Navigation.SelectedIndex = 10; SavedWeatherList.Visibility = Visibility.Collapsed;
        ForecastList.ItemsSource = WeatherSceneFixture(); SetExpanded(true); await AwaitShellAsync(); UpdateLayout(); await Task.Delay(200);
        var scenes = VisualDescendants<WeatherScene>(ForecastList).ToArray();
        check("each forecast date has its own weather scene", scenes.Length == 9 && scenes.Select(x => x.Code).SequenceEqual(WeatherSceneFixture().Select(x => x.Code)), null);
        var first = scenes.First(); double started = first.FrameTime; await Task.Delay(130);
        check("visible weather scene animates", first.IsAnimating && first.FrameTime > started, null);
        Capture(Path.Combine(output, "weather-animated.png"));
        Motion.Reduced = true; WeatherScene.RefreshMotion(); await Task.Delay(70);
        check("reduced motion stops weather clocks but preserves weather kind", scenes.All(x => !x.IsAnimating && x.FrameTime == 0) && WeatherScene.KindFor(first.Code) == WeatherSceneKind.Sun, null);
        Motion.Reduced = false; WeatherScene.RefreshMotion(); await Task.Delay(70);
        check("weather motion resumes when enabled", first.IsAnimating, null);
        Navigation.SelectedIndex = 0; await AwaitShellAsync();
        check("weather clocks stop outside the weather tab", scenes.All(x => !x.IsAnimating), null);
        Navigation.SelectedIndex = 10; await AwaitShellAsync(); SetExpanded(false); await AwaitShellAsync();
        check("weather clocks stop when island is compact", scenes.All(x => !x.IsAnimating), null);
        Motion.Reduced = reduced; ForecastList.ItemsSource = oldSource; SavedWeatherList.Visibility = cityVisibility;
        Navigation.SelectedIndex = oldTab; SetExpanded(true); await AwaitShellAsync();
    }
    private async Task CaptureWeatherShowcaseAsync()
    {
        string output = Environment.GetCommandLineArgs().SkipWhile(x => x != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "OSTROVOK-weather");
        Directory.CreateDirectory(output);
        try
        {
            timer.Stop(); lyricTimer.Stop(); leaveTimer.Stop(); media.Updated -= UpdateMedia; media.Dispose();
            Motion.VerificationOverride = true; settings.Detached = true; settings.InterfaceFont = InterfaceFonts.Aminahs;
            Pin.IsChecked = true; Left = Top = 80; ApplyShellAppearance();
            var city = new WeatherLocation("Орёл", "Орловская область", "Россия", 52.97, 36.07);
            WeatherCityList.Add(settings, city);
            weatherSnapshots[city] = new WeatherSnapshot(city.Name, DateTime.Now, 2, -1, 2.4, 74, 71, 3, -2, WeatherSceneFixture(), WeatherSceneFixture());
            RenderWeatherState(); RenderSavedWeatherCities(); forecastDays = true;
            Navigation.SelectedIndex = 10; SetExpanded(true); await AwaitShellAsync();
            SavedWeatherList.Visibility = Visibility.Collapsed; ForecastList.ItemsSource = WeatherSceneFixture().Take(6).ToArray();
            Height = Math.Min(600, Services.WindowInterop.WorkArea(handle).Height); UpdateLayout(); await Task.Delay(200);
            WeatherStatus.Text = "Демонстрация анимаций · тестовый прогноз";
            for (int frame = 0; frame < 150; frame++)
            {
                Capture(Path.Combine(output, $"{frame:0000}.png")); await Task.Delay(40);
            }
            File.WriteAllText(Path.Combine(output, "complete.txt"), "WPF forecast controls. Synthetic conditions for animation demonstration, not a live weather forecast.");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); }
        finally { Close(); Application.Current.Shutdown(); }
    }
}
