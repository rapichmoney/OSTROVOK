using System.Windows.Controls;

namespace DesktopIsland;

public partial class MainWindow
{
    private readonly HashSet<ContextMenu> openIslandMenus = [];
    private DateTime popupGraceUntil;
    private bool IslandInteractionActive => openIslandMenus.Count > 0 || DateTime.UtcNow < popupGraceUntil;

    private void TrackIslandMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) => { openIslandMenus.Add(menu); leaveTimer.Stop(); };
        menu.Closed += (_, _) =>
        {
            openIslandMenus.Remove(menu);
            popupGraceUntil = DateTime.UtcNow.AddSeconds(1);
            if (!closing && expanded) leaveTimer.Start();
        };
    }

    private void TryAutoCollapse()
    {
        if (IslandInteractionActive) return;
        leaveTimer.Stop();
        if (!IsMouseOver && Pin.IsChecked != true && !seeking && !draggingWindow) SetExpanded(false);
    }
}
