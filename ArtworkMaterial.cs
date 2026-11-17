using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

namespace DesktopIsland;

public partial class MainWindow
{
    private ImageSource? paletteArtwork;
    private Color? artworkColor;
    private bool paletteActive, paletteEnabled;

    private void UpdateArtworkMaterial(bool active, ImageSource? artwork)
    {
        if (ReferenceEquals(paletteArtwork, artwork) && paletteActive == active && paletteEnabled == settings.ArtworkTint) return;
        paletteArtwork = artwork; paletteActive = active; paletteEnabled = settings.ArtworkTint;
        artworkColor = active && settings.ArtworkTint ? SampleArtworkColor(artwork as BitmapSource) : null;
        if (handle == IntPtr.Zero) return;
        var previous = (Shell.Background as LinearGradientBrush)?.GradientStops.Select(x => x.Color).ToArray();
        ApplyShellAppearance();
        if (Motion.Enabled && previous?.Length == 3 && Shell.Background is LinearGradientBrush next)
            for (int i = 0; i < 3; i++) next.GradientStops[i].BeginAnimation(GradientStop.ColorProperty,
                new ColorAnimation(previous[i], next.GradientStops[i].Color, TimeSpan.FromMilliseconds(320)) { FillBehavior = FillBehavior.Stop });
    }

    private (Color Top, Color Middle, Color Bottom, Color Rim) CompactMaterialColors()
    {
        if (!paletteActive || !settings.ArtworkTint || artworkColor is not Color color)
            return (Color.FromRgb(64, 64, 66), Color.FromRgb(40, 40, 42), Color.FromRgb(27, 27, 29), Color.FromRgb(118, 118, 120));
        Color Mix(double strength, byte floor) => Color.FromRgb((byte)(floor + color.R * strength), (byte)(floor + color.G * strength), (byte)(floor + color.B * strength));
        double peak = Math.Max(1, (int)Math.Max(color.R, Math.Max(color.G, color.B)));
        return (Mix(Math.Min(.48, 78 / peak), 28), Mix(.32, 23), Mix(.22, 20), Mix(.34, 66));
    }

    private static Color? SampleArtworkColor(BitmapSource? source)
    {
        if (source == null || source.PixelWidth < 1 || source.PixelHeight < 1) return null;
        try
        {
            var small = new TransformedBitmap(source, new ScaleTransform(16d / source.PixelWidth, 16d / source.PixelHeight));
            var pixels = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            byte[] data = new byte[pixels.PixelWidth * pixels.PixelHeight * 4];
            pixels.CopyPixels(data, pixels.PixelWidth * 4, 0);
            double red = 0, green = 0, blue = 0, weight = 0;
            for (int i = 0; i < data.Length; i += 4)
            {
                int b = data[i], g = data[i + 1], r = data[i + 2];
                int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                if (data[i + 3] < 160 || max < 32) continue;
                double importance = .15 + (max - min) / 255d;
                red += r * importance; green += g * importance; blue += b * importance; weight += importance;
            }
            if (weight <= 0) return null;
            double rMean = red / weight, gMean = green / weight, bMean = blue / weight;
            double neutral = (rMean + gMean + bMean) / 3;
            byte Chroma(double channel) => (byte)Math.Clamp(neutral + (channel - neutral) * 1.45, 0, 255);
            return Color.FromRgb(Chroma(rMean), Chroma(gMean), Chroma(bMean));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException) { return null; }
    }
}
