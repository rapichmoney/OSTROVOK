using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace DesktopIsland.Services;

internal sealed class YouTubeBrowserBridge
{
    internal sealed record Video(string Id, string Title, string Artist, bool Playing, double Position, double Duration, long Received);
    internal static readonly YouTubeBrowserBridge Shared = new();
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(600) };
    private Video? cached;
    private long sampledAt;
    internal string Status { get; private set; } = "Ожидаем браузер";
    internal static bool IsYouTubePage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var page) && page.Scheme == "https" && (page.Host is "www.youtube.com" or "youtube.com" or "m.youtube.com") && (page.AbsolutePath == "/watch" || page.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal));
    internal static Video? Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        string id = data.GetProperty("id").GetString() ?? "";
        string title = data.GetProperty("title").GetString() ?? "";
        string artist = data.GetProperty("artist").GetString() ?? "";
        if (!Regex.IsMatch(id, "^[a-zA-Z0-9_-]{11}$") || title.Length is < 1 or > 1000 || artist.Length > 300) return null;
        double duration = data.GetProperty("duration").GetDouble(), position = data.GetProperty("position").GetDouble();
        if (!double.IsFinite(duration) || !double.IsFinite(position) || duration < 0 || position < 0) return null;
        return new(id, title, artist, data.GetProperty("playing").GetBoolean(), position, duration, Environment.TickCount64);
    }
    internal async Task<Video?> ReadAsync()
    {
        if (Environment.TickCount64 - sampledAt < 1500) return cached;
        sampledAt = Environment.TickCount64;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var targets = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:9227/json/list", deadline.Token));
            Video? found = null;
            foreach (var target in targets.RootElement.EnumerateArray())
            {
                if (target.GetProperty("type").GetString() != "page" || !IsYouTubePage(target.GetProperty("url").GetString() ?? "")) continue;
                if (!Uri.TryCreate(target.GetProperty("webSocketDebuggerUrl").GetString(), UriKind.Absolute, out var socket) || socket.Scheme != "ws" || socket.Host != "127.0.0.1" || socket.Port != 9227) continue;
                using var connection = new YandexLyricsBridge.CdpConnection();
                await connection.OpenAsync(socket, deadline.Token);
                var data = await connection.EvaluateAsync("""
                    (() => {
                      const v = document.querySelector('video.html5-main-video');
                      const id = new URL(location.href).searchParams.get('v') || location.pathname.match(/^\/shorts\/([\w-]{11})/)?.[1];
                      if (!v || !id) return null;
                      return {id, title: document.querySelector('ytd-watch-metadata h1')?.textContent?.trim() || document.title.replace(/ - YouTube$/, ''),
                        artist: document.querySelector('#owner #channel-name a')?.textContent?.trim() || 'YouTube',
                        playing: !v.paused && !v.ended, position: v.currentTime, duration: Number.isFinite(v.duration) ? v.duration : 0};
                    })()
                    """, deadline.Token);
                var video = Parse(data);
                if (video != null && (found == null || video.Playing)) found = video;
                if (video?.Playing == true) break;
            }
            cached = found; Status = found == null ? "Нет видео YouTube" : "YouTube подключён";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or WebSocketException or JsonException or InvalidOperationException or KeyNotFoundException)
        { Status = "Ожидаем подключение браузера"; if (cached != null && Environment.TickCount64 - cached.Received > 8000) cached = null; }
        return cached;
    }
}
