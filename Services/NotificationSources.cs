using System.Security.Cryptography;
using System.Text;

namespace DesktopIsland.Services;

public sealed record WindowsNotificationApp(string Id, string Name)
{
    public System.Windows.Media.ImageSource? Icon { get; init; }
}
internal sealed record NotificationSource(string Id, string Name, string Icon, string Address);

internal static class NotificationSources
{
    internal const string Bell = "M 5,17 L 5,10 A 7,7 0 0 1 19,10 L 19,17 L 22,20 L 2,20 Z M 9,23 L 15,23";
    internal static readonly NotificationSource[] All =
    [
        new("telegram", "Telegram", "M 22,2 L 15,22 L 11,13 L 2,9 Z M 22,2 L 11,13", "tg://"),
        new("vk", "ВКонтакте", "M 2,5 L 2,16 Q 2,22 9,22 L 9,5 M 14,5 L 14,22 M 22,5 L 14,14 L 23,22", "https://vk.com/im"),
        new("discord", "Discord", "M 5,5 Q 12,1 19,5 L 23,19 L 18,22 L 16,19 L 8,19 L 6,22 L 1,19 Z M 8,10 L 8,14 M 16,10 L 16,14", "discord://")
    ];
    internal static NotificationSource? Find(string id) => All.FirstOrDefault(x => x.Id == id);
    internal static NotificationSource? Identify(string appId, string name)
    {
        string n = name.Trim();
        if (TelegramNotificationService.IsTelegram(appId, n)) return Find("telegram");
        if (n.Equals("VK", StringComparison.OrdinalIgnoreCase) || n.Equals("ВКонтакте", StringComparison.OrdinalIgnoreCase) || n.Equals("VK Мессенджер", StringComparison.OrdinalIgnoreCase) || n.Equals("VK Messenger", StringComparison.OrdinalIgnoreCase) || appId.Contains("VKMessenger", StringComparison.OrdinalIgnoreCase) || appId.StartsWith("VKontakte.", StringComparison.OrdinalIgnoreCase)) return Find("vk");
        if (n.Equals("Discord", StringComparison.OrdinalIgnoreCase) || appId.StartsWith("com.squirrel.Discord.", StringComparison.OrdinalIgnoreCase)) return Find("discord");
        return null;
    }
    internal static bool Enabled(AppSettings settings, string sourceId, string appId) => sourceId == "windows"
        ? settings.AdditionalNotificationApps.Contains(appId, StringComparer.OrdinalIgnoreCase)
        : Find(sourceId) != null && settings.NotificationSources.Contains(sourceId, StringComparer.Ordinal);
}

// Disabled sources are tracked by metadata alone. Enabling one never replays
// notifications that were already sitting in Action Center.
internal sealed class NotificationDeduplicator
{
    private readonly Dictionary<uint, (string Key, string? Signature)> seen = [];
    private static string Key(string appId, long created) => appId + "\n" + created;
    internal void Skip(uint id, string appId, long created) => seen[id] = (Key(appId, created), null);
    internal bool Observe(uint id, string appId, long created, string title, string body, bool baseline)
    {
        string key = Key(appId, created);
        string signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(title + "\n" + body)));
        bool exists = seen.TryGetValue(id, out var previous) && previous.Key == key;
        bool changed = !exists || previous.Signature != null && previous.Signature != signature;
        seen[id] = (key, signature);
        return !baseline && changed;
    }
    internal void Retain(HashSet<uint> present)
    {
        foreach (uint id in seen.Keys.Where(x => !present.Contains(x)).ToArray()) seen.Remove(id);
        while (seen.Count > 1024) seen.Remove(seen.Keys.First());
    }
    internal void Clear() => seen.Clear();
}
