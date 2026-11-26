using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopIsland.Services;

public sealed record TelegramMessage(uint Id, string Title, string Body, DateTimeOffset Created, string SourceId = "telegram", string AppId = "", string AppName = "")
{
    public string SourceName => SourceId == "windows" ? AppName : NotificationSources.Find(SourceId)?.Name ?? "Уведомление";
    public string When => Created.ToLocalTime().ToString("HH:mm · dd.MM");
    public ImageSource? AppIcon { get; init; }
    public ImageSource Icon => NotificationBrandIcons.Get(SourceId) ?? AppIcon ?? NotificationBrandIcons.Fallback;
}

public sealed class TelegramNotificationService : IDisposable
{
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool subscribed;
    private readonly NotificationDeduplicator seen = new();
    private readonly Dictionary<string, WindowsNotificationApp> apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?> appIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim scanGate = new(1, 1);
    private bool disposed;
    private int generation;
    private bool lastScanSucceeded;
    public string Status { get; private set; } = "Подключение не включено";
    public bool Connected { get; private set; }
    public int ReceivedCount { get; private set; }
    public event Action<TelegramMessage>? Received;
    public Func<string, string, bool> SourceEnabled { get; set; } = (source, _) => source == "telegram";
    public WindowsNotificationApp[] ObservedApplications => apps.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    public static bool HasIdentity
    {
        get { try { return Windows.ApplicationModel.Package.Current.Id.Name == "DesktopIsland.Desktop"; } catch (Exception) { return false; } }
    }

    public TelegramNotificationService() => poll.Tick += async (_, _) => await ScanAsync(false);

    public async Task<string> ConnectAsync(bool requestAccess)
    {
        int revision = ++generation;
        poll.Stop(); Connected = false;
        if (!HasIdentity) return Status = "Сначала подключите пакет Windows кнопкой ниже, затем перезапустите остров.";
        try
        {
            var listener = UserNotificationListener.Current;
            var access = requestAccess ? await listener.RequestAccessAsync() : listener.GetAccessStatus();
            if (disposed || revision != generation) return Status;
            if (access != UserNotificationListenerAccessStatus.Allowed)
                return Status = access == UserNotificationListenerAccessStatus.Denied ? "Доступ запрещён. Разрешите Desktop Island доступ к уведомлениям в параметрах Windows." : "Нужно разрешить доступ к уведомлениям Windows.";
            ReceivedCount = 0; Connected = true;
            if (!subscribed)
            {
                try { listener.NotificationChanged += NotificationChanged; subscribed = true; }
                catch (System.Runtime.InteropServices.COMException) { /* Polling remains available on desktop builds without events. */ }
            }
            await ScanAsync(true);
            if (disposed || revision != generation) return Status;
            if (!lastScanSucceeded) Connected = false;
            if (Connected) { poll.Start(); Status = "Подключено к уведомлениям Windows. Получено: " + ReceivedCount + "."; }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException or NotImplementedException)
        { Status = "Windows не предоставила доступ к уведомлениям (0x" + ex.HResult.ToString("X8") + ")."; }
        return Status;
    }

    private async Task ScanAsync(bool baseline)
    {
        if (disposed || !Connected || !baseline && scanGate.CurrentCount == 0) return;
        int revision = generation;
        await scanGate.WaitAsync();
        if (disposed || !Connected || revision != generation) { scanGate.Release(); return; }
        lastScanSucceeded = false;
        try
        {
            if (baseline) seen.Clear();
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            { Disconnect(); Status = "Доступ к уведомлениям отозван в Windows."; return; }
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (disposed || !Connected || revision != generation) return;
            var present = new HashSet<uint>();
            foreach (var notification in notifications)
            {
                try
                {
                    var appInfo = notification.AppInfo;
                    if (appInfo == null) continue;
                    string appId = appInfo.AppUserModelId ?? "";
                    string appName = appInfo.DisplayInfo?.DisplayName ?? "";
                    if (appId.Length == 0) continue;
                    present.Add(notification.Id);
                    if (appId.Length > 0 && appName.Length > 0 && apps.Count < 128) apps[appId] = new(appId, appName);
                    var source = NotificationSources.Identify(appId, appName);
                    string sourceId = source?.Id ?? "windows";
                    // Classify by Windows metadata, never by sender or message body.
                    if (!SourceEnabled(sourceId, appId)) { seen.Skip(notification.Id, appId, notification.CreationTime.UtcTicks); continue; }
                    var visual = notification.Notification.Visual;
                    var binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric)
                        ?? visual.Bindings.FirstOrDefault(x => x.GetTextElements().Count > 0);
                    if (binding == null) continue;
                    string[] parts = binding.GetTextElements().Select(x => x.Text ?? "").ToArray();
                    string title = parts.FirstOrDefault() ?? source?.Name ?? appName;
                    string body = string.Join("\n", parts.Skip(1));
                    if (title.Length > 256) title = title[..256];
                    if (body.Length > 1200) body = body[..1200];
                    bool changed = seen.Observe(notification.Id, appId, notification.CreationTime.UtcTicks, title, body, baseline);
                    if (changed && (title.Length > 0 || body.Length > 0))
                    {
                        var icon = source == null ? await ReadAppIconAsync(appId, appInfo.DisplayInfo) : NotificationBrandIcons.Get(sourceId);
                        if (disposed || !Connected || revision != generation) return;
                        if (!SourceEnabled(sourceId, appId)) continue;
                        if (apps.ContainsKey(appId)) apps[appId] = new(appId, appName) { Icon = icon };
                        ReceivedCount++; Status = "Подключено к уведомлениям Windows. Получено: " + ReceivedCount + ".";
                        Received?.Invoke(new(notification.Id, title, body, notification.CreationTime, sourceId, appId, appName) { AppIcon = icon });
                    }
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or NotImplementedException) { }
            }
            seen.Retain(present);
            lastScanSucceeded = true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException or NotImplementedException)
        { Status = "Не удалось прочитать уведомления Windows. Подключитесь повторно."; }
        finally { scanGate.Release(); }
    }

    internal static bool IsTelegram(string appId, string name) => appId.Contains("telegram", StringComparison.OrdinalIgnoreCase) || name.Equals("Telegram", StringComparison.OrdinalIgnoreCase) || name.Equals("Telegram Desktop", StringComparison.OrdinalIgnoreCase);
    private async Task<BitmapSource?> ReadAppIconAsync(string id, Windows.ApplicationModel.AppDisplayInfo? info)
    {
        if (info == null) return null;
        if (appIcons.TryGetValue(id, out var cached)) return cached;
        BitmapSource? icon = null;
        try
        {
            using var input = await info.GetLogo(new Windows.Foundation.Size(64, 64)).OpenReadAsync();
            using var stream = input.AsStreamForRead();
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64; image.StreamSource = stream; image.EndInit(); image.Freeze(); icon = image;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or IOException or InvalidOperationException or ArgumentException or NotSupportedException) { }
        if (appIcons.Count >= 128) appIcons.Remove(appIcons.Keys.First());
        appIcons[id] = icon;
        return icon;
    }
    private void NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (!disposed && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(new Action(async () => await ScanAsync(false)));
    }
    public void Disconnect() { ++generation; poll.Stop(); seen.Clear(); Connected = false; Status = "Подключение выключено"; }
    public void Dispose() { disposed = true; Disconnect(); if (subscribed) { try { UserNotificationListener.Current.NotificationChanged -= NotificationChanged; } catch (System.Runtime.InteropServices.COMException) { } } }
}
