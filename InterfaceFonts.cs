using System.Windows.Media;

namespace DesktopIsland;

internal static class InterfaceFonts
{
    internal const string Aminahs = "Aminahs Schoolbook Sans Serif Bd";
    private static readonly Lazy<FontFamily> bundled = new(() => new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Aminahs Schoolbook Sans Serif"));
    internal static FontFamily Resolve(string name) => name == Aminahs
        ? bundled.Value
        : new FontFamily("Segoe UI");
}

public partial class MainWindow
{
    private string appliedInterfaceFont = "";
    private void ApplyInterfaceFont()
    {
        if (appliedInterfaceFont == settings.InterfaceFont) return;
        appliedInterfaceFont = settings.InterfaceFont;
        FontFamily = InterfaceFonts.Resolve(settings.InterfaceFont);
        Clock.FontFamily = HomeClock.FontFamily = FontFamily;
        measuredCompactLine = compactMeasuredTitle = "";
        foreach (var fill in lyricFills) fill.InvalidateVisual();
        compactReadAlong?.InvalidateVisual();
    }
}
