namespace DesktopIsland.Services;

// Keep rendering continuous while the player supplies periodically quantized samples.
public sealed class SynchronizedPlaybackClock
{
    private double anchor, duration, correction;
    private DateTimeOffset sampledAt;
    private bool initialized, playing;
    private const double CorrectionSeconds = 2;

    public double At(DateTimeOffset now)
    {
        double elapsed = playing ? Math.Max(0, (now - sampledAt).TotalSeconds) : 0;
        double value = anchor + elapsed + correction * Math.Min(1, elapsed / CorrectionSeconds);
        return Math.Clamp(value, 0, duration > 0 ? duration : double.MaxValue);
    }
    public void Sample(double position, double length, bool isPlaying, DateTimeOffset now, bool discontinuity = false)
    {
        if (!double.IsFinite(position) || !double.IsFinite(length)) return;
        double predicted = initialized ? At(now) : position;
        double error = position - predicted;
        duration = Math.Max(0, length);
        bool snap = !initialized || discontinuity || playing != isPlaying || !isPlaying || Math.Abs(error) > 1.5;
        anchor = Math.Max(0, snap ? position : predicted);
        correction = snap ? 0 : error;
        sampledAt = now; playing = isPlaying; initialized = true;
    }
}
