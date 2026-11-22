namespace DesktopIsland;

public partial class MainWindow
{
    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("DesktopIsland.Assets.Brand.ostrovok.ico")
            ?? throw new InvalidOperationException("OSTROVOK tray icon resource is missing");
        using var icon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
        return (System.Drawing.Icon)icon.Clone();
    }
}
