namespace DesktopIsland.Services;

internal sealed class BrowserConnectionPolicy
{
    private bool requested;
    internal void Request() => requested = true;
    internal void Disable() => requested = false;
    internal bool TryStart()
    {
        if (!requested) return false;
        requested = false;
        return true;
    }
}
