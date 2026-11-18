using DiscordRPC;
using System.Text;

namespace DesktopIsland.Services;

public sealed class DiscordPresenceService : IDisposable
{
    public const string DefaultApplicationId = "1572368124349784084";
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
    public static RichPresence CreatePresence(bool showTrack, bool available, bool playing, string title, string artist, double position, double duration, DateTime now, string artworkUrl = "", string trackUrl = "", bool video = false)
    {
        var presence = new RichPresence { Details = "OSTROVOK", State = "Остров в режиме покоя", Type = ActivityType.Playing };
        presence.Assets = new Assets { LargeImageKey = "ostrovok", LargeImageText = "OSTROVOK · музыка и уведомления на Windows" };
        var github = new DiscordRPC.Button { Label = "GitHub", Url = "https://github.com/rapichmoney/OSTROVOK" };
        presence.Buttons = [github];
        if (available && showTrack)
        {
            presence.Details = Limit(title); presence.State = Limit((playing ? "" : "На паузе · ") + artist);
            presence.Type = video ? ActivityType.Watching : ActivityType.Listening;
            if (video && string.IsNullOrWhiteSpace(artist)) presence.State = playing ? "Видео" : "Видео на паузе";
            bool validCover = Uri.TryCreate(artworkUrl, UriKind.Absolute, out var cover) && cover.Scheme == "https" && cover.Host.EndsWith(".yandex.net", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(cover.Query) && string.IsNullOrEmpty(cover.UserInfo);
            presence.Assets = new Assets
            {
                LargeImageKey = validCover ? artworkUrl : "ostrovok", LargeImageText = Limit(title + " · " + artist),
                SmallImageKey = validCover ? "ostrovok" : null, SmallImageText = validCover ? playing ? "Воспроизводится · OSTROVOK" : "На паузе · OSTROVOK" : null
            };
            if (Uri.TryCreate(trackUrl, UriKind.Absolute, out var page) && page.Scheme == "https" && page.Host == "music.yandex.ru" && page.AbsolutePath.StartsWith("/track/", StringComparison.Ordinal) && page.AbsolutePath[7..].Length > 0 && page.AbsolutePath[7..].All(char.IsAsciiDigit) && string.IsNullOrEmpty(page.Query) && string.IsNullOrEmpty(page.UserInfo))
                presence.Buttons = [new DiscordRPC.Button { Label = "Открыть трек", Url = trackUrl }, github];
            if (playing && duration > 0) presence.Timestamps = new Timestamps { Start = now.AddSeconds(-Math.Clamp(position, 0, duration)), End = now.AddSeconds(Math.Max(0, duration - position)) };
        }
        else if (available) presence.State = playing ? "Плеер открыт" : "Музыка на паузе";
        return presence;
    }
    public void Update(bool enabled, string id, bool showTrack, MediaService media)
    {
        id = string.IsNullOrWhiteSpace(id) ? DefaultApplicationId : id.Trim();
        if (!enabled) { Dispose(); Status = "Выключено"; return; }
        if (!ValidApplicationId(id)) { Dispose(); Status = "Укажите Application ID приложения OSTROVOK"; return; }
        try
        {
            if (client == null || applicationId != id)
            {
                Dispose(); applicationId = id; client = new DiscordRpcClient(id) { SkipIdenticalPresence = true };
                client.OnReady += (sender, _) => { if (ReferenceEquals(client, sender)) Status = "Подключено к Discord"; };
                client.OnPresenceUpdate += (sender, _) => { if (ReferenceEquals(client, sender)) Status = "Подключено к Discord · статус обновлён"; };
                client.OnConnectionFailed += (sender, _) => { if (ReferenceEquals(client, sender)) Status = "Ожидаем запущенный Discord"; };
                client.OnError += (sender, _) => { if (ReferenceEquals(client, sender)) Status = "Discord отклонил обновление статуса"; };
                Status = "Подключение к Discord…"; client.Initialize(); updatedAt = 0;
            }
            if (Environment.TickCount64 - updatedAt < 5000) return;
            updatedAt = Environment.TickCount64;
            client.SetPresence(CreatePresence(showTrack, media.Available, media.Playing, media.Title, media.Artist, media.Position, media.Duration, DateTime.UtcNow, media.ArtworkUrl, media.TrackPageUrl, media.IsVideo));
        }
        catch (Exception) { Dispose(); Status = "Не удалось подключить RPC. Проверьте Application ID и Discord."; }
    }
    public void Dispose()
    {
        var previous = client; client = null; applicationId = "";
        try { previous?.Dispose(); } catch (Exception) { }
    }
}
