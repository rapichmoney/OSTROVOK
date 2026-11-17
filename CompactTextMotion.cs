using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopIsland;

public partial class MainWindow
{
    private int compactTextRevision;
    private void UpdateCompactText(string text)
    {
        if (CompactTrack.Text == text) return;
        int revision = ++compactTextRevision;
        bool animate = Motion.Enabled && !expanded && Header.IsVisible && !HasCompactNotification && CompactTrack.Text.Length > 0 && text.Length > 0 && CompactTextContent.ActualWidth > 1 && CompactTextContent.ActualHeight > 1;
        CompactTextSnapshot.BeginAnimation(OpacityProperty, null);
        CompactTextSnapshot.Visibility = Visibility.Collapsed;
        if (animate)
        {
            Motion.ClearContentMotion(CompactTextContent);
            var dpi = VisualTreeHelper.GetDpi(this);
            var image = new RenderTargetBitmap((int)Math.Ceiling(CompactTextContent.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(CompactTextContent.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            image.Render(CompactTextContent); image.Freeze();
            CompactTextSnapshot.Source = image; CompactTextSnapshot.Visibility = Visibility.Visible; CompactTextSnapshot.Opacity = 1;
        }
        CompactTrack.Text = text; CompactLyricFill.Visibility = Visibility.Collapsed;
        if (animate)
        {
            Motion.Enter(CompactTextContent, 3, 260);
            Motion.Tween(CompactTextSnapshot, OpacityProperty, 0, 180, completed: () =>
            {
                if (revision != compactTextRevision) return;
                CompactTextSnapshot.Source = null; CompactTextSnapshot.Visibility = Visibility.Collapsed;
            });
        }
        else StopCompactTextTransition();
    }
    private void StopCompactTextTransition()
    {
        ++compactTextRevision;
        Motion.ClearContentMotion(CompactTextContent);
        CompactTextSnapshot.BeginAnimation(OpacityProperty, null);
        CompactTextSnapshot.Source = null; CompactTextSnapshot.Visibility = Visibility.Collapsed;
    }
}
