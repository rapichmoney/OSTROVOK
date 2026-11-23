using System.ComponentModel;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly UsbIphoneConnections usbConnections = new();
    private readonly CancellationTokenSource usbCancellation = new();
    private readonly DispatcherTimer usbPoll = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer usbDebounce = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer deviceNoticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool usbScanning, usbScanAgain, deviceNoticeActive, deviceNoticeWasVisible, deviceNoticePaused;
    private int usbRetries;
    private bool DeviceNoticeVisible => deviceNoticeActive && !UpdateNoticeActive && !telegramNoticeActive && !(clipboardNoticeActive && settings.ClipboardNotifications);

    private void InitializeDeviceNotices()
    {
        deviceNoticeTimer.Tick += DismissDeviceNotice;
        usbPoll.Tick += async (_, _) => await ScanUsbAsync();
        usbDebounce.Tick += async (_, _) =>
        {
            usbDebounce.Stop();
            await ScanUsbAsync();
            if (!closing && usbRetries-- > 0) { usbDebounce.Interval = TimeSpan.FromSeconds(2); usbDebounce.Start(); }
        };
        if (App.VerificationMode) return;
        usbPoll.Start();
        _ = ScanUsbAsync();
    }

    private void UsbDevicesChanged()
    {
        if (closing || App.VerificationMode) return;
        usbRetries = 3;
        usbDebounce.Stop(); usbDebounce.Interval = TimeSpan.FromMilliseconds(650); usbDebounce.Start();
    }

    private async Task ScanUsbAsync()
    {
        if (closing) return;
        if (usbScanning) { usbScanAgain = true; return; }
        usbScanning = true;
        try
        {
            do
            {
                usbScanAgain = false;
                var token = usbCancellation.Token;
                var identities = await Task.Run(() => UsbIphoneMonitor.ReadConnected(token), token);
                if (!closing) ApplyUsbSnapshot(identities);
            } while (usbScanAgain && !closing);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is Win32Exception or System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        { App.Diagnostics?.Error(DiagnosticArea.Devices, ex); }
        finally { usbScanning = false; }
    }

    private void ShowIphoneNotice()
    {
        if (closing) return;
        deviceNoticeTimer.Stop(); deviceNoticeActive = true;
        App.Diagnostics?.Event(DiagnosticEvent.UsbNoticeShown, DiagnosticArea.Devices, usbConnections.Count);
        UpdateClipboardNoticeVisibility();
    }

    private void ApplyUsbSnapshot(string[] identities)
    {
        int before = usbConnections.Count;
        int arrived = usbConnections.Observe(identities);
        UsbPhoneStatus.Visibility = usbConnections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UsbPhoneStatus.ToolTip = usbConnections.Count > 1 ? "iPhone по USB: " + usbConnections.Count : "iPhone подключён по USB";
        if (before != usbConnections.Count)
        {
            App.Diagnostics?.Event(DiagnosticEvent.UsbConnectionCount, DiagnosticArea.Devices, usbConnections.Count);
            if (IsLoaded && !expanded && !HasCompactNotification) UpdateCompactNotification(true);
        }
        if (arrived > 0) ShowIphoneNotice();
        else if (usbConnections.Count == 0 && deviceNoticeActive) DismissDeviceNotice(this, EventArgs.Empty);
    }

    private void OpenIphoneStatus(object sender, RoutedEventArgs e)
    {
        if (usbConnections.Count > 0) ShowIphoneNotice();
        e.Handled = true;
    }

    private void DismissDeviceNotice(object? sender, EventArgs e)
    {
        deviceNoticeTimer.Stop(); deviceNoticeActive = false;
        UpdateClipboardNoticeVisibility();
    }

    private void RenderDeviceNotice()
    {
        StopTelegramPlaneFlight(); StopCompactTransition();
        Motion.ClearContentMotion(CompactCardTitle); Motion.ClearContentMotion(CompactCardText); Motion.ClearContentMotion(CompactCardIcon);
        PolishedMotion.CancelSwap(CompactCardTitle); PolishedMotion.CancelSwap(CompactCardText);
        CompactCardTitle.Text = "iPhone подключён";
        CompactCardText.Text = "По USB";
        CompactCardIcon.Data = Geometry.Parse("M 7,1 L 17,1 Q 20,1 20,4 L 20,22 Q 20,25 17,25 L 7,25 Q 4,25 4,22 L 4,4 Q 4,1 7,1 Z M 9,4 L 15,4 M 10,22 L 14,22");
        CompactCardIcon.Stroke = (Brush)FindResource("Ink"); CompactCardIcon.Fill = Brushes.Transparent;
        CompactCardIcon.StrokeDashArray = null; CompactCardIcon.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null); CompactCardIcon.StrokeDashOffset = 0;
        CompactCardGhostIcon.BeginAnimation(OpacityProperty, null); CompactCardGhostIcon.Opacity = 0;
        CompactCardAction.Visibility = Visibility.Collapsed;
        NoticeQueueCount.Text = ""; NoticeLifetime.Visibility = Visibility.Collapsed;
        NoticeLifetimeScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null); noticeLifetimeClock = null;
        compactNotificationWidth = 360; compactNoticeSignature = "iphone-usb";
        if (!deviceNoticeTimer.IsEnabled && !deviceNoticePaused) deviceNoticeTimer.Start();
    }

    private void StopDeviceNotices()
    {
        usbPoll.Stop(); usbDebounce.Stop(); deviceNoticeTimer.Stop(); usbCancellation.Cancel();
        usbCancellation.Dispose();
    }
}
