using System.Text.Json;
using System.Windows.Media;
using DesktopIsland.Services;

namespace DesktopIsland;

internal static class SelfTests
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var report = new List<object>();
        var root = Path.Combine(Path.GetTempPath(), "DesktopIsland-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int failures = 0;
        void Check(string name, bool pass, string detail = "") { report.Add(new { name, pass, detail }); if (!pass) failures++; }
        try
        {
            var store = new LocalStore(root);
            Check("Telegram voice playback never enters music selection", MediaService.ExcludedMediaSource("TelegramDesktop.TDesktop") && MediaService.ExcludedMediaSource("org.telegram.desktop") && !MediaService.ExcludedMediaSource("ru.yandex.desktop.music") && !MediaService.ExcludedMediaSource("chrome"));
            Check("YouTube browser adapter ignores unrelated pages", YouTubeBrowserBridge.IsYouTubePage("https://www.youtube.com/watch?v=abcdefghijk") && !YouTubeBrowserBridge.IsYouTubePage("https://youtube.com.example.org/watch?v=abcdefghijk") && !YouTubeBrowserBridge.IsYouTubePage("https://mail.example.com/"));
            using var youtubeFixture = JsonDocument.Parse("""{"id":"abcdefghijk","title":"Example video","artist":"Example channel","playing":true,"position":20,"duration":300}""");
            Check("YouTube adapter reads title channel playing state and timeline", YouTubeBrowserBridge.Parse(youtubeFixture.RootElement) is { Title: "Example video", Artist: "Example channel", Playing: true, Position: 20, Duration: 300 });
            var youtubePresence = DiscordPresenceService.CreatePresence(true, true, true, "Example video", "Example channel", 20, 300, DateTime.UtcNow, "https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", "https://www.youtube.com/watch?v=abcdefghijk", true);
            Check("YouTube RPC contains video thumbnail link and watch timer", youtubePresence.Type == DiscordRPC.ActivityType.Watching && youtubePresence.Assets.LargeImageKey.Contains("i.ytimg.com") && youtubePresence.Buttons.First().Label == "Открыть видео" && youtubePresence.Timestamps != null);
            if (args.Contains("--youtube-browser-current"))
            {
                var youtube = await YouTubeBrowserBridge.Shared.ReadAsync();
                Check("live browser YouTube connection", youtube != null, youtube == null ? YouTubeBrowserBridge.Shared.Status : $"title={youtube.Title}, artist={youtube.Artist}, playing={youtube.Playing}, position={youtube.Position:0}, duration={youtube.Duration:0}");
            }
            Check("YouTube fallback extracts only the displayed video title", YouTubeWindowProbe.VideoTitle("Example - YouTube — Яндекс Браузер") == "Example" && YouTubeWindowProbe.VideoTitle("Unrelated browser window") == null);
            if (args.Contains("--youtube-window-current"))
            {
                var probe = new YouTubeWindowProbe();
                var video = await probe.ReadAsync();
                if (video == null) { await Task.Delay(600); video = await probe.ReadAsync(); }
                Check("live YouTube fallback detects playing video", video?.Playing == true, video?.Title ?? probe.Status);
            }
            Check("playing music wins over playing background video", MediaService.SessionPriority(true, false) > MediaService.SessionPriority(true, true));
            Check("playing video wins over paused music", MediaService.SessionPriority(true, true) > MediaService.SessionPriority(false, false));
            Check("browser video metadata classified without fetching browsing history", MediaService.VideoSource("chrome", "Channel", null) && MediaService.VideoSource("browser", "Channel", Windows.Media.MediaPlaybackType.Video));
            Check("browser music stays music when declared by Windows", !MediaService.VideoSource("chrome", "Artist", Windows.Media.MediaPlaybackType.Music));
            var videoPresence = DiscordPresenceService.CreatePresence(true, true, true, "Video title", "Channel", 5, 60, DateTime.UtcNow, video: true);
            Check("Discord video presence shows Watching title channel and timer", videoPresence.Type == DiscordRPC.ActivityType.Watching && videoPresence.Details == "Video title" && videoPresence.State == "Channel" && videoPresence.Timestamps != null);
            var intro = new TrackIntroduction();
            Check("new track introduction lasts its first second", intro.Begin("A", 1000) && intro.IsActive("A", 1999) && !intro.IsActive("A", 2000));
            Check("metadata refresh does not restart track introduction", !intro.Begin("A", 2100) && !intro.IsActive("A", 2100));
            Check("next song gets a separate introduction", intro.Begin("B", 2200) && intro.IsActive("B", 2500) && !intro.IsActive("A", 2500));
            Check("Discord Application ID validates without using account tokens", DiscordPresenceService.ValidApplicationId("123456789012345678") && !DiscordPresenceService.ValidApplicationId("not-an-id"));
            var privatePresence = DiscordPresenceService.CreatePresence(false, true, true, "Private title", "Private artist", 10, 100, DateTime.UtcNow);
            Check("Discord can show island without revealing track metadata", privatePresence.Details == "OSTROVOK" && !privatePresence.State.Contains("Private"));
            var playingPresence = DiscordPresenceService.CreatePresence(true, true, true, "Synthetic title", "Synthetic artist", 10, 100, DateTime.UtcNow);
            Check("Discord music presence contains artist and playback timestamps", playingPresence.Details == "Synthetic title" && playingPresence.State == "Synthetic artist" && playingPresence.Timestamps != null);
            var pausedPresence = DiscordPresenceService.CreatePresence(true, true, false, "Synthetic title", "Synthetic artist", 10, 100, DateTime.UtcNow);
            Check("Discord pause does not keep a running playback timer", pausedPresence.State.StartsWith("На паузе") && pausedPresence.Timestamps == null);
            var coverPresence = DiscordPresenceService.CreatePresence(true, true, true, "Track", "Artist", 10, 100, DateTime.UtcNow, "https://avatars.yandex.net/get-music-content/album/400x400", "https://music.yandex.ru/track/123");
            Check("Discord cover and song link use current public music metadata", coverPresence.Assets.LargeImageKey.StartsWith("https://avatars.yandex.net/") && coverPresence.Assets.SmallImageKey == "ostrovok" && coverPresence.Buttons.First().Url == "https://music.yandex.ru/track/123");
            Check("Discord includes a GitHub shortcut within its two button limit", coverPresence.Buttons.Length == 2 && coverPresence.Buttons.Last().Label == "GitHub" && coverPresence.Buttons.Last().Url == "https://github.com/rapichmoney/OSTROVOK");
            var hiddenCover = DiscordPresenceService.CreatePresence(false, true, true, "Private title", "Private artist", 10, 100, DateTime.UtcNow, "https://avatars.yandex.net/get-music-content/private/400x400", "https://music.yandex.ru/track/123");
            Check("Discord private mode hides artwork song link and image tooltip", hiddenCover.Assets.LargeImageKey == "ostrovok" && !hiddenCover.Assets.LargeImageText.Contains("Private") && !hiddenCover.Buttons.Any(x => x.Url.Contains("/track/")));
            var invalidCover = DiscordPresenceService.CreatePresence(true, true, false, "Track", "Artist", 10, 100, DateTime.UtcNow, "https://localhost/image?token=private", "https://example.com/track/123");
            Check("Discord rejects private or unrelated image and track URLs", invalidCover.Assets.LargeImageKey == "ostrovok" && invalidCover.Buttons.Single().Label == "GitHub");
            var stableWidth = new StableMusicWidth();
            Check("first music title can start compact", stableWidth.Resolve(220, 544, true) == 220);
            Check("timed lyrics widen current music session", stableWidth.Resolve(544, 544, true) == 544);
            Check("next title and repeated lyric searches never shrink music session", Enumerable.Range(0, 20).All(_ => stableWidth.Resolve(220, 544, true) == 544));
            Check("manual width limit still takes effect", stableWidth.Resolve(220, 440, true) == 440);
            Check("idle clears retained music width", stableWidth.Resolve(318, 544, false) == 318 && stableWidth.Resolve(220, 544, true) == 220);
            using (var fallback = new MediaService())
            {
                fallback.UpdateNativePlayback(new YandexLyricsBridge.PlaybackState(21, .46, "test", false, false, true, true, "Synthetic", "Test", 128, true));
                Check("native player remains usable without Windows media session", fallback.Available && fallback.CanSeek && fallback.CanPlay && fallback.Source == "ru.yandex.desktop.music");
                fallback.UpdateNativePlayback(new YandexLyricsBridge.PlaybackState(8, .46, "test", false, false, true, true, "Synthetic", "Test", 128, false));
                Check("native paused position accepts backwards seek", fallback.Position == 8 && !fallback.Playing && fallback.Duration == 128);
            }
            if (args.Contains("--yandex-playback"))
            {
                var liveBridge = new YandexLyricsBridge();
                var liveState = await liveBridge.PlaybackAsync();
                Check("live CSharp Yandex playback connection", liveState != null, liveBridge.PlaybackError);
                if (liveState != null)
                {
                    using var liveMedia = new MediaService { SelectedSource = "ru.yandex.desktop.music" };
                    await liveMedia.InitializeAsync(); liveMedia.UpdateNativePlayback(liveState); await liveMedia.RefreshAsync();
                    Check("native playback status survives a Windows media refresh", liveMedia.Source != "ru.yandex.desktop.music" || liveMedia.Playing == liveState.Playing);
                }
            }
            foreach (var backdrop in new[] { new[] { Color.FromRgb(24, 26, 29), Color.FromRgb(75, 78, 84) }, new[] { Color.FromRgb(230, 230, 230), Colors.White }, new[] { Color.FromRgb(106, 65, 95), Color.FromRgb(21, 23, 26) } })
            {
                var contrast = ControlContrast.For(backdrop);
                Check("slider rail stays visible on background " + backdrop[0], backdrop.All(x => ControlContrast.Ratio(x, contrast.Rail) >= 3));
                Check("slider thumb and rim keep opposite colors " + backdrop[0], ControlContrast.Ratio(contrast.Ink, contrast.Rim) >= 20);
            }
            var pause = new PausePresentation();
            Check("paused music stays visible during grace period", pause.IsActive(true, false, 1000) && pause.IsActive(true, false, 5999));
            Check("paused music becomes idle after five seconds", !pause.IsActive(true, false, 6000));
            Check("playback resumes presentation immediately", pause.IsActive(true, true, 6001));
            Check("second pause receives its own grace period", pause.IsActive(true, false, 9000) && !pause.IsActive(true, false, 14000));
            Check("closed player clears pause deadline", !pause.IsActive(false, false, 15000) && pause.IsActive(true, false, 20000));
            Check("missing settings use defaults", store.Load("settings.json", () => new AppSettings()).Topmost);
            var settings = new AppSettings { Left = -1920, Top = 12, ClipboardEnabled = true, Pinned = true };
            Check("save settings", store.Save("settings.json", settings));
            var loaded = store.Load("settings.json", () => new AppSettings());
            Check("settings round trip preserves negative monitor coordinates", loaded.Left == -1920 && loaded.Top == 12 && loaded.Pinned && loaded.ClipboardEnabled);
            const string note = "Первая строка\nВторая строка — проверка кириллицы";
            Check("unicode notes round trip", store.SaveText("notes.txt", note) && store.LoadText("notes.txt") == note);
            Check("writes leave no partial temporary file", !File.Exists(Path.Combine(root, "notes.txt.writing")));
            File.WriteAllText(Path.Combine(root, "bad.json"), "{ broken");
            bool errorRaised = false; store.Error += _ => errorRaised = true;
            Check("corrupt JSON falls back", store.Load("bad.json", () => new AppSettings()).Topmost);
            Check("corrupt JSON raises an error and keeps original", errorRaised && Directory.GetFiles(root, "bad.json.damaged-*").Length == 1 && File.ReadAllText(Path.Combine(root, "bad.json")) == "{ broken");
            var task = new TaskItem { Text = "Проверочная задача" }; bool notified = false;
            task.PropertyChanged += (_, e) => notified = e.PropertyName == "Done"; task.Done = true;
            store.Save("tasks.json", new[] { task });
            Check("task completion notifies and persists", notified && store.Load("tasks.json", () => Array.Empty<TaskItem>()).Single().Done);
            var entry = new CalendarEntry { Title = "Проверка напоминания", StartsAt = new DateTime(2026, 11, 13, 14, 30, 0), Reminded = true };
            store.Save("calendar.json", new[] { entry }); var recovered = store.Load("calendar.json", () => Array.Empty<CalendarEntry>()).Single();
            Check("calendar reminder ID and state persist", recovered.Id == entry.Id && recovered.Reminded && recovered.StartsAt == entry.StartsAt);
            Check("temporary download classification", DownloadEntry.IsTemporary("music.CRDOWNLOAD") && DownloadEntry.IsTemporary("a.part") && !DownloadEntry.IsTemporary("report.pdf"));
            Check("human file sizes", DownloadEntry.FormatBytes(1048576) == "1,0 МБ" || DownloadEntry.FormatBytes(1048576) == "1.0 МБ");
            var monitor = new SystemMonitor(); monitor.Refresh(); await Task.Delay(150); monitor.Refresh();
            Check("native memory metrics", monitor.MemoryTotal > 0 && monitor.MemoryUsed <= monitor.MemoryTotal && monitor.MemoryPercent is >= 0 and <= 100);
            Check("native CPU sample is bounded", monitor.Cpu is >= 0 and <= 100);
            var audio = new AudioService();
            Check("read Windows audio endpoint without changing volume", audio.Refresh() && audio.Volume is >= 0 and <= 1, audio.Error);
            using var media = new MediaService(); await media.InitializeAsync();
            if (args.Contains("--media-current"))
                Check("current media snapshot", true, $"available={media.Available}, playing={media.Playing}, video={media.IsVideo}, title={media.Title}, artist={media.Artist}, source={media.Source}, sessions={media.Sessions.Count}");
            Check("Windows media API initializes", media.Error == null, media.Error ?? $"sessions={media.Sessions.Count}");
            Check("web links reject executable schemes", ShelfItem.IsWebLink("https://example.com/path?q=1") && !ShelfItem.IsWebLink("javascript:alert(1)") && !ShelfItem.IsWebLink("file:///C:/test.exe"));
            Check("content determines shell height", MainWindow.PageHeight(0, false) < MainWindow.PageHeight(0, true) && MainWindow.PageHeight(1, false) != MainWindow.PageHeight(5, false));
            Check("ten selectable animation profiles", SettingsWindow.AnimationStyles.Distinct().Count() == 10 && SettingsWindow.AnimationStyles.All(x => Motion.ShellProfile(x).Duration >= 100));
            foreach (string style in SettingsWindow.AnimationStyles)
            {
                foreach (bool opening in new[] { true, false })
                {
                    var plan = MotionDynamics.Plan(style, opening);
                    double from = opening ? 44 : 900, target = opening ? 900 : 44;
                    var safe = MotionDynamics.SafeGeometry(plan.Height.Easing, from, target, 30, 1000);
                    bool valid = Enumerable.Range(0, 501).All(i => { double size = from + (target - from) * safe.Ease(i / 500d); return double.IsFinite(size) && size >= 29.99 && size <= 1000.01; });
                    Check("spring geometry remains valid: " + style + (opening ? " open" : " close"), valid && Math.Abs(safe.Ease(1) - 1) < .0001);
                }
            }
            Check("stretch separates width and height timing", MotionDynamics.Plan("Тянучка", true).Height.Delay > MotionDynamics.Plan("Тянучка", true).Width.Delay);
            Check("animation tempo changes duration", MotionDynamics.Plan("Капля", true, 140).Duration < MotionDynamics.Plan("Капля", true, 70).Duration);
            const string weatherFixture = """
            {"current":{"time":"2026-11-13T14:00","temperature_2m":5,"relative_humidity_2m":93,"apparent_temperature":1,"weather_code":3,"wind_speed_10m":5},"hourly":{"time":["2026-11-13T13:00","2026-11-13T14:00","2026-11-13T15:00"],"temperature_2m":[4,5,6],"weather_code":[3,3,61]},"daily":{"time":["2026-11-13","2026-11-14"],"temperature_2m_max":[8,9],"temperature_2m_min":[2,3],"weather_code":[3,61]}}
            """;
            var weather = WeatherService.Parse("Тестовый город", weatherFixture);
            Check("weather current conditions parsed", weather.Temperature == 5 && weather.FeelsLike == 1 && weather.Humidity == 93 && weather.High == 8 && weather.Low == 2);
            Check("weather hourly and daily series parsed", weather.Hours.Length == 3 && weather.Days.Length == 2 && weather.Days[1].Low == 3 && WeatherService.Describe(61) == "Дождь");
            var lrc = LyricsService.ParseLrc("[ar:Synthetic]\n[offset:+100]\n[00:01.20]Первая тестовая строка\n[00:03.500][00:06.50]Повтор\n[00:05.00]\n[00:07.05]<00:07.05>Тест <00:07.50>слов");
            Check("LRC fractions, repeated timestamps and blanks", lrc.Length == 5 && Math.Abs(lrc[0].Seconds - 1.1) < .001 && lrc[2].Text == "" && lrc[3].Text == "Повтор");
            Check("LRC word tags do not leak into text", lrc[4].Text == "Тест слов");
            Check("lyric line changes at exact timestamp", LyricsService.LineAt(lrc, 1.09) == -1 && LyricsService.LineAt(lrc, 1.1) == 0 && LyricsService.LineAt(lrc, 3.4) == 1);
            Check("lyric selection follows seeking backwards", LyricsService.LineAt(lrc, 7) == 4 && LyricsService.LineAt(lrc, 2) == 0);
            Check("manual lyric offset applies", LyricsService.LineAt(lrc, .6, .5) == 0 && LyricsService.LineAt(lrc, 1.1, -.5) == -1);
            var playbackClock = new PlaybackClock(); var sampleAt = DateTimeOffset.UtcNow;
            playbackClock.Sample(10, 100, true, 1, sampleAt, sampleAt);
            Check("playing clock advances between Windows samples", playbackClock.At(sampleAt.AddSeconds(1.25)) == 11.25);
            playbackClock.Sample(20, 100, false, 1, sampleAt, sampleAt);
            Check("paused clock stays fixed", playbackClock.At(sampleAt.AddSeconds(5)) == 20);
            playbackClock.Sample(40, 100, true, 2, sampleAt, sampleAt);
            Check("playback speed and duration respected", playbackClock.At(sampleAt.AddSeconds(2)) == 44 && playbackClock.At(sampleAt.AddSeconds(90)) == 100);
            playbackClock.Sample(3, 100, true, 1, sampleAt.AddSeconds(4), sampleAt.AddSeconds(4));
            Check("seek replaces previous clock anchor", playbackClock.At(sampleAt.AddSeconds(4.5)) == 3.5);
            var smooth = new SynchronizedPlaybackClock();
            smooth.Sample(10, 100, true, sampleAt);
            double beforeCorrection = smooth.At(sampleAt.AddSeconds(1));
            smooth.Sample(10.7, 100, true, sampleAt.AddSeconds(1));
            Check("late native sample does not rewind rendered lyric position", Math.Abs(smooth.At(sampleAt.AddSeconds(1)) - beforeCorrection) < .00001);
            double beforeForwardCorrection = smooth.At(sampleAt.AddSeconds(2));
            smooth.Sample(12.2, 100, true, sampleAt.AddSeconds(2));
            Check("early native sample does not jump rendered lyric position", Math.Abs(smooth.At(sampleAt.AddSeconds(2)) - beforeForwardCorrection) < .00001);
            double lastPosition = smooth.At(sampleAt.AddSeconds(2)); bool monotonic = true;
            for (int frame = 1; frame <= 600; frame++)
            {
                double seconds = 2 + frame / 60d;
                if (frame % 60 == 0) smooth.Sample(10 + seconds + (frame % 120 == 0 ? -.3 : .25), 100, true, sampleAt.AddSeconds(seconds));
                double value = smooth.At(sampleAt.AddSeconds(seconds)); monotonic &= value >= lastPosition; lastPosition = value;
            }
            Check("native jitter remains continuous over 600 rendering frames", monotonic);
            smooth.Sample(3, 100, true, sampleAt.AddSeconds(13), true);
            Check("explicit backwards seek remains immediate", smooth.At(sampleAt.AddSeconds(13)) == 3);
            smooth.Sample(3.25, 100, true, sampleAt.AddSeconds(13.1), true);
            Check("sub-second manual seek bypasses smoothing", smooth.At(sampleAt.AddSeconds(13.1)) == 3.25);
            smooth.Sample(5, 100, false, sampleAt.AddSeconds(14));
            Check("smoothed clock freezes on pause", smooth.At(sampleAt.AddSeconds(20)) == 5);
            smooth.Sample(50, 100, true, sampleAt.AddSeconds(21));
            Check("native external seek is detected", smooth.At(sampleAt.AddSeconds(21)) == 50);
            using var matchingRecord = JsonDocument.Parse("""{"trackName":"Example","artistName":"Artist","duration":200}""");
            Check("lyrics do not match wrong artist or version", LyricsService.Matches(matchingRecord.RootElement, new LyricTrack("Example", "Artist", "", 200)) && !LyricsService.Matches(matchingRecord.RootElement, new LyricTrack("Example", "Other", "", 200)) && !LyricsService.Matches(matchingRecord.RootElement, new LyricTrack("Example", "Artist", "", 210)));
            if (args.Contains("--lyrics-live"))
            {
                var foundLyrics = await new LyricsService().FindAsync(new LyricTrack("Wonderwall", "Oasis", "", 258));
                Check("live lyrics lookup returns timed text", foundLyrics.Synced && foundLyrics.RecordId != null, $"record={foundLyrics.RecordId}, timedLines={foundLyrics.Lines.Length}, status={foundLyrics.Status}");
            }
            if (args.Contains("--lyrics-current"))
            {
                Check("current Windows music session available", media.Available, media.Available ? $"title={media.Title}, artist={media.Artist}, duration={media.Duration:0}, playing={media.Playing}, source={media.Source}" : "No active Windows media session");
                if (media.Available)
                {
                    var actual = await new LyricsService().FindAsync(new LyricTrack(media.Title, media.Artist, media.Album, media.Duration));
                    Check("current track timed lyrics found", actual.Synced, $"record={actual.RecordId}, timedLines={actual.Lines.Length}, status={actual.Status}");
                }
            }
            using var nativeFixture = JsonDocument.Parse("""{"state":"ready","id":"test","lines":[{"seconds":1.2,"text":"Тестовая строка"}],"plain":""}""");
            Check("native Yandex timing data parsed", YandexLyricsBridge.ParseResult(nativeFixture.RootElement)?.Lines.Single().Seconds == 1.2);
            using var nativePlain = JsonDocument.Parse("""{"state":"ready","id":"test","lines":[],"plain":"Текст без таймкодов"}""");
            Check("native plain text does not become timed lyrics", YandexLyricsBridge.ParseResult(nativePlain.RootElement) is { Synced: false, PlainText: "Текст без таймкодов" });
            LyricLine[] nativeGaps = [new(1, "Тест", 2), new(4, "Следующая строка", 5)];
            Check("native end timestamps clear highlight during silence", LyricsService.LineAt(nativeGaps, 1.5) == 0 && LyricsService.LineAt(nativeGaps, 2) == -1 && LyricsService.LineAt(nativeGaps, 3) == -1 && LyricsService.LineAt(nativeGaps, 4.5) == 1 && LyricsService.LineAt(nativeGaps, 5) == -1);
            await LyricsFallbackTests.RunAsync(Check);
            Check("Telegram filter accepts native Telegram identifiers", TelegramNotificationService.IsTelegram("TelegramDesktop.TDesktop", "Telegram") && TelegramNotificationService.IsTelegram("Win32.SomeId", "Telegram Desktop"));
            Check("Telegram filter excludes other applications", !TelegramNotificationService.IsTelegram("DesktopIsland.Desktop", "Desktop Island") && !TelegramNotificationService.IsTelegram("Microsoft.Outlook", "Outlook"));
            if (args.Contains("--notifications-live"))
            {
                Check("Windows notification package identity available", TelegramNotificationService.HasIdentity);
                if (TelegramNotificationService.HasIdentity)
                {
                    var access = Windows.UI.Notifications.Management.UserNotificationListener.Current.GetAccessStatus();
                    Check("Windows notification access granted", access == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed, access.ToString());
                    if (access == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
                    {
                        var notifications = await Windows.UI.Notifications.Management.UserNotificationListener.Current.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
                        Check("Windows notification enumeration succeeds", true, "count=" + notifications.Count);
                        if (args.Contains("--notifications-watch"))
                        {
                            string watchReport = (args.SkipWhile(x => x != "--report").Skip(1).FirstOrDefault() ?? Path.Combine(root, "report.json")) + ".watch.json";
                            var observed = new Dictionary<uint, object>(); int maximum = notifications.Count;
                            for (int sample = 0; sample < 360; sample++)
                            {
                                var current = await Windows.UI.Notifications.Management.UserNotificationListener.Current.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
                                maximum = Math.Max(maximum, current.Count);
                                foreach (var item in current)
                                {
                                    try
                                    {
                                        string appId = item.AppInfo.AppUserModelId, name = item.AppInfo.DisplayInfo.DisplayName;
                                        observed[item.Id] = new { appId, name, telegram = TelegramNotificationService.IsTelegram(appId, name), templates = item.Notification.Visual.Bindings.Select(x => x.Template).ToArray() };
                                    }
                                    catch (Exception ex) { observed[item.Id] = new { error = ex.HResult.ToString("X8") }; }
                                }
                                File.WriteAllText(watchReport, JsonSerializer.Serialize(new { sample, maximum, observed = observed.Values.ToArray() }));
                                await Task.Delay(250);
                            }
                            Check("notification watch completed", true, "maximum=" + maximum + ", sources=" + observed.Count);
                        }
                    }
                }
            }
            Check("translation skips Russian clipboard text", !TranslationService.ShouldTranslate("Это проверка русского текста"));
            Check("translation accepts foreign scripts", TranslationService.ShouldTranslate("Hello world") && TranslationService.ShouldTranslate("Bonjour le monde") && TranslationService.ShouldTranslate("你好世界"));
            Check("translation ignores links numbers and long tokens", !TranslationService.ShouldTranslate("https://example.com") && !TranslationService.ShouldTranslate("12345") && !TranslationService.ShouldTranslate(new string('a', 64)));
            Check("translation length limit does not truncate text", !TranslationService.ShouldTranslate(new string('a', 4001)));
            using var translatedFixture = JsonDocument.Parse("""[[["Привет, ","Hello ",null],["мир","world",null]],null,"en"]""");
            Check("translation joins segments and detects source language", TranslationService.Parse(translatedFixture.RootElement) == new TranslationResult("Привет, мир", "en"));
            int googleCalls = 0, fallbackCalls = 0;
            using var fallbackClient = new System.Net.Http.HttpClient(new TranslationHandler(request =>
            {
                if (request.RequestUri!.Host == "translate.googleapis.com") { googleCalls++; return new(System.Net.HttpStatusCode.TooManyRequests); }
                fallbackCalls++;
                return new(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("""{"responseStatus":200,"responseData":{"translatedText":"Привет, мир!","detectedLanguage":"en"}}""") };
            }));
            var resilientTranslator = new TranslationService(fallbackClient);
            var fallbackResult = await resilientTranslator.TranslateAsync("Hello world", System.Threading.CancellationToken.None);
            Check("translation falls back when Google rate limits requests", fallbackResult.Text == "Привет, мир!" && fallbackResult.Provider == "MyMemory" && googleCalls == 1 && fallbackCalls == 1);
            await resilientTranslator.TranslateAsync("Hello again", System.Threading.CancellationToken.None);
            Check("translation cooldown avoids repeated Google rate limited calls", googleCalls == 1 && fallbackCalls == 2);
            await resilientTranslator.TranslateAsync("Hello world", System.Threading.CancellationToken.None);
            Check("repeated translation uses memory cache", googleCalls == 1 && fallbackCalls == 2);
            string unicodeSample = string.Concat(Enumerable.Repeat("Проверка 😀 translation\n", 70));
            var segments = TranslationService.SplitFallbackText(unicodeSample);
            Check("fallback segments preserve Unicode and all source characters within API byte limit", string.Concat(segments) == unicodeSample && segments.All(x => System.Text.Encoding.UTF8.GetByteCount(x) <= 480 && !char.IsLowSurrogate(x[0]) && !char.IsHighSurrogate(x[^1])));
            using var quotaFixture = JsonDocument.Parse("""{"responseStatus":429,"responseDetails":"LIMIT"}""");
            bool quotaRejected = false;
            try { TranslationService.ParseFallback(quotaFixture.RootElement); } catch (TranslationUnavailableException) { quotaRejected = true; }
            Check("fallback quota errors never become translation text", quotaRejected);
            using var canceled = new System.Threading.CancellationTokenSource(); canceled.Cancel();
            bool translationCanceled = false;
            try { await resilientTranslator.TranslateAsync("Canceled request", canceled.Token); } catch (OperationCanceledException) { translationCanceled = true; }
            Check("translation cancellation prevents outgoing requests", translationCanceled && googleCalls == 1 && fallbackCalls == 2);
            var replyGate = new ClipboardTranslationGate();
            Check("Russian copy does not translate before English activation", replyGate.SelectTarget("Привет, как дела?", true) == null);
            Check("English copy requests Russian without arming reply early", replyGate.SelectTarget("Hello, how are you?", true) == "ru" && !replyGate.EnglishReplyArmed);
            replyGate.Complete("ru", "en");
            Check("English to Russian success arms reverse Russian copy", replyGate.SelectTarget("У меня всё хорошо", true) == "en");
            Check("new Russian copy can replace a pending reverse request", replyGate.SelectTarget("У меня отлично", true) == "en");
            replyGate.Complete("en", "ru");
            Check("successful reverse translation consumes activation", !replyGate.EnglishReplyArmed && replyGate.SelectTarget("Следующий русский текст", true) == null);
            replyGate.Complete("ru", "fr");
            Check("non-English incoming translation does not arm English reply", !replyGate.EnglishReplyArmed);
            replyGate.Complete("ru", "en"); replyGate.SelectTarget("Привет", false);
            Check("disabling translation clears reverse activation", !replyGate.EnglishReplyArmed);
            int targetRequests = 0;
            using var targetClient = new System.Net.Http.HttpClient(new TranslationHandler(request =>
            {
                targetRequests++;
                bool english = request.RequestUri!.Query.Contains("tl=en", StringComparison.Ordinal);
                return new(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(english ? """[[["English fixture",null,null]],null,"ru"]""" : """[[["Русский пример",null,null]],null,"en"]""") };
            }));
            var targetTranslator = new TranslationService(targetClient);
            var russianTarget = await targetTranslator.TranslateAsync("fixture", System.Threading.CancellationToken.None);
            var englishTarget = await targetTranslator.TranslateAsync("fixture", System.Threading.CancellationToken.None, "en");
            Check("translation cache keeps target languages separate", targetRequests == 2 && russianTarget.Text != englishTarget.Text);
            if (args.Contains("--translate-live"))
            {
                var translated = await new TranslationService().TranslateAsync("Hello world", System.Threading.CancellationToken.None);
                Check("live translation returns Russian text", translated.Language == "en" && translated.Text.Any(c => c is >= 'А' and <= 'я'), "Synthetic text only");
                var reverse = await new TranslationService().TranslateAsync("Привет, мир", System.Threading.CancellationToken.None, "en");
                Check("live reverse translation returns English text", reverse.Language == "ru" && reverse.Text.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'), "Synthetic text only");
            }
            if (args.Contains("--yandex-live"))
            {
                var native = new YandexLyricsBridge();
                string? target = args.SkipWhile(x => x != "--yandex-track-id").Skip(1).FirstOrDefault();
                var nativeResult = await native.FindAsync(new LyricTrack(media.Title, media.Artist, media.Album, media.Duration), System.Threading.CancellationToken.None, target, args.Contains("--yandex-plain"));
                Check("live Yandex native lyrics available", nativeResult != null && (nativeResult.Synced || nativeResult.PlainText.Length > 0), $"target={target ?? "current"}, timedLines={nativeResult?.Lines.Length ?? 0}, plainCharacters={nativeResult?.PlainText.Length ?? 0}, status={native.Status}");
            }
            if (args.Contains("--weather-live"))
            {
                var service = new WeatherService(); var found = await service.SearchAsync("Орёл");
                Check("live weather city search", found.Length > 0);
                if (found.Length > 0)
                {
                    var actual = await service.FetchAsync(found[0]);
                    Check("live forecast has hourly and weekly data", actual.Hours.Length == 24 && actual.Days.Length == 7 && double.IsFinite(actual.Temperature), $"city={actual.City}, retrieved={actual.RetrievedAt:O}");
                }
            }
        }
        catch (Exception ex) { failures++; report.Add(new { name = "unhandled exception", pass = false, detail = ex.ToString() }); }
        var output = args.SkipWhile(x => x != "--report").Skip(1).FirstOrDefault() ?? Path.Combine(root, "report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { failures, checks = report }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    private sealed class TranslationHandler(Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> respond) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken token)
            => Task.FromResult(respond(request));
    }
}
