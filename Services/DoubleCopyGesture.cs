namespace DesktopIsland.Services;

internal sealed record DoubleCopyRequest(IntPtr Window, uint BeforeFirstCopy);

internal sealed class DoubleCopyGesture
{
    private bool down;
    private long? first;
    private IntPtr owner;
    private uint sequence;
    internal void Reset() { down = false; first = null; owner = IntPtr.Zero; }
    internal void KeyUp(int key) { if (key == 0x43) down = false; }
    internal DoubleCopyRequest? KeyDown(int key, bool control, bool otherModifiers, long now, IntPtr window, uint clipboardSequence)
    {
        if (key != 0x43)
        {
            if (key is not (0x11 or 0xA2 or 0xA3)) first = null;
            return null;
        }
        if (down) return null;
        down = true;
        if (!control || otherModifiers || window == IntPtr.Zero) { first = null; return null; }
        if (first is long previous && owner == window && now >= previous && now - previous <= 650)
        { first = null; return new(window, sequence); }
        first = now; owner = window; sequence = clipboardSequence;
        return null;
    }
    internal static string? OppositeLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || Uri.TryCreate(text.Trim(), UriKind.Absolute, out _)) return null;
        int russian = 0, english = 0;
        foreach (char c in text)
        {
            if (c is >= 'А' and <= 'я' or 'Ё' or 'ё') russian++;
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') english++;
        }
        return russian > 0 && russian >= english ? "en" : english > 0 ? "ru" : null;
    }
}
