using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIsland.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DesktopIsland;

public partial class MainWindow : Window
{
    private readonly LocalStore store;
    private readonly AppSettings settings;
    private readonly MediaService media = new();
    private readonly SystemMonitor monitor = new();
    private readonly AudioService audio = new();
    private readonly DownloadsService downloads;
    private readonly ObservableCollection<ShelfItem> files = [];
    private readonly ObservableCollection<TaskItem> tasks = [];
    private readonly ObservableCollection<CalendarEntry> calendar = [];
    private readonly ObservableCollection<ClipboardEntry> clipboard = [];
    private readonly ObservableCollection<ActivityEntry> activity = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer noteTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Forms.NotifyIcon? tray;
    private HwndSource? source;
    private IntPtr handle;
    private bool ready, expanded, closing, clipboardRegistered, hotkeyRegistered, seeking, updatingPlayers, ticking;
    private bool readingClipboard;
    private bool updatingVolume;
    private bool suppressHover = App.VerificationMode;
    private string lastClipboard = "", previousBattery = "", previousNetwork = "";
    private int tickCount;
    private Point dragStart;
    private DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime selectedDay = DateTime.Today;
    private string? verificationRoot;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public MainWindow()
    {
        if (App.VerificationMode) verificationRoot = Path.Combine(Path.GetTempPath(), "DesktopIsland-ui-" + Guid.NewGuid().ToString("N"));
        store = new LocalStore(verificationRoot);
        InitializeComponent();
        Icon = BrandIcon;
        Timeline.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(BeginSeek), true);
        Timeline.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Seek), true);
        ContentTabs.SelectedIndex = 0;
        downloads = new DownloadsService(Dispatcher);
        store.Error += message => AddActivity("Хранилище", message);
        downloads.Activity += (title, detail) => AddActivity(title, detail, true);
        settings = store.Load("settings.json", () => new AppSettings());
        WeatherCityList.Normalize(settings);
        if (!App.VerificationMode && settings.ClipboardFeatureVersion < 1) { settings.ClipboardEnabled = true; settings.ClipboardFeatureVersion = 1; }
        Motion.Reduced = settings.ReducedMotion; LessMotion.IsChecked = settings.ReducedMotion;
        Notes.Text = store.LoadText("notes.txt");
        foreach (var path in store.Load("files.json", () => Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)) files.Add(new ShelfItem { Path = path });
        foreach (var address in store.Load("links.json", () => Array.Empty<string>()).Where(ShelfItem.IsWebLink).Distinct(StringComparer.OrdinalIgnoreCase)) files.Add(new ShelfItem { Path = address });
        foreach (var item in store.Load("tasks.json", () => new List<TaskItem>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))) AttachTask(item);
        foreach (var item in store.Load("calendar.json", () => new List<CalendarEntry>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title))) calendar.Add(item);
        Shelf.ItemsSource = files; TaskList.ItemsSource = tasks; ClipboardList.ItemsSource = clipboard;
        DownloadList.ItemsSource = downloads.Items; ActivityList.ItemsSource = activity;
        Pin.IsChecked = settings.Pinned; StayOnTop.IsChecked = settings.Topmost;
        HideFullscreen.IsChecked = settings.HideInFullscreen; WatchClipboard.IsChecked = settings.ClipboardEnabled;
        WatchDownloads.IsChecked = settings.DownloadsEnabled; DownloadFolderText.Text = string.IsNullOrEmpty(settings.DownloadFolder) ? "Папка не выбрана" : settings.DownloadFolder;
        Topmost = settings.Topmost;
        if (!App.VerificationMode)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            Autostart.IsChecked = key?.GetValue("DesktopIsland") is string;
        }
        ready = true;
        media.Updated += UpdateMedia;
        timer.Tick += Tick;
        leaveTimer.Tick += (_, _) => TryAutoCollapse();
        noteTimer.Tick += (_, _) => FlushNotes();
        SourceInitialized += InitializeNative;
        Loaded += OnLoaded;
        Closing += OnClosing;
        BuildMonth(); BuildToolbar();
        InitializeLyrics();
        InitializeClipboardNotices();
        InitializeTelegramNotices();
        if (settings.LastTab is >= 0 and <= 10 && settings.LastTab != 9) Navigation.SelectedIndex = settings.LastTab;
        RefreshIdleContent();
    }

    private void InitializeNative(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(this).Handle;
        WindowInterop.HideFromAltTab(handle);
        source = HwndSource.FromHwnd(handle); source?.AddHook(WindowMessage);
        if (App.VerificationMode) return;
        hotkeyRegistered = WindowInterop.RegisterHotKey(handle, WindowInterop.HotkeyId, 0x4003, 0x20);
        if (!hotkeyRegistered) AddActivity("Горячая клавиша", "Ctrl + Alt + Пробел занята другим приложением. Остров можно открыть через трей.");
        UpdateClipboardListener();
        tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "OSTROVOK", Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Показать остров", null, (_, _) => Dispatcher.Invoke(Reveal));
        menu.Items.Add("Скрыть остров", null, (_, _) => Dispatcher.Invoke(HideAnimated));
        menu.Items.Add("Настройки", null, (_, _) => Dispatcher.Invoke(() => OpenPreferences()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeIdleGlow();
        if (settings.Left is double left && settings.Top is double top && double.IsFinite(left) && double.IsFinite(top))
        {
            Left = left; Top = top;
            // Windows selects the nearest monitor, so disconnected displays are handled too.
            ClampToMonitor();
        }
        else ResetPosition(this, new RoutedEventArgs());
        if (!settings.Detached) Top = WindowInterop.WorkArea(handle).Top;
        Width = CompactPresentationWidth; Height = CompactHeight;
        Left += (CompactWidth - Width) / 2; ClampToMonitor(); ApplyShellAppearance();
        if (settings.Pinned) SetExpanded(true);
        if (settings.DownloadsEnabled) StartDownloads();
        RefreshSystem(); RefreshVolume(); UpdateClock(); RefreshIdleContent(); timer.Start();
        if (App.VerificationMode) { if (Environment.GetCommandLineArgs().Contains("--showcase-weather")) await CaptureWeatherShowcaseAsync(); else if (Environment.GetCommandLineArgs().Contains("--showcase-update")) await CaptureUpdateShowcaseAsync(); else if (Environment.GetCommandLineArgs().Contains("--verify-motion")) await VerifyMotionAsync(); else if (Environment.GetCommandLineArgs().Contains("--showcase")) await CaptureShowcaseAsync(); else await VerifyUiAsync(); return; }
        if (Environment.GetCommandLineArgs().Contains("--connect-telegram"))
        {
            Reveal(); OpenPreferences(3);
            await ConnectTelegramNotificationsAsync();
            preferences?.SelectPage(3);
        }
        else if (settings.TelegramNotificationsEnabled && !settings.AutoSetup) await telegramNotifications.ConnectAsync(false);
        await media.InitializeAsync(); UpdateMedia();
        await audio.RefreshDeviceNameAsync(); AudioDeviceText.Text = audio.DeviceName;
        _ = LoadWeatherAsync();
        await AutoConnectAsync(false);
        if (Environment.GetCommandLineArgs().Contains("--updated"))
        {
            AddActivity("Обновление установлено", "OSTROVOK " + Services.AppUpdateService.CurrentVersion.ToString(3));
            ShowClipboardNotice(new ClipboardEntry(), "Обновлено до " + Services.AppUpdateService.CurrentVersion.ToString(3), "Настройки сохранены", 7);
        }
        if (Environment.GetCommandLineArgs().Contains("--update-failed")) ShowClipboardNotice(new ClipboardEntry(), "Обновление не установлено", "Предыдущая версия восстановлена. Повторите попытку в настройках системы.", 8);
        AddActivity("Приложение запущено", "Музыка, файлы, заметки, задачи и календарь готовы.");
    }

    private async void Tick(object? sender, EventArgs e)
    {
        if (closing || ticking) return;
        ticking = true;
        try
        {
            if (DateTime.UtcNow >= nextUpdateCheck) _ = CheckAppUpdatesAsync();
            UpdateClock(); CheckReminders(); downloads.Tick();
            RefreshIdleContent();
            UpdateDiscordPresence();
            if (!App.VerificationMode) _ = RefreshYandexPlaybackAsync();
            if (tickCount % 60 == 0 && !App.VerificationMode) _ = LoadWeatherAsync();
            if (++tickCount % 2 == 0)
            {
                RefreshSystem(); RefreshVolume();
                await media.RefreshAsync();
                if (tickCount % 10 == 0) { await audio.RefreshDeviceNameAsync(); AudioDeviceText.Text = audio.DeviceName; }
            }
            var hideForFullscreen = settings.HideInFullscreen && WindowInterop.OtherAppFullscreen(handle);
            Opacity = hideForFullscreen ? 0 : 1;
            IsHitTestVisible = !hideForFullscreen;
        }
        catch (Exception ex) { Status.Text = "Обновление: " + ex.Message; }
        finally { ticking = false; }
    }

    private void UpdateClock() { UpdateIdleClockHands(DateTime.Now); Clock.Text = DateTime.Now.ToString("HH:mm"); HomeClock.Text = Clock.Text; HomeDate.Text = DateTime.Now.ToString("dddd, d MMMM", CultureInfo.GetCultureInfo("ru-RU")); }
    private void RefreshSystem()
    {
        monitor.Refresh(); RefreshGamepad(); Battery.Text = monitor.HasBattery ? monitor.Battery : DateTime.Now.ToString("ddd, d MMM", CultureInfo.GetCultureInfo("ru-RU")); PowerText.Text = monitor.BatteryDetail;
        CpuText.Text = $"{monitor.Cpu:0}%"; Motion.Progress(CpuBar, monitor.Cpu);
        MemoryText.Text = $"{DownloadEntry.FormatBytes(monitor.MemoryUsed)} / {DownloadEntry.FormatBytes(monitor.MemoryTotal)}"; Motion.Progress(MemoryBar, monitor.MemoryPercent);
        NetworkText.Text = monitor.Network; NetworkRate.Text = $"Приём {DownloadEntry.FormatBytes(monitor.ReceiveRate)}/с   ·   Отправка {DownloadEntry.FormatBytes(monitor.SendRate)}/с";
        ReceiveText.Text = DownloadEntry.FormatBytes(monitor.ReceiveRate) + "/с"; SendText.Text = DownloadEntry.FormatBytes(monitor.SendRate) + "/с";
        DiskText.Text = monitor.Disk;
        if (previousBattery.Length > 0 && monitor.BatteryDetail != previousBattery) AddActivity("Питание", monitor.BatteryDetail);
        if (previousNetwork.Length > 0 && monitor.Network != previousNetwork) AddActivity("Сеть", monitor.Network);
        previousBattery = monitor.BatteryDetail; previousNetwork = monitor.Network;
    }
    private void SetExpanded(bool value)
    {
        if (expanded == value) return;
        AnimateExpansion(value);
    }
    private void ClampToMonitor()
    {
        var area = WindowInterop.WorkArea(handle);
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        MaxHeight = area.Height;
    }
    public void Reveal() { collapseUntilPointerLeaves = false; bool wasHidden = !IsVisible || hiding; ++hideRevision; hiding = false; BeginAnimation(OpacityProperty, null); BeginAnimation(TopProperty, null); Motion.ClearContentMotion(Shell); ClearCollapseFrame(); Opacity = 1; Show(); IsHitTestVisible = true; SelectOpeningTab(); SetExpanded(true); if (wasHidden) AnimateRevealShell(); Activate(); }
    private void SelectOpeningTab() { if (!expanded && (settings.OpeningTab == "Всегда первая вкладка" || settings.OpeningTab == "Плеер, если играет музыка" && media.Available)) Navigation.SelectedIndex = 0; }
    private void Expand(object sender, MouseEventArgs e) { if (suppressHover || collapseUntilPointerLeaves) return; leaveTimer.Stop(); if (!expanded && HasCompactNotification) { if (settings.HoldNoticesOnHover) PauseCompactNotificationTimers(); return; } if (!expanded && CompactEdgeAt(e.GetPosition(this).X)) return; if (!settings.ExpandOnClick && !draggingWindow) { SelectOpeningTab(); SetExpanded(true); } }
    private void Collapse(object sender, MouseEventArgs e) { collapseUntilPointerLeaves = false; if (suppressHover) return; if (!expanded && HasCompactNotification) { if (settings.HoldNoticesOnHover) ResumeCompactNotificationTimers(); return; } leaveTimer.Start(); }
    private void MoveIsland(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        leaveTimer.Stop(); if (shellMoving) StopShellMotion(true);
        var previous = new Point(Left, Top); bool wasCompact = !expanded;
        draggingWindow = true; try { DragMove(); } finally { draggingWindow = false; }
        if ((previous - new Point(Left, Top)).Length > 3) { settings.Detached = true; ApplyShellAppearance(); }
        else if (wasCompact && settings.ExpandOnClick) { SelectOpeningTab(); SetExpanded(true); }
        ClampToMonitor(); SaveSettings();
    }
    private void ResetPosition(object sender, RoutedEventArgs e)
    {
        if (shellMoving) StopShellMotion(true);
        var area = WindowInterop.WorkArea(handle);
        settings.Detached = false;
        Left = area.Left + (area.Width - Width) / 2; Top = area.Top;
        ApplyShellAppearance();
        if (ready) SaveSettings();
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Pin.IsChecked = false; settings.Pinned = false; SetExpanded(false); SaveSettings(); e.Handled = true; }
    }
    private void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedIndex != 0) CancelTrackVisualHold();
        if (ContentTabs == null) return;
        if (ready && !TabEnabled(Navigation.SelectedIndex)) { Navigation.SelectedIndex = 0; return; }
        int previousPage = ContentTabs.SelectedIndex;
        var outgoingPage = previousPage != Navigation.SelectedIndex ? CapturePageForTransition() : null;
        if (ready && Navigation.SelectedIndex == 9 && !App.VerificationMode) { Navigation.SelectedIndex = 0; OpenPreferences(); return; }
        if (!navigatingForClipboard) clipboardReturnTab = null;
        if (Navigation.SelectedIndex is >= 0 and <= 10 && Navigation.SelectedIndex != 2 && Navigation.SelectedIndex != 9) lastNonClipboardTab = Navigation.SelectedIndex;
        ContentTabs.SelectedIndex = Navigation.SelectedIndex;
        if (ready) { UpdateToolbar(); settings.LastTab = Navigation.SelectedIndex; if (expanded) AnimateExpansion(true); SaveSettings(); }
        if (ready && IsLoaded && expanded) TransitionToPage(outgoingPage, Navigation.SelectedIndex >= previousPage ? 1 : -1);
        Status.Text = Navigation.SelectedIndex switch
        {
            1 => $"Файлов на полке: {files.Count}. Оригиналы остаются на месте.",
            2 => "Двойной щелчок копирует запись. Вставьте её сочетанием Ctrl + V.",
            5 => "Локальный календарь. Напоминания приходят, пока приложение запущено.",
            6 => "Размер берётся из файла. Общий размер загрузки и проценты не определяются.",
            _ => "Данные хранятся на этом компьютере"
        };
    }

    private void UpdateMedia()
    {
        if (closing) return;
        var trackKey = media.Source + "\n" + media.Title + "\n" + media.Artist;
        bool previouslyAvailable = MediaPanel.Visibility == Visibility.Visible;
        bool trackChanged = trackKey != displayedTrack;
        if (trackChanged && media.Available && displayedTrack.Length > 0) BeginTrackVisualHold(trackKey);
        displayedTrack = trackKey;
        // Keep real controls unchanged while artwork loads; never layer a held bitmap over live metadata.
        if (media.ArtworkReady || !previouslyAvailable)
        {
            bool identityChanged = TrackTitle.Text != media.Title || TrackArtist.Text != media.Artist || !ReferenceEquals(Artwork.Source, media.Artwork);
            void CommitIdentity() { TrackTitle.Text = media.Title; TrackArtist.Text = media.Artist; Artwork.Source = media.Artwork; ArtFallback.Visibility = media.Artwork == null ? Visibility.Visible : Visibility.Collapsed; }
            if (identityChanged && expanded && previouslyAvailable) PolishedMotion.Swap(TrackIdentity, CommitIdentity, 420, 6);
            else CommitIdentity();
        }
        MediaSource.Text = media.Error ?? (string.IsNullOrEmpty(media.Source) ? "Плееры Windows" : media.Source);
        UpdatePlaybackButton(media.Playing);
        PlayButton.IsEnabled = media.CanPlay; NextButton.IsEnabled = media.CanNext; PreviousButton.IsEnabled = media.CanPrevious;
        Timeline.IsEnabled = CanSeekSelectedPlayer && media.Duration > 0;
        if (!seeking) { Timeline.Maximum = Math.Max(1, media.Duration); Timeline.Value = Math.Min(Timeline.Maximum, media.Position); }
        if (!seeking) Elapsed.Text = FormatTime(media.Position); DurationText.Text = FormatTime(media.Duration);
        var sources = media.Sessions.Select(x => x.SourceAppUserModelId).Distinct().ToArray();
        if (!sources.SequenceEqual(Players.Items.Cast<string>()))
        {
            updatingPlayers = true; Players.ItemsSource = sources;
            Players.SelectedItem = media.Source; Players.IsEnabled = sources.Length > 0; updatingPlayers = false;
        }
        else if (Players.SelectedItem as string != media.Source) { updatingPlayers = true; Players.SelectedItem = media.Source; updatingPlayers = false; }
        RefreshIdleContent(); UpdateToolbar(); UpdateYandexRating();
        if (!App.VerificationMode) _ = RefreshYandexPlaybackAsync();
        RefreshTrackLyrics();
        TryCommitTrackVisual();
        if (expanded && Navigation.SelectedIndex == 0 && previouslyAvailable != media.Available) AnimateExpansion(true);
    }
    private static string FormatTime(double seconds) => seconds >= 3600 ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");
    private async void SelectPlayer(object sender, SelectionChangedEventArgs e) { if (updatingPlayers || Players.SelectedItem is not string id) return; media.SelectedSource = id; await media.RefreshAsync(); }
    private async Task MediaCommand(string command, double seconds = 0)
    {
        bool success = command == "seek" ? await SeekSelectedPlayerAsync(seconds) : await CommandSelectedPlayerAsync(command);
        if (!success) Status.Text = media.Error ?? "Плеер не выполнил команду";
        await media.RefreshAsync();
    }
    private async void TogglePlayback(object sender, RoutedEventArgs e) => await MediaCommand("toggle");
    private async void Previous(object sender, RoutedEventArgs e) => await MediaCommand("previous");
    private async void Next(object sender, RoutedEventArgs e) => await MediaCommand("next");
    private void BeginSeek(object sender, MouseButtonEventArgs e) { seeking = true; Elapsed.Text = FormatTime(Timeline.Value); }
    private void PreviewSeekTime(object sender, RoutedPropertyChangedEventArgs<double> e) { if (seeking) Elapsed.Text = FormatTime(e.NewValue); }
    private async void Seek(object sender, MouseButtonEventArgs e)
    {
        try { double target = await Dispatcher.InvokeAsync(() => Timeline.Value, DispatcherPriority.Input); await MediaCommand("seek", target); QueueCurrentLyricScroll(true); }
        finally { seeking = false; UpdateLyricPosition(media.Position); }
    }
    private void OpenSound(object sender, RoutedEventArgs e) => Open("ms-settings:sound");
    private void RefreshVolume()
    {
        audio.Refresh(); updatingVolume = true;
        double playerVolume = HasYandexVolume ? yandexPlayback!.Volume : audio.Volume;
        bool playerAvailable = HasYandexVolume || audio.Available;
        bool playerMuted = HasYandexVolume ? playerVolume <= .001 : audio.Muted;
        if (!writingYandexVolume) Volume.Value = playerVolume * 100;
        Volume.IsEnabled = playerAvailable; MuteButton.IsEnabled = playerAvailable;
        Volume.ToolTip = HasYandexVolume ? "Громкость Яндекс Музыки" : IsYandexSelected ? "Системная громкость. Прямое подключение Яндекс Музыки восстанавливается автоматически." : "Громкость системы";
        VolumeText.ToolTip = Volume.ToolTip;
        IdleVolume.Value = audio.Volume * 100; IdleVolume.IsEnabled = audio.Available; IdleVolumeText.Text = $"{audio.Volume * 100:0}%";
        VolumeText.Text = playerAvailable ? $"{playerVolume * 100:0}%" : IsYandexSelected ? "Нет связи" : "Нет устройства";
        VolumeGlyph.Data = Geometry.Parse(playerMuted ? "M 2,8 L 7,8 L 13,3 L 13,21 L 7,16 L 2,16 Z M 17,8 L 23,16 M 23,8 L 17,16" : "M 2,8 L 7,8 L 13,3 L 13,21 L 7,16 L 2,16 Z M 17,7 C 21,10 21,14 17,17");
        MuteButton.ToolTip = playerMuted ? "Включить звук" : "Выключить звук";
        System.Windows.Automation.AutomationProperties.SetName(MuteButton, (string)MuteButton.ToolTip); updatingVolume = false;
    }
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready || updatingVolume) return;
        if (ReferenceEquals(sender, Volume) && HasYandexVolume) { QueueYandexVolume(e.NewValue / 100); VolumeText.Text = $"{e.NewValue:0}%"; return; }
        if (!audio.SetVolume((float)(e.NewValue / 100))) Status.Text = "Не удалось изменить громкость: " + audio.Error;
        updatingVolume = true; IdleVolume.Value = e.NewValue; if (!IsYandexSelected) Volume.Value = e.NewValue; updatingVolume = false;
        IdleVolumeText.Text = $"{e.NewValue:0}%"; if (!IsYandexSelected) VolumeText.Text = IdleVolumeText.Text;
    }
    private void ToggleMute(object sender, RoutedEventArgs e) { if (HasYandexVolume) { QueueYandexVolume((yandexPlayback?.Volume ?? 0) > .001 ? 0 : lastYandexVolume); return; } if (!audio.SetMuted(!audio.Muted)) Status.Text = audio.Error; RefreshVolume(); }

    private void ReceiveFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
        else if (e.Data.GetData(DataFormats.UnicodeText) is string address && ShelfItem.IsWebLink(address.Trim()))
        {
            address = address.Trim();
            if (!files.Any(x => x.Path.Equals(address, StringComparison.OrdinalIgnoreCase))) files.Add(new ShelfItem { Path = address });
            SaveShelfReferences(); Navigation.SelectedIndex = 1;
        }
        e.Handled = true;
    }
    private void FilesOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.UnicodeText) is string text && ShelfItem.IsWebLink(text.Trim())) { SetExpanded(true); Navigation.SelectedIndex = 1; e.Effects = DragDropEffects.Copy; }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }
    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths.Where(x => File.Exists(x) || Directory.Exists(x)))
            if (!files.Any(x => x.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) files.Add(new ShelfItem { Path = p });
        SaveShelfReferences(); Navigation.SelectedIndex = 1;
        Status.Text = $"Файлов на полке: {files.Count}";
    }
    private void AddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Добавить файлы на полку" };
        if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
    }
    private void OpenShelf(object sender, MouseButtonEventArgs e) { if (Shelf.SelectedItem is ShelfItem item) Open(item.Path); }
    private void ShowShelfFolder(object sender, RoutedEventArgs e) { if (Shelf.SelectedItem is ShelfItem item) ShowInFolder(item.Path); }
    private void ShelfDragStart(object sender, MouseButtonEventArgs e) => dragStart = e.GetPosition(Shelf);
    private void DragShelf(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Shelf.SelectedItem is not ShelfItem item) return;
        var delta = e.GetPosition(Shelf) - dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (item.IsLink) { DragDrop.DoDragDrop(Shelf, new DataObject(DataFormats.UnicodeText, item.Path), DragDropEffects.Copy | DragDropEffects.Link); return; }
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) { Status.Text = "Файл больше не доступен"; return; }
        DragDrop.DoDragDrop(Shelf, new DataObject(DataFormats.FileDrop, new[] { item.Path }), DragDropEffects.Copy | DragDropEffects.Link);
    }
    private void RemoveFile(object sender, RoutedEventArgs e)
    {
        if (Shelf.SelectedItem is ShelfItem item) files.Remove(item);
        SaveShelfReferences();
        Status.Text = "Ссылка убрана с полки. Исходный файл остался на месте.";
    }
    private void SaveNotes(object sender, TextChangedEventArgs e)
    {
        if (!ready) return;
        NotesState.Text = "Сохраняется…"; noteTimer.Stop(); noteTimer.Start();
    }
    private void FlushNotes()
    {
        noteTimer.Stop();
        NotesState.Text = store.SaveText("notes.txt", Notes.Text) ? "Сохранено на этом компьютере" : "Не удалось сохранить. Скопируйте текст перед закрытием.";
    }
    private void AttachTask(TaskItem item) { tasks.Add(item); item.PropertyChanged += TaskChanged; }
    private void TaskChanged(object? sender, PropertyChangedEventArgs e) => store.Save("tasks.json", tasks.ToArray());
    private void AddTask(object sender, RoutedEventArgs e)
    {
        var text = TaskInput.Text.Trim(); if (text.Length == 0) return;
        AttachTask(new TaskItem { Text = text }); TaskInput.Clear(); store.Save("tasks.json", tasks.ToArray());
    }
    private void TaskKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { AddTask(sender, e); e.Handled = true; } }
    private void ClearDone(object sender, RoutedEventArgs e)
    {
        foreach (var task in tasks.Where(x => x.Done).ToArray()) { task.PropertyChanged -= TaskChanged; tasks.Remove(task); }
        store.Save("tasks.json", tasks.ToArray());
    }

    private void BuildMonth()
    {
        MonthTitle.Text = month.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")); MonthGrid.Children.Clear();
        foreach (var name in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" }) MonthGrid.Children.Add(new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 4) });
        var offset = ((int)month.DayOfWeek + 6) % 7;
        for (int i = 0; i < 42; i++)
        {
            var date = month.AddDays(i - offset);
            var button = new Button { Content = date.Day.ToString(), Tag = date, Padding = new Thickness(0, 4, 0, 4), Margin = new Thickness(2), Opacity = date.Month == month.Month ? 1 : .4 };
            if (date == selectedDay) { button.Background = (Brush)FindResource("Accent"); button.Foreground = new SolidColorBrush(Color.FromRgb(23, 27, 20)); }
            else if (calendar.Any(x => x.StartsAt.Date == date)) button.BorderBrush = (Brush)FindResource("Accent");
            button.Click += (_, _) => { selectedDay = date; if (date.Month != month.Month) month = new(date.Year, date.Month, 1); BuildMonth(); };
            MonthGrid.Children.Add(button);
        }
        SelectedDay.Text = selectedDay.ToString("dd.MM.yyyy");
        bool weekView = settings.CalendarView == "Неделя";
        DateTime periodStart = weekView ? selectedDay.AddDays(-(((int)selectedDay.DayOfWeek + 6) % 7)) : selectedDay;
        var entries = calendar.Where(x => x.StartsAt.Date >= periodStart && x.StartsAt.Date < periodStart.AddDays(weekView ? 7 : 1)).OrderBy(x => x.StartsAt).ToArray();
        EventList.ItemsSource = entries;
        CalendarEmpty.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MonthGrid.Visibility = settings.CalendarView == "Месяц" ? Visibility.Visible : Visibility.Collapsed;
        if (settings.CalendarView != "Месяц") MonthTitle.Text = weekView ? $"{periodStart:dd.MM} – {periodStart.AddDays(6):dd.MM}" : selectedDay.ToString("ddd, d MMM", CultureInfo.GetCultureInfo("ru-RU"));
        if (ready && IsLoaded && expanded) Motion.Enter(MonthGrid, 4, 160);
    }
    private void PreviousMonth(object sender, RoutedEventArgs e) => ShiftCalendar(-1);
    private void NextMonth(object sender, RoutedEventArgs e) => ShiftCalendar(1);
    private void ShiftCalendar(int direction)
    {
        if (month <= new DateTime(1900, 2, 1) && direction < 0 || month >= new DateTime(9998, 11, 1) && direction > 0) return;
        if (settings.CalendarView == "Месяц") month = month.AddMonths(direction);
        else { selectedDay = selectedDay.AddDays(direction * (settings.CalendarView == "Неделя" ? 7 : 1)); month = new(selectedDay.Year, selectedDay.Month, 1); }
        BuildMonth();
    }
    private void ChangeCalendarView(string view) { settings.CalendarView = view; BuildMonth(); if (expanded && Navigation.SelectedIndex == 5) AnimateExpansion(true); SaveSettings(); }
    private void CalendarDay(object sender, RoutedEventArgs e) => ChangeCalendarView("День");
    private void CalendarWeek(object sender, RoutedEventArgs e) => ChangeCalendarView("Неделя");
    private void CalendarMonth(object sender, RoutedEventArgs e) => ChangeCalendarView("Месяц");
    private void AddEvent(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EventTitle.Text)) { Status.Text = "Введите название события"; return; }
        if (!TimeOnly.TryParseExact(EventTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) { Status.Text = "Время нужно в формате ЧЧ:ММ, например 14:30"; return; }
        calendar.Add(new CalendarEntry { Title = EventTitle.Text.Trim(), StartsAt = selectedDay.Add(time.ToTimeSpan()) });
        store.Save("calendar.json", calendar.ToArray()); EventTitle.Clear(); BuildMonth(); Status.Text = "Событие сохранено";
    }
    private void RemoveEvent(object sender, RoutedEventArgs e) { if (EventList.SelectedItem is CalendarEntry item) calendar.Remove(item); store.Save("calendar.json", calendar.ToArray()); BuildMonth(); }
    private void CheckReminders()
    {
        var now = DateTime.Now;
        foreach (var entry in calendar.Where(x => !x.Reminded && x.StartsAt <= now && x.StartsAt > now.AddDays(-1)).ToArray())
        {
            entry.Reminded = true; AddActivity("Напоминание", $"{entry.Title} · {entry.StartsAt:HH:mm}", true); store.Save("calendar.json", calendar.ToArray());
        }
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x007E or 0x02E0) Dispatcher.BeginInvoke(new Action(() => { if (closing) return; StopShellMotion(true); ApplyShellAppearance(); AnimateExpansion(expanded); ClampToMonitor(); }));
        if (message == WindowInterop.ClipboardMessage && settings.ClipboardEnabled && !App.VerificationMode) _ = ReadClipboardAsync();
        if (message == WindowInterop.HotkeyMessage && wParam.ToInt32() == WindowInterop.HotkeyId) { if (IsVisible && !hiding) HideAnimated(); else Reveal(); handled = true; }
        return IntPtr.Zero;
    }
    private async Task ReadClipboardAsync()
    {
        if (readingClipboard) return;
        readingClipboard = true;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!settings.ClipboardEnabled || closing) return;
                try
                {
                    if (System.Windows.Clipboard.ContainsFileDropList())
                    {
                        HandleCopiedFiles(System.Windows.Clipboard.GetFileDropList().Cast<string>().ToArray()); return;
                    }
                    if (!System.Windows.Clipboard.ContainsText()) return;
                    var text = System.Windows.Clipboard.GetText();
                    if (text.Length == 0 || text.Length > 65536 || text == lastClipboard) return;
                    HandleCopiedText(AddClipboardText(text)); return;
                }
                catch (ExternalException) { await Task.Delay(80); }
            }
        }
        finally { readingClipboard = false; }
    }
    private void UpdateClipboardListener()
    {
        if (handle == IntPtr.Zero || App.VerificationMode) return;
        if (settings.ClipboardEnabled && !clipboardRegistered)
        {
            clipboardRegistered = WindowInterop.AddClipboardFormatListener(handle);
            if (!clipboardRegistered) { settings.ClipboardEnabled = false; WatchClipboard.IsChecked = false; AddActivity("Буфер обмена", "Windows не разрешила наблюдение за буфером."); }
        }
        else if (!settings.ClipboardEnabled && clipboardRegistered) { WindowInterop.RemoveClipboardFormatListener(handle); clipboardRegistered = false; }
        if (settings.ClipboardEnabled)
        {
            if (!doubleCopyHook.Start()) AddActivity("Перевод", "Не удалось включить двойное Ctrl+C.");
        }
        else { doubleCopyHook.Stop(); doubleCopyPending = false; doubleCopyRevision++; }
        ClipboardHint.Text = settings.ClipboardEnabled ? "Сбор включён. История хранится только до закрытия приложения." : "Сбор выключен. Включите его в настройках.";
    }
    private void CopyClipboardItem(object sender, RoutedEventArgs e)
    {
        if (ClipboardList.SelectedItem is not ClipboardEntry item) return;
        try { lastClipboard = item.Text; System.Windows.Clipboard.SetText(item.Text); ShowClipboardNotice(item, "Текст скопирован", item.Text, 5); Status.Text = "Текст скопирован. Нажмите Ctrl + V в нужном окне."; }
        catch (ExternalException) { Status.Text = "Буфер занят другим приложением. Повторите копирование."; }
    }
    private void ClearClipboard(object sender, RoutedEventArgs e) { StopClipboardTranslation(); doubleCopyPending = false; doubleCopyRevision++; translationGate.Reset(); DismissClipboardNotice(this, EventArgs.Empty); noticeEntry = null; clipboard.Clear(); lastClipboard = ""; ClipboardHint.Text = "История приложения очищена. Системный буфер не изменён."; }
    private void ClipboardSettingChanged(object sender, RoutedEventArgs e)
    {
        settings.ClipboardEnabled = WatchClipboard.IsChecked == true; UpdateClipboardListener();
        if (!settings.ClipboardEnabled) { clipboard.Clear(); lastClipboard = ""; StopClipboardTranslation(); translationGate.Reset(); DismissClipboardNotice(this, new RoutedEventArgs()); } SaveSettings();
    }
    private void ChooseDownloadFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "Папка для наблюдения за новыми загрузками", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(new FolderDialogOwner(handle)) != Forms.DialogResult.OK) return;
        settings.DownloadFolder = dialog.SelectedPath; DownloadFolderText.Text = dialog.SelectedPath;
        if (settings.DownloadsEnabled) StartDownloads(); SaveSettings();
    }
    private void DownloadsSettingChanged(object sender, RoutedEventArgs e)
    {
        settings.DownloadsEnabled = WatchDownloads.IsChecked == true;
        if (settings.DownloadsEnabled) StartDownloads(); else { downloads.Dispose(); DownloadsHint.Text = "Наблюдение выключено"; }
        SaveSettings();
    }
    private void StartDownloads()
    {
        try { downloads.Start(settings.DownloadFolder); DownloadsHint.Text = "Новые файлы в " + settings.DownloadFolder; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            settings.DownloadsEnabled = false; WatchDownloads.IsChecked = false; DownloadsHint.Text = ex.Message; AddActivity("Папка загрузок", ex.Message);
        }
    }
    private void OpenDownload(object sender, MouseButtonEventArgs e) { if (DownloadList.SelectedItem is DownloadEntry item) Open(item.Path); }
    private void ShowDownloadFolder(object sender, RoutedEventArgs e) { if (DownloadList.SelectedItem is DownloadEntry item) ShowInFolder(item.Path); }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        settings.Pinned = Pin.IsChecked == true; settings.Topmost = StayOnTop.IsChecked == true; settings.HideInFullscreen = HideFullscreen.IsChecked == true;
        Topmost = settings.Topmost; SaveSettings();
    }
    private void AutostartChanged(object sender, RoutedEventArgs e)
    {
        if (App.VerificationMode) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (Autostart.IsChecked == true) key.SetValue("DesktopIsland", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("DesktopIsland", false);
            settings.StartWithWindows = Autostart.IsChecked == true; SaveSettings();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Autostart.IsChecked = false; Status.Text = "Не удалось изменить автозапуск: " + ex.Message; }
    }
    private void SaveSettings()
    {
        if (!ready || !IsLoaded) return;
        // Store the compact anchor, independent of the currently expanded width.
        settings.Left = Left + Width / 2 - CompactWidth / 2; settings.Top = Top;
        store.Save("settings.json", settings);
    }
    private void AddActivity(string title, string detail, bool notify = false)
    {
        activity.Insert(0, new ActivityEntry { Title = title, Detail = detail }); while (activity.Count > 100) activity.RemoveAt(activity.Count - 1);
        if (Status != null) Status.Text = title + ": " + detail;
        if (notify && tray != null) { tray.BalloonTipTitle = title; tray.BalloonTipText = detail; tray.ShowBalloonTip(5000); }
    }
    private void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { AddActivity("Не удалось открыть", ex.Message); }
    }
    private void ShowInFolder(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) { Status.Text = "Путь больше не доступен"; return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false }); }
        catch (Exception ex) { AddActivity("Проводник", ex.Message); }
    }
    private void OpenDataFolder(object sender, RoutedEventArgs e) => Open(store.Root);
    private void HideToTray(object sender, RoutedEventArgs e) => HideAnimated();
    private void Exit(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ready && !store.SaveText("notes.txt", Notes.Text))
        {
            e.Cancel = true; NotesState.Text = "Не удалось сохранить. Скопируйте текст перед закрытием."; Navigation.SelectedIndex = 3; Reveal(); return;
        }
        closing = true; CancelTrackVisualHold(); timer.Stop(); leaveTimer.Stop(); noteTimer.Stop();
        StopShellMotion(true);
        StopLyrics();
        StopPageTransition();
        StopCompactTransition();
        StopClipboardTranslation(); clipboardNoticeTimer.Stop();
        doubleCopyHook.Dispose();
        telegramNotifications.Dispose(); telegramNoticeTimer.Stop(); telegramQueue.Clear();
        media.Dispose(); discordPresence.Dispose();
        preferences?.Close();
        SaveSettings(); downloads.Dispose();
        if (clipboardRegistered) WindowInterop.RemoveClipboardFormatListener(handle);
        if (hotkeyRegistered) WindowInterop.UnregisterHotKey(handle, WindowInterop.HotkeyId);
        source?.RemoveHook(WindowMessage); clipboard.Clear();
        if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); }
    }
}

// NativeWindowOwner avoids opening a folder picker behind the always-on-top island.
internal sealed class FolderDialogOwner : Forms.IWin32Window
{
    public IntPtr Handle { get; }
    public FolderDialogOwner(IntPtr handle) => Handle = handle;
}
