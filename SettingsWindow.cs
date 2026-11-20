using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIsland.Services;

namespace DesktopIsland;

internal sealed class SettingsWindow : Window
{
    private readonly MainWindow island;
    private readonly AppSettings settings;
    private readonly ListBox navigation = new();
    private readonly ContentControl page = new();
    private readonly DispatcherTimer commit = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock previewClock = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly IslandMotionPreview preview;
    private ScrollViewer? pageScroll;
    private bool initialized;
    private TextBlock? discordStatus;
    private readonly DockPanel sidebar;
    private readonly DockPanel titlebar;
    private readonly Border windowFrame;
    private string paletteSignature = "";
    internal static readonly string[] AnimationStyles = ["Капля", "Пружина", "Плавно", "Волна", "Желе", "Тянучка", "Мяч", "Резинка", "Щелчок", "Пузырь"];

    public SettingsWindow(MainWindow island, AppSettings settings)
    {
        this.island = island; this.settings = settings;
        preview = new IslandMotionPreview(previewClock);
        Title = "Настройки Desktop Island"; Width = 860; Height = 680; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = new SolidColorBrush(Color.FromRgb(20, 22, 25));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int dark = 1, border = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        };
        Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");
        var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        sidebar = new DockPanel { Margin = new Thickness(0, 0, 1, 0) };
        var brand = new StackPanel { Margin = new Thickness(20, 24, 10, 24) };
        brand.Children.Add(new TextBlock { Text = "Desktop Island", FontSize = 19, FontWeight = FontWeights.SemiBold });
        brand.Children.Add(new TextBlock { Text = "Настройки · 1.0.3", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        ((TextBlock)brand.Children[1]).SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new TextBlock { Text = "Изменения сохраняются\nна этом компьютере", Foreground = (Brush)FindResource("Muted"), FontSize = 12, Margin = new Thickness(20, 12, 12, 20) };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        foreach (var title in new[] { "Остров", "Внешний вид", "Погода", "Данные", "Система", "Музыка" }) navigation.Items.Add(title);
        navigation.Margin = new Thickness(10, 0, 10, 0); navigation.SelectionChanged += (_, _) => BuildPage(); sidebar.Children.Add(navigation); root.Children.Add(sidebar);
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(32, 24, 24, 24) };
        pageScroll = scroll;
        scroll.Resources[SystemParameters.VerticalScrollBarWidthKey] = 10d;
        Grid.SetColumn(scroll, 1); root.Children.Add(scroll);
        var frame = new DockPanel(); titlebar = new DockPanel { Height = 42, LastChildFill = true };
        titlebar.MouseLeftButtonDown += (_, args) => { if (args.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else DragMove(); };
        var close = new Button { Content = "Закрыть", Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) }; close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); titlebar.Children.Add(close);
        var minimize = new Button { Content = "Свернуть", Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) }; minimize.Click += (_, _) => WindowState = WindowState.Minimized; DockPanel.SetDock(minimize, Dock.Right); titlebar.Children.Add(minimize);
        var titleLabel = new TextBlock { Text = "Настройки Desktop Island", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "Secondary"); titlebar.Children.Add(titleLabel);
        DockPanel.SetDock(titlebar, Dock.Top); frame.Children.Add(titlebar); frame.Children.Add(root);
        windowFrame = new Border { BorderThickness = new Thickness(1), Child = frame }; Content = windowFrame;
        ApplyIslandPalette();
        commit.Tick += (_, _) => { commit.Stop(); island.ApplyPreferences(); };
        clockTimer.Tick += (_, _) => { previewClock.Text = DateTime.Now.ToString("HH:mm    d MMMM"); if (discordStatus != null) discordStatus.Text = island.DiscordPresenceStatus; }; clockTimer.Start();
        Closed += (_, _) => { commit.Stop(); clockTimer.Stop(); island.ApplyPreferences(); };
        initialized = true; SelectPage(0);
    }
    internal void ApplyIslandPalette()
    {
        var palette = island.SettingsPalette;
        FontFamily = island.FontFamily;
        string signature = palette.ToString();
        if (signature == paletteSignature) return;
        paletteSignature = signature;
        static Color Mix(Color a, Color b, double fraction) => Color.FromRgb((byte)(a.R * (1 - fraction) + b.R * fraction), (byte)(a.G * (1 - fraction) + b.G * fraction), (byte)(a.B * (1 - fraction) + b.B * fraction));
        Color top = Mix(palette.Top, palette.Bottom, .18);
        var background = new LinearGradientBrush(top, palette.Bottom, new Point(0, 0), new Point(0, 1));
        Background = windowFrame.Background = background;
        titlebar.Background = new SolidColorBrush(top);
        sidebar.Background = new SolidColorBrush(Mix(palette.Top, palette.Bottom, .35));
        windowFrame.BorderBrush = new SolidColorBrush(palette.Rim);
        Resources["ControlSurface"] = new SolidColorBrush(Mix(palette.Top, palette.Bottom, .45));
        Resources["SelectionSurface"] = new SolidColorBrush(Mix(palette.Top, Colors.White, .16));
        Resources["HoverSurface"] = new SolidColorBrush(Mix(palette.Top, Colors.White, .22));
        Resources["InputSurface"] = new SolidColorBrush(palette.Bottom);
        Resources["SwitchOff"] = new SolidColorBrush(Mix(palette.Top, palette.Bottom, .2));
        Resources["PopupSurface"] = new SolidColorBrush(palette.Middle);
        Resources["Line"] = Resources["PopupBorder"] = new SolidColorBrush(Mix(palette.Rim, palette.Bottom, .45));
        Resources["Accent"] = new SolidColorBrush(Mix(palette.Top, Colors.White, .78));
        Resources["Muted"] = new SolidColorBrush(Color.FromRgb(215, 217, 221));
        Resources["Secondary"] = new SolidColorBrush(Color.FromRgb(230, 232, 235));
        var contrast = ControlContrast.For([palette.Top, palette.Bottom]);
        Resources["SliderRail"] = new SolidColorBrush(contrast.Rail);
        Resources["SliderInk"] = new SolidColorBrush(contrast.Ink);
        Resources["SliderRim"] = new SolidColorBrush(contrast.Rim);
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal void SelectPage(int index) { int target = Math.Clamp(index, 0, 5); if (navigation.SelectedIndex == target) BuildPage(); else navigation.SelectedIndex = target; }
    private void Changed() { if (!initialized) return; commit.Stop(); commit.Start(); UpdatePreview(); }
    private void UpdatePreview() { preview.Configure(settings.AnimationStyle, settings.Radius, settings.AnimationSpeed, settings.SpringStrength); previewClock.Text = DateTime.Now.ToString("HH:mm    d MMMM"); }
    private void BuildPage()
    {
        var content = new StackPanel();
        string title = navigation.SelectedItem as string ?? "Остров";
        content.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 24) });
        switch (navigation.SelectedIndex)
        {
            case 0:
                Section(content, "Раскрытие");
                Choice(content, "Как открывать остров", settings.ExpandOnClick ? "Кликом" : "Наведением", ["Наведением", "Кликом"], x => settings.ExpandOnClick = x == "Кликом");
                Choice(content, "При раскрытии открывать", settings.OpeningTab, ["Последняя вкладка", "Плеер, если играет музыка", "Всегда первая вкладка"], x => settings.OpeningTab = x);
                Toggle(content, "Оставлять раскрытым", settings.Pinned, x => settings.Pinned = x);
                Section(content, "Свёрнутый остров");
                Toggle(content, "Мини-режим: только часы", settings.MiniMode, x => settings.MiniMode = x);
                Toggle(content, "Раскладка и Caps Lock", settings.ShowKeyboard, x => settings.ShowKeyboard = x);
                Toggle(content, "Дата или заряд батареи", settings.ShowDateOrBattery, x => settings.ShowDateOrBattery = x);
                Toggle(content, "Температура рядом с часами", settings.ShowWeather, x => settings.ShowWeather = x);
                Section(content, "Положение");
                Toggle(content, "Открепить от верхнего края", settings.Detached, x => settings.Detached = x);
                Toggle(content, "Поверх других окон", settings.Topmost, x => settings.Topmost = x);
                Toggle(content, "Скрывать в полноэкранных приложениях", settings.HideInFullscreen, x => settings.HideInFullscreen = x);
                Hint(content, "Перетаскивайте за часы или пустое место верхней панели. Ctrl + Alt + Пробел скрывает и показывает остров. Escape сворачивает его.");
                break;
            case 1:
                Section(content, "Предпросмотр");
                if (preview.Parent is Panel oldParent) oldParent.Children.Remove(preview);
                content.Children.Add(preview); UpdatePreview();
                Hint(content, "Наведите курсор на предпросмотр, чтобы раскрыть остров.");
                Section(content, "Размер и форма");
                Toggle(content, "Цвет острова по обложке", settings.ArtworkTint, x => settings.ArtworkTint = x);
                Toggle(content, "Свечение рамки свёрнутого острова", settings.IdleGlow, x => settings.IdleGlow = x);
                Choice(content, "Шрифт острова", settings.InterfaceFont, ["Segoe UI", "Aminahs Schoolbook Sans Serif Bd"], x => settings.InterfaceFont = x);
                Hint(content, "Aminahs Schoolbook встроен в приложение. Установка в Windows не требуется.");
                Scale(content, "Размер свёрнутого", settings.CompactScale, 75, 150, "%", x => settings.CompactScale = x);
                Scale(content, "Размер раскрытого", settings.ExpandedScale, 80, 150, "%", x => settings.ExpandedScale = x);
                Scale(content, "Скругление", settings.Radius, 0, 40, " px", x => settings.Radius = x);
                Section(content, "Анимация");
                Toggle(content, "Меньше анимаций", settings.ReducedMotion, x => settings.ReducedMotion = x);
                Choice(content, "Раскрытие", settings.AnimationStyle, AnimationStyles, x => settings.AnimationStyle = x);
                Scale(content, "Темп переходов", settings.AnimationSpeed, 70, 140, "%", x => settings.AnimationSpeed = x);
                Scale(content, "Упругость", settings.SpringStrength, 0, 100, "%", x => settings.SpringStrength = x);
                Choice(content, "Скрытие", settings.HideStyle, ["Капля вверх", "Схлопывание", "Растворение", "Без анимации"], x => settings.HideStyle = x);
                Action(content, "Повторить предпросмотр", () => { UpdatePreview(); preview.Play(); });
                Hint(content, "Если анимации отключены в Windows, переходы также отключаются. Постоянных движений в покое нет.");
                break;
            case 5:
                Section(content, "Тексты песен");
                Toggle(content, "Брать текст из приложения Яндекс Музыки", settings.YandexLyricsEnabled, x => settings.YandexLyricsEnabled = x);
                var connect = new Button { Content = "Подключить Яндекс Музыку", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 6) };
                var connectStatus = new TextBlock { Text = "Используется локальный порт 127.0.0.1:9226. Первое подключение перезапустит плеер. Аккаунт остаётся в Яндекс Музыке.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
                connect.Click += async (_, _) => { connect.IsEnabled = false; try { connectStatus.Text = await island.ConnectYandexAsync(); } catch (Exception) { connectStatus.Text = "Не удалось подключиться. Откройте окно плеера и повторите."; } finally { connect.IsEnabled = true; } };
                content.Children.Add(connect); content.Children.Add(connectStatus);
                Toggle(content, "Искать текст текущей песни", settings.LyricsEnabled, x => settings.LyricsEnabled = x);
                Toggle(content, "Строка песни на свёрнутом острове", settings.LyricsOnCompact, x => settings.LyricsOnCompact = x);
                Scale(content, "Сдвиг текста: плюс — раньше, минус — позже", settings.LyricsAdjustment * 10, -100, 100, " × 0,1 с", x => settings.LyricsAdjustment = x / 10);
                Hint(content, "Сначала используется доступный текст Яндекс Музыки, затем LRCLIB и lrcmux. Во внешние источники передаются только данные песни. Аудио, пароли и токены не передаются.");
                break;
            case 2: WeatherPage(content); break;
            case 3:
                Section(content, "Уведомления Telegram");
                var telegramState = new TextBlock { Text = island.TelegramNotificationStatus, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
                var connectTelegram = new Button { Content = "Подключить уведомления Telegram", HorizontalAlignment = HorizontalAlignment.Left };
                connectTelegram.Click += async (_, _) => { connectTelegram.IsEnabled = false; telegramState.Text = "Подключаем…"; try { telegramState.Text = await island.ConnectTelegramNotificationsAsync(); } catch (Exception) { telegramState.Text = "Не удалось подключиться. Повторите подключение."; } finally { connectTelegram.IsEnabled = true; } };
                content.Children.Add(connectTelegram); content.Children.Add(telegramState);
                Action(content, "Открыть доступ к уведомлениям Windows", () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { telegramState.Text = "Откройте параметры Windows → Конфиденциальность → Уведомления."; } });
                Action(content, "Отключить уведомления Telegram", () => { island.DisableTelegramNotifications(); telegramState.Text = island.TelegramNotificationStatus; });
                Hint(content, "Нужно разрешение Windows на чтение уведомлений. Остров показывает только новые системные уведомления Telegram; старые не воспроизводит. Сообщение видно 5 секунд. Текст хранится в памяти до окончания показа, не записывается на диск и не отправляется в переводчик. В Telegram должны быть включены системные уведомления Windows.");
                Section(content, "Буфер обмена");
                Toggle(content, "Собирать новый текст из буфера", settings.ClipboardEnabled, x => { island.SetPrivacyOptions(x, settings.DownloadsEnabled); });
                Toggle(content, "Уведомлять о копировании в острове", settings.ClipboardNotifications, x => settings.ClipboardNotifications = x);
                Toggle(content, "Удерживать уведомление при наведении", settings.HoldNoticesOnHover, x => settings.HoldNoticesOnHover = x);
                Toggle(content, "Переводить иностранный текст на русский", settings.AutoTranslateClipboard, x => settings.AutoTranslateClipboard = x);
                Toggle(content, "Вставлять перевод после двойного Ctrl+C", settings.AutoPasteDoubleCopy, x => settings.AutoPasteDoubleCopy = x);
                Hint(content, "Автовставка работает в исходном редактируемом поле. При смене окна, выделения или буфера перевод остаётся для ручной вставки.");
                Hint(content, "Текст отправляется в Google Translate или MyMemory. EN→RU не меняет системный буфер. После успешного EN→RU следующий русский текст переводится на английский и заменяет буфер. Затем активация сбрасывается. Оригиналы доступны в истории; предел — 4 000 символов.");
                Hint(content, "До 50 записей в памяти. При закрытии приложения история удаляется. Уже скопированный текст при включении не считывается.");
                Section(content, "Загрузки");
                Toggle(content, "Наблюдать за выбранной папкой", settings.DownloadsEnabled, x => { island.SetPrivacyOptions(settings.ClipboardEnabled, x); });
                Hint(content, string.IsNullOrEmpty(settings.DownloadFolder) ? "Папка не выбрана" : settings.DownloadFolder);
                Action(content, "Выбрать папку", () => { island.ChooseDownloads(); BuildPage(); });
                Section(content, "Хранилище"); Action(content, "Открыть папку данных", island.ShowDataFolder);
                break;
            case 4:
                Section(content, "Discord Rich Presence");
                Toggle(content, "Показывать OSTROVOK в Discord", settings.DiscordPresenceEnabled, x => settings.DiscordPresenceEnabled = x);
                Toggle(content, "Показывать песню и исполнителя", settings.DiscordShowTrack, x => settings.DiscordShowTrack = x);
                content.Children.Add(new TextBlock { Text = "Application ID", Margin = new Thickness(0, 12, 0, 8) });
                var discordId = new TextBox { Text = settings.DiscordApplicationId, MaxLength = 20, Margin = new Thickness(0, 0, 0, 8) };
                System.Windows.Automation.AutomationProperties.SetName(discordId, "Discord Application ID");
                discordId.TextChanged += (_, _) => { settings.DiscordApplicationId = discordId.Text.Trim(); Changed(); }; content.Children.Add(discordId);
                discordStatus = new TextBlock { Text = island.DiscordPresenceStatus, Foreground = (Brush)FindResource("Secondary"), TextWrapping = TextWrapping.Wrap };
                content.Children.Add(discordStatus);
                Hint(content, "Нужен запущенный Discord. Буфер обмена, заметки и уведомления в статус не передаются.");
                Section(content, "Запуск");
                Toggle(content, "Запускать при входе в Windows", island.AutostartEnabled, island.SetAutostart);
                Section(content, "Версия"); Hint(content, "Desktop Island 1.0.3 · Windows x64");
                Hint(content, "Код приложения написан отдельно. Данные и настройки Dyland не импортируются.");
                Action(content, "Показать остров", island.PreviewIsland);
                Action(content, "Открыть папку данных", island.ShowDataFolder);
                break;
        }
        page.Content = content; pageScroll?.ScrollToTop(); Motion.RevealContent(content, 320, 35);
    }
    private void WeatherPage(StackPanel content)
    {
        Hint(content, "Прогноз Open-Meteo. Выберите город из результатов поиска; он будет сохранён на этом компьютере.");
        var input = new TextBox { Text = settings.WeatherCity, Margin = new Thickness(0, 14, 0, 10), Height = 38 };
        content.Children.Add(input);
        var results = new ListBox { MaxHeight = 220, DisplayMemberPath = "Label" };
        var state = new TextBlock { Text = settings.WeatherLocation?.Label ?? "Город не выбран", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 10) };
        var search = new Button { Content = "Найти город", HorizontalAlignment = HorizontalAlignment.Left };
        search.Click += async (_, _) =>
        {
            if (input.Text.Trim().Length < 2) { state.Text = "Введите хотя бы две буквы"; return; }
            search.IsEnabled = false; state.Text = "Ищем город…";
            try { var locations = await new WeatherService().SearchAsync(input.Text.Trim()); results.ItemsSource = locations; state.Text = locations.Length == 0 ? "Город не найден" : "Выберите город в списке"; }
            catch (Exception ex) { state.Text = "Не удалось выполнить поиск: " + ex.Message; }
            finally { search.IsEnabled = true; }
        };
        input.KeyDown += (_, args) => { if (args.Key == Key.Enter) search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        results.SelectionChanged += async (_, _) => { if (results.SelectedItem is WeatherLocation location) { state.Text = "Загружаем прогноз для " + location.Label; await island.SetWeatherLocationAsync(location); state.Text = "Выбран город: " + location.Label; } };
        content.Children.Add(search); content.Children.Add(state); content.Children.Add(results);
        Hint(content, "Название города отправляется в поиск Open-Meteo; координаты выбранного города — в сервис прогноза. Автоматического определения местоположения нет.");
        Action(content, "Открыть погоду на острове", () => { island.ShowWeatherPage(); });
    }
    private void Toggle(StackPanel parent, string text, bool value, Action<bool> set)
    {
        var check = new CheckBox { Content = text, Style = (Style)System.Windows.Application.Current.FindResource("SettingsSwitch"), IsChecked = value };
        check.Click += (_, _) => { set(check.IsChecked == true); Changed(); }; parent.Children.Add(check);
    }
    private void Choice(StackPanel parent, string label, string value, string[] values, Action<string> set)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 8) });
        var select = new ComboBox { ItemsSource = values, SelectedItem = values.Contains(value) ? value : values[0], Margin = new Thickness(0, 0, 0, 8) };
        select.SelectionChanged += (_, _) => { if (select.SelectedItem is string option) { set(option); Changed(); } }; parent.Children.Add(select);
    }
    private void Scale(StackPanel parent, string label, double value, double min, double max, string unit, Action<double> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 16, 0, 4) };
        var number = new TextBlock { Text = $"{value:0}{unit}", Foreground = (Brush)FindResource("Secondary"), HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(new TextBlock { Text = label }); row.Children.Add(number); parent.Children.Add(row);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
        slider.ValueChanged += (_, _) => { set(slider.Value); number.Text = $"{slider.Value:0}{unit}"; Changed(); }; parent.Children.Add(slider);
    }
    private void Section(StackPanel parent, string text) => parent.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("Secondary"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 24, 0, 8) });
    private void Hint(StackPanel parent, string text) => parent.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 20, Margin = new Thickness(0, 8, 0, 8) });
    private static void Action(StackPanel parent, string text, Action action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 4) };
        button.Click += (_, _) => action(); parent.Children.Add(button);
    }
}
