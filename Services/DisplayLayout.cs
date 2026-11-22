namespace DesktopIsland.Services;

internal static class DisplayLayout
{
    internal static double Scale(double requested, double width, double height, double availableWidth, double availableHeight)
        => Math.Max(.25, Math.Min(requested, Math.Min(Math.Max(1, availableWidth - 8) / width, Math.Max(1, availableHeight) / height)));
    internal static int[] NormalizeHidden(IEnumerable<int>? tabs) => tabs?.Where(x => x is >= 1 and <= 10 && x != 9).Distinct().Order().ToArray() ?? [];
}
