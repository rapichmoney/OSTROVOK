using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIsland.Services;
using Microsoft.Win32;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly LyricsService lyricsService = new();
    private readonly YandexLyricsBridge yandexLyrics = new();
    private readonly DispatcherTimer lyricTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? lyricCancellation;
    private string requestedLyricsKey = "";
    private int lyricsRevision;
    private LyricsResult? lyricsResult;
    private int currentLyricIndex = -1;
    private readonly List<TextBlock> lyricViews = [];
    private readonly List<KaraokeLine> lyricFills = [];
    private KaraokeLine? compactReadAlong;
    private bool localLyrics;
    private bool lyricScrollPending;
    private bool lyricScrollImmediate;
    private bool lyricsLoading, lyricsPendingHadContent;
    private double LyricPositionAdjustment => settings.LyricsAdjustment + (settings.ReadAlong ? settings.ReadAlongAdjustment : 0);
    private double lyricsPendingWidth;

    private void InitializeLyrics()
    {
        compactReadAlong = new KaraokeLine(CompactTrack, singleLine: true) { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        CompactTextContent.Children.Add(compactReadAlong);
        lyricTimer.Tick += (_, _) => UpdateLyricPosition(media.Position);
        lyricTimer.Start();
        if (!App.VerificationMode) CompositionTarget.Rendering += RenderCompactLyrics;
        Header.IsVisibleChanged += (_, _) => UpdateCompactEqualizer();
        LyricsScroll.IsVisibleChanged += (_, _) => QueueCurrentLyricScroll();
        LyricsScroll.SizeChanged += (_, _) => QueueCurrentLyricScroll();
        InitializeLyricsInteraction();
    }
    private void RefreshTrackLyrics(bool force = false)
    {
        if (App.VerificationMode && !force) return;
        if (media.IsVideo)
        {
            requestedLyricsKey = ""; ++lyricsRevision; lyricCancellation?.Cancel();
            ShowLyrics(null, "Видео · " + media.Artist); return;
        }
        if (!settings.LyricsEnabled || !media.Available)
        {
            requestedLyricsKey = ""; ++lyricsRevision; lyricCancellation?.Cancel();
            ShowLyrics(null, settings.LyricsEnabled ? "Текст песни появится при воспроизведении" : "Поиск текста выключен в настройках"); return;
        }
        var track = new LyricTrack(media.Title, media.Artist, media.Album, media.Duration);
        string requestKey = media.Source + "\n" + track.Key + "\n" + settings.YandexLyricsEnabled;
        if (!force && requestedLyricsKey == requestKey) return;
        requestedLyricsKey = requestKey; localLyrics = false;
        int revision = ++lyricsRevision;
        lyricCancellation?.Cancel(); lyricCancellation?.Dispose(); lyricCancellation = new CancellationTokenSource();
        var token = lyricCancellation.Token;
        if (!force && settings.YandexLyricsEnabled && media.Source == "ru.yandex.desktop.music" && yandexLyrics.TryGetCached(track) is LyricsResult cached)
        {
            ShowLyrics(cached, cached.Status); UpdateLyricPosition(media.Position); return;
        }
        BeginLyricsLoading();
        _ = FindTrackLyricsAsync(track, revision, token, force);
    }
    private async Task FindTrackLyricsAsync(LyricTrack track, int revision, CancellationToken token, bool force)
    {
        try
        {
            LyricsResult? result = null;
            if (settings.YandexLyricsEnabled && media.Source == "ru.yandex.desktop.music")
            {
                LyricsStatus.Text = "Получаем текст из Яндекс Музыки…";
                result = await yandexLyrics.FindAsync(track, token, bypassCache: force);
            }
            if (result == null) { if (revision == lyricsRevision) LyricsStatus.Text = "Ищем текст во внешних источниках…"; result = await lyricsService.FindAsync(track, token, force); }
            if (closing || token.IsCancellationRequested || revision != lyricsRevision || localLyrics) return;
            ShowLyrics(result, result.Status); UpdateLyricPosition(media.Position);
            if (!result.Synced && settings.YandexLyricsEnabled && media.Source == "ru.yandex.desktop.music")
                _ = UpgradeYandexLyricsAsync(track, revision, token);
        }
        catch (OperationCanceledException) { if (!token.IsCancellationRequested && revision == lyricsRevision && !closing) ShowLyrics(null, "Сервис текста не ответил. Повторите поиск в настройках музыки."); }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if (revision == lyricsRevision && !closing && !token.IsCancellationRequested) ShowLyrics(null, "Не удалось получить текст. Повторите поиск в настройках музыки.");
        }
    }
    private async Task UpgradeYandexLyricsAsync(LyricTrack track, int revision, CancellationToken token)
    {
        try
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                if (attempt >= 3 && !yandexLyrics.HasSyncedLyricsHint) return;
                int delay = attempt < 3 ? 1200 << attempt : 15000;
                await Task.Delay(delay, token);
                if (closing || revision != lyricsRevision || localLyrics || lyricsResult?.Synced == true) return;
                var timed = await yandexLyrics.FindAsync(track, token);
                if (closing || revision != lyricsRevision || localLyrics || token.IsCancellationRequested) return;
                if (timed?.Synced == true) { ShowLyrics(timed, timed.Status); UpdateLyricPosition(media.Position); return; }
            }
        }
        catch (OperationCanceledException) { }
    }
    private void BeginLyricsLoading()
    {
        lyricsPendingHadContent = PlayerHasLyrics;
        lyricsPendingWidth = (!expanded && !HasCompactNotification ? Width : StandbyCompactWidth) / (Math.Clamp(settings.CompactScale, 75, 150) / 100);
        ShowLyrics(null, "Ищем текст песни…", loading: true);
        RefreshIdleContent();
    }
    private void ShowLyrics(LyricsResult? result, string status, bool loading = false)
    {
        PrepareLyricsScrollForTrack();
        lyricsLoading = loading;
        if (!loading) lyricsReadyVisualKey = CurrentVisualKey;
        LyricMotion.Stop(LyricsScroll);
        lyricsResult = result; currentLyricIndex = -1; lyricViews.Clear(); lyricFills.Clear(); LyricsLines.Children.Clear();
        LyricsStatus.Text = result?.Synced == true ? "Текст песни" : !string.IsNullOrWhiteSpace(result?.PlainText) ? "Текст без таймкодов" : result != null ? "Текст песни не найден" : status.StartsWith("Не удалось", StringComparison.Ordinal) ? "Текст песни недоступен" : status;
        LyricsStatus.Visibility = PlayerHasLyrics ? Visibility.Collapsed : Visibility.Visible;
        LyricsStatus.ToolTip = status;
        if (result?.Synced == true)
        {
            foreach (var line in result.Lines)
            {
                var view = new TextBlock { Text = line.Text.Length == 0 ? " " : line.Text, FontSize = 16, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(181, 189, 198)), Margin = new Thickness(0, 8, 0, 8), LineHeight = 24 };
                System.Windows.Automation.AutomationProperties.SetAutomationId(view, "LyricLine-" + lyricViews.Count);
                System.Windows.Automation.AutomationProperties.SetHelpText(view, line.Seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                view.Cursor = CanSeekSelectedPlayer ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
                view.ToolTip = CanSeekSelectedPlayer ? "Перейти к " + FormatTime(line.Seconds) : "Плеер не разрешает перемотку";
                view.Focusable = true;
                view.MouseLeftButtonUp += async (_, e) => { e.Handled = true; await SeekLyricAsync(result, line); };
                view.KeyDown += async (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { e.Handled = true; await SeekLyricAsync(result, line); } };
                var row = new Grid();
                var fill = new KaraokeLine(view) { Margin = new Thickness(0, 8, 0, 8), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                row.Children.Add(view); row.Children.Add(fill);
                lyricViews.Add(view); lyricFills.Add(fill); LyricsLines.Children.Add(row);
            }
        }
        else if (!string.IsNullOrWhiteSpace(result?.PlainText)) LyricsLines.Children.Add(new TextBlock { Text = result.PlainText, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(188, 188, 188)), LineHeight = 22 });
        if (!manualLyricsScroll) LyricsScroll.ScrollToHome();
        if (!loading) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(TryCommitTrackVisual));
        if (!App.VerificationMode && IsLoaded && expanded && Navigation.SelectedIndex == 0 && media.Available) AnimateExpansion(true);
    }
    private void UpdateLyricPosition(double position)
    {
        UpdateTrackIntroduction();
        if (closing) return;
        if (media.Available && !seeking)
        {
            Timeline.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            Timeline.Value = Math.Min(Timeline.Maximum, position); Elapsed.Text = FormatTime(position);
        }
        if (lyricsResult?.Synced != true) return;
        int index = LyricsService.LineAt(lyricsResult.Lines, position, LyricPositionAdjustment);
        if (index == currentLyricIndex) { UpdateCompactLyricProgress(position, index); UpdateExpandedLyricProgress(position); return; }
        if (currentLyricIndex >= 0 && currentLyricIndex < lyricViews.Count)
        {
            lyricViews[currentLyricIndex].Foreground = new SolidColorBrush(Color.FromRgb(181, 189, 198)); lyricViews[currentLyricIndex].FontWeight = FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetItemStatus(lyricViews[currentLyricIndex], "");
            var previousFill = lyricFills[currentLyricIndex];
            if (settings.ReadAlong)
            {
                int read = index > currentLyricIndex ? lyricsResult.Lines[currentLyricIndex].Text.Length : 0;
                previousFill.TimingFrame = new ReadAlongFrame(-1, 0, read, 0, previousFill.TimingFrame?.FromSource == true);
                previousFill.WordPosition = -1;
            }
            Motion.Tween(previousFill, OpacityProperty, 0, 620, completed: () => { if (lyricFills.IndexOf(previousFill) != currentLyricIndex) previousFill.Visibility = Visibility.Collapsed; });
        }
        currentLyricIndex = index;
        if (index >= 0 && index < lyricViews.Count)
        {
            var current = lyricViews[index]; current.Foreground = new SolidColorBrush(settings.ReadAlong ? Color.FromRgb(116, 124, 132) : Color.FromRgb(181, 189, 198)); current.FontWeight = FontWeights.SemiBold;
            lyricFills[index].Visibility = Visibility.Visible;
            Motion.Tween(lyricFills[index], OpacityProperty, 1, 260);
            System.Windows.Automation.AutomationProperties.SetItemStatus(current, "Текущая строка");
            QueueCurrentLyricScroll();
        }
        RefreshIdleContent();
        UpdateCompactLyricProgress(position, index);
        UpdateExpandedLyricProgress(position);
    }
    private void UpdateExpandedLyricProgress(double position)
    {
        if (!expanded || Navigation.SelectedIndex != 0 || lyricsResult?.Synced != true || currentLyricIndex < 0 || currentLyricIndex >= lyricFills.Count) return;
        var line = lyricsResult.Lines[currentLyricIndex];
        double end = line.EndSeconds ?? (currentLyricIndex + 1 < lyricsResult.Lines.Length ? lyricsResult.Lines[currentLyricIndex + 1].Seconds : media.Duration);
        lyricFills[currentLyricIndex].ReadAlong = settings.ReadAlong;
        double time = position + settings.LyricsAdjustment + (settings.ReadAlong ? settings.ReadAlongAdjustment : 0);
        var frame = ReadAlongTiming.At(line, end, time);
        lyricFills[currentLyricIndex].TimingFrame = frame;
        lyricFills[currentLyricIndex].WordPosition = frame.Position;
        lyricFills[currentLyricIndex].Progress = end > line.Seconds ? (time - line.Seconds) / (end - line.Seconds) : 1;
        if (settings.ReadAlong) LyricsStatus.ToolTip = lyricsResult.Status + (line.Words?.Length > 0 ? "\nRead Along: метки слов из источника" : "\nRead Along: приблизительно по таймкодам строки");
    }
    private void QueueCurrentLyricScroll(bool immediate = false)
    {
        if (manualLyricsScroll) return;
        lyricScrollImmediate |= immediate;
        if (lyricScrollPending || closing || !expanded || Navigation.SelectedIndex != 0) return;
        lyricScrollPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            lyricScrollPending = false;
            bool snap = lyricScrollImmediate; lyricScrollImmediate = false;
            if (manualLyricsScroll || closing || !expanded || Navigation.SelectedIndex != 0 || !LyricsScroll.IsVisible || currentLyricIndex < 0 || currentLyricIndex >= lyricViews.Count) return;
            LyricsLines.UpdateLayout();
            double y = lyricViews[currentLyricIndex].TranslatePoint(new Point(0, 0), LyricsLines).Y;
            LyricMotion.ScrollTo(LyricsScroll, Math.Max(0, y - LyricsScroll.ViewportHeight * .35), snap);
        }));
    }
    private void RetryLyrics(object sender, RoutedEventArgs e) => RefreshTrackLyrics(true);
    private async Task SeekLyricAsync(LyricsResult expected, LyricLine line)
    {
        manualLyricsScroll = false; FollowLyricsButton.Visibility = Visibility.Collapsed;
        if (!ReferenceEquals(expected, lyricsResult) || !CanSeekSelectedPlayer) return;
        double position = Math.Clamp(line.Seconds - LyricPositionAdjustment, 0, Math.Max(0, media.Duration));
        if (await SeekSelectedPlayerAsync(position))
        {
            await media.RefreshAsync(); UpdateLyricPosition(media.Position);
            QueueCurrentLyricScroll(true);
        }
        else LyricsStatus.Text = "Плеер не выполнил перемотку. Повторите нажатие.";
    }
    private void OpenLyricsSettings(object sender, RoutedEventArgs e) => OpenPreferences(5);
    private void OpenLyricsFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Открыть текст с таймкодами для текущего трека", Filter = "Текст LRC (*.lrc)|*.lrc", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 2_000_000) { LyricsStatus.Text = "Файл слишком большой (предел — 2 МБ)"; return; }
            var lines = LyricsService.ParseLrc(File.ReadAllText(dialog.FileName));
            if (lines.Length == 0) { LyricsStatus.Text = "В файле не найдены таймкоды LRC"; return; }
            lyricCancellation?.Cancel(); ++lyricsRevision; localLyrics = true;
            ShowLyrics(new LyricsResult("Локальный LRC · синхронизация по времени плеера", lines), "Локальный LRC · синхронизация по времени плеера"); UpdateLyricPosition(media.Position);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LyricsStatus.Text = "Не удалось прочитать LRC: " + ex.Message; }
    }
    private void StopLyrics()
    {
        CompositionTarget.Rendering -= RenderCompactLyrics;
        LyricMotion.Stop(LyricsScroll);
        compactPlaybackPlaying = false; UpdateCompactEqualizer();
        lyricTimer.Stop(); ++lyricsRevision; lyricCancellation?.Cancel(); lyricCancellation?.Dispose();
    }
    internal async Task<string> ConnectYandexAsync()
    {
        settings.YandexLyricsEnabled = true; SaveSettings();
        string status = await yandexLyrics.ConnectPlayerAsync();
        await media.InitializeAsync(); await media.RefreshAsync();
        yandexPlayback = await yandexLyrics.PlaybackAsync();
        if (yandexPlayback != null) { yandexStateAt = Environment.TickCount64; media.UpdateNativePlayback(yandexPlayback); UpdateMedia(); }
        RefreshVolume(); UpdateYandexRating(); RefreshTrackLyrics(true);
        if (yandexPlayback == null) return status + " Ожидаем доступ к управлению; повторные проверки выполняются автоматически.";
        return yandexPlayback.CanLike && yandexPlayback.CanDislike ? "Подключено: звук, перемотка, лайк и дизлайк доступны." : "Плеер подключён. Кнопки оценки пока недоступны: включите обычный трек и откройте панель управления Яндекс Музыки.";
    }
}
