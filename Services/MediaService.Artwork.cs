using System.Net.Http;
using System.Windows.Media.Imaging;

namespace DesktopIsland.Services;

public sealed partial class MediaService
{
    private static readonly HttpClient artworkHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string requestedArtwork = "";
    private async void LoadYouTubeArtwork(string id)
    {
        try
        {
            var bytes = await artworkHttp.GetByteArrayAsync("https://i.ytimg.com/vi/" + id + "/hqdefault.jpg");
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 240; image.StreamSource = stream; image.EndInit(); image.Freeze();
            if (disposed || Source != "youtube.bridge" || youtubeId != id) return;
            Artwork = image;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or NotSupportedException or ArgumentException) { }
        finally { if (!disposed && Source == "youtube.bridge" && youtubeId == id) { ArtworkReady = true; Updated?.Invoke(); } }
    }
    private async void LoadNativeArtwork(YandexLyricsBridge.PlaybackState state)
    {
        string request = state.TrackId + "\n" + state.ArtworkUrl;
        if (requestedArtwork == request) return;
        if (!Uri.TryCreate(state.ArtworkUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.Host.EndsWith(".yandex.net", StringComparison.OrdinalIgnoreCase)) { ArtworkReady = true; return; }
        requestedArtwork = request;
        try
        {
            var bytes = await artworkHttp.GetByteArrayAsync(uri);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 240; image.StreamSource = stream; image.EndInit(); image.Freeze();
            if (disposed || nativeTrackId != state.TrackId || !string.Equals(Title, state.Title, StringComparison.OrdinalIgnoreCase)) return;
            Artwork = image; ArtworkReady = true; Updated?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        { if (requestedArtwork == request) { ArtworkReady = true; Updated?.Invoke(); } }
    }
}
