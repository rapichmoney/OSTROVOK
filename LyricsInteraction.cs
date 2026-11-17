using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopIsland;

public partial class MainWindow
{
    private bool manualLyricsScroll;
    private string lyricsScrollOwner = "";
    private bool collapseUntilPointerLeaves;
    private void InitializeLyricsInteraction()
    {
        LyricsScroll.AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(ScrollLyricsManually), true);
        LyricsScroll.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null && node != LyricsScroll; node = VisualTreeHelper.GetParent(node))
                if (node is ScrollBar) { BeginManualLyricsScroll(); break; }
        }), true);
        LyricsScroll.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => BeginManualLyricsScroll()), true);
        LyricsScroll.PreviewKeyDown += (_, e) => { if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) BeginManualLyricsScroll(); };
    }
    private void BeginManualLyricsScroll()
    {
        manualLyricsScroll = true; lyricsScrollOwner = CurrentVisualKey;
        LyricMotion.Stop(LyricsScroll); FollowLyricsButton.Visibility = Visibility.Visible;
    }
    private void ScrollLyricsManually(object sender, MouseWheelEventArgs e)
    {
        if (LyricsScroll.ScrollableHeight <= 0) return;
        BeginManualLyricsScroll();
        double step = SystemParameters.WheelScrollLines < 0 ? LyricsScroll.ViewportHeight * .8 : Math.Max(1, SystemParameters.WheelScrollLines) * 16;
        LyricsScroll.ScrollToVerticalOffset(Math.Clamp(LyricsScroll.VerticalOffset - e.Delta / 120d * step, 0, LyricsScroll.ScrollableHeight));
        e.Handled = true;
    }
    private void ResumeLyricsFollow(object sender, RoutedEventArgs e)
    {
        manualLyricsScroll = false; FollowLyricsButton.Visibility = Visibility.Collapsed;
        QueueCurrentLyricScroll(true);
    }
    private void PrepareLyricsScrollForTrack()
    {
        if (lyricsScrollOwner == CurrentVisualKey && manualLyricsScroll) return;
        lyricsScrollOwner = CurrentVisualKey; manualLyricsScroll = false; FollowLyricsButton.Visibility = Visibility.Collapsed;
    }
    private void CollapseFromPlayerTab(object sender, MouseButtonEventArgs e)
    {
        if (!expanded) return;
        leaveTimer.Stop(); collapseUntilPointerLeaves = true; SetExpanded(false); e.Handled = true;
    }
}
