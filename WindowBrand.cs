using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopIsland;

internal sealed class WindowBrand : IDisposable
{
    internal static void InitializeProcess()
    {
        string id = "rapichmoney.OSTROVOK";
        try { id = Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App"; } catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
        SetCurrentProcessExplicitAppUserModelID(id);
    }
    private readonly System.Drawing.Icon small, large;
    private readonly Window window;
    internal WindowBrand(Window window)
    {
        this.window = window;
        using var stream = typeof(App).Assembly.GetManifestResourceStream("DesktopIsland.Assets.Brand.ostrovok.ico")!;
        using var icon = new System.Drawing.Icon(stream);
        small = new System.Drawing.Icon(icon, 16, 16); large = new System.Drawing.Icon(icon, 32, 32);
        window.Icon = MainWindow.BrandIcon; window.ShowInTaskbar = true;
        window.SourceInitialized += Apply; window.Loaded += Apply;
        window.Closed += (_, _) => Dispose();
    }
    private void Apply(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        SendMessage(handle, 0x80, new IntPtr(0), small.Handle);
        SendMessage(handle, 0x80, new IntPtr(1), large.Handle);
    }
    internal static bool NativeIconsPresent(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && SendMessage(handle, 0x7F, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero && SendMessage(handle, 0x7F, new IntPtr(1), IntPtr.Zero) != IntPtr.Zero;
    }
    public void Dispose()
    { window.SourceInitialized -= Apply; window.Loaded -= Apply; small.Dispose(); large.Dispose(); }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr parameter, IntPtr value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
