using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private async Task CaptureUpdateShowcaseAsync()
    {
        string output = Environment.GetCommandLineArgs().SkipWhile(x => x != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "OSTROVOK-update");
        Directory.CreateDirectory(output);
        try
        {
            timer.Stop(); lyricTimer.Stop(); leaveTimer.Stop(); media.Updated -= UpdateMedia; media.Dispose();
            Motion.VerificationOverride = true; settings.Detached = true; settings.PlayerHeight = 620;
            settings.InterfaceFont = InterfaceFonts.Aminahs; settings.ReadAlong = true; settings.CompactWidthOverride = 500;
            Pin.IsChecked = true; Left = Top = 80; ApplyShellAppearance();
            var colors = new[] { Color.FromRgb(69, 103, 116), Color.FromRgb(109, 88, 70), Color.FromRgb(70, 104, 87) };
            BitmapImage Cover(int n)
            {
                var scene = new DrawingVisual();
                using (var dc = scene.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(colors[n % 3]), null, new Rect(0, 0, 120, 120));
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(24, 28, 31)), null, new Point(60, 60), 43, 43);
                    dc.DrawEllipse(new SolidColorBrush(colors[n % 3]), null, new Point(60, 60), 14, 14);
                    dc.DrawEllipse(Brushes.WhiteSmoke, null, new Point(60, 60), 3, 3);
                }
                var bitmap = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(scene);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
            }
            string[] names = ["Тихий город", "Вечерний маршрут", "Новый день"];
            void Track(int n)
            {
                media.UpdateNativePlayback(new YandexLyricsBridge.PlaybackState(0, .5, "demo-" + n, false, false, true, true, names[n % 3], "OSTROVOK · демо", 120, true));
                typeof(MediaService).GetProperty(nameof(media.Artwork))!.SetValue(media, Cover(n));
                UpdateMedia();
            }
            Track(0);
            var lyrics = new LyricsResult("Демонстрационные метки слов", [
                new(0,"Город за окном становится тише",4.5,[new(0,0,5),new(.9,6,2),new(1.4,9,5),new(2.2,15,10),new(3.4,26,4)]),
                new(4.5,"Мы продолжаем путь без остановок",9,[new(4.5,0,2),new(5.1,3,10),new(6.2,14,4),new(7.1,19,3),new(7.7,23,9)]),
                new(9,"Свет остаётся рядом с нами",13.5,[new(9,0,4),new(9.8,5,8),new(10.9,14,5),new(11.7,20,1),new(12.1,22,4)])]);
            ShowLyrics(lyrics, lyrics.Status); UpdateLyricPosition(0); RefreshIdleContent();
            await Task.Delay(1200); UpdateTrackIntroduction();
            AnimateExpansion(true); await AwaitShellAsync();
            await Record("read-along-expanded", 220, 710, frame => UpdateLyricPosition(frame * .045));
            SetExpanded(false); await AwaitShellAsync();
            await Record("read-along-compact", 220, 160, frame => { double t = frame * .045; UpdateLyricPosition(t); RefreshIdleContent(); UpdateCompactLyricProgress(t, LyricsService.LineAt(lyrics.Lines, t)); });
            SetExpanded(true); await AwaitShellAsync();
            await Record("track-switching", 145, 710, frame =>
            {
                if (frame is 15 or 65 or 72 or 79 or 110) { Track(frame % 3); ShowLyrics(null, "Ищем текст во внешних источниках…", loading: true); }
                if (frame is 45 or 95 or 130) { ShowLyrics(lyrics, lyrics.Status); UpdateLyricPosition(1); }
            });
            await Record("collapse", 130, 710, frame => { if (frame == 15 || frame == 85) SetExpanded(false); if (frame == 50) SetExpanded(true); });
            await Record("update-notice",100,160,frame =>
            {
                if (frame != 15) return;
                availableRelease = new AppRelease(new Version(1,0,20),new Uri("https://example.test/archive"),2048,"",new Uri("https://example.test/checksum"));
                updateDetail = "Настройки сохранятся";
                UpdateCompactNotification(true);
            });
            availableRelease = null;
            File.WriteAllText(Path.Combine(output,"complete.txt"), "Actual WPF controls. Synthetic tracks, original artwork and original text. No external messages, clipboard or audio.");

            async Task Record(string name, int count, int height, Action<int> update)
            {
                string frames = Path.Combine(output, name); Directory.CreateDirectory(frames);
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                {
                    update(i); UpdateLayout();
                    var native = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(ActualWidth)), Math.Max(1, (int)Math.Ceiling(ActualHeight)), 96, 96, PixelFormats.Pbgra32); native.Render((Visual)Content);
                    var scene = new DrawingVisual();
                    using (var dc = scene.RenderOpen())
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15,18,20)),null,new Rect(0,0,720,height));
                        var caption = new FormattedText(name == "update-notice" ? "Уведомление об обновлении · демонстрация" : name.StartsWith("read-along") ? "Read Along · демонстрационные метки слов" : name == "collapse" ? "Сворачивание · интерфейс OSTROVOK" : "Смена треков · тестовая очередь", CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.LightGray,1);
                        dc.DrawText(caption,new Point(24,12)); dc.DrawImage(native,new Rect((720-ActualWidth)/2,48,ActualWidth,ActualHeight));
                    }
                    var image = new RenderTargetBitmap(720,height,96,96,PixelFormats.Pbgra32); image.Render(scene);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using(var file = File.Create(Path.Combine(frames,$"{i:0000}.png"))) encoder.Save(file);
                    int delay = (int)((i + 1) * 40 - clock.ElapsedMilliseconds); if (delay > 0) await Task.Delay(delay); else await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString()); }
        finally { Close(); Application.Current.Shutdown(); }
    }
}
