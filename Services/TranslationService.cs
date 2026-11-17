using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Net;

namespace DesktopIsland.Services;

public sealed record TranslationResult(string Text, string Language, string Provider = "Google Translate");
public sealed class TranslationUnavailableException(string message) : Exception(message);

public sealed class TranslationService
{
    private readonly HttpClient client;
    private DateTimeOffset googleRetryAt;
    private readonly Dictionary<string, TranslationResult> cache = [];
    public TranslationService(HttpClient? client = null) => this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

    public static bool ShouldTranslate(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || Uri.TryCreate(text.Trim(), UriKind.Absolute, out _)) return false;
        var letters = text.Where(char.IsLetter).ToArray();
        if (letters.Length < 2 || (text.Length >= 32 && !text.Any(char.IsWhiteSpace))) return false;
        int russian = letters.Count(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё');
        return russian < letters.Length * .65;
    }

    public async Task<TranslationResult> TranslateAsync(string text, CancellationToken token, string targetLanguage = "ru")
    {
        if (targetLanguage is not ("ru" or "en")) throw new ArgumentOutOfRangeException(nameof(targetLanguage));
        if (text.Length > 4000) throw new ArgumentOutOfRangeException(nameof(text));
        token.ThrowIfCancellationRequested();
        string cacheKey = targetLanguage + "\n" + text;
        if (cache.TryGetValue(cacheKey, out var cached)) return cached;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        var requestToken = budget.Token;
        if (DateTimeOffset.UtcNow >= googleRetryAt)
        {
            try { return Remember(cacheKey, await TranslateGoogleAsync(text, requestToken, targetLanguage)); }
            catch (OperationCanceledException) when (!requestToken.IsCancellationRequested) { }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IndexOutOfRangeException) { }
        }
        try { return Remember(cacheKey, await TranslateFallbackAsync(text, requestToken, targetLanguage)); }
        catch (HttpRequestException) { throw new TranslationUnavailableException("Нет соединения с сервисами перевода. Исходный текст сохранён."); }
        catch (JsonException) { throw new TranslationUnavailableException("Резервный переводчик вернул непонятный ответ. Исходный текст сохранён."); }
    }

    private TranslationResult Remember(string text, TranslationResult result)
    {
        if (cache.Count >= 32) cache.Clear();
        cache[text] = result; return result;
    }

    private async Task<TranslationResult> TranslateGoogleAsync(string text, CancellationToken token, string targetLanguage)
    {
        string uri = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" + targetLanguage + "&dt=t&q=" + Uri.EscapeDataString(text);
        using var response = await client.GetAsync(uri, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            googleRetryAt = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return Parse(json.RootElement);
    }

    private async Task<TranslationResult> TranslateFallbackAsync(string text, CancellationToken token, string targetLanguage)
    {
        var output = new StringBuilder(); string language = "";
        foreach (string part in SplitFallbackText(text))
        {
            if (string.IsNullOrWhiteSpace(part)) { output.Append(part); continue; }
            using var response = await client.GetAsync("https://api.mymemory.translated.net/get?langpair=autodetect%7C" + targetLanguage + "&q=" + Uri.EscapeDataString(part.TrimEnd()), token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new TranslationUnavailableException("Сервисы перевода ограничили запросы. Попробуйте позже; исходный текст сохранён.");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var result = ParseFallback(document.RootElement);
            if (language.Length == 0) language = result.Language;
            output.Append(result.Text.TrimEnd());
            output.Append(part.AsSpan(part.TrimEnd().Length));
        }
        return new(output.ToString(), language, "MyMemory");
    }

    internal static TranslationResult ParseFallback(JsonElement root)
    {
        string status = root.TryGetProperty("responseStatus", out var state) ? state.ToString() : "";
        if (status != "200" || root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
            throw new TranslationUnavailableException("Сервисы перевода ограничили запросы. Попробуйте позже; исходный текст сохранён.");
        if (!root.TryGetProperty("responseData", out var data) || !data.TryGetProperty("translatedText", out var translated) || translated.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(translated.GetString()))
            throw new JsonException("Translation response has no text");
        string language = data.TryGetProperty("detectedLanguage", out var detected) && detected.ValueKind == JsonValueKind.String ? detected.GetString() ?? "" : "";
        return new(WebUtility.HtmlDecode(translated.GetString()!), language, "MyMemory");
    }

    internal static string[] SplitFallbackText(string text)
    {
        var parts = new List<string>(); int start = 0;
        while (start < text.Length)
        {
            int end = start, lastSpace = -1, bytes = 0;
            foreach (var rune in text.AsSpan(start).EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 480) break;
                bytes += rune.Utf8SequenceLength; end += rune.Utf16SequenceLength;
                if (Rune.IsWhiteSpace(rune)) lastSpace = end;
            }
            if (end < text.Length && lastSpace > start) end = lastSpace;
            parts.Add(text[start..end]); start = end;
        }
        return parts.ToArray();
    }

    internal static TranslationResult Parse(JsonElement root)
    {
        var output = new StringBuilder();
        foreach (var part in root[0].EnumerateArray())
            if (part.ValueKind == JsonValueKind.Array && part.GetArrayLength() > 0 && part[0].ValueKind == JsonValueKind.String) output.Append(part[0].GetString());
        string language = root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String ? root[2].GetString() ?? "" : "";
        if (output.Length == 0) throw new JsonException("No translation returned");
        return new(output.ToString(), language);
    }
}
