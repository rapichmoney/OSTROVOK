using DiscordRPC;
using System.Text;

namespace DesktopIsland.Services;

public sealed class DiscordPresenceService : IDisposable
{
    private DiscordRpcClient? client;
    private string applicationId = "";
    private long updatedAt;
    public string Status { get; private set; } = "Выключено";
    public static bool ValidApplicationId(string id) => id.Length is >= 17 and <= 20 && id.All(char.IsAsciiDigit);
    private static string Limit(string text)
    {
        var result = new StringBuilder(); int bytes = 0;
        foreach (var rune in text.EnumerateRunes()) { if (bytes + rune.Utf8SequenceLength > 120) break; result.Append(rune.ToString()); bytes += rune.Utf8SequenceLength; }
        return result.ToString();
    }
    public static RichPresence CreatePresence(bool showTrack, bool available, bool playing, string title, string artist, double position, double duration, DateTime now)
    {
        var presence = new RichPresence { Details = "OSTROVOK", State = "Остров в режиме покоя", Type = ActivityType.Playing };
        if (available && showTrack)
        {
            presence.Details = Limit(title); presence.State = Limit((playing ? "" : "На паузе · ") + artist);
            presence.Type = ActivityType.Listening;
            if (playing && duration > 0) presence.Timestamps = new Timestamps { Start = now.AddSeconds(-Math.Clamp(position, 0, duration)), End = now.AddSeconds(Math.Max(0, duration - position)) };
        }
        else if (available) presence.State = playing ? "Плеер открыт" : "Музыка на паузе";
        return presence;
    }
    public void Update(bool enabled, string id, bool showTrack, MediaService media)
    {
        id = id.Trim();
        if (!enabled) { Dispose(); Status = "Выключено"; return; }
        if (!ValidApplicationId(id)) { Dispose(); Status = "Укажите Application ID приложения OSTROVOK"; return; }
        try
        {
            if (client == null || applicationId != id)
            {
                Dispose(); applicationId = id; client = new DiscordRpcClient(id) { SkipIdenticalPresence = true };
                client.OnReady += (sender, _) => { if (ReferenceEquals(client, sender)) Status = "Подключено к Discord"; };
                client.OnConnectionFailed += (sender, _) => { if (ReferenceEquals(client, sender)) Status = "Ожидаем запущенный Discord"; };
                client.OnError += (sender, _) => { if (ReferenceEquals(client, sender)) Status = "Discord отклонил обновление статуса"; };
                Status = "Подключение к Discord…"; client.Initialize(); updatedAt = 0;
            }
            if (Environment.TickCount64 - updatedAt < 5000) return;
            updatedAt = Environment.TickCount64;
            client.SetPresence(CreatePresence(showTrack, media.Available, media.Playing, media.Title, media.Artist, media.Position, media.Duration, DateTime.UtcNow));
        }
        catch (Exception) { Dispose(); Status = "Не удалось подключить RPC. Проверьте Application ID и Discord."; }
    }
    public void Dispose()
    {
        var previous = client; client = null; applicationId = "";
        try { previous?.Dispose(); } catch (Exception) { }
    }
}
