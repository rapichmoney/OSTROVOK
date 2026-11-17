namespace DesktopIsland;

public partial class MainWindow
{
    private readonly Services.TrackIntroduction introduction = new();
    private bool introductionVisible;
    private bool IsTrackIntroduction => introduction.IsActive(CurrentVisualKey, Environment.TickCount64);
    private void BeginTrackIntroduction()
    {
        if (introduction.Begin(CurrentVisualKey, Environment.TickCount64)) { introductionVisible = true; RefreshIdleContent(); }
    }
    private void UpdateTrackIntroduction()
    {
        if (introductionVisible && !IsTrackIntroduction) { introductionVisible = false; RefreshIdleContent(); }
    }
}
