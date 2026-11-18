using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace DesktopIsland.Services;

public sealed class YandexLyricsBridge
{
    public sealed record PlaybackState(double Position, double Volume, string TrackId, bool Liked, bool Disliked, bool CanLike, bool CanDislike, string Title = "", string Artist = "", double Duration = 0, bool Playing = false, string ArtworkUrl = "");
    private static readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
    public int Port { get; set; } = 9226;
    public string Status { get; private set; } = "Не подключено";
    public string PlaybackError { get; private set; } = "";
    public bool HasSyncedLyricsHint { get; private set; }
    public bool PrefetchEnabled { get; set; } = true;
    private readonly string cacheClient = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, (DateTime At, LyricsResult Result)> warmLyrics = [];
    private static string WarmKey(LyricTrack track)
    {
        static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return Normalize(track.Title) + "\n" + Normalize(track.Artist) + "\n" + Math.Round(track.Duration);
    }
    internal LyricsResult? TryGetCached(LyricTrack track) => warmLyrics.TryGetValue(WarmKey(track), out var entry) && DateTime.UtcNow - entry.At < TimeSpan.FromHours(1) ? entry.Result : null;
    private void CacheLyrics(LyricTrack track, LyricsResult? result)
    {
        if (result?.Synced != true) return;
        if (warmLyrics.Count >= 64) warmLyrics.Remove(warmLyrics.Keys.First());
        warmLyrics[WarmKey(track)] = (DateTime.UtcNow, result);
    }
    private static readonly string bootstrap = ReadBootstrap();
    private static string ReadBootstrap()
    {
        using var stream = typeof(YandexLyricsBridge).Assembly.GetManifestResourceStream("DesktopIsland.Services.YandexAdapter.js")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public async Task<LyricsResult?> FindAsync(LyricTrack track, CancellationToken cancellation, string? diagnosticTrackId = null, bool plainOnly = false, bool bypassCache = false)
    {
        HasSyncedLyricsHint = false;
        if (!bypassCache && !plainOnly && diagnosticTrackId == null && TryGetCached(track) is LyricsResult warm) { HasSyncedLyricsHint = true; return warm; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var cdp = await ConnectAsync(deadline.Token);
            if (cdp == null) { Status = "Подключите Яндекс Музыку в настройках"; return null; }
            var installed = await cdp.EvaluateAsync(bootstrap, deadline.Token);
            if (installed.ValueKind != JsonValueKind.True) { Status = "Откройте окно Яндекс Музыки"; return null; }
            var metadata = await cdp.EvaluateAsync("window.__islandYandex.metadata()", deadline.Token);
            if (metadata.ValueKind != JsonValueKind.Object) { Status = "В Яндекс Музыке нет выбранного трека"; return null; }
            if (diagnosticTrackId == null && !MetadataMatches(metadata, track)) { Status = "Плеер переключает трек"; return null; }
            HasSyncedLyricsHint = metadata.TryGetProperty("hasSyncLyrics", out var hint) && hint.ValueKind == JsonValueKind.True;
            var idJson = diagnosticTrackId == null ? "null" : JsonSerializer.Serialize(diagnosticTrackId);
            var result = await cdp.EvaluateAsync($"window.__islandYandex.read({idJson},{(plainOnly ? "true" : "false")})", deadline.Token);
            if (diagnosticTrackId == null)
            {
                var after = await cdp.EvaluateAsync("window.__islandYandex.metadata()", deadline.Token);
                if (!MetadataMatches(after, track) || after.GetProperty("id").GetString() != metadata.GetProperty("id").GetString()) return null;
            }
            var parsed = ParseResult(result);
            if (diagnosticTrackId == null) CacheLyrics(track, parsed);
            Status = parsed?.Status ?? "Яндекс Музыка не отдала текст этого трека"; return parsed;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { Status = "Яндекс Музыка не ответила вовремя"; return null; }
        catch (Exception ex) when (ex is HttpRequestException or WebSocketException or JsonException or InvalidOperationException or KeyNotFoundException)
        { Status = "Нет соединения с текстом Яндекс Музыки"; return null; }
    }
    public async Task<PlaybackState?> PlaybackAsync(string? command = null, double value = 0, string? expectedTrackId = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var cdp = await ConnectAsync(deadline.Token);
            if (cdp == null || (await cdp.EvaluateAsync(bootstrap, deadline.Token)).ValueKind != JsonValueKind.True) { PlaybackError = "Ожидание плеера"; return null; }
            string expression = command == null ? PrefetchEnabled ? "(()=>{const a=window.__islandYandex;a.warmNext();return {...a.playback(),prefetched:a.takePrefetch(" + JsonSerializer.Serialize(cacheClient) + ")};})()" : "window.__islandYandex.playback()" : "window.__islandYandex.control(" + JsonSerializer.Serialize(command) + "," + JsonSerializer.Serialize(value) + "," + JsonSerializer.Serialize(expectedTrackId) + ")";
            var result = await cdp.EvaluateAsync(expression, deadline.Token);
            if (result.ValueKind != JsonValueKind.Object || command != null && (!result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)) return null;
            PlaybackError = "";
            if (result.TryGetProperty("prefetched", out var prefetched) && prefetched.ValueKind == JsonValueKind.Object)
            {
                var next = prefetched.GetProperty("track");
                CacheLyrics(new LyricTrack(next.GetProperty("title").GetString() ?? "", next.GetProperty("artist").GetString() ?? "", "", next.GetProperty("duration").GetDouble()), ParseResult(prefetched.GetProperty("result")));
            }
            return new(result.GetProperty("position").GetDouble(), result.GetProperty("volume").GetDouble(), result.GetProperty("trackId").GetString() ?? "", result.GetProperty("liked").GetBoolean(), result.GetProperty("disliked").GetBoolean(), result.GetProperty("canLike").GetBoolean(), result.GetProperty("canDislike").GetBoolean(), result.GetProperty("title").GetString() ?? "", result.GetProperty("artist").GetString() ?? "", result.GetProperty("duration").GetDouble(), result.GetProperty("playing").GetBoolean(), result.GetProperty("artworkUrl").GetString() ?? "");
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or WebSocketException or JsonException or InvalidOperationException or KeyNotFoundException) { PlaybackError = ex.GetType().Name + ": " + ex.Message; return null; }
    }
    internal static bool MetadataMatches(JsonElement metadata, LyricTrack track)
    {
        if (metadata.ValueKind != JsonValueKind.Object) return false;
        string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return Normalize(metadata.GetProperty("title").GetString() ?? "") == Normalize(track.Title)
            && Normalize(metadata.GetProperty("artist").GetString() ?? "") == Normalize(track.Artist);
    }
    internal static LyricsResult? ParseResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || result.GetProperty("state").GetString() != "ready") return null;
        var lines = result.GetProperty("lines").EnumerateArray().Select(x => new LyricLine(x.GetProperty("seconds").GetDouble(), x.GetProperty("text").GetString() ?? "", x.TryGetProperty("endSeconds", out var end) && end.ValueKind == JsonValueKind.Number && end.GetDouble() > x.GetProperty("seconds").GetDouble() ? end.GetDouble() : null)).Where(x => double.IsFinite(x.Seconds) && x.Seconds >= 0).OrderBy(x => x.Seconds).ToArray();
        var plain = result.GetProperty("plain").GetString() ?? "";
        return new LyricsResult(lines.Length > 0 ? "Яндекс Музыка · текст с таймкодами" : "Яндекс Музыка · текст без таймкодов", lines, plain);
    }
    private async Task<CdpConnection?> ConnectAsync(CancellationToken cancellation)
    {
        using var response = await http.GetAsync($"http://127.0.0.1:{Math.Clamp(Port, 1024, 65535)}/json/list", cancellation);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        foreach (var target in json.RootElement.EnumerateArray())
        {
            if (target.GetProperty("type").GetString() != "page" || !(target.GetProperty("url").GetString() ?? "").StartsWith("music-application://desktop/", StringComparison.Ordinal)) continue;
            var uri = new Uri(target.GetProperty("webSocketDebuggerUrl").GetString()!);
            if (uri.Scheme != "ws" || uri.Host is not ("127.0.0.1" or "localhost") || uri.Port != Port) continue;
            var connection = new CdpConnection(); await connection.OpenAsync(uri, cancellation); return connection;
        }
        return null;
    }
    public async Task<string> ConnectPlayerAsync()
    {
        YandexLaunchSetup.Install();
        try { using var existing = await ConnectAsync(CancellationToken.None); if (existing != null) return Status = "Подключено к Яндекс Музыке"; } catch (Exception) { }
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "YandexMusic", "Яндекс Музыка.exe");
        if (!File.Exists(path)) return Status = "Яндекс Музыка не найдена в стандартной папке установки";
        var running = Process.GetProcesses().Where(p => p.ProcessName == "Яндекс Музыка").ToArray();
        var main = running.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (running.Length > 0 && main == null) return Status = "Откройте окно Яндекс Музыки и повторите подключение";
        if (main != null)
        {
            main.CloseMainWindow(); await Task.Delay(700);
            if (!main.HasExited) { main.Kill(); await main.WaitForExitAsync(); }
        }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"/d /s /c \"\"{path}\" --remote-debugging-address=127.0.0.1 --remote-debugging-port={Math.Clamp(Port, 1024, 65535)} >NUL 2>&1\"" };
        start.Environment.Remove("ELECTRON_RUN_AS_NODE"); start.Environment.Remove("NODE_OPTIONS");
        Process.Start(start);
        for (int i = 0; i < 30; i++) { await Task.Delay(300); try { using var connected = await ConnectAsync(CancellationToken.None); if (connected != null) return Status = "Яндекс Музыка подключена. Выберите песню."; } catch (Exception) { } }
        return Status = "Плеер запущен. Дождитесь загрузки и повторите подключение.";
    }

    internal sealed class CdpConnection : IDisposable
    {
        private readonly ClientWebSocket socket = new();
        private int sequence;
        public Task OpenAsync(Uri uri, CancellationToken cancellation) { socket.Options.Proxy = null; return socket.ConnectAsync(uri, cancellation); }
        public async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken cancellation, string? sessionId = null)
        {
            int id = ++sequence;
            var request = new Dictionary<string, object> { ["id"] = id, ["method"] = method, ["params"] = parameters };
            if (sessionId != null) request["sessionId"] = sessionId;
            var payload = JsonSerializer.SerializeToUtf8Bytes(request);
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellation);
            while (true)
            {
                using var body = new MemoryStream(); var buffer = new byte[16384]; WebSocketReceiveResult frame;
                do { frame = await socket.ReceiveAsync(buffer, cancellation); if (frame.MessageType == WebSocketMessageType.Close) throw new WebSocketException(); await body.WriteAsync(buffer.AsMemory(0, frame.Count), cancellation); } while (!frame.EndOfMessage);
                using var json = JsonDocument.Parse(body.ToArray()); var root = json.RootElement;
                if (!root.TryGetProperty("id", out var received) || received.GetInt32() != id) continue;
                if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("Yandex adapter request failed");
                return root.GetProperty("result").Clone();
            }
        }
        public async Task<JsonElement> EvaluateAsync(string expression, CancellationToken cancellation, string? sessionId = null)
        {
            var reply = await CallAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true }, cancellation, sessionId);
            if (reply.TryGetProperty("exceptionDetails", out _)) throw new InvalidOperationException("Yandex adapter is not available for this version");
            return reply.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : default;
        }
        public void Dispose() => socket.Dispose();
    }
}
