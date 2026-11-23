namespace DesktopIsland;

public partial class MainWindow
{
    private async Task VerifyDeviceNoticeAsync(Action<string, bool, string> check, string output)
    {
        bool reduced = Motion.Reduced;
        Motion.Reduced = false;
        SetExpanded(false); await AwaitShellAsync();
        int tab = Navigation.SelectedIndex, entries = clipboard.Count;
        ApplyUsbSnapshot(["USB\\VID_05AC&PID_12A8\\test-phone"]); UpdateLayout();
        check("an iPhone already connected on startup triggers a notice", DeviceNoticeVisible && usbConnections.Count == 1 && UsbPhoneStatus.Visibility == Visibility.Visible, "");
        check("iPhone notice uses a static phone icon without an action button", DeviceNoticeVisible && CompactCard.IsVisible && CompactCardTitle.Text == "iPhone подключён" && CompactCardText.Text == "По USB" && CompactCardAction.Visibility == Visibility.Collapsed && !CompactCardIcon.HasAnimatedProperties && !CompactCard.HasAnimatedProperties && !shellMoving && CompactSnapshot.Source == null, "");
        check("device notification does not navigate or use clipboard history", Navigation.SelectedIndex == tab && clipboard.Count == entries, "");
        check("device notification hides playback header and fits available space", Header.Visibility == Visibility.Collapsed && CompactCardTitle.ActualWidth > 100 && Height <= CompactPresentationHeight + .1, "");
        await Task.Delay(100); Capture(Path.Combine(output, "iphone-usb-notice.png"));
        PauseCompactNotificationTimers(); UpdateCompactNotification(false);
        check("hover pauses the USB notice until pointer leaves", !deviceNoticeTimer.IsEnabled, "");
        ResumeCompactNotificationTimers();
        var copied = new ClipboardEntry { Text = "Проверка приоритета" };
        ShowClipboardNotice(copied, "Текст скопирован", copied.Text, 5);
        check("clipboard notifications take priority and restore their action", !DeviceNoticeVisible && CompactCardTitle.Text == "Текст скопирован" && CompactCardAction.Visibility == Visibility.Visible && !deviceNoticeTimer.IsEnabled, "");
        DismissClipboardNotice(this, EventArgs.Empty);
        check("USB notice resumes after another notification", DeviceNoticeVisible && CompactCardTitle.Text == "iPhone подключён" && deviceNoticeTimer.IsEnabled, "");
        SetExpanded(true); await AwaitShellAsync();
        check("USB notice is visible above expanded content without changing tabs", DeviceNoticeVisible && CompactCard.IsVisible && System.Windows.Controls.Grid.GetRow(CompactCard) == 1 && Navigation.SelectedIndex == tab, "");
        DismissDeviceNotice(this, EventArgs.Empty);
        SetExpanded(false); await AwaitShellAsync();
        ShowIphoneNotice();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (deviceNoticeActive && watch.Elapsed < TimeSpan.FromSeconds(6)) await Task.Delay(50);
        check("USB notice expires after five seconds and restores compact content instantly", !deviceNoticeActive && Header.IsVisible && !CompactCard.IsVisible && !shellMoving && CompactSnapshot.Source == null && watch.Elapsed.TotalSeconds >= 4.9, watch.Elapsed.TotalSeconds.ToString("0.00"));
        UpdateLayout();
        var phoneBounds = UsbPhoneStatus.TransformToAncestor(Header).TransformBounds(new Rect(UsbPhoneStatus.RenderSize));
        check("connected iPhone keeps a visible status icon inside the compact header", UsbPhoneStatus.IsVisible && phoneBounds.Right <= Header.ActualWidth && phoneBounds.Left >= 0, "");
        Capture(Path.Combine(output, "iphone-usb-status.png"));
        UsbPhoneStatus.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check("clicking phone status reopens its static notification", DeviceNoticeVisible && CompactCardTitle.Text == "iPhone подключён" && !CompactCardIcon.HasAnimatedProperties, "");
        ApplyUsbSnapshot([]); UpdateLayout();
        check("unplugging clears both status icon and stale connection notice", usbConnections.Count == 0 && UsbPhoneStatus.Visibility == Visibility.Collapsed && !deviceNoticeActive && Header.IsVisible, "");
        Motion.Reduced = reduced;
    }
}
