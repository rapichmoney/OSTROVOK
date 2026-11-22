using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private async Task VerifyMotionAsync()
    {
        var checks = new List<object>(); int failures = 0;
        void Check(string name, bool pass, string? detail = null) { checks.Add(new { name, pass, detail }); if (!pass) failures++; }
        string output = Environment.GetCommandLineArgs().SkipWhile(x => x != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(store.Root, "motion");
        Directory.CreateDirectory(output);
        try
        {
            timer.Stop(); lyricTimer.Stop(); leaveTimer.Stop(); Pin.IsChecked = true;
            Motion.VerificationOverride = true; settings.InterfaceFont = InterfaceFonts.Aminahs;
            var immediate = new Border { Opacity = .8 };
            Motion.Tween(immediate, OpacityProperty, .1, 300);
            Check("animation first frame retains the current value", Math.Abs(immediate.Opacity - .8) < .001);
            Motion.Tween(immediate, OpacityProperty, .2, 300, 100);
            Check("delayed animation retains its starting value", Math.Abs(immediate.Opacity - .8) < .001);
            settings.Detached = true; ApplyShellAppearance();
            Check("Read Along defaults to off", !settings.ReadAlong);
            using (var icon = LoadTrayIcon()) Check("tray icon resource loads at 32 pixels", icon.Width == 32 && icon.Height == 32);
            SetExpanded(true); await AwaitShellAsync();
            foreach (int tab in new[] { 0, 10, 7, 0, 2, 1, 0 })
            {
                Navigation.SelectedIndex = tab; UpdateToolbar(); await Task.Delay(520); UpdateLayout();
                Check("only active caption after transition " + tab, toolbar.All(x => x.Value.Label.Visibility == (x.Key == tab ? Visibility.Visible : Visibility.Collapsed)));
                var item = toolbar[tab];
                var point = item.Button.TranslatePoint(new Point(), ToolbarIndicatorLayer);
                Check("indicator reaches active button " + tab, Math.Abs(Canvas.GetLeft(ToolbarIndicator) - point.X) < 2 && Math.Abs(ToolbarIndicator.Width - item.Button.ActualWidth) < 2, $"indicator={Canvas.GetLeft(ToolbarIndicator)}/{ToolbarIndicator.Width}, button={point.X}/{item.Button.ActualWidth}");
                Check("toolbar fits at tab " + tab, TopNavigation.ActualWidth < 420);
            }
            Navigation.SelectedIndex = 10; UpdateToolbar(); await Task.Delay(60);
            Navigation.SelectedIndex = 7; UpdateToolbar(); await Task.Delay(50);
            Navigation.SelectedIndex = 0; UpdateToolbar(); await Task.Delay(520);
            Check("interrupted selection ends on requested tab", animatedTab == 0 && toolbar[0].Label.IsVisible && toolbar.Where(x => x.Key != 0).All(x => !x.Value.Label.IsVisible));
            media.UpdateNativePlayback(new YandexLyricsBridge.PlaybackState(0, .5, "motion-fixture", false, false, true, true, "Проверка анимаций", "OSTROVOK", 30, true));
            UpdateMedia();
            settings.ReadAlong = true; settings.PlayerHeight = 620;
            var fixture = new LyricsResult("Тест", [new(0, "Город за окном становится тише", Words: [new(0, 0, 5), new(.8, 6, 2), new(1.4, 9, 5), new(2, 15, 10), new(2.6, 26, 4)]), new(3, "Мы продолжаем путь без остановок"), new(6, "Свет остаётся рядом с нами")]);
            ShowLyrics(fixture, "Текст с таймкодами");
            currentLyricIndex = -1; UpdateLyricPosition(.3); AnimateExpansion(true); UpdateLayout(); await AwaitShellAsync();
            Check("Read Along renders in the real player viewport", MediaPanel.IsVisible && LyricsScroll.ActualHeight >= 140 && lyricFills[0].ActualWidth > 0 && lyricFills[0].IsVisible);
            Check("Read Along is applied to active line", lyricFills[0].ReadAlong);
            Capture(Path.Combine(output, "read-along-word-before.png"));
            var markerBefore = lyricFills[0].MarkerBounds;
            UpdateExpandedLyricProgress(.9); Capture(Path.Combine(output, "read-along-word-start.png"));
            Check("word transition first frame retains previous marker position", !markerBefore.IsEmpty && Math.Abs(lyricFills[0].MarkerBounds.Left - markerBefore.Left) < 1);
            await Task.Delay(150); Capture(Path.Combine(output, "read-along-word-after.png"));
            Check("source word transition reaches the next word", lyricFills[0].MarkerBounds.Left > markerBefore.Left + 2 && lyricFills[0].TimingFrame?.Start == 6);
            settings.ReadAlongAdjustment = .4;
            UpdateLyricPosition(2.8);
            Check("word offset applies across line boundaries", currentLyricIndex == 1);
            Check("outgoing line releases its active word during the line fade", lyricFills[0].TimingFrame is { Active: false } && lyricFills[0].TimingFrame!.ReadThrough == fixture.Lines[0].Text.Length);
            settings.ReadAlongAdjustment = 0; UpdateLyricPosition(.3);
            lyricFills[0].Progress = .6; await Task.Delay(360); Capture(Path.Combine(output, "read-along.png"));
            settings.ReadAlong = false; foreach (var fill in lyricFills) fill.ReadAlong = false;
            Check("ordinary lyric highlight remains available", lyricFills.All(x => !x.ReadAlong));
            Check("main window uses branded icon", Icon is System.Windows.Media.Imaging.BitmapSource windowBitmap && windowBitmap.PixelWidth == 32);
            var settingsTest = new SettingsWindow(this, settings);
            Check("settings window uses branded icon", ReferenceEquals(settingsTest.Icon, BrandIcon));
            settingsTest.Close();
            int maximumSnapshots = 0;
            for (int i = 0; i < 20; i++)
            {
                int n = i; PolishedMotion.Swap(TrackIdentity, () => TrackTitle.Text = "Тест " + n, 420, 6);
                maximumSnapshots = Math.Max(maximumSnapshots, PolishedMotion.SnapshotCount(TrackIdentity)); await Task.Delay(8);
            }
            Check("rapid track changes keep at most one snapshot", maximumSnapshots <= 1);
            await Task.Delay(500);
            Check("interrupted track transitions leave no snapshot", PolishedMotion.SnapshotCount(TrackIdentity) == 0 && (System.Windows.Documents.AdornerLayer.GetAdornerLayer(TrackIdentity)?.GetAdorners(TrackIdentity)?.Length ?? 0) == 0);
            for (int i = 0; i < 20; i++)
            {
                media.UpdateNativePlayback(new YandexLyricsBridge.PlaybackState(0, .5, "rapid-" + i, false, false, true, true, "Быстрая смена " + i, "Тест", 30, true));
                UpdateMedia(); await Task.Delay(8);
            }
            await Task.Delay(650);
            Check("real media updates leave only the latest title", TrackTitle.Text == "Быстрая смена 19" && TrackArtist.Text == "Тест");
            Check("real media updates remove outgoing overlays", PolishedMotion.SnapshotCount(TrackIdentity) == 0);
            BeginTrackVisualHold(CurrentVisualKey, verification: true); UpdateMedia(); await Task.Delay(500);
            Check("lyrics search never holds a bitmap over live track controls", PolishedMotion.SnapshotCount(TrackIdentity) == 0);
            CancelTrackVisualHold();

            yandexPlayback = new YandexLyricsBridge.PlaybackState(0, .5, "fixture", true, false, true, true);
            UpdateYandexRating(); await Task.Delay(340);
            Check("liked track uses red ink", YandexLikeIcon.Fill is SolidColorBrush liked && liked.Color == Color.FromRgb(233, 78, 96));
            yandexPlayback = yandexPlayback with { Liked = false }; UpdateYandexRating(); await Task.Delay(340);
            Check("unliked track returns to secondary ink", YandexLikeIcon.Fill is SolidColorBrush unliked && unliked.Color == ((SolidColorBrush)FindResource("Secondary")).Color);
            Timeline.ApplyTemplate();
            var track = (Track)Timeline.Template.FindName("PART_Track", Timeline);
            Check("slider has visible filled section", track.DecreaseRepeatButton.Opacity == 1);
            var menu = new ContextMenu(); TrackIslandMenu(menu); menu.Items.Add(new MenuItem { Header = "Проверка" }); menu.PlacementTarget = PlayButton; menu.IsOpen = true;
            await Task.Delay(390); Check("menu animation reaches opaque state", menu.Opacity > .99); menu.IsOpen = false; await Task.Delay(400);
            Check("closing menu releases island interaction guard", openIslandMenus.Count == 0, $"isOpen={menu.IsOpen}, count={openIslandMenus.Count}");
            SetExpanded(false); await AwaitShellAsync();
            settings.ReadAlong = true; ShowLyrics(fixture, "Текст с таймкодами");
            UpdateLyricPosition(.9); RefreshIdleContent(); UpdateCompactLyricProgress(.9, 0); await Task.Delay(400); UpdateLayout();
            Check("Read Along is rendered in compact island", compactReadAlong?.IsVisible == true && CompactTrack.Opacity == 0 && !CompactLyricFill.IsVisible);
            Check("compact Read Along follows source word timing", compactReadAlong?.WordPosition > 5);
            Check("both player views resolve the same source word", compactReadAlong?.TimingFrame == ReadAlongTiming.At(fixture.Lines[0],3,.9));
            Capture(Path.Combine(output, "compact-read-along.png"));
            settings.ReadAlong = false; UpdateCompactLyricProgress(.9, 0);
            Check("disabling Read Along restores compact fill", compactReadAlong?.IsVisible == false && CompactTrack.Opacity == 1 && CompactLyricFill.IsVisible);
            settings.TelegramNotificationsEnabled = true;
            ReceiveTelegramNotification(new TelegramMessage(101, "Проверка", "Первое сообщение", DateTimeOffset.UtcNow));
            ReceiveTelegramNotification(new TelegramMessage(102, "Проверка", "Второе сообщение", DateTimeOffset.UtcNow));
            await Task.Delay(120);
            Check("queue count comes from pending messages", NoticeQueueCount.Text == "ещё 1" && telegramQueue.Count == 1);
            Check("notification duration indicator is visible in compact mode", NoticeLifetime.IsVisible && noticeLifetimeClock != null);
            PauseCompactNotificationTimers(); await Task.Delay(60);
            Check("hover pauses duration indicator", noticeLifetimeClock?.IsPaused == true);
            ResumeCompactNotificationTimers(); await Task.Delay(60);
            Check("leaving notification resumes duration indicator", noticeLifetimeClock?.IsPaused == false);
            SetExpanded(true); await AwaitShellAsync();
            Check("duration indicator stays out of expanded toolbar", !NoticeLifetime.IsVisible);
            DisableTelegramNotifications();
            Check("Telegram icon has the agreed left offset", CompactCardIcon.Margin.Left == 2);
            settings.HiddenTabs = [1,3,5,10]; ApplyTabVisibility(); await Task.Delay(520);
            Check("hidden tabs are absent from toolbar", toolbar.Where(x => settings.HiddenTabs.Contains(x.Key)).All(x => x.Value.Button.Visibility == Visibility.Collapsed));
            Navigation.SelectedIndex = 3; await Task.Delay(100);
            Check("hidden tabs cannot become active", Navigation.SelectedIndex == 0);
            settings.HiddenTabs = []; ApplyTabVisibility(); await Task.Delay(520);
            Check("restoring tabs restores navigation", toolbar.Values.All(x => x.Button.Visibility == Visibility.Visible));
            SetExpanded(true); await AwaitShellAsync();
            double outgoingHeight = Height;
            SetExpanded(false);
            Check("collapse first frame retains expanded geometry", Math.Abs(Height - outgoingHeight) < 1);
            await Task.Delay(60);
            Check("collapse has only one live outgoing content layer", TrackVisualSnapshot.Source == null && (collapseContentActive ? TopNavigation.IsVisible && !Header.IsVisible : Header.IsVisible && !TopNavigation.IsVisible));
            await Task.Delay(110);
            Check("compact content appears before collapse finishes", !collapseContentActive && Header.IsVisible && Header.Opacity > .05);
            Check("collapse renders intermediate geometry", Height > CompactPresentationHeight + 1 && Height < outgoingHeight - 1);
            await AwaitShellAsync();
            Check("collapse releases the retained frame", TrackVisualSnapshot.Source == null);
            SetExpanded(true); await AwaitShellAsync();
            SetExpanded(false); await Task.Delay(60); settings.MiniMode = true; RefreshIdleContent();
            await AwaitShellAsync();
            Check("collapse settles a compact state change received during motion", Math.Abs(Width - CompactPresentationWidth) < .1 && Math.Abs(Height - CompactPresentationHeight) < .1 && !shellMoving);
            settings.MiniMode = false; RefreshIdleContent(); UpdateCompactNotification(true); await AwaitShellAsync();
            HideAnimated(); await Task.Delay(80);
            Check("hide marks glow as unavailable from first frame", hiding);
            Reveal(); await AwaitShellAsync(); await Task.Delay(650);
            Check("interrupting hide restores visible content", IsVisible && !hiding && Opacity == 1 && Shell.Opacity == 1);
            telegramNoticeActive = clipboardNoticeActive = false;
            availableRelease = new AppRelease(new Version(99,1,2),new Uri("https://example.test/archive"),2048,"",new Uri("https://example.test/checksum"));
            updateDetail = "Скачивание и установка с сохранением настроек";
            UpdateCompactNotification(true); await AwaitShellAsync();
            Check("update announcement has a single install button", CompactCardTitle.Text.Contains("99.1.2") && CompactCardAction.Content?.ToString() == "Обновить" && UpdateLaterButton.IsVisible);
            UpdateLayout();
            var laterPosition = UpdateLaterButton.TranslatePoint(new Point(), CompactCard);
            var updatePosition = CompactCardAction.TranslatePoint(new Point(), CompactCard);
            Check("update actions share one centered row with a gap", Math.Abs(laterPosition.Y - updatePosition.Y) < 1 && updatePosition.X - laterPosition.X - UpdateLaterButton.ActualWidth >= 7 && updatePosition.X + CompactCardAction.ActualWidth <= CompactCard.ActualWidth + 1);
            await Task.Delay(350); Capture(Path.Combine(output,"update-notification.png"));
            PostponeAppUpdate(this,new RoutedEventArgs()); await AwaitShellAsync();
            Check("update notice can be postponed", !UpdateNoticeActive);
            availableRelease = null;
            settings.ReducedMotion = true; Motion.Reduced = true;
            Navigation.SelectedIndex = 7; UpdateToolbar(); UpdateLayout(); await Task.Delay(50);
            Check("reduced motion reaches final selection immediately", animatedTab == 7 && !ToolbarIndicator.HasAnimatedProperties);
            Motion.Reduced = false;
            Check("verification never opens browser connection", App.VerificationMode);
        }
        catch (Exception ex) { failures++; checks.Add(new { name = "exception", pass = false, detail = ex.ToString() }); }
        File.WriteAllText(Path.Combine(output, "motion-report.json"), JsonSerializer.Serialize(new { failures, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown(failures == 0 ? 0 : 1);
    }
}
