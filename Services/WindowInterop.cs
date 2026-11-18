using System.Runtime.InteropServices;

namespace DesktopIsland.Services;

internal static class WindowInterop
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    internal static void HideFromAltTab(IntPtr window)
    {
        const int extendedStyle = -20;
        long style = GetWindowLongPtr(window, extendedStyle).ToInt64();
        SetWindowLongPtr(window, extendedStyle, new IntPtr((style | 0x80L) & ~0x40000L));
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x37);
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    internal static void SetAnimatedBounds(IntPtr window, double left, double top, double width, double height, System.Windows.DpiScale dpi)
    {
        if (window == IntPtr.Zero) return;
        SetWindowPos(window, IntPtr.Zero, (int)Math.Round(left * dpi.DpiScaleX), (int)Math.Round(top * dpi.DpiScaleY),
            (int)Math.Round(width * dpi.DpiScaleX), (int)Math.Round(height * dpi.DpiScaleY), 0x0014);
    }
    public const int ClipboardMessage = 0x031D, HotkeyMessage = 0x0312, HotkeyId = 937;
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    public static System.Windows.Rect WorkArea(IntPtr own)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromWindow(own, 2);
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            var source = System.Windows.Interop.HwndSource.FromHwnd(own);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var topLeft = transform.Transform(new Point(info.Work.Left, info.Work.Top));
            var bottomRight = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));
            return new System.Windows.Rect(topLeft, bottomRight);
        }
        return SystemParameters.WorkArea;
    }
    public static bool OtherAppFullscreen(IntPtr own)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == own || !GetWindowRect(foreground, out var window)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(foreground, 2), ref info)) return false;
        // Exclude desktop shell windows.
        var className = new System.Text.StringBuilder(256); GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        return window.Left <= info.Monitor.Left && window.Top <= info.Monitor.Top && window.Right >= info.Monitor.Right && window.Bottom >= info.Monitor.Bottom;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int capacity);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
}
