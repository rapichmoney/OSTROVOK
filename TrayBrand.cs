namespace DesktopIsland;

public partial class MainWindow
{
    private static readonly Lazy<System.Windows.Media.Imaging.BitmapSource> windowIcon = new(() =>
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("DesktopIsland.Assets.Brand.ostrovok.ico")!;
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderBy(x => Math.Abs(x.PixelWidth - 32)).First(); frame.Freeze(); return frame;
    });
    internal static System.Windows.Media.ImageSource BrandIcon => windowIcon.Value;
    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("DesktopIsland.Assets.Brand.ostrovok.ico")
            ?? throw new InvalidOperationException("OSTROVOK tray icon resource is missing");
        using var icon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
        return (System.Drawing.Icon)icon.Clone();
    }
}
