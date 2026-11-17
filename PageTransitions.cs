using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopIsland;

public partial class MainWindow
{
    private int pageTransitionRevision;
    private RenderTargetBitmap? CapturePageForTransition()
    {
        if (!Motion.Enabled || !expanded || !IsLoaded || ContentTabs.ActualWidth < 1 || ContentTabs.ActualHeight < 1) return null;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ContentTabs.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ContentTabs.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(ContentTabs); bitmap.Freeze(); return bitmap;
    }

    private void TransitionToPage(RenderTargetBitmap? previous, int direction)
    {
        int revision = ++pageTransitionRevision;
        Motion.ClearContentMotion(ContentTabs);
        if (!Motion.Enabled || previous == null) { PageSnapshot.Source = null; PageSnapshot.Visibility = Visibility.Collapsed; return; }
        PageSnapshot.Source = previous; PageSnapshot.Visibility = Visibility.Visible;
        PageSnapshot.BeginAnimation(OpacityProperty, null); PageSnapshot.Opacity = 1;
        var outgoing = new TranslateTransform(); PageSnapshot.RenderTransform = outgoing;
        Motion.Tween(outgoing, TranslateTransform.XProperty, -direction * 16, 150);
        Motion.Tween(PageSnapshot, OpacityProperty, 0, 125, completed: () =>
        {
            if (revision != pageTransitionRevision) return;
            PageSnapshot.Visibility = Visibility.Collapsed; PageSnapshot.Source = null; PageSnapshot.RenderTransform = Transform.Identity;
        });
        var incoming = new TranslateTransform(direction * 24, 0); ContentTabs.RenderTransform = incoming;
        ContentTabs.Opacity = 0;
        Motion.Tween(ContentTabs, OpacityProperty, 1, 230, 60);
        Motion.Tween(incoming, TranslateTransform.XProperty, 0, 430, 60, easingOverride: new MotionDynamics.PhysicalResponse(.88, 12));
    }

    private void StopPageTransition()
    {
        ++pageTransitionRevision;
        PageSnapshot.BeginAnimation(OpacityProperty, null); PageSnapshot.Source = null; PageSnapshot.Visibility = Visibility.Collapsed;
        PageSnapshot.RenderTransform = Transform.Identity; Motion.ClearContentMotion(ContentTabs);
    }
}
