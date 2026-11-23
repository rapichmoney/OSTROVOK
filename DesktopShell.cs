using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly Dictionary<int, (Button Button, TextBlock Label)> toolbar = [];
    private Button? sectionsButton;
    private TextBlock? sectionsLabel;
    private SettingsWindow? preferences;
    private bool draggingWindow;
    private bool weatherBusy;
    private readonly WeatherService weatherService = new();
    private WeatherSnapshot? forecast;
    private bool forecastDays;
    private int hideRevision;
    private string lastKeyboardLabel = "";
    private readonly PausePresentation pausePresentation = new();
    private bool compactMusicActive;
    private double CompactWidth => (settings.CompactWidthOverride > 0 ? Math.Clamp(settings.CompactWidthOverride, settings.MiniMode ? 100 : 220, 1600) : settings.MiniMode ? 100 : 318) * Math.Clamp(settings.CompactScale, 75, 150) / 100;
    private double CompactHeight => 44 * Math.Clamp(settings.CompactScale, 75, 150) / 100;
    private double ExpandedBaseWidth => settings.ExpandedWidthOverride > 0 ? Math.Clamp(settings.ExpandedWidthOverride, 440, 900) : 440;
    private double ExpandedUiScale
    {
        get { var area = Services.WindowInterop.WorkArea(handle); return Services.DisplayLayout.Scale(Math.Clamp(settings.ExpandedScale, 80, 150) / 100, ExpandedBaseWidth, ExpandedBaseHeight, area.Width, area.Height); }
    }
    private double ExpandedWidth => ExpandedBaseWidth * ExpandedUiScale;
    private bool PlayerHasLyrics => lyricsLoading ? lyricsPendingHadContent : lyricsResult?.Synced == true || !string.IsNullOrWhiteSpace(lyricsResult?.PlainText);
    private double ExpandedBaseHeight => trackVisualPending && expanded && Navigation.SelectedIndex == 0 ? heldWindowHeight / Math.Max(.25, heldContentScale) : (Navigation.SelectedIndex == 0 && media.Available ? PlayerHasLyrics ? settings.PlayerHeight > 0 ? Math.Clamp(settings.PlayerHeight, 480, 1600) : 480 : 432 : 30 + (Navigation.SelectedIndex == 5 && settings.CalendarView != "Месяц" ? settings.CalendarView == "Неделя" ? 390 : 330 : PageHeight(Navigation.SelectedIndex, media.Available)));
    private double ExpandedHeight => ExpandedBaseHeight * ExpandedUiScale;
    internal static double PageHeight(int page, bool playing) => page switch
    {
        0 => playing ? 450 : 156, 1 => 332, 2 => 350, 3 => 370, 4 => 350,
        5 => 460, 6 => 350, 7 => 370, 8 => 390, 9 => 520, 10 => 520, _ => 350
    };

    private void BuildToolbar()
    {
        TopNavigation.Children.Clear(); toolbar.Clear();
        AddTab(0, "Часы", "M 12,2 A 10,10 0 1 0 12,22 A 10,10 0 1 0 12,2 M 12,6 L 12,12 L 16,14");
        AddTab(1, "Файлы", "M 2,6 L 10,6 L 12,9 L 22,9 L 22,21 L 2,21 Z");
        AddTab(2, "Буфер", "M 8,4 L 5,4 L 5,22 L 21,22 L 21,4 L 18,4 M 9,2 L 17,2 L 17,6 L 9,6 Z");
        AddTab(3, "Заметки", "M 4,20 L 6,14 L 17,3 L 21,7 L 10,18 Z M 15,5 L 19,9");
        AddTab(5, "Календарь", "M 3,5 L 21,5 L 21,22 L 3,22 Z M 3,10 L 21,10 M 7,2 L 7,7 M 17,2 L 17,7");
        AddTab(10, "Погода", "M 6,18 C 0,18 0,10 6,10 C 7,2 17,2 18,10 C 25,10 25,18 18,18 Z");
        AddTab(8, "События", "M 12,2 A 10,10 0 1 0 12,22 A 10,10 0 1 0 12,2 M 7,9 L 17,9 M 7,14 L 14,14");
        AddTab(7, "Монитор", "M 2,3 L 22,3 L 22,19 L 2,19 Z M 8,22 L 16,22 M 5,13 L 8,13 L 10,7 L 14,16 L 16,11 L 19,11");
        var more = IconButton("Разделы", "M 3,3 L 9,3 L 9,9 L 3,9 Z M 15,3 L 21,3 L 21,9 L 15,9 Z M 3,15 L 9,15 L 9,21 L 3,21 Z M 15,15 L 21,15 L 21,21 L 15,21 Z", false);
        sectionsButton = more; sectionsLabel = null;
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu(); TrackIslandMenu(menu);
            foreach (var item in new[] { (1, "Полка файлов"), (3, "Заметки"), (4, "Задачи"), (5, "Календарь"), (10, "Погода"), (7, "Монитор"), (6, "Загрузки"), (8, "События") })
            {
                if (!TabEnabled(item.Item1)) continue;
                var action = new MenuItem { Header = item.Item2 }; int index = item.Item1;
                action.Click += (_, _) => Navigation.SelectedIndex = index; menu.Items.Add(action);
            }
            var position = new MenuItem { Header = settings.Detached ? "Прикрепить к верхнему краю" : "Открепить остров" };
            position.Click += (_, _) => { settings.Detached = !settings.Detached; if (!settings.Detached) ResetPosition(this, new RoutedEventArgs()); else { Top += 24; SaveSettings(); } ApplyShellAppearance(); };
            menu.Items.Add(position);
            more.ContextMenu = menu; menu.PlacementTarget = more; menu.IsOpen = true;
        };
        var pin = IconButton("Закрепить раскрытый остров", "M 8,3 L 16,3 L 15,9 L 19,14 L 5,14 L 9,9 Z M 12,14 L 12,23", false);
        pin.Click += (_, _) => { Pin.IsChecked = Pin.IsChecked != true; settings.Pinned = Pin.IsChecked == true; SaveSettings(); };
        DockPanel.SetDock(pin, Dock.Right); TopNavigation.Children.Add(pin);
        var gear = IconButton("Настройки", "M 9,2 L 15,2 L 16,6 L 20,8 L 23,12 L 20,16 L 16,18 L 15,22 L 9,22 L 8,18 L 4,16 L 1,12 L 4,8 L 8,6 Z M 12,8 A 4,4 0 1 0 12,16 A 4,4 0 1 0 12,8", false);
        if (gear.Content is System.Windows.Shapes.Path cog)
        { cog.Data = SettingsGear(); cog.Width = cog.Height = 16; cog.StrokeThickness = 1.6; }
        System.Windows.Automation.AutomationProperties.SetAutomationId(gear, "OpenSettings");
        gear.Click += (_, _) => TogglePreferences(); DockPanel.SetDock(gear, Dock.Right); TopNavigation.Children.Add(gear);
        DockPanel.SetDock(more, Dock.Right); TopNavigation.Children.Add(more); ApplyTabVisibility();
    }
    private static Geometry SettingsGear()
    {
        var outline = new StreamGeometry();
        using (var drawing = outline.Open())
        {
            for (int tooth = 0; tooth < 8; tooth++)
                for (int edge = 0; edge < 4; edge++)
                {
                    double angle = (tooth * 45 + new[] { -16d, -10, 10, 16 }[edge]) * Math.PI / 180;
                    double radius = edge is 0 or 3 ? 7.2 : 10;
                    var point = new Point(12 + Math.Cos(angle) * radius, 12 + Math.Sin(angle) * radius);
                    if (tooth == 0 && edge == 0) drawing.BeginFigure(point, false, true);
                    else drawing.LineTo(point, true, false);
                }
        }
        var geometry = new GeometryGroup(); geometry.Children.Add(outline);
        geometry.Children.Add(new EllipseGeometry(new Point(12, 12), 3.1, 3.1)); geometry.Freeze(); return geometry;
    }
    private static Button IconButton(string title, string geometry, bool showCaption = true, string? caption = null)
    {
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Stroke = new SolidColorBrush(Color.FromRgb(218, 218, 218)), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform, Width = 14, Height = 14 };
        object content = icon;
        if (showCaption)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; stack.Children.Add(icon);
            stack.Children.Add(new TextBlock { Text = caption ?? title, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); content = stack;
        }
        var button = new Button { Content = content, ToolTip = title, Padding = new Thickness(6, 8, 6, 8), Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinWidth = 26, Margin = new Thickness(0) };
        button.Loaded += (_, _) => { if (button.Template.FindName("Frame", button) is Border frame) frame.CornerRadius = new CornerRadius(16); };
        System.Windows.Automation.AutomationProperties.SetName(button, caption ?? title); return button;
    }
    private void AddTab(int index, string title, string geometry)
    {
        var button = IconButton(title, geometry, false); var icon = (System.Windows.Shapes.Path)button.Content;
        button.Content = null;
        var label = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Medium, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Visible, VerticalAlignment = VerticalAlignment.Center };
        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; stack.Children.Add(icon); stack.Children.Add(label); button.Content = stack;
        button.Click += (_, _) => Navigation.SelectedIndex = index;
        if (index == 0) { button.MouseDoubleClick += CollapseFromPlayerTab; button.ToolTip = "Плеер · двойное нажатие сворачивает остров"; }
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "IslandTab" + index);
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        toolbar[index] = (button, label); TopNavigation.Children.Add(button);
    }
    private void UpdateToolbar()
    {
        foreach (var (index, item) in toolbar)
        {
            if (IsLoaded && TopNavigation.IsVisible) continue;
            item.Label.Visibility = index == Navigation.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
            var palette = CompactMaterialColors();
            Color tabColor = Color.FromRgb((byte)(palette.Top.R * .7 + 255 * .12), (byte)(palette.Top.G * .7 + 255 * .12), (byte)(palette.Top.B * .7 + 255 * .12));
            var color = index == Navigation.SelectedIndex ? tabColor : Colors.Transparent;
            item.Button.BorderThickness = index == Navigation.SelectedIndex ? new Thickness(1) : new Thickness(0);
            item.Button.BorderBrush = new SolidColorBrush(Color.FromArgb(90, palette.Rim.R, palette.Rim.G, palette.Rim.B));
            item.Label.FontWeight = index == Navigation.SelectedIndex ? FontWeights.SemiBold : FontWeights.Medium;
            if (item.Button.Background is not SolidColorBrush background || background.Color != color)
            {
                item.Button.Background = new SolidColorBrush(color);
                Motion.RefreshButtonBackground(item.Button);
            }
        }
        if (toolbar.TryGetValue(0, out var first)) first.Label.Text = media.Available ? "Плеер" : "Часы";
        AnimateToolbarSelection();
        if (sectionsButton != null && sectionsLabel != null)
        {
            bool selected = !toolbar.ContainsKey(Navigation.SelectedIndex);
            sectionsLabel.Text = selected ? Navigation.SelectedIndex switch { 1 => "Файлы", 3 => "Заметки", 4 => "Задачи", 5 => "Календарь", 6 => "Загрузки", 7 => "Монитор", 8 => "События", 10 => "Погода", _ => "Разделы" } : "Разделы";
            var desired = selected ? Color.FromRgb(48, 53, 60) : Colors.Transparent;
            if (sectionsButton.Background is not SolidColorBrush brush || brush.Color != desired) { sectionsButton.Background = new SolidColorBrush(desired); Motion.RefreshButtonBackground(sectionsButton); }
        }
    }
    private void ApplyShellAppearance()
    {
        ApplyInterfaceFont();
        double scale = expanded ? ExpandedUiScale : Math.Clamp(settings.CompactScale, 75, 150) / 100;
        Shell.LayoutTransform = new ScaleTransform(scale, scale);
        bool docked = !settings.Detached && Top <= WindowInterop.WorkArea(handle).Top + 2;
        Shell.CornerRadius = docked ? new CornerRadius(0, 0, settings.Radius, settings.Radius) : new CornerRadius(expanded ? settings.Radius : 20);
        Shell.BorderThickness = docked ? new Thickness(0, 0, 0, 1) : new Thickness(1);
        if (!expanded)
        {
            var colors = CompactMaterialColors();
            Shell.Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(colors.Top, 0), new(colors.Middle, .32), new(colors.Bottom, 1)
            }, new Point(0, 0), new Point(0, 1));
            Shell.BorderBrush = new LinearGradientBrush(colors.Rim, Color.FromRgb(54, 56, 61), new Point(0, 0), new Point(0, 1));
            Shell.BorderThickness = new Thickness(1);
            Shell.CornerRadius = new CornerRadius(22);
        }
        else
        {
            var tint = CompactMaterialColors();
            bool colored = paletteActive && settings.ArtworkTint && artworkColor != null;
            Color top = colored ? tint.Top : Color.FromRgb(33, 36, 40);
            Color middle = colored ? Color.FromRgb((byte)((tint.Middle.R + 24) / 2), (byte)((tint.Middle.G + 26) / 2), (byte)((tint.Middle.B + 29) / 2)) : Color.FromRgb(24, 26, 29);
            Shell.Background = new LinearGradientBrush(new GradientStopCollection { new(top, 0), new(middle, .58), new(Color.FromRgb(21, 23, 26), 1) }, new Point(0, 0), new Point(0, 1));
            Shell.BorderBrush = new SolidColorBrush(Color.FromRgb(54, 59, 67));
        }
        UpdateControlContrast();
        UpdateToolbar();
        preferences?.ApplyIslandPalette();
    }
    internal (Color Top, Color Middle, Color Bottom, Color Rim) SettingsPalette => CompactMaterialColors();
    private void UpdateControlContrast()
    {
        if (Shell.Background is not LinearGradientBrush gradient) return;
        var colors = ControlContrast.For(gradient.GradientStops.Select(x => x.Color).ToArray());
        Shell.Resources["SliderRail"] = new SolidColorBrush(colors.Rail);
        Shell.Resources["SliderInk"] = new SolidColorBrush(colors.Ink);
        Shell.Resources["SliderRim"] = new SolidColorBrush(colors.Rim);
    }
    private void RefreshIdleContent()
    {
        bool wasActive = compactMusicActive;
        compactMusicActive = pausePresentation.IsActive(media.Available && !string.IsNullOrWhiteSpace(media.Title), media.Playing, Environment.TickCount64);
        if (!compactMusicActive) { stableMusicWidth.Resolve(0, 0, false); CancelTrackVisualHold(); }
        HomeClock.Text = DateTime.Now.ToString("HH:mm"); HomeDate.Text = DateTime.Now.ToString("dddd, d MMMM", CultureInfo.GetCultureInfo("ru-RU"));
        UpdateKeyboardIndicator(KeyboardStatus());
        KeyboardText.Visibility = settings.ShowKeyboard && !settings.MiniMode ? Visibility.Visible : Visibility.Collapsed;
        KeyboardBadge.Visibility = KeyboardText.Visibility;
        Battery.Text = !monitor.HasBattery ? DateTime.Now.ToString("ddd, d MMM", CultureInfo.GetCultureInfo("ru-RU")) : monitor.Battery;
        IdleDateCaption.Text = !monitor.HasBattery ? CultureInfo.GetCultureInfo("ru-RU").TextInfo.ToTitleCase(DateTime.Now.ToString("dddd", CultureInfo.GetCultureInfo("ru-RU"))) : "Питание";
        Battery.Visibility = settings.ShowDateOrBattery && !settings.MiniMode ? Visibility.Visible : Visibility.Collapsed;
        IdleDateBadge.Visibility = Battery.Visibility;
        string compactText = compactMusicActive && !settings.MiniMode ? CompactTrackCaption : "";
        if (compactMusicActive && settings.LyricsOnCompact && HasTimedCompactLyrics && !settings.MiniMode)
            compactText = lyricsResult!.Lines[currentLyricIndex].Text;
        UpdateCompactText(compactText);
        CompactWeather.Text = forecast != null ? $"{forecast.Temperature:0}°" : "";
        CompactWeather.Visibility = settings.ShowWeather && !settings.MiniMode && forecast != null ? Visibility.Visible : Visibility.Collapsed;
        CompactWeatherGroup.Visibility = CompactWeather.Visibility;
        CompactWeatherGlyph.Text = forecast?.Code switch { 0 => "☀", 1 or 2 or 3 => "☁", 45 or 48 => "🌫", >= 71 and <= 77 => "❄", >= 95 => "⛈", _ => "🌧" };
        ApplyCompactPlayback(compactMusicActive && !settings.MiniMode, media.Playing, media.Artwork);
        HomePanel.Visibility = media.Available ? Visibility.Collapsed : Visibility.Visible;
        MediaPanel.Visibility = media.Available ? Visibility.Visible : Visibility.Collapsed;
        UpdateClipboardNoticeVisibility();
        if (wasActive != compactMusicActive && !expanded && IsLoaded && !HasCompactNotification) { Motion.RevealContent(Header, 320, 0); UpdateCompactNotification(true); }
        UpdateIdleGlow();
    }
    private static string KeyboardStatus()
    {
        uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        try { int layout = (int)(GetKeyboardLayout(thread).ToInt64() & 0xFFFF); if (layout == 0) return ""; string language = CultureInfo.GetCultureInfo(layout).TwoLetterISOLanguageName.ToUpperInvariant(); return (language == "EN" ? "ENG" : language) + ((GetKeyState(0x14) & 1) != 0 ? " CAPS" : ""); }
        catch (CultureNotFoundException) { return ""; }
    }
    private void UpdateKeyboardIndicator(string label)
    {
        if (label == lastKeyboardLabel) return;
        bool changed = lastKeyboardLabel.Length > 0;
        lastKeyboardLabel = label; KeyboardText.Text = label;
        var background = new SolidColorBrush(Color.FromRgb(45, 51, 59));
        KeyboardBadge.Background = background;
        if (changed && Motion.Enabled && Header.IsVisible)
        {
            background.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(Color.FromRgb(68, 85, 101), background.Color, TimeSpan.FromMilliseconds(420)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
            KeyboardText.Opacity = .5;
            Motion.Tween(KeyboardText, OpacityProperty, 1, 180);
        }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);

    private void OpenPreferences(int page = 0)
    {
        if (preferences == null) { preferences = new SettingsWindow(this, settings); preferences.Closed += (_, _) => preferences = null; }
        preferences.Topmost = Topmost;
        if (preferences.WindowState == WindowState.Minimized) preferences.WindowState = WindowState.Normal;
        preferences.Show(); preferences.SelectPage(page); preferences.Activate();
        idleGlow?.Hide();
    }
    internal void RefreshSettingsLayer() => UpdateIdleGlow();
    private void TogglePreferences()
    {
        if (preferences?.IsVisible == true && preferences.WindowState != WindowState.Minimized) preferences.Close(); else OpenPreferences();
    }
    internal void ApplyPreferences()
    {
        if (closing) return;
        if (!settings.AutoTranslateClipboard || !settings.ClipboardEnabled) { StopClipboardTranslation(); translationGate.Reset(); }
        settings.CompactScale = Math.Clamp(settings.CompactScale, 75, 150); settings.ExpandedScale = Math.Clamp(settings.ExpandedScale, 80, 150); settings.Radius = Math.Clamp(settings.Radius, 0, 40);
        Pin.IsChecked = settings.Pinned; StayOnTop.IsChecked = settings.Topmost; HideFullscreen.IsChecked = settings.HideInFullscreen;
        LessMotion.IsChecked = settings.ReducedMotion; Motion.Reduced = settings.ReducedMotion;
        WeatherScene.RefreshMotion();
        ApplyTabVisibility();
        Topmost = settings.Topmost; if (preferences != null) preferences.Topmost = Topmost; ApplyShellAppearance(); RefreshIdleContent();
        foreach (var fill in lyricFills) fill.ReadAlong = settings.ReadAlong;
        RefreshTrackLyrics();
        StopShellMotion(); AnimateExpansion(expanded); SaveSettings();
    }
    internal void SetPrivacyOptions(bool collectClipboard, bool observeDownloads)
    {
        WatchClipboard.IsChecked = collectClipboard; ClipboardSettingChanged(this, new RoutedEventArgs());
        WatchDownloads.IsChecked = observeDownloads; DownloadsSettingChanged(this, new RoutedEventArgs());
    }
    internal void SetAutostart(bool enabled) { Autostart.IsChecked = enabled; AutostartChanged(this, new RoutedEventArgs()); }
    internal bool AutostartEnabled => Autostart.IsChecked == true;
    internal void ChooseDownloads() => ChooseDownloadFolder(this, new RoutedEventArgs());
    internal void ShowDataFolder() => OpenDataFolder(this, new RoutedEventArgs());
    internal void ShowDiagnosticsFolder()
    {
        if (App.VerificationMode) return;
        try
        {
            string folder = App.Diagnostics?.Root ?? DiagnosticLog.DefaultRoot;
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { App.Diagnostics?.Error(DiagnosticArea.Storage, ex); Status.Text = "Не удалось открыть папку журналов"; }
    }
    internal void PreviewIsland() { Reveal(); Navigation.SelectedIndex = 0; }
    internal void ShowWeatherPage() { preferences?.Hide(); Reveal(); Navigation.SelectedIndex = 10; }

    private void AddShelfLink(object sender, RoutedEventArgs e)
    {
        var dialog = new LinkWindow { Owner = this };
        if (dialog.ShowDialog() != true || !ShelfItem.IsWebLink(dialog.Address)) return;
        if (!files.Any(x => x.Path.Equals(dialog.Address, StringComparison.OrdinalIgnoreCase))) files.Add(new ShelfItem { Path = dialog.Address });
        SaveShelfReferences();
    }
    private void ClearShelf(object sender, RoutedEventArgs e) { files.Clear(); SaveShelfReferences(); }
    private void SaveShelfReferences()
    {
        store.Save("files.json", files.Where(x => !x.IsLink).Select(x => x.Path).ToArray());
        store.Save("links.json", files.Where(x => x.IsLink).Select(x => x.Path).ToArray());
    }
    private async void RefreshWeather(object sender, RoutedEventArgs e) => await LoadWeatherAsync(true);
    private void OpenWeatherSettings(object sender, RoutedEventArgs e) => OpenPreferences(2);
    private void WeatherHours(object sender, RoutedEventArgs e) { forecastDays = false; RenderForecast(); }
    private void WeatherDays(object sender, RoutedEventArgs e) { forecastDays = true; RenderForecast(); }
    private void RenderForecast()
    {
        var points = forecast == null ? null : forecastDays ? forecast.Days : forecast.Hours;
        if (ReferenceEquals(ForecastList.ItemsSource, points)) return;
        ForecastList.ItemsSource = points; Motion.Enter(ForecastList, 5);
    }
    internal async Task SetWeatherLocationAsync(WeatherLocation location)
    {
        await AddWeatherCityAsync(location);
    }
    private async Task LoadWeatherAsync(bool force = false)
    {
        await LoadSavedWeatherAsync(force);
    }
}
