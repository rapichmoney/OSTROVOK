using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopIsland;

public partial class MainWindow
{
    private bool compactNoticeGeometryActive;
    private string compactNoticeSignature = "";
    private double compactNotificationWidth = 460;
    private bool HasCompactNotification => UpdateNoticeActive || telegramNoticeActive || (clipboardNoticeActive && settings.ClipboardNotifications) || deviceNoticeActive;
    private double CompactPresentationWidth => trackVisualPending && !HasCompactNotification ? heldWindowWidth : HasCompactNotification ? Math.Max(StandbyCompactWidth, compactNotificationWidth * Math.Clamp(settings.CompactScale, 75, 150) / 100) : StandbyCompactWidth;
    private double CompactPresentationHeight => HasCompactNotification ? 72 * Math.Clamp(settings.CompactScale, 75, 150) / 100 : CompactHeight;

    private void UpdateCompactNotification(bool resizeShell)
    {
        bool reduced = Motion.Reduced;
        bool visible = DeviceNoticeVisible;
        bool instant = visible || deviceNoticeWasVisible;
        if (instant)
        {
            Motion.Reduced = true;
            if (visible != deviceNoticeWasVisible && shellMoving) StopShellMotion(true);
        }
        try { UpdateCompactNotificationCore(resizeShell); }
        finally { deviceNoticeWasVisible = visible; Motion.Reduced = reduced; }
    }

