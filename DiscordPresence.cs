namespace DesktopIsland;

public partial class MainWindow
{
    private readonly Services.DiscordPresenceService discordPresence = new();
    internal string DiscordPresenceStatus => discordPresence.Status;
    private void UpdateDiscordPresence()
    {
        if (!App.VerificationMode) discordPresence.Update(settings.DiscordPresenceEnabled, settings.DiscordApplicationId, settings.DiscordShowTrack, media);
        var arguments = Environment.GetCommandLineArgs();
        string? report = arguments.SkipWhile(x => x != "--media-diagnostics").Skip(1).FirstOrDefault();
        if (report != null)
        {
            var bridge = Services.YouTubeBrowserBridge.Shared;
            var presence = discordPresence.LastPresence;
            try { File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { updated = DateTimeOffset.UtcNow, media.Source, media.SourceChangeCount, media.Title, media.Artist, media.Playing, media.IsVideo, media.ArtworkReady, doubleCopyShortcutActive = doubleCopyHook.IsActive, autoPasteDoubleCopyEnabled = settings.AutoPasteDoubleCopy, readAlongEnabled = settings.ReadAlong, wordTimingLines = lyricsResult?.Lines.Count(x => x.Words?.Length > 0) ?? 0, volumeMode = HasYandexVolume ? "yandex" : "system", canLike = yandexPlayback?.CanLike == true, canDislike = yandexPlayback?.CanDislike == true, hasBattery = monitor.HasBattery, expandedScale = ExpandedUiScale, lastAutoPasteSucceeded, browserStatus = bridge.Status, browserVideo = bridge.LastVideo, browserCommand = bridge.LastCommandStatus, browserConnectionAttempts = bridge.ConnectionAttempts, browserAttachedPages = bridge.AttachedPages, rpcStatus = discordPresence.Status, rpcType = presence?.Type.ToString(), rpcTitle = presence?.Details, rpcArtist = presence?.State })); }
            catch (IOException) { }
        }
    }
}
