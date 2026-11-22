using System.Globalization;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Text.RegularExpressions;

namespace DesktopIsland;

internal sealed class KaraokeLine(TextBlock source) : FrameworkElement
{
    private FormattedText? formatted;
    private double measuredWidth, measuredDpi;
    private string measuredFont = "";
    private bool readAlong;
    private int markerStart = -1;
    private static readonly DependencyProperty MarkerProperty = DependencyProperty.Register("Marker", typeof(Rect), typeof(KaraokeLine), new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ReadAlong
    {
        get => readAlong;
        set { if (readAlong == value) return; readAlong = value; source.Foreground = new SolidColorBrush(value ? Color.FromRgb(116, 124, 132) : Color.FromRgb(181, 189, 198)); markerStart = -1; BeginAnimation(MarkerProperty, null); InvalidateVisual(); }
    }
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(KaraokeLine), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, Math.Clamp(value, 0, 1)); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        string text = source.Text;
        if (ActualWidth < 1 || (!ReadAlong && Progress <= 0) || text.Length == 0) return;
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
        if (ReadAlong)
        {
            var words = Regex.Matches(text, @"\S+").Cast<Match>().ToArray();
            if (words.Length == 0) return;
            var word = words.FirstOrDefault(x => x.Index + x.Length > characters) ?? words[^1];
            if (word.Index > 0 && formatted.BuildHighlightGeometry(origin, 0, word.Index) is Geometry played)
            { dc.PushClip(played); dc.DrawText(formatted, origin); dc.Pop(); }
            if (formatted.BuildHighlightGeometry(origin, word.Index, word.Length) is not Geometry geometry) return;
            var target = geometry.Bounds; target.Inflate(2, 1);
            if (markerStart != word.Index || ((Rect)GetValue(MarkerProperty)).IsEmpty)
            {
                markerStart = word.Index;
                var from = (Rect)GetValue(MarkerProperty);
                BeginAnimation(MarkerProperty, null); SetValue(MarkerProperty, target);
                if (Motion.Enabled && !from.IsEmpty && Math.Abs(from.Y - target.Y) < 12)
                    BeginAnimation(MarkerProperty, new RectAnimation(from, target, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            }
            var marker = (Rect)GetValue(MarkerProperty);
            dc.DrawRoundedRectangle(Brushes.WhiteSmoke, null, marker, 4, 4);
            dc.PushClip(new RectangleGeometry(marker, 4, 4));
            formatted.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(20, 22, 25)));
            dc.DrawText(formatted, origin); dc.Pop(); formatted.SetForegroundBrush(Brushes.White);
            return;
        }
        if (complete > 0 && formatted.BuildHighlightGeometry(origin, 0, complete) is Geometry filled) clip.Children.Add(filled);
        if (complete < text.Length && formatted.BuildHighlightGeometry(origin, complete, 1) is Geometry next)
        {
            var bounds = next.Bounds;
            if (!bounds.IsEmpty) clip.Children.Add(new RectangleGeometry(new Rect(bounds.X, bounds.Y, bounds.Width * (characters - complete), bounds.Height)));
        }
        dc.PushClip(clip); dc.DrawText(formatted, origin); dc.Pop();
    }
}
