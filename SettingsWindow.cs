using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopIsland.Services;

namespace DesktopIsland;

internal sealed partial class SettingsWindow : Window
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
    private readonly Border windowFrame;
    private readonly WindowBrand nativeBrand;
    private string paletteSignature = "";
    private readonly SolidColorBrush captionBrush = new();
    private readonly Dictionary<DependencyObject, int> colorRevisions = new();
    internal static readonly string[] AnimationStyles = ["Капля", "Пружина", "Плавно", "Волна", "Желе", "Тянучка", "Мяч", "Резинка", "Щелчок", "Пузырь"];

    public SettingsWindow(MainWindow island, AppSettings settings)
    {
        this.island = island; this.settings = settings;
        Icon = MainWindow.BrandIcon;
        nativeBrand = new WindowBrand(this);
        preview = new IslandMotionPreview(previewClock);
        Title = "Настройки Desktop Island"; Width = 860; Height = 680; MinWidth = 720; MinHeight = 520;
        var work = Services.WindowInterop.WorkArea(new System.Windows.Interop.WindowInteropHelper(island).Handle);
        Width = Math.Min(Width, work.Width); Height = Math.Min(Height, work.Height); MinWidth = Math.Min(MinWidth, work.Width); MinHeight = Math.Min(MinHeight, work.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = new SolidColorBrush(Color.FromRgb(20, 22, 25));
        Owner = island; Topmost = island.Topmost;
        WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int dark = 1, border = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
            ApplyNativeCaptionPalette();
        };
        Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");
        var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        sidebar = new DockPanel { Margin = new Thickness(0, 0, 1, 0) };
        var brand = new StackPanel { Margin = new Thickness(20, 24, 10, 24) };
        brand.Children.Add(new TextBlock { Text = "Desktop Island", FontSize = 19, FontWeight = FontWeights.SemiBold });
        brand.Children.Add(new TextBlock { Text = "Настройки · " + typeof(App).Assembly.GetName().Version!.ToString(3), Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        ((TextBlock)brand.Children[1]).SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new TextBlock { Text = "Изменения сохраняются\nна этом компьютере", Foreground = (Brush)FindResource("Muted"), FontSize = 12, Margin = new Thickness(20, 12, 12, 20) };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        foreach (var title in new[] { "Остров", "Внешний вид", "Погода", "Данные", "Система", "Музыка", "Вкладки" }) navigation.Items.Add(title);
        navigation.Margin = new Thickness(10, 0, 10, 0); navigation.SelectionChanged += (_, _) => BuildPage(); sidebar.Children.Add(navigation); root.Children.Add(sidebar);
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(32, 24, 24, 24) };
        pageScroll = scroll;
        scroll.Resources[SystemParameters.VerticalScrollBarWidthKey] = 10d;
        Grid.SetColumn(scroll, 1); root.Children.Add(scroll);
        windowFrame = new Border { BorderThickness = new Thickness(1), Child = root }; Content = windowFrame;
        captionBrush.Changed += (_, _) => ApplyNativeCaptionPalette();
        ApplyIslandPalette();
        commit.Tick += (_, _) => { commit.Stop(); island.ApplyPreferences(); };
        clockTimer.Tick += (_, _) => { previewClock.Text = DateTime.Now.ToString("HH:mm    d MMMM"); if (discordStatus != null) discordStatus.Text = island.DiscordPresenceStatus; }; clockTimer.Start();
        island.WeatherCitiesUpdated += RefreshWeatherCities;
        StateChanged += (_, _) => island.RefreshSettingsLayer();
        Closed += (_, _) => { island.WeatherCitiesUpdated -= RefreshWeatherCities; commit.Stop(); clockTimer.Stop(); island.ApplyPreferences(); };
        initialized = true; SelectPage(0);
    }
    private void ApplyNativeCaptionPalette()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var color = captionBrush.Color;
        int caption = color.R | color.G << 8 | color.B << 16;
        int ink = 0xF1F1F1;
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, 36, ref ink, sizeof(int));
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
        if (Background is not LinearGradientBrush background)
        { background = new LinearGradientBrush(top, palette.Bottom, new Point(0, 0), new Point(0, 1)); Background = windowFrame.Background = background; }
        ThemeColor(background.GradientStops[0], GradientStop.ColorProperty, top);
        ThemeColor(background.GradientStops[1], GradientStop.ColorProperty, palette.Bottom);
        ThemeColor(captionBrush, SolidColorBrush.ColorProperty, top);
        sidebar.Background = Paint("Sidebar", Mix(palette.Top, palette.Bottom, .35));
        windowFrame.BorderBrush = Paint("WindowRim", palette.Rim);
        Paint("ControlSurface", Mix(palette.Top, palette.Bottom, .45));
        Paint("SelectionSurface", Mix(palette.Top, Colors.White, .16));
        Paint("HoverSurface", Mix(palette.Top, Colors.White, .22));
        Paint("InputSurface", palette.Bottom);
        Paint("SwitchOff", Mix(palette.Top, palette.Bottom, .2));
        Paint("PopupSurface", palette.Middle);
        Paint("Line", Mix(palette.Rim, palette.Bottom, .45)); Paint("PopupBorder", Mix(palette.Rim, palette.Bottom, .45));
        Paint("Accent", Mix(palette.Top, Colors.White, .78));
        Paint("Muted", Color.FromRgb(215, 217, 221));
        Paint("Secondary", Color.FromRgb(230, 232, 235));
        var contrast = ControlContrast.For([palette.Top, palette.Bottom]);
        Paint("SliderRail", contrast.Rail); Paint("SliderInk", contrast.Ink); Paint("SliderRim", contrast.Rim);
    }
    private SolidColorBrush Paint(string key, Color color)
    {
        if (!Resources.Contains(key) || Resources[key] is not SolidColorBrush brush || brush.IsFrozen)
        { var created = new SolidColorBrush(color); Resources[key] = created; return created; }
        ThemeColor(brush, SolidColorBrush.ColorProperty, color); return brush;
    }
    private void ThemeColor(Animatable target, DependencyProperty property, Color color)
    {
        Color from = (Color)target.GetValue(property);
        int revision = colorRevisions.TryGetValue(target, out int old) ? old + 1 : 1; colorRevisions[target] = revision;
        if (!IsVisible || !Motion.Enabled) { target.BeginAnimation(property, null); target.SetValue(property, color); return; }
        target.SetValue(property, from);
        var animation = new ColorAnimation(from, color, TimeSpan.FromMilliseconds(420)) { FillBehavior = FillBehavior.HoldEnd, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        animation.Completed += (_, _) => { if (colorRevisions[target] == revision) { target.SetValue(property, color); target.BeginAnimation(property, null); } };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal void SelectPage(int index) { int target = Math.Clamp(index, 0, 6); if (navigation.SelectedIndex == target) BuildPage(); else navigation.SelectedIndex = target; }
    private void Changed() { if (!initialized) return; commit.Stop(); commit.Start(); UpdatePreview(); }
    private void UpdatePreview() { preview.Configure(settings.AnimationStyle, settings.Radius, settings.AnimationSpeed, settings.SpringStrength); previewClock.Text = DateTime.Now.ToString("HH:mm    d MMMM"); }
    private void BuildPage()
    {
        weatherCitiesPanel = null;
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
                Section(content, "Плеер");
                Hint(content, "Источник воспроизведения");
                var players = new ComboBox { ItemsSource = island.PlaybackSources, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = island.PlaybackSource, Margin = new Thickness(0, 8, 0, 12) };
                System.Windows.Automation.AutomationProperties.SetName(players, "Источник воспроизведения");
                players.SelectionChanged += async (_, _) => { if (players.SelectedValue is string id) await island.SelectPlaybackSourceAsync(id); };
                content.Children.Add(players);
                Action(content, island.YouTubeConnectionAction, async () => { await island.ToggleYouTubeConnectionAsync(); BuildPage(); });
                Action(content, "Настройки звука Windows", island.OpenWindowsSoundSettings);
                Section(content, "Тексты песен");
                Action(content, "Искать текст текущей песни заново", island.RetryCurrentLyrics);
                Action(content, "Открыть файл LRC для текущей песни", island.OpenCurrentLyricsFile);
                Toggle(content, "Брать текст из приложения Яндекс Музыки", settings.YandexLyricsEnabled, x => settings.YandexLyricsEnabled = x);
                var connect = new Button { Content = "Подключить Яндекс Музыку", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 6) };
                var connectStatus = new TextBlock { Text = "Используется локальный порт 127.0.0.1:9226. Первое подключение перезапустит плеер. Аккаунт остаётся в Яндекс Музыке.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
                connect.Click += async (_, _) => { connect.IsEnabled = false; try { connectStatus.Text = await island.ConnectYandexAsync(); } catch (Exception) { connectStatus.Text = "Не удалось подключиться. Откройте окно плеера и повторите."; } finally { connect.IsEnabled = true; } };
                content.Children.Add(connect); content.Children.Add(connectStatus);
                Toggle(content, "Искать текст текущей песни", settings.LyricsEnabled, x => settings.LyricsEnabled = x);
                Toggle(content, "Строка песни на свёрнутом острове", settings.LyricsOnCompact, x => settings.LyricsOnCompact = x);
                Toggle(content, "Read Along — плашка на текущем слове", settings.ReadAlong, x => settings.ReadAlong = x);
                Scale(content, "Сдвиг слов Read Along", settings.ReadAlongAdjustment * 1000, -1000, 1000, " мс", x => settings.ReadAlongAdjustment = x / 1000);
                Hint(content, "Выключено по умолчанию. Если источник даёт только таймкоды строк, время слов рассчитывается приблизительно.");
                Hint(content, island.ReadAlongTimingStatus);
                Scale(content, "Сдвиг текста: плюс — раньше, минус — позже", settings.LyricsAdjustment * 10, -100, 100, " × 0,1 с", x => settings.LyricsAdjustment = x / 10);
                Hint(content, "Сначала используется доступный текст Яндекс Музыки, затем LRCLIB и lrcmux. Во внешние источники передаются только данные песни. Аудио, пароли и токены не передаются.");
                break;
            case 6:
                Section(content, "Разделы острова");
                Hint(content, "Скрытие убирает вкладку и её пункт меню. Файлы и заметки сохраняются. Плеер и настройки доступны всегда.");
                foreach (var tab in MainWindow.CustomTabs)
                {
                    int id = tab.Id;
                    Toggle(content, tab.Title, !settings.HiddenTabs.Contains(id), enabled =>
                    { settings.HiddenTabs = Services.DisplayLayout.NormalizeHidden(enabled ? settings.HiddenTabs.Where(x => x != id) : settings.HiddenTabs.Append(id)); });
                }
                Action(content, "Показать все вкладки", () => { settings.HiddenTabs = []; Changed(); BuildPage(); });
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
                Section(content, "Подключения");
                Toggle(content, "Автоматически проверять подключения", settings.AutoSetup, x => settings.AutoSetup = x);
                Hint(content, island.ConnectionSummary);
                Action(content, "Проверить подключения", async () => { await island.AutoConnectAsync(true); BuildPage(); });
                Toggle(content, "Заряд геймпада", settings.ShowGamepadBattery, x => settings.ShowGamepadBattery = x);
                Hint(content, "Поддерживаются XInput-геймпады. Windows передаёт уровень заряда, а не точный процент; USB-устройства могут не передавать заряд.");
                Section(content, "Запуск");
                Toggle(content, "Запускать при входе в Windows", island.AutostartEnabled, island.SetAutostart);
                Section(content, "Обновления");
                Toggle(content, "Проверять обновления на GitHub", settings.CheckUpdates, x => settings.CheckUpdates = x);
                Hint(content, island.UpdateStatus);
                Action(content, "Проверить обновления", async () => { await island.CheckAppUpdatesAsync(true); BuildPage(); });
                if (island.UpdateStatus.StartsWith("Доступна", StringComparison.Ordinal)) Action(content, "Скачать и установить", async () => await island.InstallAppUpdateAsync());
                Section(content, "Версия"); Hint(content, "OSTROVOK " + typeof(App).Assembly.GetName().Version!.ToString(3) + " · Windows x64");
                Hint(content, "Код приложения написан отдельно. Данные и настройки Dyland не импортируются.");
                Action(content, "Показать остров", island.PreviewIsland);
                Action(content, "Открыть папку данных", island.ShowDataFolder);
                break;
        }
        PolishedMotion.Swap(page, () => { page.Content = content; pageScroll?.ScrollToTop(); }, 320, 5);
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
