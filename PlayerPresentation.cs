using System.Windows.Controls;

namespace DesktopIsland;

public partial class MainWindow
{
    private void UpdatePlaybackButton(bool playing)
    {
        PlaybackIcon.Data = System.Windows.Media.Geometry.Parse(playing ? "M 2,2 L 8,2 L 8,22 L 2,22 Z M 14,2 L 20,2 L 20,22 L 14,22 Z" : "M 5,2 L 19,12 L 5,22 Z");
        string action = playing ? "Пауза" : "Воспроизвести";
        PlayButton.ToolTip = action;
        System.Windows.Automation.AutomationProperties.SetName(PlayButton, action);
    }
    private void OpenLyricsMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); TrackIslandMenu(menu);
        AddMenuAction(menu, "Искать текст заново", () => RetryLyrics(this, new RoutedEventArgs()));
        AddMenuAction(menu, "Открыть файл LRC", () => OpenLyricsFile(this, new RoutedEventArgs()));
        AddMenuAction(menu, "Источники и синхронизация", () => OpenLyricsSettings(this, new RoutedEventArgs()));
        menu.PlacementTarget = LyricsMenuButton; menu.IsOpen = true;
    }
    private void OpenPlayerOptions(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); TrackIslandMenu(menu);
        foreach (string source in media.Sessions.Select(x => x.SourceAppUserModelId).Distinct())
        {
            string selected = source;
            var item = new MenuItem { Header = (source == media.Source ? "Выбран: " : "Выбрать: ") + FriendlyPlayerName(source) };
            item.Click += async (_, _) => { media.SelectedSource = selected; await media.RefreshAsync(); };
            menu.Items.Add(item);
        }
        var youtube = Services.YouTubeBrowserBridge.Shared;
        var youtubeItem = new MenuItem { Header = youtube.IsConnected ? "Отключить YouTube" : youtube.IsConnecting ? "Отменить подключение YouTube" : "Подключить YouTube" };
        youtubeItem.Click += async (_, _) =>
        {
            if (youtube.IsConnected || youtube.IsConnecting) await youtube.DisconnectAsync();
            else youtube.RequestConnection();
            await media.RefreshAsync(); Status.Text = youtube.Status;
        };
        menu.Items.Add(youtubeItem);
        AddMenuAction(menu, "Настройки звука Windows", () => OpenSound(this, new RoutedEventArgs()));
        AddMenuAction(menu, "Настройки музыки", () => OpenPreferences(5));
        menu.PlacementTarget = PlayerOptionsButton; menu.IsOpen = true;
    }
    private static void AddMenuAction(ContextMenu menu, string title, Action action)
    {
        var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item);
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
