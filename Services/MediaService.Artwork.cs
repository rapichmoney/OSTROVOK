using System.Net.Http;
using System.Windows.Media.Imaging;
using System.Threading;

namespace DesktopIsland.Services;

public sealed partial class MediaService
{
    private static readonly HttpClient artworkHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string requestedArtwork = "";
    private string artworkOwner = "", completedNativeArtwork = "";
    private long artworkRevision, nextArtworkRetry;
    private int artworkFailures;
    private bool nativeArtworkLoading;
    private CancellationTokenSource? artworkCancellation;
    internal Func<Uri, CancellationToken, Task<byte[]>> FetchArtwork { get; set; } = (uri, token) => artworkHttp.GetByteArrayAsync(uri, token);
    internal Func<long> ArtworkNow { get; set; } = () => Environment.TickCount64;
    private string CurrentArtworkOwner => Source + "\n" + Title + "\n" + Artist;
    private void SelectArtworkOwner(string owner, bool force = false)
    {
        if (!force && artworkOwner == owner) return;
        artworkOwner = owner; ++artworkRevision;
        artworkCancellation?.Cancel(); artworkCancellation?.Dispose(); artworkCancellation = null;
        requestedArtwork = completedNativeArtwork = ""; nativeArtworkLoading = false;
        nextArtworkRetry = 0; artworkFailures = 0;
        Artwork = null; ArtworkReady = true;
    }
    private bool PreferNativeArtwork => Source == "ru.yandex.desktop.music" && lastNativeState is { } state && state.TrackId == nativeTrackId && state.Title == Title && state.Artist == Artist && ValidNativeArtwork(state.ArtworkUrl);
    private static bool ValidNativeArtwork(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host.EndsWith(".yandex.net", StringComparison.OrdinalIgnoreCase);
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
        string owner = CurrentArtworkOwner;
        string request = state.TrackId + "\n" + owner + "\n" + state.ArtworkUrl;
        if (!ValidNativeArtwork(state.ArtworkUrl)) { if (!nativeArtworkLoading) ArtworkReady = true; return; }
        if (completedNativeArtwork == request || requestedArtwork == request && (nativeArtworkLoading || ArtworkNow() < nextArtworkRetry)) return;
        artworkCancellation?.Cancel(); artworkCancellation?.Dispose();
        artworkCancellation = new CancellationTokenSource();
        var token = artworkCancellation.Token;
        long revision = ++artworkRevision;
        requestedArtwork = request;
        if (completedNativeArtwork.Length == 0) Artwork = null;
        nativeArtworkLoading = true;
        bool current() => !disposed && revision == artworkRevision && requestedArtwork == request && artworkOwner == owner && Source == "ru.yandex.desktop.music" && nativeTrackId == state.TrackId && Title == state.Title && Artist == state.Artist;
        try
        {
            var bytes = await FetchArtwork(new Uri(state.ArtworkUrl), token);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 240; image.StreamSource = stream; image.EndInit(); image.Freeze();
            if (!current()) return;
            completedNativeArtwork = request; artworkFailures = 0;
            Artwork = image; ArtworkReady = true; Updated?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            if (current())
            {
                nextArtworkRetry = ArtworkNow() + Math.Min(30000, 2000 * (1 << Math.Min(4, artworkFailures++)));
                ArtworkReady = true; Updated?.Invoke();
            }
        }
        finally { if (current()) nativeArtworkLoading = false; }
    }
}
