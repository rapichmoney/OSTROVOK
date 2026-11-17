namespace DesktopIsland.Services;

public sealed class StableMusicWidth
{
    private double retained;
    public double Resolve(double desired, double maximum, bool musicSession)
    {
        if (!musicSession) { retained = 0; return desired; }
        retained = Math.Min(maximum, Math.Max(retained, desired));
        return retained;
    }
}
