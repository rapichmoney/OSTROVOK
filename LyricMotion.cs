using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace DesktopIsland;

internal static class LyricMotion
{
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(LyricMotion), new PropertyMetadata(0d, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
    private static readonly DependencyProperty RevisionProperty = DependencyProperty.RegisterAttached("Revision", typeof(int), typeof(LyricMotion), new PropertyMetadata(0));

    internal static void ScrollTo(ScrollViewer viewer, double offset, bool immediate = false)
    {
        offset = Math.Clamp(offset, 0, viewer.ScrollableHeight);
        int revision = (int)viewer.GetValue(RevisionProperty) + 1; viewer.SetValue(RevisionProperty, revision);
        double from = viewer.VerticalOffset;
        viewer.BeginAnimation(OffsetProperty, null);
        viewer.SetValue(OffsetProperty, offset);
        if (immediate || !Motion.Enabled || Math.Abs(from - offset) > viewer.ViewportHeight * 1.5) { viewer.ScrollToVerticalOffset(offset); return; }
        var animation = new DoubleAnimation(from, offset, TimeSpan.FromMilliseconds(620))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) => { if (revision == (int)viewer.GetValue(RevisionProperty)) viewer.BeginAnimation(OffsetProperty, null); };
        viewer.BeginAnimation(OffsetProperty, animation);
    }

    internal static void Stop(ScrollViewer viewer)
    {
        double current = viewer.VerticalOffset;
        viewer.SetValue(RevisionProperty, (int)viewer.GetValue(RevisionProperty) + 1);
        viewer.BeginAnimation(OffsetProperty, null); viewer.SetValue(OffsetProperty, current); viewer.ScrollToVerticalOffset(current);
    }
}
