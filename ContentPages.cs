using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace DesktopIsland;

// Navigation is provided by separate, labelled buttons. Expose the selected page
// as a content pane rather than a tab strip whose containers are not displayed.
public sealed class ContentPages : TabControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new ContentPagesPeer(this);
    private sealed class ContentPagesPeer(ContentPages owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(ContentPages);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
    }
}
