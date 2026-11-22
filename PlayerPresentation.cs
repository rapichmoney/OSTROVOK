using System.Windows.Controls;

namespace DesktopIsland;

public partial class MainWindow
{
    private bool? lastPlaybackVisual;
    private void UpdatePlaybackButton(bool playing)
    {
        if (lastPlaybackVisual != playing) PolishedMotion.Swap(PlaybackIcon, () => PlaybackIcon.Data = System.Windows.Media.Geometry.Parse(playing ? "M 2,2 L 8,2 L 8,22 L 2,22 Z M 14,2 L 20,2 L 20,22 L 14,22 Z" : "M 5,2 L 19,12 L 5,22 Z"), 180, 0);
        lastPlaybackVisual = playing;
        string action = playing ? "Пауза" : "Воспроизвести";
        PlayButton.ToolTip = action;
        System.Windows.Automation.AutomationProperties.SetName(PlayButton, action);
    }
    internal KeyValuePair<string,string>[] PlaybackSources => new[] { new KeyValuePair<string,string>("", "Автоматически") }
        .Concat(media.Sessions.Select(x => x.SourceAppUserModelId).Distinct().Select(x => new KeyValuePair<string,string>(x, FriendlyPlayerName(x)))).ToArray();
    internal string PlaybackSource => media.SelectedSource ?? "";
    internal string ReadAlongTimingStatus => lyricsResult?.Synced != true ? "Для текущего трека синхронизированный текст пока не найден."
        : lyricsResult.Lines.Any(x => x.Words?.Length > 0) ? "В текущем тексте есть метки слов. Учитываются начало и окончание слова, включая паузы."
        : "В текущем тексте есть только метки строк. Подсветка слов рассчитывается приблизительно.";
    internal async Task SelectPlaybackSourceAsync(string id) { media.SelectedSource = id.Length == 0 ? null : id; await media.RefreshAsync(); }
    internal void RetryCurrentLyrics() => RetryLyrics(this, new RoutedEventArgs());
    internal void OpenCurrentLyricsFile() => OpenLyricsFile(this, new RoutedEventArgs());
    internal void OpenWindowsSoundSettings() => OpenSound(this, new RoutedEventArgs());
    internal string YouTubeConnectionAction => Services.YouTubeBrowserBridge.Shared.IsConnected ? "Отключить YouTube" : Services.YouTubeBrowserBridge.Shared.IsConnecting ? "Отменить подключение YouTube" : "Подключить YouTube";
    internal async Task ToggleYouTubeConnectionAsync()
    {
        var youtube = Services.YouTubeBrowserBridge.Shared;
        if (youtube.IsConnected || youtube.IsConnecting) await youtube.DisconnectAsync(); else youtube.RequestConnection();
        await media.RefreshAsync(); Status.Text = youtube.Status;
    }
    private static string FriendlyPlayerName(string source)
    {
        if (source.Contains("yandex", StringComparison.OrdinalIgnoreCase)) return "Яндекс Музыка";
        if (source.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (source.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (source.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (source.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        return source;
    }
}
