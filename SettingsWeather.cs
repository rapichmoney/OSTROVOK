using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIsland.Services;

namespace DesktopIsland;

internal sealed partial class SettingsWindow
{
    private StackPanel? weatherCitiesPanel;
    private void WeatherPage(StackPanel content)
    {
        Hint(content, "Первый город — основной для мини-острова. Меняйте порядок стрелками.");
        weatherCitiesPanel = new StackPanel { Margin = new Thickness(0, 16, 0, 12) };
        content.Children.Add(weatherCitiesPanel); RefreshWeatherCities();
        Section(content, "Добавить город");
        var input = new TextBox { Margin = new Thickness(0, 8, 0, 10), Height = 38, ToolTip = "Название города" };
        System.Windows.Automation.AutomationProperties.SetName(input, "Название города");
        var results = new ListBox { MaxHeight = 180, DisplayMemberPath = "Label", Visibility = Visibility.Collapsed };
        var state = new TextBlock { Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) };
        var search = new Button { Content = "Найти город", HorizontalAlignment = HorizontalAlignment.Left };
        var add = new Button { Content = "Добавить выбранный город", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8), Visibility = Visibility.Collapsed };
        search.Click += async (_, _) =>
        {
            string query = input.Text.Trim();
            if (query.Length < 2) { state.Text = "Введите хотя бы две буквы"; return; }
            search.IsEnabled = false; state.Text = "Ищем город…"; add.Visibility = Visibility.Collapsed;
            try
            {
                var locations = await new WeatherService().SearchAsync(query);
                results.ItemsSource = locations; results.Visibility = locations.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                state.Text = locations.Length == 0 ? "Город не найден" : "Выберите город из списка";
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            { state.Text = "Не удалось найти город. Проверьте соединение."; }
            finally { search.IsEnabled = true; }
        };
        results.SelectionChanged += (_, _) => add.Visibility = results.SelectedItem is WeatherLocation ? Visibility.Visible : Visibility.Collapsed;
        add.Click += async (_, _) =>
        {
            if (results.SelectedItem is not WeatherLocation location) return;
            bool exists = island.SavedWeatherCities.Any(x => WeatherCityList.Same(x, location));
            var pending = island.AddWeatherCityAsync(location);
            state.Text = exists ? "Этот город уже в списке" : "Город добавлен: " + location.Name;
            results.Visibility = add.Visibility = Visibility.Collapsed; input.Clear();
            await pending;
        };
        input.KeyDown += (_, args) => { if (args.Key == Key.Enter) search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        content.Children.Add(input); content.Children.Add(search); content.Children.Add(state); content.Children.Add(results); content.Children.Add(add);
        Hint(content, "Прогноз Open-Meteo. В сервис передаются название города для поиска и координаты для прогноза.");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var refresh = new Button { Content = "Обновить", Padding = new Thickness(12, 7, 12, 7) };
        refresh.Click += async (_, _) => await island.RefreshSavedWeatherAsync();
        var open = new Button { Content = "Погода на острове", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 7, 12, 7) };
        open.Click += (_, _) => island.ShowWeatherPage(); footer.Children.Add(refresh); footer.Children.Add(open); content.Children.Add(footer);
        _ = island.RefreshSavedWeatherAsync(false);
    }
    private void RefreshWeatherCities()
    {
        if (weatherCitiesPanel == null) return;
        weatherCitiesPanel.Children.Clear();
        var cities = island.SavedWeatherCities;
        if (cities.Count == 0) { Hint(weatherCitiesPanel, "Города ещё не добавлены"); return; }
        for (int index = 0; index < cities.Count; index++)
        {
            var city = cities[index];
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8), ToolTip = city.Label };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            label.Children.Add(new TextBlock { Text = $"{index + 1}. {city.Name}", FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var summary = new TextBlock { Text = island.WeatherCitySummary(city) + (index == 0 ? " · основной" : ""), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            summary.SetResourceReference(TextBlock.ForegroundProperty, "Secondary"); label.Children.Add(summary); row.Children.Add(label);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(actions, 1);
            actions.Children.Add(CityAction("Выше: " + city.Name, "M 4,14 L 12,6 L 20,14 M 12,6 L 12,22", () => island.MoveWeatherCityAsync(city, -1), index > 0));
            actions.Children.Add(CityAction("Ниже: " + city.Name, "M 4,10 L 12,18 L 20,10 M 12,2 L 12,18", () => island.MoveWeatherCityAsync(city, 1), index < cities.Count - 1));
            actions.Children.Add(CityAction("Удалить: " + city.Name, "M 5,5 L 19,19 M 19,5 L 5,19", () => island.RemoveWeatherCityAsync(city), true));
            row.Children.Add(actions);
            var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
            border.SetResourceReference(Border.BorderBrushProperty, "Line"); weatherCitiesPanel.Children.Add(border);
        }
    }
    private Button CityAction(string title, string geometry, Func<Task> action, bool enabled)
    {
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), StrokeThickness = 1.4, Width = 14, Height = 14, Stretch = Stretch.Uniform };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Secondary");
        var button = new Button { Content = icon, Style = (Style)FindResource("QuietButton"), Width = 32, Height = 32, Padding = new Thickness(8), ToolTip = title, IsEnabled = enabled };
        System.Windows.Automation.AutomationProperties.SetName(button, title); button.Click += async (_, _) => await action(); return button;
    }
}
