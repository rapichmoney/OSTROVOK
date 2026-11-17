namespace DesktopIsland.Services;

public sealed class PausePresentation
{
    private long? pausedAt;
    public bool IsActive(bool available, bool playing, long now)
    {
        if (!available || playing) { pausedAt = null; return available; }
        pausedAt ??= now;
        return now - pausedAt.Value < 5000;
    }
}
