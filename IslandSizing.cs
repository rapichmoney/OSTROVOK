using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private double compactDragPointer, compactDragLeft, compactDragWidth;
    private bool CompactEdgeAt(double x) => x <= 22 || x >= ActualWidth - 22;

    private void CompactHover(object sender, MouseEventArgs e)
    {
        if (!expanded && !draggingWindow && !suppressHover && !CompactEdgeAt(e.GetPosition(this).X)) Expand(sender, e);
    }

    private void CompactResizeStarted(object sender, DragStartedEventArgs e)
    {
        leaveTimer.Stop();
        if (shellMoving) StopShellMotion(true);
        draggingWindow = true;
        compactDragPointer = System.Windows.Forms.Cursor.Position.X;
        compactDragLeft = Left; compactDragWidth = Width;
    }

    private void CompactResizeDelta(object sender, DragDeltaEventArgs e)
    {
        double delta = (System.Windows.Forms.Cursor.Position.X - compactDragPointer) / System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        SetCompactResize(compactDragLeft, compactDragWidth, delta, ReferenceEquals(sender, CompactLeftHandle));
    }

    private void SetCompactResize(double originalLeft, double originalWidth, double delta, bool leftEdge)
    {
        var area = WindowInterop.WorkArea(handle);
        double scale = Math.Clamp(settings.CompactScale, 75, 150) / 100;
        double fixedEdge = leftEdge ? originalLeft + originalWidth : originalLeft;
        double maximum = Math.Max(1, leftEdge ? fixedEdge - area.Left : area.Right - fixedEdge);
        double minimum = Math.Min((settings.MiniMode ? 100 : 220) * scale, maximum);
        Width = Math.Clamp(originalWidth + (leftEdge ? -delta : delta), minimum, Math.Min(maximum, 1600 * scale));
        Left = leftEdge ? fixedEdge - Width : fixedEdge;
        settings.CompactWidthOverride = Width / scale;
    }

    private void CompactResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        draggingWindow = false; SaveSettings();
    }

    private void ResetCompactWidth(object sender, MouseButtonEventArgs e)
    {
        double center = Left + Width / 2;
        settings.CompactWidthOverride = 0; Width = CompactWidth; Left = center - Width / 2;
        ClampToMonitor(); SaveSettings(); e.Handled = true;
    }

    private void ResizeIslandStarted(object sender, DragStartedEventArgs e)
    {
        leaveTimer.Stop();
        if (shellMoving) StopShellMotion(true);
        draggingWindow = true;
    }

    private void ResizeIslandDelta(object sender, DragDeltaEventArgs e) => ResizeIslandBy(e.VerticalChange);

    private void ResizeIslandBy(double delta)
    {
        var area = WindowInterop.WorkArea(handle);
        double scale = ExpandedUiScale;
        double maximum = Math.Max(1, area.Bottom - Top);
        Height = Math.Clamp(Height + delta, Math.Min(480 * scale, maximum), maximum);
        if (Navigation.SelectedIndex == 0 && media.Available) settings.PlayerHeight = Height / scale;
    }

    private void ResizeExpandedWidth(object sender, DragDeltaEventArgs e)
    {
        var area = WindowInterop.WorkArea(handle);
        double scale = ExpandedUiScale;
        double desired = Math.Clamp((Width + e.HorizontalChange) / scale, 440, 900);
        settings.ExpandedWidthOverride = desired;
        ApplyShellAppearance(); Width = Math.Min(ExpandedWidth, area.Right - Left);
    }
    private void ResizeIslandCompleted(object sender, DragCompletedEventArgs e)
    {
        draggingWindow = false; SaveSettings();
    }

    private void ResetIslandHeight(object sender, MouseButtonEventArgs e)
    {
        if (shellMoving) StopShellMotion(true);
        settings.PlayerHeight = 0;
        Height = Math.Min(ExpandedHeight, WindowInterop.WorkArea(handle).Bottom - Top);
        SaveSettings(); e.Handled = true;
    }
}
