using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopIsland.Services;

internal static class NotificationBrandIcons
{
    private static readonly Dictionary<string, BitmapSource> cache = [];
    private static ImageSource? fallback;
    internal static ImageSource Fallback
    {
        get
        {
            if (fallback != null) return fallback;
            var image = new DrawingImage(new GeometryDrawing(null, new Pen(Brushes.White, 1.5), Geometry.Parse(NotificationSources.Bell)));
            image.Freeze(); return fallback = image;
        }
    }
    internal static BitmapSource? Get(string source)
    {
        if (NotificationSources.Find(source) == null) return null;
        if (cache.TryGetValue(source, out var found)) return found;
        var image = new BitmapImage(new Uri("pack://application:,,,/Assets/Notifications/" + source + ".png"));
        image.Freeze(); cache[source] = image; return image;
    }
}
