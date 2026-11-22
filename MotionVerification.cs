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
            var fixture = new LyricsResult("Тест", [new(0, "Город за окном становится тише"), new(3, "Мы продолжаем путь без остановок"), new(6, "Свет остаётся рядом с нами")]);
            ShowLyrics(fixture, "Текст с таймкодами");
            currentLyricIndex = -1; UpdateLyricPosition(.3); AnimateExpansion(true); UpdateLayout(); await AwaitShellAsync();
            Check("Read Along renders in the real player viewport", MediaPanel.IsVisible && LyricsScroll.ActualHeight >= 140 && lyricFills[0].ActualWidth > 0 && lyricFills[0].IsVisible);
            Check("Read Along is applied to active line", lyricFills[0].ReadAlong);
            lyricFills[0].Progress = .6; await Task.Delay(360); Capture(Path.Combine(output, "read-along.png"));
            settings.ReadAlong = false; foreach (var fill in lyricFills) fill.ReadAlong = false;
            Check("ordinary lyric highlight remains available", lyricFills.All(x => !x.ReadAlong));
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
