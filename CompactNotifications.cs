using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopIsland;

public partial class MainWindow
{
    private bool compactNoticeGeometryActive;
    private string compactNoticeSignature = "";
    private double compactNotificationWidth = 460;
    private bool HasCompactNotification => telegramNoticeActive || (clipboardNoticeActive && settings.ClipboardNotifications);
    private double CompactPresentationWidth => trackVisualPending && !HasCompactNotification ? heldWindowWidth : HasCompactNotification ? Math.Max(StandbyCompactWidth, compactNotificationWidth * Math.Clamp(settings.CompactScale, 75, 150) / 100) : StandbyCompactWidth;
    private double CompactPresentationHeight => HasCompactNotification ? 72 * Math.Clamp(settings.CompactScale, 75, 150) / 100 : CompactHeight;

    private void UpdateCompactNotification(bool resizeShell)
    {
        bool active = HasCompactNotification;
        if (active) CancelTrackVisualHold();
        bool transition = compactVisualNotice != active;
        if (transition) { CaptureCompactTransition(active); compactVisualNotice = active; }
        Header.Visibility = !expanded && !active ? Visibility.Visible : Visibility.Collapsed;
        CompactCard.Visibility = !expanded && active ? Visibility.Visible : Visibility.Collapsed;
        CompactLeftHandle.Visibility = CompactRightHandle.Visibility = expanded || active ? Visibility.Collapsed : Visibility.Visible;
        if (active)
        {
            string title = telegramNoticeActive ? "Telegram · " + TelegramNoticeTitle.Text : ClipboardNoticeTitle.Text;
            string body = telegramNoticeActive ? TelegramNoticeText.Text : ClipboardNoticeText.Text;
            string signature = title + "\n" + body;
            CompactCardTitle.Text = title; CompactCardText.Text = body.Replace('\r', ' ').Replace('\n', ' ');
            CompactCardAction.Content = telegramNoticeActive ? "Открыть" : copiedNoticeFiles.Length > 0 ? "На полку" : "Буфер";
            if (signature != compactNoticeSignature)
            {
                compactNoticeSignature = signature;
                var titleMeasure = new TextBlock { Text = CompactCardTitle.Text, FontFamily = FontFamily, FontSize = 14, FontWeight = FontWeights.SemiBold };
                var bodyMeasure = new TextBlock { Text = CompactCardText.Text, FontFamily = FontFamily, FontSize = 13 };
                titleMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                bodyMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double scale = Math.Clamp(settings.CompactScale, 75, 150) / 100;
                double available = (handle == IntPtr.Zero ? SystemParameters.WorkArea.Width : Services.WindowInterop.WorkArea(handle).Width) / scale - 32;
                compactNotificationWidth = Math.Clamp(Math.Ceiling(Math.Max(titleMeasure.DesiredSize.Width, bodyMeasure.DesiredSize.Width)) + 208, Math.Min(360, available), Math.Min(1040, available));
                bool busy = !telegramNoticeActive && title.Contains("Переводим", StringComparison.Ordinal);
                StopTelegramPlaneFlight();
                CompactCardIcon.Fill = telegramNoticeActive ? new SolidColorBrush(Color.FromArgb(32, 240, 244, 248)) : Brushes.Transparent;
                CompactCardIcon.Stroke = (Brush)FindResource(telegramNoticeActive ? "Ink" : "Accent");
                CompactCardIcon.Data = Geometry.Parse(telegramNoticeActive ? "M 22,2 L 15,22 L 11,13 L 2,9 Z M 22,2 L 11,13" : busy ? "M 12,2 A 10,10 0 1 1 2,12" : noticeEntry?.Translation.Length > 0 ? "M 2,5 L 15,5 M 8,2 L 8,5 M 5,5 C 5,11 9,15 14,17 M 12,5 C 12,11 8,15 2,17 M 14,22 L 19,10 L 24,22 M 16,18 L 22,18" : "M 7,7 L 21,7 L 21,23 L 7,23 Z M 4,17 L 2,17 L 2,2 L 16,2 L 16,4");
                Motion.Enter(CompactCardTitle, 0, 240);
                Motion.Enter(CompactCardText, 4, 330);
                CompactCardGhostIcon.BeginAnimation(OpacityProperty, null); CompactCardGhostIcon.Opacity = 0;
                if (!telegramNoticeActive && !busy && noticeEntry?.Translation.Length == 0 && Motion.Enabled)
                {
                    CompactCardGhostIcon.Data = CompactCardIcon.Data; CompactCardGhostIcon.Opacity = .55;
                    var ghostShift = new TranslateTransform(); CompactCardGhostIcon.RenderTransform = ghostShift;
                    Motion.Tween(CompactCardGhostIcon, OpacityProperty, 0, 440, 80);
                    Motion.Tween(ghostShift, TranslateTransform.XProperty, -5, 440, 80);
                    Motion.Tween(ghostShift, TranslateTransform.YProperty, -4, 440, 80);
                }
                if (telegramNoticeActive) FlyTelegramPlane();
                else if (!busy) Motion.Pop(CompactCardIcon, .65, 460);
                else
                {
                    var rotation = new RotateTransform(); CompactCardIcon.RenderTransform = rotation;
                    if (Motion.Enabled) rotation.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
                }
            }
        }
        else
        {
            StopTelegramPlaneFlight();
            compactNoticeSignature = "";
            CompactCardTitle.Text = CompactCardText.Text = "";
        }
        if (transition && !expanded && IsLoaded) Motion.RevealContent(active ? (FrameworkElement)CompactCard : Header, 330, 150);
        if (!resizeShell || expanded || !IsLoaded || draggingWindow) return;
        var area = Services.WindowInterop.WorkArea(handle);
        double width = Math.Min(CompactPresentationWidth, area.Width), height = Math.Min(CompactPresentationHeight, area.Height);
        if ((!shellMoving || compactNoticeGeometryActive != active) && (Math.Abs(Width - width) > .5 || Math.Abs(Height - height) > .5)) AnimateExpansion(false);
    }

    private void CompactCardActionClick(object sender, RoutedEventArgs e)
    {
        if (!telegramNoticeActive && clipboardNoticeActive && copiedNoticeFiles.Length > 0) { AddPaths(copiedNoticeFiles); DismissClipboardNotice(this, EventArgs.Empty); return; }
        if (telegramNoticeActive) OpenTelegram(sender, e);
        else OpenClipboardNotice(sender, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
    }

    private void PauseCompactNotificationTimers()
    {
        clipboardNoticeTimer.Stop(); telegramNoticeTimer.Stop();
    }
    private void ResumeCompactNotificationTimers()
    {
        if (telegramNoticeActive) telegramNoticeTimer.Start();
        if (clipboardNoticeActive) clipboardNoticeTimer.Start();
    }
}
