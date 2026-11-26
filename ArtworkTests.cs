using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIsland.Services;

namespace DesktopIsland;

internal static class ArtworkTests
{
    internal static async Task RunAsync(Action<string, bool, string> check)
    {
        byte[] Image(byte red)
        {
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, red, 255 }, 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }
        byte Pixel(BitmapSource? image) { if (image == null) return 0; var pixel = new byte[4]; image.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0); return pixel[2]; }
        YandexLyricsBridge.PlaybackState State(string id, string title) => new(10, .5, id, false, false, true, true, title, "Artist", 120, true, "https://avatars.yandex.net/" + id);
        async Task Drain() { for (int i = 0; i < 8; i++) await Task.Delay(10); }
        using var media = new MediaService();
        var first = new TaskCompletionSource<byte[]>();
        var second = new TaskCompletionSource<byte[]>();
        media.FetchArtwork = (uri, _) => uri.AbsolutePath == "/a" ? first.Task : second.Task;
        media.UpdateNativePlayback(State("a", "First"));
        media.UpdateNativePlayback(State("b", "Second"));
        second.SetResult(Image(200)); await Drain();
        check("current track commits its matching cover", Pixel(media.Artwork) == 200 && media.Title == "Second", "");
        first.SetResult(Image(40)); await Drain();
        check("late artwork from the previous track never overwrites the current cover", Pixel(media.Artwork) == 200 && media.Title == "Second", "");
        var pending = new TaskCompletionSource<byte[]>(); media.FetchArtwork = (_, _) => pending.Task;
        media.UpdateNativePlayback(State("c", "Third"));
        check("a new track never displays the previous track cover while loading", media.Artwork == null && !media.ArtworkReady, "");
        media.UpdateNativePlayback(State("d", "Fourth") with { ArtworkUrl = "" });
        pending.SetResult(Image(70)); await Drain();
        check("a track without a cover cannot receive a previous pending image", media.Artwork == null && media.Title == "Fourth", "");
        int attempts = 0; long now = 1000;
        media.ArtworkNow = () => now;
        media.FetchArtwork = (_, _) => ++attempts == 1 ? Task.FromException<byte[]>(new System.Net.Http.HttpRequestException("fixture")) : Task.FromResult(Image(150));
        var retry = State("retry", "Retry"); media.UpdateNativePlayback(retry); await Drain();
        media.UpdateNativePlayback(retry);
        check("failed cover requests are throttled rather than retried every media poll", attempts == 1 && media.ArtworkReady && media.Artwork == null, "");
        now += 2001; media.UpdateNativePlayback(retry); await Drain();
        check("failed covers recover automatically on the same track without a restart", attempts == 2 && Pixel(media.Artwork) == 150, "");
        media.UpdateNativePlayback(retry with { Title = "Changed title", ArtworkUrl = "" });
        check("reused track IDs with changed metadata clear stale artwork", media.Artwork == null && media.Title == "Changed title", "");
    }
}
