using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopIsland;

public partial class MainWindow
{
    private void ClearCollapseFrame()
    {
        TrackVisualSnapshot.BeginAnimation(OpacityProperty, null);
        TrackVisualSnapshot.Source = null; TrackVisualSnapshot.Visibility = Visibility.Collapsed;
    }
    private void CaptureCollapseFrame(bool capture)
    {
        ClearCollapseFrame();
        if (!capture || !Motion.Enabled || !Shell.IsVisible || Shell.ActualWidth < 1 || Shell.ActualHeight < 1) return;
        var dpi = VisualTreeHelper.GetDpi(Shell);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Shell.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(Shell.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(Shell); bitmap.Freeze();
        TrackVisualSnapshot.Source = bitmap; TrackVisualSnapshot.Width = Shell.ActualWidth; TrackVisualSnapshot.Height = Shell.ActualHeight;
        TrackVisualSnapshot.Visibility = Visibility.Visible; TrackVisualSnapshot.Opacity = 1;
        Motion.Tween(TrackVisualSnapshot, OpacityProperty, 0, 260, completed: ClearCollapseFrame);
    }
}
