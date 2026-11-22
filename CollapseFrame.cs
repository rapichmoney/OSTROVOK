using System.Windows.Media;
using System.Diagnostics;

namespace DesktopIsland;

public partial class MainWindow
{
    private bool collapseContentActive;
    private readonly Stopwatch collapseContentClock = new();
    private void ClearCollapseFrame()
    {
        collapseContentActive = false;
        collapseContentClock.Reset();
        TrackVisualSnapshot.BeginAnimation(OpacityProperty, null);
        TrackVisualSnapshot.Source = null; TrackVisualSnapshot.Visibility = Visibility.Collapsed;
        Motion.ClearContentMotion(TopNavigation);
    }
    private void BeginCollapseContent()
    {
        ClearCollapseFrame();
        collapseContentActive = true;
        collapseContentClock.Restart();
        HeaderRow.Height = new GridLength(64);
        Header.Visibility = CompactCard.Visibility = Visibility.Collapsed;
        TopNavigation.Visibility = Visibility.Visible;
        Motion.Tween(TopNavigation, OpacityProperty, 0, 70);
    }
    private void AdvanceCollapseContent()
    {
        if (!collapseContentActive || collapseContentClock.ElapsedMilliseconds < 70 || expanded || closing || hiding) return;
        collapseContentActive = false;
        TopNavigation.Visibility = ToolbarIndicatorLayer.Visibility = Visibility.Collapsed;
        HeaderRow.Height = new GridLength(HasCompactNotification ? 62 : 34);
        UpdateCompactNotification(false);
        Motion.Enter(HasCompactNotification ? (FrameworkElement)CompactCard : Header, 0, 140);
    }
}
