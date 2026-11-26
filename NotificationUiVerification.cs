using System.Windows.Controls;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class MainWindow
{
    private async Task VerifySocialNotificationsAsync(Action<string, bool, string> check, string output)
    {
        settings.TelegramNotificationsEnabled = true;
        SetExpanded(true); Navigation.SelectedIndex = 0; await AwaitShellAsync();
        int clipboardEntries = clipboard.Count;
        ReceiveTelegramNotification(new(201, "Проверка VK", "Демонстрационное сообщение из ВКонтакте", DateTimeOffset.UtcNow, "vk", "VKMessenger", "VK Мессенджер"));
        check("VK notification identifies its own source and enters session history", CurrentSocialName == "ВКонтакте" && SocialNoticeSource.Text == "ВКонтакте" && notificationHistory.Count == 1 && notificationHistory[0].SourceId == "vk", "");
        check("social notifications never reach clipboard or change the active page", clipboard.Count == clipboardEntries && Navigation.SelectedIndex == 0, "");
        SetExpanded(false); await AwaitShellAsync(); await Task.Delay(200);
        check("VK compact card uses its own icon without a Telegram plane", CompactCard.IsVisible && CompactCardTitle.Text.StartsWith("ВКонтакте") && !planeFlying && !FlyingTelegramPlane.IsVisible && CompactCardIcon.Data.ToString() == System.Windows.Media.Geometry.Parse(CurrentSocialIcon).ToString(), "");
        check("VK popup displays its embedded full-color official brand asset", CompactCardBrand.IsVisible && ReferenceEquals(CompactCardBrand.Source, NotificationBrandIcons.Get("vk")) && CompactCardIcon.Opacity == 0, "");
        Capture(Path.Combine(output, "vk-notification.png"));
        ReceiveTelegramNotification(new(202, "Проверка Discord", "Демонстрационное сообщение Discord", DateTimeOffset.UtcNow, "discord", "com.squirrel.Discord.Discord", "Discord"));
        DismissTelegramNotice(this, EventArgs.Empty); await Task.Delay(100);
        check("mixed social queue advances to the correct source", CurrentSocialName == "Discord" && CompactCardTitle.Text.StartsWith("Discord") && !planeFlying, "");
        DismissTelegramNotice(this, EventArgs.Empty);
        ReceiveTelegramNotification(new(203, "Проверка браузера", "Источник сайта не угадывается по тексту VK", DateTimeOffset.UtcNow, "windows", "TestBrowser", "Тестовый браузер"));
        check("unselected browser cannot add notifications to history", notificationHistory.Count == 2 && !telegramNoticeActive, "");
        SetAdditionalNotificationApp("TestBrowser", true);
        ReceiveTelegramNotification(new(204, "Проверка браузера", "Демонстрационное сообщение браузера", DateTimeOffset.UtcNow, "windows", "TestBrowser", "Тестовый браузер"));
        check("selected browser is displayed under its actual app name", CurrentSocialName == "Тестовый браузер" && notificationHistory.Count == 3, "");
        DismissTelegramNotice(this, EventArgs.Empty);
        Navigation.SelectedIndex = 11; SetExpanded(true); await AwaitShellAsync();
        check("notifications page exposes the new session inbox", ContentTabs.SelectedIndex == 11 && NotificationList.IsVisible && !NotificationEmpty.IsVisible, "");
        NotificationFilter.SelectedIndex = 2;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(notificationHistory);
        check("inbox filter selects only VK", view.Cast<TelegramMessage>().Count() == 1 && view.Cast<TelegramMessage>().All(x => x.SourceId == "vk"), "");
        NotificationFilter.SelectedIndex = 0; UpdateLayout(); Capture(Path.Combine(output, "notification-inbox.png"));
        SetNotificationSource("vk", false);
        check("disabling VK removes its stored messages", notificationHistory.All(x => x.SourceId != "vk"), "");
        ReceiveTelegramNotification(new(205, "Отключённый VK", "Не показывать", DateTimeOffset.UtcNow, "vk"));
        check("disabled network cannot enqueue a new popup", !telegramNoticeActive && notificationHistory.Count == 2, "");
        SetNotificationSource("vk", true);
        OpenPreferences(7); await Task.Delay(250);
        var settingsTree = VisualDescendants<CheckBox>(preferences!).Select(x => System.Windows.Automation.AutomationProperties.GetName(x)).ToArray();
        check("notification settings contain only the three supported network switches", NotificationSources.All.All(x => settingsTree.Contains(x.Name)) && !settingsTree.Contains("Instagram") && !settingsTree.Contains("WhatsApp"), "");
        check("notification settings show all three full-color social logos", VisualDescendants<Image>(preferences!).Count(x => NotificationSources.All.Any(s => ReferenceEquals(x.Source, NotificationBrandIcons.Get(s.Id)))) == 3, "");
        CaptureVisual(preferences!, Path.Combine(output, "notification-sources.png"));
        preferences?.Close();
        for (uint i = 300; i < 415; i++) ReceiveTelegramNotification(new(i, "Проверка лимита", "Демонстрация", DateTimeOffset.UtcNow, "vk"));
        check("session inbox and notification queue have bounded capacity", notificationHistory.Count == 100 && telegramQueue.Count <= 20, "");
        DisableTelegramNotifications(); SetAdditionalNotificationApp("TestBrowser", false); NotificationFilter.SelectedIndex = 0; Navigation.SelectedIndex = 0;
        check("disabling the listener clears history and pending notifications", notificationHistory.Count == 0 && !telegramNoticeActive && telegramQueue.Count == 0, "");
    }
}
