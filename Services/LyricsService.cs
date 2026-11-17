using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace DesktopIsland.Services;

public sealed record LyricLine(double Seconds, string Text, double? EndSeconds = null);
public sealed record LyricTrack(string Title, string Artist, string Album, double Duration)
{
    public string Key => $"{Title.Trim()}\n{Artist.Trim()}\n{Album.Trim()}\n{Math.Round(Duration)}";
}
public sealed record LyricsResult(string Status, LyricLine[] Lines, string PlainText = "", long? RecordId = null, bool Instrumental = false)
{
    public bool Synced => Lines.Length > 0;
}

public sealed class LyricsService
{
    private static readonly HttpClient sharedHttp = CreateClient();
    private readonly HttpClient http;
    public LyricsService(HttpClient? client = null) => http = client ?? sharedHttp;
    private readonly Dictionary<string, (DateTime At, LyricsResult Result)> cache = [];
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopIsland/0.6 (personal-local-build)"); return client;
    }
    public async Task<LyricsResult> FindAsync(LyricTrack track, CancellationToken cancellation = default, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist)) return new("Плеер не передал название или исполнителя", []);
        if (!force && cache.TryGetValue(track.Key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(hit.Result.Synced ? 240 : 5)) return hit.Result;
        LyricsResult? result = null; bool primaryUnavailable = false, secondaryUnavailable = false;
        try { result = await FindPrimaryAsync(track, cancellation); }
        catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException) { primaryUnavailable = true; }
        if (result?.Synced != true && result?.Instrumental != true)
        {
            try
            {
                var backup = await FindSecondaryAsync(track, cancellation);
                if (backup?.Synced == true || result == null || string.IsNullOrWhiteSpace(result.PlainText)) result = backup ?? result;
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException) { secondaryUnavailable = true; }
        }
        cancellation.ThrowIfCancellationRequested();
        result ??= new LyricsResult(primaryUnavailable || secondaryUnavailable ? "Один из сервисов недоступен; доступные источники текст не нашли. Повторите позже или откройте LRC." : "В LRCLIB и резервном сервисе текста нет. Можно открыть свой LRC.", []);
        if (!primaryUnavailable && !secondaryUnavailable)
        {
            if (cache.Count >= 128) cache.Remove(cache.OrderBy(x => x.Value.At).First().Key);
            cache[track.Key] = (DateTime.UtcNow, result);
        }
        return result;
    }
    private async Task<LyricsResult?> FindPrimaryAsync(LyricTrack track, CancellationToken cancellation)
    {
        string query = "track_name=" + Uri.EscapeDataString(track.Title) + "&artist_name=" + Uri.EscapeDataString(track.Artist);
        if (!string.IsNullOrWhiteSpace(track.Album)) query += "&album_name=" + Uri.EscapeDataString(track.Album);
        if (track.Duration is >= 1 and <= 3600) query += "&duration=" + Math.Round(track.Duration).ToString(CultureInfo.InvariantCulture);
        LyricsResult? result = null;
        using (var response = await http.GetAsync("https://lrclib.net/api/get?" + query, cancellation))
        {
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                if (Matches(json.RootElement, track)) result = ReadResult(json.RootElement);
            }
            else if (response.StatusCode != HttpStatusCode.NotFound && response.StatusCode != HttpStatusCode.BadRequest) response.EnsureSuccessStatusCode();
        }
        if (result == null)
        {
            // Never select a similarly titled cover/remix or a record with the wrong duration.
            using var response = await http.GetAsync("https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(track.Title) + "&artist_name=" + Uri.EscapeDataString(track.Artist), cancellation);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var candidates = json.RootElement.EnumerateArray().Where(x => Matches(x, track)).ToArray();
            var best = candidates.OrderByDescending(x => !string.IsNullOrWhiteSpace(GetString(x, "syncedLyrics"))).ThenBy(x => track.Duration > 0 ? Math.Abs(x.GetProperty("duration").GetDouble() - track.Duration) : 0).FirstOrDefault();
            if (best.ValueKind == JsonValueKind.Object) result = ReadResult(best);
        }
        return result;
    }
    private async Task<LyricsResult?> FindSecondaryAsync(LyricTrack track, CancellationToken cancellation)
    {
        string query = "track_name=" + Uri.EscapeDataString(track.Title) + "&artist_name=" + Uri.EscapeDataString(track.Artist);
        if (track.Duration is >= 1 and <= 3600) query += "&duration=" + Math.Round(track.Duration).ToString(CultureInfo.InvariantCulture);
        using var response = await http.GetAsync("https://api.lrcmux.dev/compat/lrclib/api/get?" + query, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return Matches(json.RootElement, track) ? ReadResult(json.RootElement, "lrcmux") : null;
    }
    internal static bool Matches(JsonElement record, LyricTrack track)
    {
        string title = Normalize(GetString(record, "trackName"));
        string artist = Normalize(GetString(record, "artistName"));
        string requestedArtist = Normalize(track.Artist);
        if (title != Normalize(track.Title) || artist.Length == 0 || requestedArtist.Length == 0) return false;
        if (artist != requestedArtist) return false;
        if (track.Duration > 0 && record.TryGetProperty("duration", out var duration) && Math.Abs(duration.GetDouble() - track.Duration) > 3) return false;
        return true;
    }
    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string GetString(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static LyricsResult ReadResult(JsonElement record, string source = "LRCLIB")
    {
        long? id = record.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;
        if (record.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True) return new("Инструментальный трек — текста нет", [], RecordId: id, Instrumental: true);
        var lines = ParseLrc(GetString(record, "syncedLyrics")); var plain = GetString(record, "plainLyrics");
        return new(lines.Length > 0 ? "Текст с таймкодами · " + source : plain.Length > 0 ? "Текст без таймкодов · " + source : "Текст не найден", lines, plain, id);
    }
    public static LyricLine[] ParseLrc(string lrc)
    {
        var offsetMatch = Regex.Match(lrc, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
        double offset = offsetMatch.Success && double.TryParse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var milliseconds) ? milliseconds / 1000 : 0;
        var result = new List<LyricLine>();
        foreach (string raw in lrc.Replace("\r", "").Split('\n'))
        {
            var stamps = Regex.Matches(raw, @"\[(\d{1,3}):([0-5]\d)(?:[\.:](\d{1,3}))?\]");
            if (stamps.Count == 0) continue;
            string text = raw[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            // Remove enhanced-LRC word timestamps while retaining the line text.
            text = Regex.Replace(text, @"<\d{1,3}:[0-5]\d(?:\.\d{1,3})?>", "");
            foreach (Match stamp in stamps)
            {
                double fraction = stamp.Groups[3].Success ? double.Parse("0." + stamp.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                double seconds = int.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture) + fraction - offset;
                result.Add(new LyricLine(Math.Max(0, seconds), text));
            }
        }
        return result.OrderBy(x => x.Seconds).GroupBy(x => (x.Seconds, x.Text)).Select(x => x.First()).ToArray();
    }
    public static int LineAt(IReadOnlyList<LyricLine> lines, double position, double adjustment = 0)
    {
        double time = position + adjustment; int low = 0, high = lines.Count - 1, answer = -1;
        while (low <= high) { int middle = (low + high) / 2; if (lines[middle].Seconds <= time) { answer = middle; low = middle + 1; } else high = middle - 1; }
        return answer >= 0 && lines[answer].EndSeconds is double end && time >= end ? -1 : answer;
    }
}
