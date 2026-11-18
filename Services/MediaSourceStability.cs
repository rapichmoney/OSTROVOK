namespace DesktopIsland.Services;

internal sealed class MediaSourceStability
{
    private string? pending;
    private long since;
    internal bool Accept(string current, string candidate, bool playingMusic, long now)
    {
        if (current == candidate || current.Length == 0 || playingMusic)
        { pending = null; return true; }
        if (pending != candidate) { pending = candidate; since = now; return false; }
        return now - since >= 1200;
    }
}
