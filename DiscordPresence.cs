namespace DesktopIsland;

public partial class MainWindow
{
    private readonly Services.DiscordPresenceService discordPresence = new();
    internal string DiscordPresenceStatus => discordPresence.Status;
    private void UpdateDiscordPresence()
    {
        if (!App.VerificationMode) discordPresence.Update(settings.DiscordPresenceEnabled, settings.DiscordApplicationId, settings.DiscordShowTrack, media);
    }
}
