namespace DesktopIsland.Services;

internal sealed class ClipboardTranslationGate
{
    internal bool EnglishReplyArmed { get; private set; }
    internal string? SelectTarget(string text, bool enabled)
    {
        if (!enabled) { Reset(); return null; }
        if (TranslationService.ShouldTranslate(text)) { Reset(); return "ru"; }
        if (!EnglishReplyArmed || text.Length > 4000 || Uri.TryCreate(text.Trim(), UriKind.Absolute, out _)) return null;
        int letters = 0, russian = 0;
        foreach (char c in text)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (c is >= 'А' and <= 'я' or 'Ё' or 'ё') russian++;
        }
        return letters >= 2 && russian >= letters * .65 ? "en" : null;
    }
    internal void Complete(string target, string detectedSource)
        => EnglishReplyArmed = target == "ru" && (detectedSource.Equals("en", StringComparison.OrdinalIgnoreCase) || detectedSource.StartsWith("en-", StringComparison.OrdinalIgnoreCase));
    internal void Reset() => EnglishReplyArmed = false;
}
