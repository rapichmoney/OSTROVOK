using System.Globalization;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Text.RegularExpressions;
using DesktopIsland.Services;

namespace DesktopIsland;

internal sealed class KaraokeLine(TextBlock source, bool singleLine = false) : FrameworkElement
{
    private FormattedText? formatted;
    private Match[] words = [];
    private double measuredWidth, measuredDpi;
    private string measuredKey = "";
    private bool readAlong;
    private int markerStart = -1;
    private int markerRevision;
    internal Rect MarkerBounds => (Rect)GetValue(MarkerProperty);
    internal double MarkerAlpha => (double)GetValue(MarkerAlphaProperty);
    private static readonly DependencyProperty FrameProperty = DependencyProperty.Register("TimingFrame", typeof(ReadAlongFrame), typeof(KaraokeLine), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    internal ReadAlongFrame? TimingFrame { get => (ReadAlongFrame?)GetValue(FrameProperty); set { if (TimingFrame != value) SetValue(FrameProperty, value); } }
    private static readonly DependencyProperty MarkerAlphaProperty = DependencyProperty.Register("MarkerAlpha", typeof(double), typeof(KaraokeLine), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
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
        ++markerRevision; markerStart = -1; BeginAnimation(MarkerProperty, null); SetValue(MarkerProperty, Rect.Empty);
        BeginAnimation(MarkerAlphaProperty, null); SetValue(MarkerAlphaProperty, 0d);
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
            var frame = TimingFrame;
            if (words.Length == 0) return;
            var word = characters >= 0 ? words.FirstOrDefault(x => x.Index + x.Length > characters) ?? words[^1] : null;
            int start = frame?.Start ?? word?.Index ?? -1, length = frame?.Length ?? word?.Length ?? 0;
            int read = frame?.ReadThrough ?? Math.Max(0, start);
            if (read > 0 && formatted.BuildHighlightGeometry(origin, 0, Math.Min(text.Length, read)) is Geometry played)
            { dc.PushClip(played); dc.DrawText(formatted, origin); dc.Pop(); }
            if (start < 0 || length <= 0)
            {
                if (markerStart >= 0) { markerStart = -1; Motion.Tween(this, MarkerAlphaProperty, 0, 80); }
                DrawMarker(dc, origin); return;
            }
            if (start > text.Length - length || formatted.BuildHighlightGeometry(origin, start, length) is not Geometry geometry) return;
            var target = geometry.Bounds; target.Inflate(2, 1);
            if (markerStart != start || ((Rect)GetValue(MarkerProperty)).IsEmpty)
            {
                markerStart = start; var from = (Rect)GetValue(MarkerProperty);
                double travel = frame != null ? Math.Clamp(frame.Duration * 180, 28, 100) : WordPosition.HasValue ? 100 : 220;
                int revision = ++markerRevision;
                BeginAnimation(MarkerProperty, null);
                if (Motion.Enabled && !from.IsEmpty && Math.Abs(from.Y - target.Y) < 12)
                {
                    SetValue(MarkerProperty, from);
                    var motion = new RectAnimation(from, target, TimeSpan.FromMilliseconds(travel)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd };
                    motion.Completed += (_, _) => { if (revision == markerRevision) { SetValue(MarkerProperty, target); BeginAnimation(MarkerProperty, null); } };
                    BeginAnimation(MarkerProperty, motion);
                }
                else SetValue(MarkerProperty, target);
                Motion.Tween(this, MarkerAlphaProperty, 1, travel);
                if (singleLine)
                {
                    double pan = Math.Clamp(target.X + target.Width / 2 - ActualWidth * .55, 0, Math.Max(0, formatted.WidthIncludingTrailingWhitespace - ActualWidth));
                    Motion.Tween(this, PanProperty, pan, Math.Min(180, Math.Max(60, travel * 1.5)));
                }
            }
            if (!Motion.Enabled) { BeginAnimation(MarkerProperty, null); SetValue(MarkerProperty, target); }
            DrawMarker(dc, origin);
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
    private void DrawMarker(DrawingContext dc, Point origin)
    {
        var marker = (Rect)GetValue(MarkerProperty); double alpha = (double)GetValue(MarkerAlphaProperty);
        if (marker.IsEmpty || alpha <= 0 || formatted == null) return;
        dc.PushOpacity(alpha); dc.DrawRoundedRectangle(Brushes.WhiteSmoke, null, marker, 4, 4);
        dc.PushClip(new RectangleGeometry(marker, 4, 4));
        formatted.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(20, 22, 25))); dc.DrawText(formatted, origin);
        dc.Pop(); dc.Pop(); formatted.SetForegroundBrush(Brushes.White);
    }
}
