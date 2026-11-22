using System.Globalization;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Text.RegularExpressions;

namespace DesktopIsland;

internal sealed class KaraokeLine(TextBlock source, bool singleLine = false) : FrameworkElement
{
    private FormattedText? formatted;
    private Match[] words = [];
    private double measuredWidth, measuredDpi;
    private string measuredKey = "";
    private bool readAlong;
    private int markerStart = -1;
    private static readonly DependencyProperty MarkerProperty = DependencyProperty.Register("Marker", typeof(Rect), typeof(KaraokeLine), new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty PanProperty = DependencyProperty.Register("Pan", typeof(double), typeof(KaraokeLine), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WordPositionProperty = DependencyProperty.Register(nameof(WordPosition), typeof(double?), typeof(KaraokeLine), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public double? WordPosition { get => (double?)GetValue(WordPositionProperty); set => SetValue(WordPositionProperty, value); }
    public bool ReadAlong
    {
        get => readAlong;
        set
        {
            if (readAlong == value) return; readAlong = value;
            if (!singleLine) source.Foreground = new SolidColorBrush(value ? Color.FromRgb(116, 124, 132) : Color.FromRgb(181, 189, 198));
            ResetMarker(); InvalidateVisual();
        }
    }
    private void ResetMarker()
    {
        markerStart = -1; BeginAnimation(MarkerProperty, null); SetValue(MarkerProperty, Rect.Empty);
        BeginAnimation(PanProperty, null); SetValue(PanProperty, 0d);
    }
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(KaraokeLine), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, Math.Clamp(value, 0, 1)); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 1 || source.Text.Length == 0 || (!ReadAlong && Progress <= 0)) return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.PushTransform(new TranslateTransform(singleLine ? -(double)GetValue(PanProperty) : 0, 0));
        try { DrawText(dc); } finally { dc.Pop(); dc.Pop(); }
    }
    private void DrawText(DrawingContext dc)
    {
        string text = source.Text;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string key = source.FontFamily.Source + source.FontSize + source.FontWeight + text;
        if (formatted == null || measuredWidth != ActualWidth || measuredDpi != dpi || measuredKey != key)
        {
            measuredKey = key; measuredWidth = ActualWidth; measuredDpi = dpi; ResetMarker();
            formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch), source.FontSize, Brushes.White, dpi);
            formatted.MaxTextWidth = singleLine ? Math.Max(ActualWidth, formatted.WidthIncludingTrailingWhitespace + 1) : ActualWidth;
            formatted.LineHeight = singleLine ? source.FontSize * 1.5 : 24;
            words = Regex.Matches(text, @"\S+").Cast<Match>().ToArray();
        }
        double characters = ReadAlong && WordPosition is double timed ? timed : Progress * text.Length;
        double rows = singleLine ? 1 : Math.Max(1, Math.Round(source.ActualHeight / source.LineHeight));
        double firstBaseline = source.BaselineOffset - (rows - 1) * (singleLine ? 0 : source.LineHeight);
        var origin = new Point(0, firstBaseline - formatted.Baseline + (singleLine ? source.TranslatePoint(new Point(), this).Y : 0));
        if (singleLine && ReadAlong)
        {
            formatted.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(133, 141, 150))); dc.DrawText(formatted, origin); formatted.SetForegroundBrush(Brushes.White);
        }
        if (ReadAlong)
        {
            if (characters < 0 || words.Length == 0) return;
            var word = words.FirstOrDefault(x => x.Index + x.Length > characters) ?? words[^1];
            if (word.Index > 0 && formatted.BuildHighlightGeometry(origin, 0, word.Index) is Geometry played)
            { dc.PushClip(played); dc.DrawText(formatted, origin); dc.Pop(); }
            if (formatted.BuildHighlightGeometry(origin, word.Index, word.Length) is not Geometry geometry) return;
            var target = geometry.Bounds; target.Inflate(2, 1);
            if (markerStart != word.Index || ((Rect)GetValue(MarkerProperty)).IsEmpty)
            {
                markerStart = word.Index; var from = (Rect)GetValue(MarkerProperty);
                BeginAnimation(MarkerProperty, null); SetValue(MarkerProperty, target);
                if (Motion.Enabled && !from.IsEmpty && Math.Abs(from.Y - target.Y) < 12)
                    BeginAnimation(MarkerProperty, new RectAnimation(from, target, TimeSpan.FromMilliseconds(WordPosition.HasValue ? 120 : 300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                if (singleLine)
                {
                    double pan = Math.Clamp(target.X + target.Width / 2 - ActualWidth * .55, 0, Math.Max(0, formatted.WidthIncludingTrailingWhitespace - ActualWidth));
                    Motion.Tween(this, PanProperty, pan, 240);
                }
            }
            if (!Motion.Enabled) { BeginAnimation(MarkerProperty, null); SetValue(MarkerProperty, target); }
            var marker = (Rect)GetValue(MarkerProperty);
            dc.DrawRoundedRectangle(Brushes.WhiteSmoke, null, marker, 4, 4);
            dc.PushClip(new RectangleGeometry(marker, 4, 4));
            formatted.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(20, 22, 25))); dc.DrawText(formatted, origin); dc.Pop(); formatted.SetForegroundBrush(Brushes.White);
            return;
        }
        int complete = Math.Clamp((int)characters, 0, text.Length);
        var clip = new GeometryGroup();
        if (complete > 0 && formatted.BuildHighlightGeometry(origin, 0, complete) is Geometry filled) clip.Children.Add(filled);
        if (complete < text.Length && formatted.BuildHighlightGeometry(origin, complete, 1) is Geometry next)
        {
            var bounds = next.Bounds;
            if (!bounds.IsEmpty) clip.Children.Add(new RectangleGeometry(new Rect(bounds.X, bounds.Y, bounds.Width * (characters - complete), bounds.Height)));
        }
        dc.PushClip(clip); dc.DrawText(formatted, origin); dc.Pop();
    }
}
