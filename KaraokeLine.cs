using System.Globalization;
using System.Windows.Media;
using System.Windows.Controls;

namespace DesktopIsland;

internal sealed class KaraokeLine(TextBlock source) : FrameworkElement
{
    private FormattedText? formatted;
    private double measuredWidth, measuredDpi;
    private string measuredFont = "";
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(KaraokeLine), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, Math.Clamp(value, 0, 1)); }

    protected override void OnRender(DrawingContext dc)
    {
        Effect ??= new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, ShadowDepth = 0, BlurRadius = 4, Opacity = .45 };
        base.OnRender(dc);
        string text = source.Text;
        if (ActualWidth < 1 || Progress <= 0 || text.Length == 0) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string font = source.FontFamily.Source + source.FontSize + source.FontWeight;
        if (formatted == null || measuredWidth != ActualWidth || measuredDpi != dpi || measuredFont != font)
        {
            measuredFont = font;
            measuredWidth = ActualWidth; measuredDpi = dpi;
            formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch), source.FontSize, Brushes.White, dpi)
            { MaxTextWidth = ActualWidth, LineHeight = 24 };
        }
        double characters = Progress * text.Length;
        int complete = Math.Min(text.Length, (int)characters);
        var clip = new GeometryGroup();
        double rows = Math.Max(1, Math.Round(source.ActualHeight / source.LineHeight));
        double firstBaseline = source.BaselineOffset - (rows - 1) * source.LineHeight;
        var origin = new Point(0, firstBaseline - formatted.Baseline);
        if (complete > 0 && formatted.BuildHighlightGeometry(origin, 0, complete) is Geometry filled) clip.Children.Add(filled);
        if (complete < text.Length && formatted.BuildHighlightGeometry(origin, complete, 1) is Geometry next)
        {
            var bounds = next.Bounds;
            if (!bounds.IsEmpty) clip.Children.Add(new RectangleGeometry(new Rect(bounds.X, bounds.Y, bounds.Width * (characters - complete), bounds.Height)));
        }
        dc.PushClip(clip); dc.DrawText(formatted, origin); dc.Pop();
    }
}
