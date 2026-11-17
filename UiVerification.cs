using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopIsland;

public partial class MainWindow
{
    private async Task VerifyUiAsync()
    {
        var checks = new List<object>(); int failures = 0;
        void Check(string name, bool pass, string? detail = null) { checks.Add(new { name, pass, detail }); if (!pass) failures++; }
        var output = Environment.GetCommandLineArgs().SkipWhile(x => x != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(store.Root, "verification");
        Directory.CreateDirectory(output);
        try
        {
            timer.Stop(); lyricTimer.Stop(); Pin.IsChecked = true;
            Motion.VerificationOverride = true;
            var bundledTypeface = new Typeface(InterfaceFonts.Resolve(InterfaceFonts.Aminahs), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            bool bundledLoaded = bundledTypeface.TryGetGlyphTypeface(out var bundledGlyph);
            Check("bundled Aminahs font resolves to its own glyphs", bundledLoaded && bundledGlyph.FamilyNames.Values.Any(x => x.Contains("Aminahs")));
            Check("bundled font supplies Russian glyphs", bundledLoaded && "АЯаяЁё".All(x => bundledGlyph.CharacterToGlyphMap.ContainsKey(x)));
            settings.InterfaceFont = InterfaceFonts.Aminahs; ApplyShellAppearance();
            Check("starts compact", Width == CompactWidth && Body.Visibility == Visibility.Collapsed);
            UpdateIdleClockHands(new DateTime(2026, 11, 15, 3, 15, 30));
            Check("idle dial shows actual hour minute and second angles", Math.Abs(IdleHourHand.Angle - 97.75) < .01 && Math.Abs(IdleMinuteHand.Angle - 93) < .01 && Math.Abs(IdleSecondHand.Angle - 180) < .01);
            UpdateIdleClockHands(DateTime.Now); UpdateLayout();
            var dateBounds = IdleDateBadge.TransformToAncestor(Header).TransformBounds(new Rect(IdleDateBadge.RenderSize));
            Check("idle calendar badge fits compact panel", IdleDateBadge.IsVisible && dateBounds.Right <= Header.ActualWidth + 1 && !string.IsNullOrWhiteSpace(Battery.Text));
            Capture(Path.Combine(output, "idle-designed.png"));
            var compactLeft = Left; var compactWidth = Width; var compactHeight = Height;
            SetCompactResize(compactLeft, compactWidth, 90, false);
            Check("compact right edge widens without expanding", Width == compactWidth + 90 && Left == compactLeft && Height == compactHeight && !expanded && Body.Visibility == Visibility.Collapsed);
            var fixedRight = Left + Width;
            SetCompactResize(Left, Width, -70, true);
            Check("compact left edge keeps opposite edge fixed", Math.Abs(Left + Width - fixedRight) < .1 && Width == compactWidth + 160 && !expanded);
            Check("compact custom width persists in settings", settings.CompactWidthOverride == Width);
            SetCompactResize(Left, Width, 10000, false);
            Check("compact resize stops at monitor edge", Left + Width <= Services.WindowInterop.WorkArea(handle).Right + .1);
            settings.CompactWidthOverride = 0; Width = compactWidth; Left = compactLeft;
            SetExpanded(true);
            Check("expansion starts animation clocks", shellMoving && HasAnimatedProperties);
            await Task.Delay(100); UpdateLayout(); Capture(Path.Combine(output, "expanding.png"));
            Check("both window dimensions change during expansion", ActualWidth > CompactWidth && ActualHeight > CompactHeight);
            await AwaitShellAsync();
            Check("hover expansion layout", Width == ExpandedWidth && Height == ExpandedHeight && Body.Visibility == Visibility.Visible);
            Check("expansion finishes without retained geometry", !shellMoving && double.IsNaN(Body.Width) && Body.Opacity == 1);
            Check("window animation clocks released", !HasAnimatedProperties);
            Check("only current toolbar tab shows a caption", toolbar.All(x => x.Value.Label.Visibility == (x.Key == Navigation.SelectedIndex ? Visibility.Visible : Visibility.Collapsed)));
            Check("captioned toolbar fits window", TopNavigation.ActualWidth <= Shell.ActualWidth - 28);
            Check("header exposes eight tabs and three utility actions", toolbar.Count == 8 && TopNavigation.Children.OfType<System.Windows.Controls.Button>().Count() == 11);
            Navigation.SelectedIndex = 0; UpdateToolbar();
            Check("selected tab uses current island palette", toolbar[0].Button.BorderThickness.Left == 1 && toolbar[0].Button.Background is SolidColorBrush selectedTab && selectedTab.Color.A == 255);
            toolbar[0].Button.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent });
            await AwaitShellAsync();
            Check("double click player collapses island and blocks hover reopening", !expanded && collapseUntilPointerLeaves);
            Expand(this, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount));
            Check("pointer over collapsed player does not reopen it", !expanded);
            collapseUntilPointerLeaves = false; SetExpanded(true); await AwaitShellAsync();
            for (int i = 0; i < 11; i++)
            {
                Navigation.SelectedIndex = i;
                if (i == 10)
                {
                    WeatherDescription.Text = "Переменная облачность"; WeatherCityLabel.Text = "Проверочный город";
                    WeatherTemperature.Text = "3°"; WeatherFeels.Text = "Ощущается как 0°";
                    WeatherHiLo.Text = "Макс. 5° · мин. 2°"; WeatherWind.Text = "Ветер 12,5 м/с"; WeatherHumidity.Text = "Влажность 82%";
                    ForecastList.ItemsSource = Enumerable.Range(0, 6).Select(x => new Services.WeatherPoint($"{(19 + x) % 24}:00", 3 + x, "Переменная облачность")).ToArray();
                }
                await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
                await AwaitShellAsync();
                Check("navigation page " + i, ContentTabs.SelectedIndex == i && ContentTabs.SelectedContent != null);
                if (i is 1 or 2)
                {
                    var actions = i == 1 ? (FrameworkElement)ShelfActions : ClipboardActions;
                    Check("all action buttons fit page " + i, VisualDescendants<System.Windows.Controls.Button>(actions).All(x =>
                    {
                        var bounds = x.TransformToAncestor(Body).TransformBounds(new Rect(x.RenderSize));
                        return bounds.Left >= 0 && bounds.Right <= Body.ActualWidth + 1 && bounds.Bottom <= Body.ActualHeight + 1;
                    }));
                }
                if (i == 10)
                {
                    var description = WeatherDescription.TransformToAncestor(Body).TransformBounds(new Rect(WeatherDescription.RenderSize));
                    var details = WeatherHiLo.TransformToAncestor(Body).TransformBounds(new Rect(WeatherHiLo.RenderSize));
                    Check("long weather description stays above weather details", description.Bottom <= details.Top && description.Right <= Body.ActualWidth);
                }
                Capture(Path.Combine(output, $"page-{i:00}.png"));
            }
            SetExpanded(false); await AwaitShellAsync();
            IdleDateBadge.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await AwaitShellAsync();
            Check("compact calendar control opens calendar page", expanded && Navigation.SelectedIndex == 5);
            Notes.Text = "Проверочная заметка\nСохраняется между запусками."; FlushNotes();
            foreach (int tab in toolbar.Keys)
            {
                Navigation.SelectedIndex = tab;
                var button = toolbar[tab].Button;
                button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
                button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            }
            Navigation.SelectedIndex = 7; await Task.Delay(220); UpdateLayout();
            Check("visited tabs release hover colors and only selected section stays highlighted", toolbar.All(x => x.Value.Button.Background is SolidColorBrush brush && (x.Key == 7 ? brush.Color.A > 0 : brush.Color == Colors.Transparent)) && toolbar.Count(x => x.Value.Label.IsVisible) == 1);
            Capture(Path.Combine(output, "single-active-tab.png"));
            Check("notes edited in control persist", store.LoadText("notes.txt") == Notes.Text);
            TaskInput.Text = "Проверить сохранение задачи"; AddTask(this, new RoutedEventArgs());
            Check("add task from input", tasks.Count == 1 && TaskInput.Text == "");
            tasks[0].Done = true;
            Check("checkbox model persists completion", store.Load("tasks.json", () => Array.Empty<TaskItem>()).Single().Done);
            ClearDone(this, new RoutedEventArgs()); Check("remove completed tasks", tasks.Count == 0);
            EventTitle.Text = "Проверка календаря"; EventTime.Text = "14:30"; AddEvent(this, new RoutedEventArgs());
            Check("calendar entry created", calendar.Count == 1 && calendar[0].StartsAt.Date == selectedDay && calendar[0].StartsAt.Hour == 14);
            EventTime.Text = "28:65"; EventTitle.Text = "Некорректное время"; AddEvent(this, new RoutedEventArgs());
            Check("invalid event time rejected", calendar.Count == 1);
            ChangeCalendarView("Неделя");
            Check("calendar week lists local events", EventList.Items.Count == 1 && MonthGrid.Visibility == Visibility.Collapsed);
            ChangeCalendarView("Месяц");
            Check("calendar month shows its day grid", MonthGrid.Visibility == Visibility.Visible && MonthGrid.Children.Count == 49);
            ChangeCalendarView("День");
            var synthetic = Path.Combine(store.Root, "sample.txt"); File.WriteAllText(synthetic, "synthetic verification file");
            AddPaths(new[] { synthetic, synthetic }); Check("shelf deduplicates files", files.Count == 1);
            Shelf.SelectedIndex = 0; RemoveFile(this, new RoutedEventArgs());
            Check("removing shelf reference preserves source", files.Count == 0 && File.Exists(synthetic));
            var folder = Path.Combine(store.Root, "downloads"); Directory.CreateDirectory(folder);
            downloads.Start(folder);
            var path = Path.Combine(folder, "sample.crdownload"); await File.WriteAllTextAsync(path, "synthetic bytes");
            await Task.Delay(700);
            Check("watcher observes newly created download", downloads.Items.Any(x => x.Path == path));
            var final = Path.Combine(folder, "sample.txt"); File.Move(path, final);
            await Task.Delay(700);
            Check("watcher handles browser rename", downloads.Items.Any(x => x.Path == final) && downloads.Items.All(x => x.Path != path));
            foreach (var item in downloads.Items) item.LastChanged = DateTime.Now.AddSeconds(-6);
            downloads.Tick();
            Check("stable download labelled without invented percent", downloads.Items.Single().Complete && downloads.Items.Single().Status == "Файл не менялся 5 секунд");
            downloads.Dispose();
            var reminder = new CalendarEntry { Title = "Проверочное напоминание", StartsAt = DateTime.Now.AddSeconds(-1) }; calendar.Add(reminder);
            CheckReminders(); int count = activity.Count; CheckReminders();
            Check("due reminder fires exactly once", reminder.Reminded && activity.Count == count && activity.Any(x => x.Detail.StartsWith("Проверочное напоминание")));
            settings.Left = -100000; settings.Top = -100000; Left = settings.Left.Value; Top = settings.Top.Value; ClampToMonitor();
            var area = Services.WindowInterop.WorkArea(handle);
            Check("offscreen position repaired", Left >= area.Left && Top >= area.Top);
            Navigation.SelectedIndex = 0; ResetPosition(this, new RoutedEventArgs());
            await media.InitializeAsync(); UpdateMedia(); await Task.Delay(400); UpdateLayout();
            Capture(Path.Combine(output, "music.png"));
            Navigation.SelectedIndex = 7; await AwaitShellAsync(); UpdateLayout(); Capture(Path.Combine(output, "system.png"));
            SetExpanded(false);
            await AwaitShellAsync(); UpdateLayout(); Capture(Path.Combine(output, "compact.png"));
            Check("animated collapse completes", Width == CompactPresentationWidth && Height == CompactHeight && Body.Visibility == Visibility.Collapsed && !shellMoving);
            SetExpanded(true); await Task.Delay(65); SetExpanded(false); await Task.Delay(60); SetExpanded(true); await AwaitShellAsync();
            Check("rapid reversal preserves final state", Width == ExpandedWidth && Height == ExpandedHeight && Body.Visibility == Visibility.Visible && Body.Opacity == 1 && !shellMoving);
            LessMotion.IsChecked = true; ReducedMotionChanged(this, new RoutedEventArgs());
            SetExpanded(false);
            Check("reduced motion collapses immediately", Width == CompactPresentationWidth && Height == CompactHeight && !shellMoving && !Motion.Enabled);
            SetExpanded(true);
            Check("reduced motion expands immediately", Width == ExpandedWidth && Body.Visibility == Visibility.Visible && Body.Opacity == 1);
            Check("reduced motion preference persists", store.Load("settings.json", () => new AppSettings()).ReducedMotion);
            settings.MiniMode = true; ApplyPreferences(); SetExpanded(false);
            Check("mini island shows only a narrow clock", Width == CompactWidth && Width == 100 && KeyboardText.Visibility == Visibility.Collapsed);
            settings.MiniMode = false; settings.CompactScale = 125; ApplyPreferences();
            Check("compact size setting changes actual geometry", Width == CompactPresentationWidth && Height == 55);
            settings.CompactScale = 100; settings.ExpandedScale = 100; ApplyPreferences();
            settings.ExpandOnClick = true; SetExpanded(false); suppressHover = false; Expand(this, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)); suppressHover = true;
            Check("click opening ignores a plain hover", !expanded);
            settings.ExpandOnClick = false; settings.ReducedMotion = false; Motion.Reduced = false;
            Navigation.SelectedIndex = 5; SetExpanded(true); await AwaitShellAsync();
            foreach (var style in SettingsWindow.AnimationStyles)
            {
                settings.AnimationStyle = style;
                SetExpanded(false); await AwaitShellAsync();
                Check("animation collapses safely: " + style, Math.Abs(Width - CompactPresentationWidth) < .1 && Math.Abs(Height - CompactHeight) < .1 && !shellMoving);
                SetExpanded(true); await AwaitShellAsync();
                Check("animation expands safely: " + style, Math.Abs(Width - ExpandedWidth) < .1 && Math.Abs(Height - ExpandedHeight) < .1 && !shellMoving);
            }
            settings.AnimationStyle = "Капля";
            files.Add(new ShelfItem { Path = "https://example.com/reference" }); SaveShelfReferences();
            Check("shelf persists links separately from legacy file list", store.Load("links.json", () => Array.Empty<string>()).Single() == "https://example.com/reference" && store.Load("files.json", () => Array.Empty<string>()).Length == 0);
            OpenPreferences(1); await Task.Delay(300);
            Check("settings open in their own window", preferences?.IsVisible == true && preferences.Title == "Настройки Desktop Island");
            if (preferences?.Content is FrameworkElement settingsVisual)
            {
                CaptureVisual(settingsVisual, Path.Combine(output, "settings-window.png"));
                Check("settings use island colors instead of a fixed background", preferences!.Background is LinearGradientBrush);
                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(preferences!);
                Check("settings remove native caption and glass strip while retaining resize border", chrome?.CaptionHeight == 0 && chrome.GlassFrameThickness == new Thickness(0) && chrome.ResizeBorderThickness.Left > 0);
                var savedPaletteImage = paletteArtwork; bool savedPaletteActive = paletteActive;
                var warmPaletteImage = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 40, 80, 180, 255 }, 4);
                UpdateArtworkMaterial(true, warmPaletteImage); UpdateLayout();
                Check("open settings follow artwork palette changes", preferences!.Background is LinearGradientBrush liveTheme && liveTheme.GradientStops[0].Color.R > liveTheme.GradientStops[0].Color.B && preferences.Resources["ControlSurface"] is SolidColorBrush controlTheme && controlTheme.Color.R > controlTheme.Color.B);
                CaptureVisual(settingsVisual, Path.Combine(output, "settings-artwork-theme.png"));
                UpdateArtworkMaterial(savedPaletteActive, savedPaletteImage);
                var previewControl = VisualDescendants<IslandMotionPreview>(settingsVisual).FirstOrDefault();
                previewControl?.Play(); await Task.Delay(150);
                Check("settings preview plays real spring geometry", previewControl != null && VisualDescendants<System.Windows.Controls.Border>(previewControl).Any(x => x.Width > 280 && x.Width <= 460));
                CaptureVisual(settingsVisual, Path.Combine(output, "settings-preview-moving.png"));
                var toggle = VisualDescendants<System.Windows.Controls.CheckBox>(settingsVisual).FirstOrDefault();
                if (toggle != null)
                {
                    toggle.BringIntoView(); toggle.ApplyTemplate(); await Task.Delay(120);
                    Check("settings checkbox uses switch motion role", Motion.GetRole(toggle) == "Switch");
                    bool wasChecked = toggle.IsChecked == true;
                    toggle.IsChecked = !wasChecked; await Task.Delay(60);
                    var knob = toggle.Template.FindName("SwitchKnob", toggle) as FrameworkElement;
                    Check("settings switch thumb animates on state change", knob?.RenderTransform is TranslateTransform switchShift && switchShift.HasAnimatedProperties, $"loaded={toggle.IsLoaded}, enabled={Motion.Enabled}, knob={knob?.GetType().Name}, transform={knob?.RenderTransform?.GetType().Name}, state={toggle.IsChecked}");
                    await Task.Delay(300);
                    Check("settings switch releases animation clock", knob?.RenderTransform is TranslateTransform settledSwitch && !settledSwitch.HasAnimatedProperties && Math.Abs(settledSwitch.X - (toggle.IsChecked == true ? 18 : 0)) < .1, $"transform={knob?.RenderTransform}, state={toggle.IsChecked}");
                    CaptureVisual(settingsVisual, Path.Combine(output, "settings-switches.png"));
                    toggle.IsChecked = wasChecked;
                }
            }
            preferences?.SelectPage(2); await Task.Delay(250);
            if (preferences?.Content is FrameworkElement weatherSettings) CaptureVisual(weatherSettings, Path.Combine(output, "weather-settings.png"));
            preferences?.Close();
            TogglePreferences();
            Check("settings button opens preferences", preferences?.IsVisible == true);
            TogglePreferences();
            Check("settings button closes preferences on second press", preferences == null);
            var moreButton = TopNavigation.Children.OfType<System.Windows.Controls.Button>().Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Другие разделы");
            moreButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Task.Delay(150);
            var moreMenu = moreButton.ContextMenu;
            Pin.IsChecked = false;
            TryAutoCollapse();
            Check("open sections menu keeps island expanded outside its window", expanded && moreMenu?.IsOpen == true && IslandInteractionActive);
            Check("sections menu keeps all secondary tools reachable and readable", moreMenu?.Background is SolidColorBrush menuBrush && menuBrush.Color == Color.FromRgb(32, 36, 42) && moreMenu.Items.Count == 9 && moreMenu.Items.OfType<System.Windows.Controls.MenuItem>().All(x => x.Foreground is SolidColorBrush ink && ink.Color == Color.FromRgb(240, 241, 243)));
            if (moreMenu != null) { CaptureVisual(moreMenu, Path.Combine(output, "more-menu.png")); moreMenu.IsOpen = false; }
            Check("closing menu gives pointer time to return to island", IslandInteractionActive);
            Pin.IsChecked = true;
            // Remaining screens are driven by the test, not the physical pointer.
            leaveTimer.Stop();
            var syntheticLyrics = new Services.LyricsResult("Проверка таймкодов · тестовые строки", [new(1, "Первая тестовая строка"), new(3, "Вторая тестовая строка"), new(6, "Третья тестовая строка")]);
            // From this point the UI uses synthetic tracks, independent of live player events.
            media.Updated -= UpdateMedia;
            CompositionTarget.Rendering -= RenderCompactLyrics;
            media.Dispose();
            typeof(Services.MediaService).GetProperty(nameof(media.Source))!.SetValue(media, "ru.yandex.desktop.music");
            media.UpdateNativePlayback(new Services.YandexLyricsBridge.PlaybackState(0, .46, "ui-fixture", false, false, true, true, "Проверочный трек", "Тест интерфейса", 180, false));
            void LayoutSyntheticPlayer()
            {
                HomePanel.Visibility = Visibility.Collapsed; MediaPanel.Visibility = Visibility.Visible;
                Height = 618; UpdateLayout();
            }
            ShowLyrics(syntheticLyrics, syntheticLyrics.Status); UpdateLyricPosition(1.5);
            Check("timed lyric highlights first line", currentLyricIndex == 0 && lyricViews[0].FontWeight == FontWeights.SemiBold);
            UpdateLyricPosition(4);
            Check("timed lyric advances and releases previous highlight", currentLyricIndex == 1 && lyricViews[0].FontWeight == FontWeights.Normal && lyricViews[1].FontWeight == FontWeights.SemiBold);
            UpdateLyricPosition(1.25);
            Check("backwards seek moves lyric highlight backwards", currentLyricIndex == 0);
            Navigation.SelectedIndex = 0; await AwaitShellAsync();
            Check("page transition releases outgoing image", PageSnapshot.Source == null && PageSnapshot.Visibility == Visibility.Collapsed);
            HomePanel.Visibility = Visibility.Collapsed; MediaPanel.Visibility = Visibility.Visible;
            Height = 618; TrackTitle.Text = "Проверочный трек"; TrackArtist.Text = "Тест интерфейса"; UpdateLayout();
            Capture(Path.Combine(output, "lyrics-synthetic.png"));
            Timeline.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = PreviewMouseLeftButtonDownEvent, Handled = true });
            Check("seek begins even when slider marks mouse event handled", seeking);
            seeking = false;
            Check("playback glyph has readable contrast on accent", PlaybackIcon.Fill is SolidColorBrush playbackInk && PlayButton.Background is SolidColorBrush playbackSurface && ContrastRatio(playbackInk.Color, playbackSurface.Color) >= 4.5);
            UpdatePlaybackButton(true); string pauseGeometry = PlaybackIcon.Data.ToString();
            Check("playing state shows pause action", System.Windows.Automation.AutomationProperties.GetName(PlayButton) == "Пауза" && ReferenceEquals(PlayButton.Content, PlaybackIcon));
            UpdatePlaybackButton(false);
            Check("stopped state shows play glyph rather than text", System.Windows.Automation.AutomationProperties.GetName(PlayButton) == "Воспроизвести" && PlaybackIcon.Data.ToString() != pauseGeometry && PlayButton.ActualWidth == 48);
            Check("transport has playback and two Yandex rating actions", PlayButton.Parent is System.Windows.Controls.StackPanel transport && transport.Children.OfType<System.Windows.Controls.Button>().Count() == 5);
            var savedSource = media.Source; var savedPlayback = yandexPlayback;
            typeof(Services.MediaService).GetProperty(nameof(media.Source))!.SetValue(media, "ru.yandex.desktop.music");
            yandexPlayback = new(10, .4, "synthetic", true, false, true, true); UpdateYandexRating(); UpdateLayout();
            Check("Yandex ratings reflect native liked state", YandexLikeButton.IsVisible && YandexDislikeButton.IsVisible && YandexLikeButton.IsEnabled && ReferenceEquals(YandexLikeIcon.Data, FindResource("Yandex_liked_xs")));
            var ratingBounds = YandexLikeButton.TransformToAncestor(MediaPanel).TransformBounds(new Rect(YandexLikeButton.RenderSize));
            Check("Yandex rating buttons fit player width", ratingBounds.Right <= MediaPanel.ActualWidth && YandexDislikeButton.TransformToAncestor(MediaPanel).Transform(new Point()).X >= 0);
            Capture(Path.Combine(output, "yandex-rating-synthetic.png"));
            Capture(Path.Combine(output, "aminahs-player.png"));
            ratingYandex = true; UpdateYandexRating();
            Check("rating command prevents double click", !YandexLikeButton.IsEnabled && !YandexDislikeButton.IsEnabled);
            ratingYandex = false;
            typeof(Services.MediaService).GetProperty(nameof(media.Source))!.SetValue(media, "other-player"); UpdateYandexRating();
            Check("Yandex ratings hidden for other players", YandexLikeButton.Visibility == Visibility.Collapsed && YandexDislikeButton.Visibility == Visibility.Collapsed);
            typeof(Services.MediaService).GetProperty(nameof(media.Source))!.SetValue(media, savedSource); yandexPlayback = savedPlayback; UpdateYandexRating();
            LyricsMenuButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            await Task.Delay(170);
            Check("quiet action hover stays translucent instead of turning white", LyricsMenuButton.Template.FindName("Frame", LyricsMenuButton) is System.Windows.Controls.Border quietFrame && quietFrame.Background is SolidColorBrush quietBrush && quietBrush.Color.A <= 32);
            LyricsMenuButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            var artworkBounds = ArtworkViewport.TransformToAncestor(MediaPanel).TransformBounds(new Rect(ArtworkViewport.RenderSize));
            Check("technical source selector is hidden while player options remain accessible", !Players.IsVisible && PlayerOptionsButton.IsVisible && artworkBounds.Top >= 0);
            foreach (var size in new[] { (Width: 480, Height: 120), (Width: 120, Height: 480) })
            {
                var pixels = new byte[size.Width * size.Height * 4];
                for (int p = 0; p < pixels.Length; p += 4) { pixels[p] = 110; pixels[p + 1] = 145; pixels[p + 2] = 175; pixels[p + 3] = 255; }
                Artwork.Source = BitmapSource.Create(size.Width, size.Height, 96, 96, PixelFormats.Bgra32, null, pixels, size.Width * 4);
                ArtFallback.Visibility = Visibility.Collapsed; UpdateLayout();
                var viewport = ArtworkViewport.TransformToAncestor(MediaPanel).TransformBounds(new Rect(ArtworkViewport.RenderSize));
                var title = TrackTitle.TransformToAncestor(MediaPanel).TransformBounds(new Rect(TrackTitle.RenderSize));
                Check("non-square artwork stays inside its slot: " + size.Width + "x" + size.Height,
                    ArtworkViewport.ActualWidth == 96 && ArtworkViewport.ActualHeight == 96 && ArtworkViewport.ClipToBounds && ArtworkViewport.Clip?.Bounds == new Rect(0, 0, 96, 96) && viewport.Right < title.Left);
                Capture(Path.Combine(output, "artwork-" + size.Width + "x" + size.Height + ".png"));
            }
            var longLyrics = new Services.LyricsResult("Проверка повторного раскрытия", Enumerable.Range(0, 40).Select(i => new Services.LyricLine(i * 5, i == 4 ? "Длинная проверочная строка, которая переносится и заполняется последовательно на нескольких строках" : "Тестовая строка " + i)).ToArray());
            ShowLyrics(longLyrics, longLyrics.Status); UpdateLyricPosition(0); await Task.Delay(80);
            UpdateLyricPosition(21); await Task.Delay(70); UpdateLayout();
            double movingOffset = LyricsScroll.VerticalOffset;
            await Task.Delay(420); UpdateLayout();
            Check("current lyric scroll eases into its reading position", LyricsScroll.VerticalOffset > movingOffset && !LyricsScroll.HasAnimatedProperties);
            double earlyProgress = lyricFills[4].Progress;
            UpdateLyricPosition(23);
            Check("wrapped active lyric fills progressively across its full text", lyricViews[4].ActualHeight > 24 && lyricFills[4].ActualHeight >= lyricViews[4].ActualHeight && lyricFills[4].Progress > earlyProgress && lyricFills[4].Progress < 1);
            Capture(Path.Combine(output, "lyrics-karaoke-expanded.png"));
            double automaticOffset = LyricsScroll.VerticalOffset;
            LyricsScroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = PreviewMouseWheelEvent });
            await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
            double manualOffset = LyricsScroll.VerticalOffset;
            Check("mouse wheel scrolls lyrics and enables manual priority", manualLyricsScroll && manualOffset > automaticOffset && FollowLyricsButton.IsVisible);
            UpdateLyricPosition(125); await Task.Delay(100); UpdateLayout();
            Check("sung line changes do not steal manual scroll position", Math.Abs(LyricsScroll.VerticalOffset - manualOffset) < 1);
            LyricsScroll.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
            Check("lyrics scrollbar drag retains manual priority", manualLyricsScroll);
            ResumeLyricsFollow(this, new RoutedEventArgs()); await Task.Delay(80); UpdateLayout();
            Check("follow button restores automatic lyric tracking", !manualLyricsScroll && !FollowLyricsButton.IsVisible && LyricsScroll.VerticalOffset > manualOffset);
            Check("keyboard language letters are white", KeyboardText.Foreground is SolidColorBrush languageInk && languageInk.Color == Colors.White);
            SetExpanded(false); await AwaitShellAsync();
            UpdateLyricPosition(125);
            Check("compact playback advances lyric index while text is hidden", currentLyricIndex == 25 && !LyricsScroll.IsVisible);
            SetExpanded(true); await AwaitShellAsync(); LayoutSyntheticPlayer(); await Task.Delay(100); UpdateLayout();
            var activeBounds = lyricViews[25].TransformToAncestor(LyricsScroll).TransformBounds(new Rect(lyricViews[25].RenderSize));
            Check("reopening paused lyrics reveals unchanged current line", currentLyricIndex == 25 && LyricsScroll.VerticalOffset > 0 && activeBounds.Top >= 0 && activeBounds.Bottom <= LyricsScroll.ActualHeight);
            Capture(Path.Combine(output, "lyrics-reopened.png"));
            Navigation.SelectedIndex = 2; await AwaitShellAsync();
            Navigation.SelectedIndex = 0; await AwaitShellAsync(); LayoutSyntheticPlayer(); await Task.Delay(100); UpdateLayout();
            activeBounds = lyricViews[25].TransformToAncestor(LyricsScroll).TransformBounds(new Rect(lyricViews[25].RenderSize));
            Check("returning to player reveals current lyric without a new timestamp", activeBounds.Top >= 0 && activeBounds.Bottom <= LyricsScroll.ActualHeight);
            SetExpanded(false); await AwaitShellAsync();
            CompactTrack.Text = longLyrics.Lines[25].Text;
            ApplyCompactPlayback(true, false, Artwork.Source);
            UpdateCompactLyricProgress(126, 25); UpdateLayout();
            double initialFill = CompactLyricFill.Clip.Bounds.Width;
            Check("compact player keeps artwork without an indicator on pause", CompactArtworkFrame.IsVisible && !CompactPlaybackBars.IsVisible && !Clock.IsVisible && !KeyboardBadge.IsVisible && !Battery.IsVisible);
            ApplyCompactPlayback(true, true, Artwork.Source); await Task.Delay(120);
            Check("playing compact indicator animates all bars", CompactPlaybackBars.IsVisible && equalizerAnimating && CompactPlaybackBars.Children.OfType<System.Windows.Shapes.Rectangle>().All(x => x.RenderTransform.HasAnimatedProperties));
            SetExpanded(true); await AwaitShellAsync();
            Check("hidden compact indicator releases animation clocks", !equalizerAnimating && CompactPlaybackBars.Children.OfType<System.Windows.Shapes.Rectangle>().All(x => !x.RenderTransform.HasAnimatedProperties));
            SetExpanded(false); await AwaitShellAsync(); ApplyCompactPlayback(true, false, Artwork.Source);
            Check("paused compact indicator hides and releases animation clocks", !equalizerAnimating && !CompactPlaybackBars.IsVisible && CompactPlaybackBars.Children.OfType<System.Windows.Shapes.Rectangle>().All(x => !x.RenderTransform.HasAnimatedProperties));
            UpdateCompactLyricProgress(128, 25);
            double laterFill = CompactLyricFill.Clip.Bounds.Width;
            Check("compact lyric fills progressively within its line interval", CompactLyricFill.IsVisible && initialFill > 0 && laterFill > initialFill);
            UpdateCompactLyricProgress(128, 25);
            Check("compact lyric fill stays fixed when playback position is paused", CompactLyricFill.Clip.Bounds.Width == laterFill);
            var existingClip = CompactLyricFill.Clip;
            ApplyCompactPlayback(true, false, Artwork.Source);
            Check("media refresh keeps unspoken text muted without replacing the fill clip", ReferenceEquals(CompactTrack.Foreground, FindResource("Muted")) && ReferenceEquals(existingClip, CompactLyricFill.Clip));
            UpdateCompactLyricProgress(128.05, 25);
            Check("sub-tick playback advances cached lyric clip smoothly", ReferenceEquals(existingClip, CompactLyricFill.Clip) && CompactLyricFill.Clip.Bounds.Width > laterFill);
            var tintedMaterial = CompactMaterialColors();
            Check("compact material follows warm artwork while keeping white text readable", tintedMaterial.Top.R > tintedMaterial.Top.B && ContrastRatio(tintedMaterial.Top, Color.FromRgb(240, 241, 243)) >= 4.5);
            settings.ArtworkTint = false; UpdateArtworkMaterial(true, Artwork.Source);
            Check("artwork tint can be disabled without changing player state", CompactMaterialColors().Top == Color.FromRgb(64, 64, 66) && CompactArtworkFrame.IsVisible);
            settings.ArtworkTint = true; UpdateArtworkMaterial(true, Artwork.Source); await Task.Delay(350);
            Capture(Path.Combine(output, "compact-karaoke.png"));
            pausePresentation.IsActive(true, true, Environment.TickCount64);
            RefreshIdleContent(); await AwaitShellAsync(); UpdateLayout();
            double loadingWidth = StandbyCompactWidth;
            BeginLyricsLoading();
            Check("lyrics search preserves compact width", lyricsLoading && Math.Abs(StandbyCompactWidth - loadingWidth) < 1);
            Check("lyrics search preserves expanded player layout", PlayerHasLyrics);
            ShowLyrics(longLyrics, longLyrics.Status); UpdateLyricPosition(125);
            Check("lyrics arrival releases loading state", !lyricsLoading && lyricsResult?.Synced == true);
            double retainedWidth = StandbyCompactWidth;
            ShowLyrics(new Services.LyricsResult("Новая песня без готового текста", []), "Новая песня без готового текста"); RefreshIdleContent();
            Check("new song without lyrics retains established music width", Math.Abs(StandbyCompactWidth - retainedWidth) < 1);
            UpdateCompactText("Смена песни A"); await Task.Delay(40);
            UpdateCompactText("Смена песни B");
            int outgoingAlpha = 0;
            if (CompactTextSnapshot.Source is BitmapSource outgoingText)
            {
                var pixels = new byte[outgoingText.PixelWidth * outgoingText.PixelHeight * 4];
                outgoingText.CopyPixels(pixels, outgoingText.PixelWidth * 4, 0);
                for (int i = 3; i < pixels.Length; i += 4) outgoingAlpha = Math.Max(outgoingAlpha, pixels[i]);
            }
            Check("interrupted compact fade captures fully opaque outgoing text", outgoingAlpha >= 240, $"header={Header.IsVisible}, width={CompactTextContent.ActualWidth}, maxAlpha={outgoingAlpha}");
            UpdateCompactText("Проверка перехода строки"); await Task.Delay(320);
            Check("compact text fade releases its outgoing snapshot", CompactTextSnapshot.Source == null && CompactTextSnapshot.Visibility == Visibility.Collapsed && CompactTextContent.Opacity == 1);
            pausePresentation.IsActive(true, true, Environment.TickCount64); RefreshIdleContent(); await AwaitShellAsync(); UpdateLayout();
            string originalTitle = media.Title;
            BeginTrackVisualHold(CurrentVisualKey, verification: true);
            var retainedFrame = TrackVisualSnapshot.Source;
            typeof(Services.MediaService).GetProperty(nameof(media.Title))!.SetValue(media, "Следующий трек");
            BeginTrackVisualHold(CurrentVisualKey, verification: true);
            typeof(Services.MediaService).GetProperty(nameof(media.ArtworkReady))!.SetValue(media, false);
            UpdateMedia();
            ShowLyrics(new Services.LyricsResult("Текста нет", []), "Текста нет"); RefreshIdleContent();
            await Task.Delay(40);
            Check("track transition retains previous frame until cover and lyric result are ready", trackVisualPending && ReferenceEquals(retainedFrame, TrackVisualSnapshot.Source) && Shell.Opacity == 0 && CompactPresentationWidth == heldWindowWidth);
            lyricsReadyVisualKey = "obsolete-track";
            typeof(Services.MediaService).GetProperty(nameof(media.ArtworkReady))!.SetValue(media, true);
            TryCommitTrackVisual(); await Task.Delay(30);
            Check("stale lyric completion cannot commit a newer track", trackVisualPending);
            lyricsReadyVisualKey = CurrentVisualKey; TryCommitTrackVisual(); await Task.Delay(360); UpdateLayout();
            Check("ready track commits in one transition and releases old frame", !trackVisualPending && TrackVisualSnapshot.Source == null && Shell.Opacity == 1);
            ShowLyrics(new Services.LyricsResult("Интро", [new Services.LyricLine(10, "Первая строка")]), "Интро"); UpdateLyricPosition(0); RefreshIdleContent();
            Check("intro shows track title and artist before first timed line", CompactTrack.Text == CompactTrackCaption && CompactTrack.Text.Contains(media.Artist) && !HasTimedCompactLyrics);
            ShowLyrics(new Services.LyricsResult("Пустое интро", [new Services.LyricLine(0, ""), new Services.LyricLine(10, "Первая строка")]), "Пустое интро"); UpdateLyricPosition(1); RefreshIdleContent();
            Check("empty timed line keeps track caption visible", CompactTrack.Text == CompactTrackCaption && !HasTimedCompactLyrics);
            await Task.Delay(1050); UpdateTrackIntroduction();
            UpdateLyricPosition(11); RefreshIdleContent();
            Check("first sung line replaces caption when its time arrives", CompactTrack.Text == "Первая строка" && HasTimedCompactLyrics);
            typeof(Services.MediaService).GetProperty(nameof(media.Title))!.SetValue(media, originalTitle);
            CancelTrackVisualHold();
            SetExpanded(true); await AwaitShellAsync(); LayoutSyntheticPlayer();
            ShowLyrics(new Services.LyricsResult("Во внешних источниках текста нет", []), "Во внешних источниках текста нет");
            Height = 432; UpdateLayout();
            Capture(Path.Combine(output, "player-without-lyrics.png"));
            Check("empty lyrics use concise status with source details in tooltip", LyricsStatus.Text == "Текст песни не найден" && (string)LyricsStatus.ToolTip == "Во внешних источниках текста нет");
            var volumeBounds = Volume.TransformToAncestor(MediaPanel).TransformBounds(new Rect(Volume.RenderSize));
            Check("short player keeps volume controls inside visible panel", volumeBounds.Top >= 0 && volumeBounds.Bottom <= MediaPanel.ActualHeight + 1);
            ShowLyrics(new Services.LyricsResult("Текст без таймкодов", [], "Проверочный обычный текст"), "Текст без таймкодов");
            Top = Services.WindowInterop.WorkArea(handle).Top;
            Height = 630; UpdateLayout();
            double oldLyricsHeight = LyricsScroll.ActualHeight, oldWidth = Width;
            Pin.IsChecked = false; settings.Pinned = false;
            ResizeIslandStarted(HeightHandle, new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
            ResizeIslandBy(160); UpdateLayout();
            Check("resizing grows lyrics viewport without scaling text or width", Height > 630 && LyricsScroll.ActualHeight > oldLyricsHeight && Width == oldWidth && TrackTitle.FontSize == 21);
            Check("resize holds view during drag without enabling pin", Pin.IsChecked == false && !settings.Pinned && draggingWindow);
            ResizeIslandBy(10000);
            Check("resize stops at work area bottom", Top + Height <= Services.WindowInterop.WorkArea(handle).Bottom + .1);
            ResizeIslandCompleted(HeightHandle, new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 160, false));
            Check("resize releases interaction state", !draggingWindow);
            Check("expanded view has separate move and resize handles", MoveHandle.IsVisible && HeightHandle.IsVisible);
            Capture(Path.Combine(output, "lyrics-resized.png"));
            UpdateLyricPosition(50);
            Check("plain lyrics do not simulate synchronization", currentLyricIndex == -1 && lyricViews.Count == 0 && LyricsLines.Children.Count == 1);
            settings.ClipboardNotifications = true; settings.AutoTranslateClipboard = false;
            var guardedReply = new ClipboardEntry { Text = "Русский ответ", Translation = "English reply" };
            noticeEntry = guardedReply; verificationClipboardText = "Другой текст";
            translationGate.Complete("ru", "en");
            await CopyReverseTranslationAsync(guardedReply, System.Threading.CancellationToken.None);
            Check("reverse reply never overwrites a newer clipboard copy", verificationClipboardText == "Другой текст" && translationGate.EnglishReplyArmed);
            verificationClipboardText = guardedReply.Text;
            await CopyReverseTranslationAsync(guardedReply, System.Threading.CancellationToken.None);
            Check("reverse reply writes English once and keeps source in history", verificationClipboardText == "English reply" && lastClipboard == "English reply" && !translationGate.EnglishReplyArmed && guardedReply.Text == "Русский ответ");
            DismissClipboardNotice(this, EventArgs.Empty); await AwaitShellAsync();
            string copiedFixture = Path.Combine(output, "copied-file-fixture.txt"); File.WriteAllText(copiedFixture, "Synthetic clipboard file test");
            HandleCopiedFiles([copiedFixture]); SetExpanded(false); await AwaitShellAsync(); UpdateLayout();
            Check("file copy shows native-style card with shelf action", CompactCard.IsVisible && CompactCardTitle.Text == "Файл скопирован" && CompactCardText.Text == "copied-file-fixture.txt" && CompactCardAction.Content as string == "На полку");
            Capture(Path.Combine(output, "file-copy-notice.png"));
            CompactCardActionClick(this, new RoutedEventArgs());
            Check("file copy card adds original file reference to shelf", files.Any(x => x.Path == copiedFixture) && !clipboardNoticeActive);
            SetExpanded(true); await AwaitShellAsync();
            var copied = new ClipboardEntry { Text = "Проверка копирования текста" };
            HandleCopiedText(copied); UpdateLayout();
            Check("clipboard notification appears inside expanded island", ClipboardNotice.IsVisible && ClipboardNoticeTitle.Text == "Текст скопирован");
            Capture(Path.Combine(output, "clipboard-notice.png"));
            SetExpanded(false); await AwaitShellAsync();
            Check("clipboard becomes a compact notification card", CompactCard.IsVisible && CompactCardTitle.Text == "Текст скопирован" && Header.Visibility == Visibility.Collapsed && !CompactNotice.IsVisible && CompactTrack.Visibility == Visibility.Collapsed && ClipboardNotice.Visibility == Visibility.Collapsed && Math.Abs(Height - CompactPresentationHeight) < .1);
            Capture(Path.Combine(output, "clipboard-compact.png"));
            double shortNoticeWidth = Width;
            var beforeNoticeColors = CompactMaterialColors();
            ShowClipboardNotice(copied, "Проверка ширины уведомления", new string('W', 150), 5);
            Check("notification inherits current island material", CompactMaterialColors() == beforeNoticeColors);
            await AwaitShellAsync(); UpdateLayout();
            Check("long notification expands horizontally while preserving compact height", Width > shortNoticeWidth && Math.Abs(Height - CompactPresentationHeight) < .1);
            Check("notification width is capped inside its monitor", Width <= 1040 && Left + Width <= Services.WindowInterop.WorkArea(handle).Right + 1);
            ShowClipboardNotice(copied, "Текст скопирован", copied.Text, 5); await AwaitShellAsync();
            Check("notification finishes without old frame or exposed resize handles", CompactSnapshot.Source == null && !CompactLeftHandle.IsVisible && !CompactRightHandle.IsVisible);
            ShowClipboardNotice(copied, "Переводим на русский…", copied.Text, 15, busy: true);
            Check("translation indicator animates during request", CompactNoticeIcon.RenderTransform is RotateTransform activeRotation && activeRotation.HasAnimatedProperties);
            var loadingRotation = CompactNoticeIcon.RenderTransform as RotateTransform;
            DismissClipboardNotice(this, EventArgs.Empty);
            Check("translation animation stops when notice closes", loadingRotation?.HasAnimatedProperties == false);
            await AwaitShellAsync();
            Check("dismissing clipboard notice restores compact content", CompactCard.Visibility == Visibility.Collapsed && Header.IsVisible && CompactTrack.Visibility == Visibility.Visible && Math.Abs(Height - CompactHeight) < .1);
            Navigation.SelectedIndex = 0;
            copied.Translation = "Проверочный перевод";
            ShowClipboardNotice(copied, "Перевод на русский", copied.Translation, 5);
            Check("collapsed notification morphs with outgoing frame and delayed incoming content", CompactSnapshot.Source != null && CompactCard.HasAnimatedProperties && shellMoving);
            OpenClipboardNotice(this, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Check("opening translated notice remembers previous player page", Navigation.SelectedIndex == 2 && clipboardReturnTab == 0 && clipboardNoticeTimer.Interval == TimeSpan.FromSeconds(5));
            var returnWatch = System.Diagnostics.Stopwatch.StartNew();
            while (clipboardNoticeActive && returnWatch.Elapsed < TimeSpan.FromSeconds(6.5)) await Task.Delay(50);
            await AwaitShellAsync();
            Check("translation returns to player after five seconds", Navigation.SelectedIndex == 0 && !clipboardNoticeActive && clipboardReturnTab == null && returnWatch.Elapsed.TotalSeconds >= 4.9, $"tab={Navigation.SelectedIndex}, active={clipboardNoticeActive}, return={clipboardReturnTab}, timer={clipboardNoticeTimer.IsEnabled}, elapsed={returnWatch.Elapsed.TotalSeconds:0.00}");
            Navigation.SelectedIndex = 3;
            ShowClipboardNotice(copied, "Перевод на русский", copied.Translation, 5);
            OpenClipboardNotice(this, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Check("translation remembers a non-player page", clipboardReturnTab == 3);
            Navigation.SelectedIndex = 1;
            DismissClipboardNotice(this, EventArgs.Empty);
            Check("manual navigation cancels automatic return", Navigation.SelectedIndex == 1 && clipboardReturnTab == null);
            settings.TelegramNotificationsEnabled = true;
            Navigation.SelectedIndex = 0; SetExpanded(true); await AwaitShellAsync();
            ReceiveTelegramNotification(new Services.TelegramMessage(1, "Проверка уведомления", "Это тест интерфейса, а не сообщение Telegram.", DateTimeOffset.UtcNow));
            Check("Telegram banner preserves player page", TelegramNotice.IsVisible && Navigation.SelectedIndex == 0 && TelegramNoticeTitle.Text == "Проверка уведомления" && ClipboardNotice.Visibility == Visibility.Collapsed);
            Capture(Path.Combine(output, "telegram-expanded.png"));
            ReceiveTelegramNotification(new Services.TelegramMessage(2, "Второе тестовое уведомление", "Проверка очереди сообщений", DateTimeOffset.UtcNow));
            Check("Telegram messages queue instead of overwriting", telegramQueue.Count == 1 && TelegramNoticeTitle.Text == "Проверка уведомления");
            DismissTelegramNotice(this, EventArgs.Empty);
            Check("Telegram queue advances to next message", telegramQueue.Count == 0 && TelegramNoticeTitle.Text == "Второе тестовое уведомление");
            SetExpanded(false); await AwaitShellAsync();
            Check("Telegram uses two-line compact card without track overlap", CompactCard.IsVisible && CompactCardTitle.Text.Contains("Telegram") && Header.Visibility == Visibility.Collapsed && !CompactTelegramHost.IsVisible && CompactTrack.Visibility == Visibility.Collapsed && Math.Abs(Height - CompactPresentationHeight) < .1);
            FlyTelegramPlane(); await Task.Delay(210);
            Check("Telegram plane flies visibly across a separate overlay", planeFlying && FlyingTelegramPlane.IsVisible && FlyingTelegramPlane.RenderTransform is TransformGroup flight && flight.Children.OfType<TranslateTransform>().Any(x => x.HasAnimatedProperties && x.X > 24) && flight.Children.OfType<RotateTransform>().Any(x => x.HasAnimatedProperties));
            Capture(Path.Combine(output, "telegram-plane-in-flight.png"));
            await Task.Delay(1100);
            Check("Telegram plane lands and releases its animation clocks", !planeFlying && !FlyingTelegramPlane.IsVisible && ReferenceEquals(FlyingTelegramPlane.RenderTransform, Transform.Identity) && !CompactCardIcon.HasAnimatedProperties);
            bool savedReduced = Motion.Reduced; Motion.Reduced = true; FlyTelegramPlane();
            Check("Telegram plane respects reduced motion", !planeFlying && !CompactCardIcon.HasAnimatedProperties);
            Motion.Reduced = savedReduced;
            Capture(Path.Combine(output, "telegram-compact.png"));
            await Task.Delay(5300); await AwaitShellAsync();
            Check("Telegram message clears after five seconds", !telegramNoticeActive && CompactCard.Visibility == Visibility.Collapsed && Header.IsVisible && CompactTrack.Visibility == Visibility.Visible && Navigation.SelectedIndex == 0);
            DisableTelegramNotifications();
            ReceiveTelegramNotification(new Services.TelegramMessage(3, "Тест", "Не должен появиться", DateTimeOffset.UtcNow));
            Check("disabled Telegram listener does not show messages", !telegramNoticeActive && telegramQueue.Count == 0);
            settings.HideStyle = "Капля вверх";
            HideAnimated(); await Task.Delay(80); Reveal(); await AwaitShellAsync(); await Task.Delay(750);
            Check("show cancels an in-flight hide without losing window", IsVisible && !hiding && IsHitTestVisible && Opacity == 1);
            foreach (string hide in new[] { "Капля вверх", "Схлопывание", "Растворение" })
            {
                settings.HideStyle = hide; HideAnimated(); await Task.Delay(520);
                Check("hide completes and releases input: " + hide, !IsVisible && !hiding && IsHitTestVisible);
                Reveal(); await AwaitShellAsync(); await Task.Delay(750);
                Check("show restores opacity and geometry: " + hide, IsVisible && IsHitTestVisible && Opacity == 1);
            }
            if (Environment.GetCommandLineArgs().Contains("--translate-live"))
            {
                settings.ClipboardEnabled = true; settings.AutoTranslateClipboard = true;
                var foreign = new ClipboardEntry { Text = "Hello world" };
                HandleCopiedText(foreign);
                for (int attempt = 0; attempt < 80 && foreign.TranslationState == "Переводим на русский…"; attempt++) await Task.Delay(200);
                Check("automatic copy flow shows real Russian translation", foreign.Translation.Any(c => c is >= 'А' and <= 'я') && CompactCardText.Text == foreign.Translation.Replace('\n', ' '));
                Check("automatic translation keeps original clipboard entry", foreign.Text == "Hello world");
                SetExpanded(true); await AwaitShellAsync();
                Check("translated notice offers separate copy action", CopyNoticeTranslation.IsVisible && ClipboardNoticeText.Text == foreign.Translation);
                Capture(Path.Combine(output, "translation-live.png"));
                Check("English to Russian live result arms next Russian copy", translationGate.EnglishReplyArmed);
                var reply = new ClipboardEntry { Text = "Привет, мир" };
                verificationClipboardText = reply.Text;
                HandleCopiedText(reply);
                for (int attempt = 0; attempt < 100 && reply.TranslationState.StartsWith("Переводим на ", StringComparison.Ordinal); attempt++) await Task.Delay(200);
                Check("activated Russian reply is translated and copied into simulated clipboard", reply.Translation.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') && verificationClipboardText == reply.Translation && lastClipboard == reply.Translation);
                Check("reverse reply preserves original and consumes trigger", reply.Text == "Привет, мир" && !translationGate.EnglishReplyArmed && ClipboardNoticeTitle.Text == "На английский · скопировано");
            }
        }
        catch (Exception ex) { failures++; checks.Add(new { name = "exception", pass = false, detail = ex.ToString() }); }
        finally
        {
            File.WriteAllText(Path.Combine(output, "ui-report.json"), JsonSerializer.Serialize(new { failures, checks }, new JsonSerializerOptions { WriteIndented = true }));
            System.Windows.Application.Current.Shutdown(failures == 0 ? 0 : 1);
        }
    }
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in VisualDescendants<T>(child)) yield return nested;
        }
    }
    private static double ContrastRatio(Color first, Color second)
    {
        double Channel(byte value) { double v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private void Capture(string path)
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(Shell); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void CaptureVisual(FrameworkElement visual, string path)
    {
        visual.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(visual);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(visual.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private async Task AwaitShellAsync()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (shellMoving && watch.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(30);
        await Task.Delay(50);
    }
}
