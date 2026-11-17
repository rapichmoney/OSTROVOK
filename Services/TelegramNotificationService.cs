using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;

namespace DesktopIsland.Services;

public sealed record TelegramMessage(uint Id, string Title, string Body, DateTimeOffset Created);

public sealed class TelegramNotificationService : IDisposable
{
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool subscribed;
    private readonly Dictionary<uint, string> seen = [];
    private bool scanning, disposed;
    private bool lastScanSucceeded;
    public string Status { get; private set; } = "Подключение не включено";
    public bool Connected { get; private set; }
    public int ReceivedCount { get; private set; }
    public event Action<TelegramMessage>? Received;
    public static bool HasIdentity
    {
        get { try { return Windows.ApplicationModel.Package.Current.Id.Name == "DesktopIsland.Desktop"; } catch (Exception) { return false; } }
    }

    public TelegramNotificationService() => poll.Tick += async (_, _) => await ScanAsync(false);

    public async Task<string> ConnectAsync(bool requestAccess)
    {
        poll.Stop(); Connected = false;
        if (!HasIdentity) return Status = "Сначала подключите пакет Windows кнопкой ниже, затем перезапустите остров.";
        try
        {
            var listener = UserNotificationListener.Current;
            var access = requestAccess ? await listener.RequestAccessAsync() : listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed)
                return Status = access == UserNotificationListenerAccessStatus.Denied ? "Доступ запрещён. Разрешите Desktop Island доступ к уведомлениям в параметрах Windows." : "Нужно разрешить доступ к уведомлениям Windows.";
            seen.Clear(); ReceivedCount = 0; Connected = true;
            if (!subscribed)
            {
                try { listener.NotificationChanged += NotificationChanged; subscribed = true; }
                catch (System.Runtime.InteropServices.COMException) { /* Polling remains available on desktop builds without events. */ }
            }
            await ScanAsync(true);
            if (!lastScanSucceeded) Connected = false;
            if (Connected) { poll.Start(); Status = "Подключено. Ожидаем новые системные уведомления Telegram."; }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        { Status = "Windows не предоставила доступ к уведомлениям (0x" + ex.HResult.ToString("X8") + ")."; }
        return Status;
    }

    private async Task ScanAsync(bool baseline)
    {
        if (scanning || disposed || !Connected) return;
        scanning = true;
        lastScanSucceeded = false;
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            { Disconnect(); Status = "Доступ к уведомлениям отозван в Windows."; return; }
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var present = new HashSet<uint>();
            foreach (var notification in notifications)
            {
                try
                {
                    // Filter the source before reading any notification text.
                    if (!IsTelegram(notification.AppInfo.AppUserModelId, notification.AppInfo.DisplayInfo.DisplayName)) continue;
                    var visual = notification.Notification.Visual;
                    var binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric)
                        ?? visual.Bindings.FirstOrDefault(x => x.GetTextElements().Count > 0);
                    if (binding == null) continue;
                    string[] parts = binding.GetTextElements().Select(x => x.Text ?? "").ToArray();
                    string title = parts.FirstOrDefault() ?? "Telegram";
                    string body = string.Join("\n", parts.Skip(1));
                    present.Add(notification.Id);
                    string signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notification.CreationTime.UtcTicks + "\n" + title + "\n" + body)));
                    bool changed = !seen.TryGetValue(notification.Id, out string? previous) || previous != signature;
                    seen[notification.Id] = signature;
                    if (!baseline && changed && (title.Length > 0 || body.Length > 0))
                    {
                        ReceivedCount++; Status = "Подключено. Получено уведомлений Telegram: " + ReceivedCount + ".";
                        Received?.Invoke(new(notification.Id, title, body, notification.CreationTime));
                    }
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException) { }
            }
            foreach (uint old in seen.Keys.Where(x => !present.Contains(x)).ToArray()) seen.Remove(old);
            lastScanSucceeded = true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        { Status = "Не удалось прочитать уведомления Windows. Подключитесь повторно."; }
        finally { scanning = false; }
    }

    internal static bool IsTelegram(string appId, string name) => appId.Contains("telegram", StringComparison.OrdinalIgnoreCase) || name.Equals("Telegram", StringComparison.OrdinalIgnoreCase) || name.Equals("Telegram Desktop", StringComparison.OrdinalIgnoreCase);
    private void NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (!disposed && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(new Action(async () => await ScanAsync(false)));
    }
    public void Disconnect() { poll.Stop(); seen.Clear(); Connected = false; Status = "Подключение выключено"; }
    public void Dispose() { disposed = true; Disconnect(); if (subscribed) { try { UserNotificationListener.Current.NotificationChanged -= NotificationChanged; } catch (System.Runtime.InteropServices.COMException) { } } }
}
