using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private async Task CaptureShowcaseAsync()
    {
        string output = Environment.GetCommandLineArgs().SkipWhile(x => x != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "OSTROVOK-showcase");
        Directory.CreateDirectory(output);
        try
        {
            timer.Stop(); lyricTimer.Stop(); leaveTimer.Stop(); IsHitTestVisible = false;
            media.Updated -= UpdateMedia; media.Dispose();
            CompositionTarget.Rendering -= RenderCompactLyrics;
            Motion.VerificationOverride = true;
            settings.Detached = true; settings.CompactWidthOverride = 480; settings.PlayerHeight = 618;
            settings.InterfaceFont = InterfaceFonts.Aminahs; settings.TelegramNotificationsEnabled = true;
            Left = 100; Top = 100; Pin.IsChecked = true;
            ApplyShellAppearance(); RefreshIdleContent(); await Task.Delay(500);
            Capture(Path.Combine(output, "idle.png"));

            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(46, 68, 64)), null, new Rect(0, 0, 240, 240));
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(22, 29, 28)), null, new Point(120, 120), 86, 86);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(83, 101, 96)), 1), new Point(120, 120), 68, 68);
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(83, 101, 96)), 1), new Point(120, 120), 54, 54);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(200, 208, 177)), null, new Point(120, 120), 26, 26);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(22, 29, 28)), null, new Point(120, 120), 5, 5);
            }
            var cover = new RenderTargetBitmap(240, 240, 96, 96, PixelFormats.Pbgra32); cover.Render(drawing); cover.Freeze();
            var coverEncoder = new PngBitmapEncoder(); coverEncoder.Frames.Add(BitmapFrame.Create(cover));
            using var coverStream = new MemoryStream(); coverEncoder.Save(coverStream); coverStream.Position = 0;
            var coverImage = new BitmapImage(); coverImage.BeginInit(); coverImage.CacheOption = BitmapCacheOption.OnLoad; coverImage.StreamSource = coverStream; coverImage.EndInit(); coverImage.Freeze();
            string trackTitle = "Вечерний маршрут", trackArtist = "OSTROVOK · демо";
            double trackDuration = 184, excerptStart = 0;
            var lyrics = new LyricsResult("Демонстрационный текст", [new(0, "Город за окном становится тише"), new(3, "Свет фонарей остаётся в стекле"), new(6, "Музыка рядом, пока ты в пути"), new(9, "Вечерний маршрут продолжается"), new(12, "И каждый поворот звучит по-своему"), new(15, "Мы оставляем день позади"), new(18, "Город за окном становится тише")]);
            string? trackFile = Environment.GetCommandLineArgs().SkipWhile(x => x != "--showcase-track").Skip(1).FirstOrDefault();
            if (trackFile != null)
            {
                using var fixture = JsonDocument.Parse(File.ReadAllText(trackFile)); var track = fixture.RootElement;
                trackTitle = track.GetProperty("Title").GetString()!; trackArtist = track.GetProperty("Artist").GetString()!; trackDuration = track.GetProperty("Duration").GetDouble();
                coverImage = new BitmapImage(); coverImage.BeginInit(); coverImage.CacheOption = BitmapCacheOption.OnLoad; coverImage.UriSource = new Uri(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(trackFile))!, track.GetProperty("Cover").GetString()!)); coverImage.EndInit(); coverImage.Freeze();
                var lines = track.GetProperty("Lines").EnumerateArray().Select(x => new LyricLine(x.GetProperty("Seconds").GetDouble(), x.GetProperty("Text").GetString()!, x.TryGetProperty("EndSeconds", out var end) ? end.GetDouble() : null)).ToArray();
                lyrics = new LyricsResult("Короткий фрагмент текста", lines); excerptStart = lines.FirstOrDefault()?.Seconds ?? 0;
                settings.PlayerHeight = 480;
            }
            typeof(MediaService).GetProperty(nameof(media.Source))!.SetValue(media, "ru.yandex.desktop.music");
            yandexPlayback = new YandexLyricsBridge.PlaybackState(0, .64, "showcase", true, false, true, true, trackTitle, trackArtist, trackDuration, true);
            media.UpdateNativePlayback(yandexPlayback);
            typeof(MediaService).GetProperty(nameof(media.Artwork))!.SetValue(media, coverImage);
            typeof(MediaService).GetProperty(nameof(media.ArtworkReady))!.SetValue(media, true);
            UpdateMedia();
            ShowLyrics(lyrics, lyrics.Status); UpdateLyricPosition(0); RefreshIdleContent();
            updatingVolume = true; Volume.Value = 64; VolumeText.Text = "64%"; updatingVolume = false;
            await Task.Delay(1200); UpdateTrackIntroduction(); RefreshIdleContent();
            Capture(Path.Combine(output, "compact.png"));

            await Record("expansion", 110, trackFile == null ? 700 : 560, frame =>
            {
                if (frame == 15) SetExpanded(true);
                if (frame == 85) SetExpanded(false);
                if (expanded) { HomePanel.Visibility = Visibility.Collapsed; MediaPanel.Visibility = Visibility.Visible; }
            });
            SetExpanded(true); await AwaitShellAsync();
            HomePanel.Visibility = Visibility.Collapsed; MediaPanel.Visibility = Visibility.Visible;
            UpdateLyricPosition(excerptStart + 1); UpdateLayout(); Capture(Path.Combine(output, "player.png"));
            await Record("lyrics", 150, trackFile == null ? 700 : 560, frame =>
            {
                double position = excerptStart + frame / 25d;
                UpdateLyricPosition(position); UpdateExpandedLyricProgress(position);
                Elapsed.Text = FormatTime(position); Timeline.Value = position;
            });
            SetExpanded(false); await AwaitShellAsync();
            await Record("notification", 115, 180, frame =>
            {
                if (frame == 15) ReceiveTelegramNotification(new TelegramMessage(1, "Команда OSTROVOK", "Демонстрация уведомления", DateTimeOffset.Now));
                if (frame == 45) Capture(Path.Combine(output, "notification.png"));
                if (frame == 90) DismissTelegramNotice(this, new RoutedEventArgs());
            });
            File.WriteAllText(Path.Combine(output, "complete.txt"), "Actual WPF interface; demonstration track, original artwork and test notification; no personal data.");

            async Task Record(string name, int count, int canvasHeight, Action<int> update)
            {
                string frames = Path.Combine(output, name); Directory.CreateDirectory(frames);
                for (int i = 0; i < count; i++)
                {
                    update(i); await Task.Delay(40); UpdateLayout();
                    var native = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(ActualWidth)), Math.Max(1, (int)Math.Ceiling(ActualHeight)), 96, 96, PixelFormats.Pbgra32);
                    native.Render((Visual)Content);
                    var scene = new DrawingVisual();
                    using (var dc = scene.RenderOpen())
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 18, 20)), null, new Rect(0, 0, 720, canvasHeight));
                        dc.DrawImage(native, new Rect((720 - ActualWidth) / 2, 24, ActualWidth, ActualHeight));
                    }
                    var bitmap = new RenderTargetBitmap(720, canvasHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(scene);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(frames, $"{i:0000}.png")); encoder.Save(stream);
                }
            }
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); }
        finally { Close(); System.Windows.Application.Current.Shutdown(); }
    }
}
