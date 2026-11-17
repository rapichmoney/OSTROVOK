namespace DesktopIsland.Services;

// The Windows session supplies a position sample, not a continuously ticking clock.
public sealed class PlaybackClock
{
    private double position, duration, rate;
    private DateTimeOffset sampledAt;
    private bool playing;
    public void Sample(double positionSeconds, double durationSeconds, bool isPlaying, double playbackRate, DateTimeOffset updatedAt, DateTimeOffset now)
    {
        duration = Math.Max(0, durationSeconds); position = Math.Max(0, positionSeconds);
        playing = isPlaying; rate = double.IsFinite(playbackRate) && playbackRate >= 0 ? playbackRate : 1;
        sampledAt = updatedAt <= now && now - updatedAt < TimeSpan.FromDays(1) ? updatedAt : now;
    }
    public double At(DateTimeOffset now)
    {
        double seconds = position + (playing ? Math.Max(0, (now - sampledAt).TotalSeconds) * rate : 0);
        return Math.Clamp(seconds, 0, duration > 0 ? duration : double.MaxValue);
    }
}
