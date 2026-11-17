using System.Windows.Media;

namespace DesktopIsland.Services;

internal static class ControlContrast
{
    internal static double Ratio(Color a, Color b)
    {
        static double L(Color color)
        {
            static double Channel(byte value) { double n = value / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        double x = L(a), y = L(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    internal static (Color Rail, Color Ink, Color Rim) For(Color[] background)
    {
        if (background.Length == 0) background = [Color.FromRgb(24, 26, 29)];
        double white = background.Min(x => Ratio(x, Colors.White)), black = background.Min(x => Ratio(x, Colors.Black));
        Color ink = white >= black ? Colors.White : Colors.Black;
        Color rail = ink;
        for (int alpha = 140; alpha <= 255; alpha += 5)
        {
            // An opaque rail avoids losing contrast when another surface is placed behind it.
            var candidate = Color.FromRgb((byte)(ink.R == 255 ? alpha : 255 - alpha), (byte)(ink.G == 255 ? alpha : 255 - alpha), (byte)(ink.B == 255 ? alpha : 255 - alpha));
            if (background.All(x => Ratio(x, candidate) >= 3)) { rail = candidate; break; }
        }
        return (rail, ink, ink == Colors.White ? Colors.Black : Colors.White);
    }
}