    private void UpdateCompactNotificationCore(bool resizeShell)
    {
        bool active = HasCompactNotification;
        NoticeLifetime.Visibility = active && !expanded && Motion.Enabled ? Visibility.Visible : Visibility.Collapsed;
        if (active) CancelTrackVisualHold();
        bool transition = compactVisualNotice != active;
        if (transition) { CaptureCompactTransition(active); compactVisualNotice = active; }
        Header.Visibility = !expanded && !collapseContentActive && !active ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Controls.Grid.SetRow(CompactCard, expanded && (UpdateNoticeActive || DeviceNoticeVisible) ? 1 : 0);
        CompactCard.MinHeight = expanded && (UpdateNoticeActive || DeviceNoticeVisible) ? 56 : 0;
        CompactCard.Visibility = !collapseContentActive && active && (!expanded || UpdateNoticeActive || DeviceNoticeVisible) ? Visibility.Visible : Visibility.Collapsed;
        CompactLeftHandle.Visibility = CompactRightHandle.Visibility = expanded || active ? Visibility.Collapsed : Visibility.Visible;
        UpdateLaterButton.Visibility = Visibility.Collapsed; CompactCardAction.IsEnabled = true;
        CompactCardAction.Visibility = Visibility.Visible;
        CompactCardAction.VerticalAlignment = VerticalAlignment.Center;
        CompactCardBrand.Source = telegramNoticeActive && !UpdateNoticeActive ? currentSocialNotice?.Icon : null;
        CompactCardBrand.Visibility = CompactCardBrand.Source == null ? Visibility.Collapsed : Visibility.Visible;
        if (!DeviceNoticeVisible) deviceNoticeTimer.Stop();
        if (UpdateNoticeActive) RenderUpdateNotice();
        else if (DeviceNoticeVisible) RenderDeviceNotice();
        else if (active)
        {
            string title = telegramNoticeActive ? CurrentSocialName + " · " + TelegramNoticeTitle.Text : ClipboardNoticeTitle.Text;
            string body = telegramNoticeActive ? TelegramNoticeText.Text : ClipboardNoticeText.Text;
            string signature = title + "\n" + body;
            if (CompactCardTitle.Text != title) PolishedMotion.Swap(CompactCardTitle, () => CompactCardTitle.Text = title, 380, 6);
            string flatBody = body.Replace('\r', ' ').Replace('\n', ' ');
            if (CompactCardText.Text != flatBody) PolishedMotion.Swap(CompactCardText, () => CompactCardText.Text = flatBody, 380, 6);
            UpdateNotificationQueue();
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
                CompactCardIcon.Fill = Brushes.Transparent;
                CompactCardIcon.Stroke = (Brush)FindResource(telegramNoticeActive ? "Ink" : "Accent");
                CompactCardIcon.Data = Geometry.Parse(telegramNoticeActive ? CurrentSocialIcon : busy ? "M 12,2 A 10,10 0 1 1 2,12" : noticeEntry?.Translation.Length > 0 ? "M 2,5 L 15,5 M 8,2 L 8,5 M 5,5 C 5,11 9,15 14,17 M 12,5 C 12,11 8,15 2,17 M 14,22 L 19,10 L 24,22 M 16,18 L 22,18" : "M 7,7 L 21,7 L 21,23 L 7,23 Z M 4,17 L 2,17 L 2,2 L 16,2 L 16,4");
                StartNoticeLifetime(telegramNoticeActive ? telegramNoticeTimer.Interval : clipboardNoticeTimer.Interval);
                CompactCardGhostIcon.BeginAnimation(OpacityProperty, null); CompactCardGhostIcon.Opacity = 0;
                if (!telegramNoticeActive && !busy && noticeEntry?.Translation.Length == 0 && Motion.Enabled)
                {
                    CompactCardGhostIcon.Data = CompactCardIcon.Data; CompactCardGhostIcon.Opacity = .55;
                    var ghostShift = new TranslateTransform(); CompactCardGhostIcon.RenderTransform = ghostShift;
                    Motion.Tween(CompactCardGhostIcon, OpacityProperty, 0, 440, 80);
                    Motion.Tween(ghostShift, TranslateTransform.XProperty, -5, 440, 80);
                    Motion.Tween(ghostShift, TranslateTransform.YProperty, -4, 440, 80);
                }
                if (!telegramNoticeActive && !busy && noticeEntry?.Translation.Length > 0)
                {
                    CompactCardIcon.Data = Geometry.Parse("M 2,12 L 9,19 L 23,3");
                    CompactCardIcon.StrokeDashArray = new DoubleCollection { 24, 24 };
                    CompactCardIcon.StrokeDashOffset = 24;
                    Motion.Tween(CompactCardIcon, System.Windows.Shapes.Shape.StrokeDashOffsetProperty, 0, 340, completed: () => CompactCardIcon.StrokeDashArray = null);
                }
                else { CompactCardIcon.StrokeDashArray = null; CompactCardIcon.StrokeDashOffset = 0; }
                if (telegramNoticeActive && CurrentNoticeIsTelegram && transition) FlyTelegramPlane();
                else if (telegramNoticeActive) CompactCardIcon.Opacity = CompactCardBrand.Source != null ? 0 : 1;
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
            NoticeLifetime.Visibility = Visibility.Collapsed; NoticeLifetimeScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null); noticeLifetimeClock = null;
        }
        if (transition && !expanded && IsLoaded) Motion.RevealContent(active ? (FrameworkElement)CompactCard : Header, 330, 150);
        if (!resizeShell || expanded || !IsLoaded || draggingWindow) return;
        var area = Services.WindowInterop.WorkArea(handle);
        double width = Math.Min(CompactPresentationWidth, area.Width), height = Math.Min(CompactPresentationHeight, area.Height);
        if ((!shellMoving || compactNoticeGeometryActive != active) && (Math.Abs(Width - width) > .5 || Math.Abs(Height - height) > .5)) AnimateExpansion(false);
    }

    private async void CompactCardActionClick(object sender, RoutedEventArgs e)
    {
        if (UpdateNoticeActive) { await InstallAppUpdateAsync(); return; }
        if (!telegramNoticeActive && clipboardNoticeActive && copiedNoticeFiles.Length > 0) { AddPaths(copiedNoticeFiles); DismissClipboardNotice(this, EventArgs.Empty); return; }
        if (telegramNoticeActive) OpenTelegram(sender, e);
        else OpenClipboardNotice(sender, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
    }

    private void PauseCompactNotificationTimers()
    {
        clipboardNoticeTimer.Stop(); telegramNoticeTimer.Stop();
        deviceNoticePaused = true; deviceNoticeTimer.Stop();
        noticeLifetimeClock?.Controller?.Pause();
    }
    private void ResumeCompactNotificationTimers()
    {
        noticeLifetimeClock?.Controller?.Resume();
        deviceNoticePaused = false;
        if (telegramNoticeActive) telegramNoticeTimer.Start();
        if (clipboardNoticeActive) clipboardNoticeTimer.Start();
        if (DeviceNoticeVisible) deviceNoticeTimer.Start();
    }
}
