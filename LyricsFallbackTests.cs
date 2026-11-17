using System.Net;
using System.Net.Http;
using System.Threading;
using DesktopIsland.Services;

namespace DesktopIsland;

internal static class LyricsFallbackTests
{
    private const string Synced = """{"id":"backup-record","trackName":"Example","artistName":"Artist","duration":200,"syncedLyrics":"[00:01.00]Тестовая строка","plainLyrics":""}""";
    private const string Plain = """{"id":1,"trackName":"Example","artistName":"Artist","duration":200,"syncedLyrics":null,"plainLyrics":"Тестовый текст"}""";
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private static HttpResponseMessage Missing() => new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
    public static async Task RunAsync(Action<string, bool, string> check)
    {
        var track = new LyricTrack("Example", "Artist", "", 200);
        using (var client = new HttpClient(new Handler(request => request.RequestUri!.Host == "lrclib.net" ? request.RequestUri.AbsolutePath.EndsWith("/search") ? Json("[]") : Missing() : Json(Synced))))
        {
            var result = await new LyricsService(client).FindAsync(track);
            check("missing primary lyrics use backup source", result.Synced && result.Status.Contains("lrcmux") && result.RecordId == null, "");
        }
        using (var client = new HttpClient(new Handler(request => request.RequestUri!.Host == "lrclib.net" ? throw new HttpRequestException("test outage") : Json(Synced))))
        {
            var result = await new LyricsService(client).FindAsync(track);
            check("primary network outage still reaches backup", result.Synced, "");
        }
        using (var client = new HttpClient(new Handler(request => Json(request.RequestUri!.Host == "lrclib.net" ? Plain : Synced))))
        {
            var result = await new LyricsService(client).FindAsync(track);
            check("backup timed lyrics preferred over primary plain text", result.Synced && result.Status.Contains("lrcmux"), "");
        }
        using (var client = new HttpClient(new Handler(request => request.RequestUri!.Host == "lrclib.net" ? Json(Plain) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
        {
            var result = await new LyricsService(client).FindAsync(track);
            check("backup outage preserves available plain lyrics", !result.Synced && result.PlainText == "Тестовый текст", "");
        }
        using (var client = new HttpClient(new Handler(request => request.RequestUri!.Host == "lrclib.net" ? request.RequestUri.AbsolutePath.EndsWith("/search") ? Json("[]") : Missing() : Json(Synced.Replace("\"Artist\"", "\"Other\"")))))
        {
            var result = await new LyricsService(client).FindAsync(track);
            check("backup cannot substitute a different artist", !result.Synced && result.PlainText.Length == 0, "");
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
