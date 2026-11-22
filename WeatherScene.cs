using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopIsland;

public enum WeatherSceneKind { Unknown, Sun, PartlyCloudy, Clouds, Fog, Rain, Snow, Storm }

public sealed class WeatherScene : FrameworkElement
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(int), typeof(WeatherScene), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public int Code { get => (int)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    internal static WeatherSceneKind KindFor(int code) => code switch
    {
        0 => WeatherSceneKind.Sun, 1 or 2 => WeatherSceneKind.PartlyCloudy, 3 => WeatherSceneKind.Clouds,
        45 or 48 => WeatherSceneKind.Fog, >= 51 and <= 67 or >= 80 and <= 82 => WeatherSceneKind.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherSceneKind.Snow, >= 95 and <= 99 => WeatherSceneKind.Storm, _ => WeatherSceneKind.Unknown
    };
    private static readonly HashSet<WeatherScene> running = [];
    private static readonly List<WeakReference<WeatherScene>> instances = [];
    private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private static double lastFrame;
    private double time;
    internal bool IsAnimating => running.Contains(this);
    internal double FrameTime => time;
    private static readonly Geometry cloud = Geometry.Parse("M -21,8 C -32,8 -33,-7 -22,-9 C -20,-23 -3,-25 4,-13 C 16,-17 26,-7 22,3 C 30,14 15,20 7,15 L -18,15 C -22,15 -25,12 -21,8 Z");
    private static readonly SolidColorBrush cloudFill = Frozen(Color.FromRgb(164, 175, 181));
    private static readonly SolidColorBrush cloudRear = Frozen(Color.FromRgb(105, 121, 131));
    private static readonly SolidColorBrush sunlight = Frozen(Color.FromRgb(225, 205, 148));
    private static readonly SolidColorBrush rain = Frozen(Color.FromRgb(162, 195, 214));
    private static readonly SolidColorBrush snow = Frozen(Color.FromRgb(224, 231, 235));
    private static SolidColorBrush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    public WeatherScene()
    {
        IsHitTestVisible = false; ClipToBounds = true;
        if (instances.Count > 128) instances.RemoveAll(reference => !reference.TryGetTarget(out _));
        instances.Add(new WeakReference<WeatherScene>(this));
        Loaded += (_, _) => UpdateClock(); Unloaded += (_, _) => Stop(); IsVisibleChanged += (_, _) => UpdateClock();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((WeatherScene)sender).UpdateClock();
    private void UpdateClock()
    {
        bool active = IsLoaded && IsVisible && Motion.Enabled && KindFor(Code) != WeatherSceneKind.Unknown;
        if (!active) { Stop(); time = 0; InvalidateVisual(); return; }
        if (running.Add(this) && running.Count == 1) CompositionTarget.Rendering += RenderFrame;
    }
    private void Stop()
    { if (running.Remove(this) && running.Count == 0) CompositionTarget.Rendering -= RenderFrame; }
    internal static void RefreshMotion()
    {
        for (int i = instances.Count - 1; i >= 0; i--)
            if (instances[i].TryGetTarget(out var scene)) scene.UpdateClock(); else instances.RemoveAt(i);
    }
    private bool WithinViewport()
    {
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollContentPresenter viewport)
            {
                try { return TransformToAncestor(viewport).TransformBounds(new Rect(RenderSize)).IntersectsWith(new Rect(viewport.RenderSize)); }
                catch (InvalidOperationException) { return false; }
            }
        return true;
    }
    private static void RenderFrame(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalSeconds;
        if (now - lastFrame < 1d / 30) return;
        lastFrame = now;
        foreach (var scene in running.ToArray())
        {
            if (!scene.IsVisible || !Motion.Enabled) { scene.UpdateClock(); continue; }
            if (!scene.WithinViewport()) continue;
            scene.time = now; scene.InvalidateVisual();
        }
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth < 1 || ActualHeight < 1) return;
        var kind = KindFor(Code); if (kind == WeatherSceneKind.Unknown) return;
        double scale = Math.Min(ActualWidth / 96, ActualHeight / 54);
        drawing.PushTransform(new TranslateTransform(ActualWidth / 2, ActualHeight / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        bool sunny = kind is WeatherSceneKind.Sun or WeatherSceneKind.PartlyCloudy;
        if (sunny)
        {
            double x = kind == WeatherSceneKind.Sun ? 0 : -13, y = kind == WeatherSceneKind.Sun ? 0 : -7;
            drawing.PushTransform(new TranslateTransform(x, y));
            drawing.PushTransform(new RotateTransform(time * 7));
            var rays = new Pen(sunlight, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int ray = 0; ray < 8; ray++)
            {
                double angle = ray * Math.PI / 4, end = 20 + Math.Sin(time * .8 + ray * .3) * 1.2;
                drawing.DrawLine(rays, new Point(Math.Cos(angle) * 15, Math.Sin(angle) * 15), new Point(Math.Cos(angle) * end, Math.Sin(angle) * end));
            }
            drawing.DrawEllipse(sunlight, null, new Point(), 10.5, 10.5); drawing.Pop(); drawing.Pop();
        }
        if (kind is WeatherSceneKind.Rain or WeatherSceneKind.Snow or WeatherSceneKind.Storm)
        {
            var pen = new Pen(rain, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            int count = kind == WeatherSceneKind.Snow ? 10 : 8;
            for (int particle = 0; particle < count; particle++)
            {
                double progress = (time * (kind == WeatherSceneKind.Snow ? .18 : .55) + particle * .137) % 1;
                double x = -28 + particle * 57d / count + Math.Sin(time * .65 + particle) * (kind == WeatherSceneKind.Snow ? 3 : 1);
                double y = 2 + progress * 29;
                drawing.PushOpacity(Math.Sin(progress * Math.PI) * .85);
                if (kind == WeatherSceneKind.Snow) drawing.DrawEllipse(snow, null, new Point(x, y), particle % 2 == 0 ? 1.4 : .9, particle % 2 == 0 ? 1.4 : .9);
                else drawing.DrawLine(pen, new Point(x, y), new Point(x - 2, y + 5));
                drawing.Pop();
            }
        }
        if (kind != WeatherSceneKind.Sun)
        {
            double drift = Math.Sin(time * .5) * 3;
            drawing.PushTransform(new TranslateTransform(-7 - drift * .6, -5)); drawing.PushTransform(new ScaleTransform(.8, .8));
            drawing.DrawGeometry(cloudRear, null, cloud); drawing.Pop(); drawing.Pop();
            drawing.PushTransform(new TranslateTransform(6 + drift, kind is WeatherSceneKind.Snow or WeatherSceneKind.Rain or WeatherSceneKind.Storm ? -8 : 1));
            drawing.DrawGeometry(cloudFill, null, cloud); drawing.Pop();
        }
        if (kind == WeatherSceneKind.Fog)
        {
            var pen = new Pen(cloudFill, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int line = 0; line < 3; line++) { double x = Math.Sin(time * .45 + line) * 4; drawing.DrawLine(pen, new Point(-29 + x, 14 + line * 5), new Point(25 + x, 14 + line * 5)); }
        }
        if (kind == WeatherSceneKind.Storm)
        {
            var bolt = Geometry.Parse("M 4,4 L -4,17 L 3,16 L -1,26 L 13,11 L 5,12 Z");
            drawing.DrawGeometry(sunlight, null, bolt);
        }
        drawing.Pop(); drawing.Pop();
    }
}
