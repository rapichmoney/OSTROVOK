using System.Diagnostics;
using System.Windows.Automation;

namespace DesktopIsland.Services;

// Read only the visible YouTube window title and its playback button. No browsing history or other tabs.
internal sealed class YouTubeWindowProbe
{
    internal sealed record Video(string Title, bool Playing);
    private Task<Video?>? pending;
    private long sampledAt;
    private Video? cached;
    internal string Status { get; private set; } = "Not sampled";
    internal static string? VideoTitle(string caption)
    {
        int end = caption.LastIndexOf(" - YouTube", StringComparison.Ordinal);
        if (end <= 0) return null;
        return caption[..end].Trim();
    }
    internal async Task<Video?> ReadAsync()
    {
        if (pending == null && Environment.TickCount64 - sampledAt >= 2000)
            pending = Task.Run(ReadVisibleVideo);
        if (pending != null)
        {
            try { cached = await pending.WaitAsync(TimeSpan.FromMilliseconds(350)); pending = null; sampledAt = Environment.TickCount64; }
            catch (TimeoutException) { return Environment.TickCount64 - sampledAt < 4000 ? cached : null; }
        }
        return cached;
    }
    private Video? ReadVisibleVideo()
    {
        Status = "No YouTube window";
        foreach (string browser in new[] { "browser", "chrome", "msedge", "firefox" })
        foreach (var process in Process.GetProcessesByName(browser))
        using (process)
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || VideoTitle(process.MainWindowTitle) is not string title) continue;
                Status = "YouTube window; playback control unavailable";
                var window = AutomationElement.FromHandle(process.MainWindowHandle);
                var buttons = new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "Пауза (k)"), new PropertyCondition(AutomationElement.NameProperty, "Pause (k)")));
                if (window.FindFirst(TreeScope.Descendants, buttons) != null) { Status = "Playing"; return new Video(title, true); }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or ElementNotAvailableException) { Status = ex.GetType().Name; }
        }
        return null;
    }
}
