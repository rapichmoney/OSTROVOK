namespace DesktopIsland.Services;

public sealed class TrackIntroduction
{
    private string owner = "";
    private long until;
    public bool Begin(string key, long now)
    {
        if (owner == key) return false;
        owner = key; until = now + 1000; return true;
    }
    public bool IsActive(string key, long now) => owner == key && now < until;
}
