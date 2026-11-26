using System.Windows.Media;
using DesktopIsland.Services;

namespace DesktopIsland;

internal static class NotificationTests
{
    internal static void Run(Action<string, bool, string> check)
    {
        check("Windows source catalog has valid functional icons and fixed launch addresses", NotificationSources.All.All(x => Geometry.Parse(x.Icon) != null && Uri.TryCreate(x.Address, UriKind.Absolute, out _)), "");
        check("three supported social logos are bundled frozen images", NotificationSources.All.Select(x => x.Id).SequenceEqual(new[] { "telegram", "vk", "discord" }) && NotificationSources.All.All(x => NotificationBrandIcons.Get(x.Id) is { PixelWidth: >= 32, PixelHeight: >= 32, IsFrozen: true }), "");
        check("unknown Windows apps do not receive a made-up network logo", NotificationBrandIcons.Get("windows") == null && NotificationBrandIcons.Get("unknown") == null, "");
        check("VK is identified by app metadata", NotificationSources.Identify("VKMessenger.App", "VK Мессенджер")?.Id == "vk", "");
        check("Discord remains supported and removed integrations have no catalog entries or bundled logos", NotificationSources.Identify("com.squirrel.Discord.Discord", "Discord")?.Id == "discord" && NotificationSources.Find("instagram") == null && NotificationSources.Find("whatsapp") == null && NotificationBrandIcons.Get("instagram") == null && NotificationBrandIcons.Get("whatsapp") == null, "");
        check("browser notifications do not get attributed to a guessed social network", NotificationSources.Identify("Yandex.Browser", "Яндекс") == null && NotificationSources.Identify("Chrome", "Google Chrome") == null && NotificationSources.Identify("Outlook", "Outlook") == null, "");
        var settings = new AppSettings { NotificationSources = ["vk"] };
        check("per-network switches exclude disabled sources", NotificationSources.Enabled(settings, "vk", "VKMessenger") && !NotificationSources.Enabled(settings, "telegram", "TelegramDesktop"), "");
        check("unknown Windows apps require explicit selection", !NotificationSources.Enabled(settings, "windows", "Yandex.Browser"), "");
        settings.AdditionalNotificationApps = ["Yandex.Browser"];
        check("explicit Windows app selection matches the exact app identity", NotificationSources.Enabled(settings, "windows", "yandex.browser") && !NotificationSources.Enabled(settings, "windows", "other.browser"), "");
        var seen = new NotificationDeduplicator();
        check("existing Windows notifications establish a quiet baseline", !seen.Observe(1, "vk", 100, "Sender", "old", true), "");
        check("the same notification is not delivered twice", !seen.Observe(1, "vk", 100, "Sender", "old", false), "");
        check("new notification text is delivered once", seen.Observe(1, "vk", 100, "Sender", "new", false) && !seen.Observe(1, "vk", 100, "Sender", "new", false), "");
        seen.Skip(2, "browser", 101);
        check("enabling a source never replays its existing toast", !seen.Observe(2, "browser", 101, "Sender", "already present", false), "");
        check("reused notification IDs with new creation time are new messages", seen.Observe(2, "browser", 102, "Sender", "already present", false), "");
        check("different Windows apps cannot collide by notification ID", seen.Observe(2, "other", 102, "Sender", "already present", false), "");
        seen.Retain([]);
        check("removed notifications leave the deduplication cache", seen.Observe(1, "vk", 100, "Sender", "new", false), "");
    }
}
