using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace DesktopIsland.Services;

internal sealed class YouTubeBrowserBridge
{
    internal sealed record Video(string Id, string Title, string Artist, bool Playing, double Position, double Duration, long Received);
    internal static readonly YouTubeBrowserBridge Shared = new();
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly BrowserConnectionPolicy connectionPolicy = new();
    private readonly Dictionary<string, string> sessions = [];
    private readonly Dictionary<string, long> pageRetryAt = [];
    private YandexLyricsBridge.CdpConnection? browser;
    private Task<YandexLyricsBridge.CdpConnection>? connecting;
    private CancellationTokenSource? approval;
    private Video? cached;
    private string? selectedTarget, selectedSession;
    private long sampledAt;
    private static readonly Lazy<string> controls = new(() =>
    {
        using var stream = typeof(YouTubeBrowserBridge).Assembly.GetManifestResourceStream("DesktopIsland.Services.YouTubeControls.js")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    });
    internal string Status { get; private set; } = "YouTube: подключите через меню плеера";
    internal string? LastCommandStatus { get; private set; }
    internal Video? LastVideo => cached;
    internal bool IsConnecting => connecting != null;
    internal bool IsConnected => browser != null;
    internal int ConnectionAttempts { get; private set; }
    internal int AttachedPages => sessions.Count;
    internal void RequestConnection()
    {
        if (browser != null || connecting != null) return;
        connectionPolicy.Request(); sampledAt = 0; Status = "Ожидаем браузер";
    }
    internal bool CanControl(string id) => browser != null && selectedSession != null && cached?.Id == id && Environment.TickCount64 - cached.Received < 8000;
    internal static string ToggleExpression(string id, bool playing = true)
    {
        if (!Regex.IsMatch(id, "^[a-zA-Z0-9_-]{11}$")) throw new ArgumentException("Invalid video ID", nameof(id));
        return controls.Value + "(" + JsonSerializer.Serialize(id) + "," + (playing ? "true" : "false") + ")";
    }
    internal static Uri? ParseEndpoint(string[] lines)
    {
        if (lines.Length < 2 || !int.TryParse(lines[0], out int port) || port is < 1024 or > 65535 || !Regex.IsMatch(lines[1], "^/devtools/browser/[a-zA-Z0-9-]+$")) return null;
        return new Uri($"ws://127.0.0.1:{port}{lines[1]}");
    }
    private static Uri? DiscoverEndpoint()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string path = Path.Combine(root, "Yandex", "YandexBrowser", "User Data", "DevToolsActivePort");
        try { return File.Exists(path) ? ParseEndpoint(File.ReadAllLines(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    private static async Task<YandexLyricsBridge.CdpConnection> ConnectAsync(Uri endpoint, CancellationToken token)
    {
        var connection = new YandexLyricsBridge.CdpConnection();
        try
        {
            await connection.OpenAsync(endpoint, token);
            await connection.CallAsync("Browser.getVersion", new { }, token);
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    internal static bool IsYouTubePage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var page) && page.Scheme == "https" && (page.Host is "www.youtube.com" or "youtube.com" or "m.youtube.com") && (page.AbsolutePath == "/watch" || page.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal));
    internal static Video? Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        string id = data.GetProperty("id").GetString() ?? "", title = data.GetProperty("title").GetString() ?? "", artist = data.GetProperty("artist").GetString() ?? "";
        if (!Regex.IsMatch(id, "^[a-zA-Z0-9_-]{11}$") || title.Length is < 1 or > 1000 || artist.Length > 300) return null;
        double duration = data.GetProperty("duration").GetDouble(), position = data.GetProperty("position").GetDouble();
        if (!double.IsFinite(duration) || !double.IsFinite(position) || duration < 0 || position < 0) return null;
        return new(id, title, artist, data.GetProperty("playing").GetBoolean(), position, duration, Environment.TickCount64);
    }
    private void CloseConnection()
    {
        connectionPolicy.Disable();
        approval?.Cancel(); approval?.Dispose(); approval = null;
        browser?.Dispose(); browser = null;
        sessions.Clear(); pageRetryAt.Clear(); cached = null; selectedSession = selectedTarget = null;
    }
    internal async Task DisconnectAsync()
    {
        approval?.Cancel();
        await operations.WaitAsync();
        try
        {
            if (connecting != null) { try { (await connecting).Dispose(); } catch (Exception) { } connecting = null; }
            CloseConnection(); Status = "YouTube: подключение выключено";
        }
        finally { operations.Release(); }
    }
    private async Task DropPageAsync(string target, CancellationToken token)
    {
        if (!sessions.Remove(target, out string? session)) return;
        if (selectedTarget == target) selectedTarget = selectedSession = null;
        if (browser != null)
        {
            try { await browser.CallAsync("Target.detachFromTarget", new { sessionId = session }, token); }
            catch (InvalidOperationException) { }
        }
    }
    internal async Task<Video?> ReadAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (Environment.TickCount64 - sampledAt < 1500) return cached;
            sampledAt = Environment.TickCount64;
            if (browser == null)
            {
                if (connecting == null)
                {
                    var endpoint = DiscoverEndpoint();
                    if (endpoint == null || !connectionPolicy.TryStart()) return null;
                    approval = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    ConnectionAttempts++;
                    connecting = ConnectAsync(endpoint, approval.Token);
                }
                if (!connecting.IsCompleted) { Status = "Подтвердите подключение OSTROVOK в браузере"; return null; }
                var attempt = connecting; connecting = null;
                browser = await attempt;
                approval?.Dispose(); approval = null;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var reply = await browser.CallAsync("Target.getTargets", new { }, deadline.Token);
            var pages = reply.GetProperty("targetInfos").EnumerateArray()
                .Where(x => x.GetProperty("type").GetString() == "page" && IsYouTubePage(x.GetProperty("url").GetString() ?? ""))
                .OrderByDescending(x => x.GetProperty("targetId").GetString() == selectedTarget).ToArray();
            var liveIds = pages.Select(x => x.GetProperty("targetId").GetString()!).ToHashSet();
            foreach (string id in sessions.Keys.Where(x => !liveIds.Contains(x)).ToArray()) await DropPageAsync(id, deadline.Token);
            foreach (string id in pageRetryAt.Keys.Where(x => !liveIds.Contains(x)).ToArray()) pageRetryAt.Remove(id);
            Video? found = null;
            string? foundTarget = null, foundSession = null;
            foreach (var page in pages)
            {
                string id = page.GetProperty("targetId").GetString()!;
                if (pageRetryAt.TryGetValue(id, out long retry) && Environment.TickCount64 < retry) continue;
                try
                {
                    if (!sessions.TryGetValue(id, out string? session))
                    {
                        if (sessions.Count >= 8)
                            await DropPageAsync(sessions.Keys.First(x => x != selectedTarget), deadline.Token);
                        session = (await browser.CallAsync("Target.attachToTarget", new { targetId = id, flatten = true }, deadline.Token)).GetProperty("sessionId").GetString()!;
                        sessions[id] = session;
                    }
                    var data = await browser.EvaluateAsync("""
                        (() => {
                          const v = document.querySelector('video.html5-main-video');
                          const id = new URL(location.href).searchParams.get('v') || location.pathname.match(/^\/shorts\/([\w-]{11})/)?.[1];
                          if (!v || !id) return null;
                          return {id, title: document.querySelector('ytd-watch-metadata h1')?.textContent?.trim() || document.title.replace(/ - YouTube$/, ''),
                            artist: document.querySelector('#owner #channel-name a')?.textContent?.trim() || 'YouTube',
                            playing: !v.paused && !v.ended, position: v.currentTime, duration: Number.isFinite(v.duration) ? v.duration : 0};
                        })()
                        """, deadline.Token, session);
                    var video = Parse(data);
                    if (video != null && (found == null || video.Playing)) { found = video; foundTarget = id; foundSession = session; }
                    if (video?.Playing == true) break;
                }
                catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException)
                {
                    await DropPageAsync(id, deadline.Token);
                    pageRetryAt[id] = Environment.TickCount64 + 30000;
                }
            }
            cached = found; selectedTarget = foundTarget; selectedSession = foundSession;
            Status = found == null ? "Нет видео YouTube" : "YouTube подключён";
            return cached;
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            CloseConnection(); Status = "Связь с YouTube прервана. Подключите через меню плеера.";
            return null;
        }
        finally { operations.Release(); }
    }
    internal async Task<bool> SetPlayingAsync(string expectedId, bool playing)
    {
        string? target = selectedTarget;
        LastCommandStatus = playing ? "Request: play" : "Request: pause";
        await operations.WaitAsync();
        try
        {
            if (!CanControl(expectedId) || target != selectedTarget) { LastCommandStatus = "Selection changed"; return false; }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var reply = await browser!.EvaluateAsync(ToggleExpression(expectedId, playing), deadline.Token, selectedSession, userGesture: true);
            if (!reply.GetProperty("ok").GetBoolean()) { LastCommandStatus = "Player rejected command"; return false; }
            cached = cached! with { Playing = reply.GetProperty("playing").GetBoolean(), Position = reply.GetProperty("position").GetDouble(), Duration = reply.GetProperty("duration").GetDouble(), Received = Environment.TickCount64 };
            sampledAt = Environment.TickCount64;
            LastCommandStatus = cached.Playing ? "Applied: playing" : "Applied: paused";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException)
        {
            if (target != null) { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2)); try { await DropPageAsync(target, deadline.Token); } catch (Exception) { } }
            LastCommandStatus = "Video tab changed"; sampledAt = 0; return false;
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        { CloseConnection(); LastCommandStatus = "Connection closed"; Status = "Связь с YouTube прервана. Подключите через меню плеера."; return false; }
        finally { operations.Release(); }
    }
}
