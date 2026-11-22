using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Runtime.CompilerServices;

namespace DesktopIsland;

// Small, cancellable transitions. No idle loops and no clocks kept after completion.
public static class Motion
{
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, long>> revisions = new();
    public static bool Reduced { get; set; }
    internal static bool VerificationOverride { get; set; }
    public static bool Enabled => !Reduced && (SystemParameters.ClientAreaAnimation || VerificationOverride);
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(string), typeof(Motion), new PropertyMetadata("", RoleChanged));
    public static string GetRole(DependencyObject obj) => (string)obj.GetValue(RoleProperty);
    public static void SetRole(DependencyObject obj, string value) => obj.SetValue(RoleProperty, value);

    public static void Tween(DependencyObject target, DependencyProperty property, double to, double milliseconds = 180, double delay = 0, Action? completed = null, IEasingFunction? easingOverride = null)
    {
        var animated = (IAnimatable)target;
        var tokens = revisions.GetOrCreateValue(target);
        long token = tokens.TryGetValue(property, out long previous) ? previous + 1 : 1;
        tokens[property] = token;
        double from = (double)target.GetValue(property);
        animated.BeginAnimation(property, null);
        target.SetValue(property, to);
        if (!Enabled || milliseconds <= 0) { completed?.Invoke(); return; }
        AnimationTimeline animation;
        var easing = easingOverride ?? new CubicEase { EasingMode = EasingMode.EaseOut };
        if (delay > 0)
        {
            var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(delay + milliseconds), FillBehavior = FillBehavior.Stop };
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + milliseconds)), easing));
            animation = frames;
        }
        else animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { FillBehavior = FillBehavior.Stop, EasingFunction = easing };
        animation.Completed += (_, _) =>
        {
            if (tokens[property] != token) return;
            animated.BeginAnimation(property, null);
            completed?.Invoke();
        };
        animated.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Enter(FrameworkElement element, double distance = 8, double duration = 190)
    {
        if (!Enabled) { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1; element.RenderTransform = Transform.Identity; return; }
        var transform = new TranslateTransform(0, distance); element.RenderTransform = transform;
        element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 0;
        Tween(element, UIElement.OpacityProperty, 1, duration);
        Tween(transform, TranslateTransform.YProperty, 0, duration);
    }

    internal static void RevealContent(FrameworkElement element, double duration = 420, double delay = 60)
    {
        if (!Enabled) { ClearContentMotion(element); return; }
        var scale = new ScaleTransform(.975, .975);
        var shift = new TranslateTransform(0, -5);
        var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(shift);
        element.RenderTransformOrigin = new Point(.5, 0); element.RenderTransform = group;
        element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 0;
        var response = new MotionDynamics.PhysicalResponse(.87, 12);
        Tween(element, UIElement.OpacityProperty, 1, Math.Min(260, duration), delay);
        Tween(scale, ScaleTransform.ScaleXProperty, 1, duration, delay, easingOverride: response);
        Tween(scale, ScaleTransform.ScaleYProperty, 1, duration, delay, easingOverride: response);
        Tween(shift, TranslateTransform.YProperty, 0, duration, delay, easingOverride: response);
    }
    internal static void Pop(FrameworkElement element, double from = .7, double duration = 460)
    {
        if (!Enabled) { element.RenderTransform = Transform.Identity; return; }
        var scale = new ScaleTransform(from, from); element.RenderTransformOrigin = new Point(.5, .5); element.RenderTransform = scale;
        var response = new MotionDynamics.PhysicalResponse(.72, 12);
        Tween(scale, ScaleTransform.ScaleXProperty, 1, duration, easingOverride: response);
        Tween(scale, ScaleTransform.ScaleYProperty, 1, duration, easingOverride: response);
    }

    internal static void ClearContentMotion(FrameworkElement element)
    {
        if (element.RenderTransform is ScaleTransform rootScale) { rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
        if (element.RenderTransform is TranslateTransform rootShift) { rootShift.BeginAnimation(TranslateTransform.XProperty, null); rootShift.BeginAnimation(TranslateTransform.YProperty, null); }
        if (element.RenderTransform is RotateTransform rootRotation) rootRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        if (element.RenderTransform is TransformGroup group)
            foreach (var transform in group.Children)
            {
                if (transform is ScaleTransform scale) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
                if (transform is TranslateTransform shift) { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null); }
                if (transform is RotateTransform rotation) rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        element.RenderTransform = Transform.Identity;
        element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1;
    }

    public static void Progress(System.Windows.Controls.Primitives.RangeBase control, double value)
        => Tween(control, System.Windows.Controls.Primitives.RangeBase.ValueProperty, value, 320);

    internal static (double Duration, IEasingFunction Easing) ShellProfile(string style)
    {
        var profile = UnboundedShellProfile(style);
        return (profile.Duration, new BoundedEasing(profile.Easing));
    }
    private sealed class BoundedEasing(IEasingFunction source) : IEasingFunction
    {
        public double Ease(double normalizedTime) => Math.Clamp(source.Ease(normalizedTime), 0, 1.015);
    }
    private static (double Duration, IEasingFunction Easing) UnboundedShellProfile(string style) => style switch
    {
        "Пружина" => (420, new BackEase { Amplitude = .2, EasingMode = EasingMode.EaseOut }),
        "Плавно" => (420, new SineEase { EasingMode = EasingMode.EaseInOut }),
        "Волна" => (460, new CircleEase { EasingMode = EasingMode.EaseInOut }),
        "Желе" => (520, new ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = EasingMode.EaseOut }),
        "Тянучка" => (460, new QuinticEase { EasingMode = EasingMode.EaseInOut }),
        "Мяч" => (480, new BounceEase { Bounces = 2, Bounciness = 4, EasingMode = EasingMode.EaseOut }),
        "Резинка" => (430, new ElasticEase { Oscillations = 1, Springiness = 9, EasingMode = EasingMode.EaseOut }),
        "Щелчок" => (160, new ExponentialEase { Exponent = 6, EasingMode = EasingMode.EaseOut }),
        "Пузырь" => (400, new BackEase { Amplitude = .12, EasingMode = EasingMode.EaseOut }),
        _ => (340, new CubicEase { EasingMode = EasingMode.EaseOut })
    };

    private static void RoleChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not FrameworkElement element || ((string)e.OldValue != "" && (string)e.NewValue != "Switch")) return;
        switch ((string)e.NewValue)
        {
            case "Button" when element is Button button:
                button.MouseEnter += (_, _) => Hover(button, true);
                button.MouseLeave += (_, _) => { Hover(button, false); Press(button, false); };
                button.PreviewMouseLeftButtonDown += (_, _) => Press(button, true);
                button.PreviewMouseLeftButtonUp += (_, _) => Press(button, false);
                button.LostMouseCapture += (_, _) => Press(button, false);
                button.PreviewKeyDown += (_, args) => { if (args.Key == Key.Space) Press(button, true); };
                button.PreviewKeyUp += (_, args) => { if (args.Key == Key.Space) Press(button, false); };
                break;
            case "Slider" when element is Slider slider:
                SliderMotion.Attach(slider); break;
            case "Item":
                element.Loaded += (_, _) => Enter(element, 5, 160);
                break;
            case "Check" when element is CheckBox check:
                check.Checked += (_, _) => CheckChanged(check, true);
                check.Unchecked += (_, _) => CheckChanged(check, false);
                break;
            case "Switch" when element is CheckBox toggle:
                toggle.Loaded += (_, _) => SwitchMoved(toggle, true);
                toggle.Checked += (_, _) => SwitchMoved(toggle, false);
                toggle.Unchecked += (_, _) => SwitchMoved(toggle, false);
                break;
        }
    }
    private static void Hover(Button button, bool over)
    {
        if (button.Template.FindName("Frame", button) is not Border frame || button.Background is not SolidColorBrush normal) return;
        var start = (frame.Background as SolidColorBrush)?.Color ?? normal.Color;
        var end = over ? normal.Color.A == 0 ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(normal.Color.A, Lighten(normal.Color.R), Lighten(normal.Color.G), Lighten(normal.Color.B)) : normal.Color;
        var brush = new SolidColorBrush(end); frame.Background = brush;
        if (Enabled)
        {
            var animation = new ColorAnimation(start, end, TimeSpan.FromMilliseconds(130)) { FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) =>
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                if (!over && ReferenceEquals(frame.Background, brush)) frame.ClearValue(Border.BackgroundProperty);
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
        else if (!over) frame.ClearValue(Border.BackgroundProperty);
    }
    internal static void RefreshButtonBackground(Button button)
    {
        if (button.Template?.FindName("Frame", button) is not Border frame) return;
        frame.ClearValue(Border.BackgroundProperty);
        if (button.IsMouseOver) Hover(button, true);
    }
    private static byte Lighten(byte channel) => (byte)Math.Min(255, channel + 13);
    private static void Press(Button button, bool pressed)
    {
        if (button.Template.FindName("Frame", button) is not Border frame) return;
        if (frame.RenderTransform is not ScaleTransform) frame.RenderTransform = new ScaleTransform(1, 1);
        frame.RenderTransformOrigin = new Point(.5, .5);
        var scale = (ScaleTransform)frame.RenderTransform;
        Tween(scale, ScaleTransform.ScaleXProperty, pressed ? .93 : 1, pressed ? 70 : 160);
        Tween(scale, ScaleTransform.ScaleYProperty, pressed ? .93 : 1, pressed ? 70 : 160);
    }
    private static void CheckChanged(CheckBox check, bool value)
    {
        if (check.Template?.FindName("Mark", check) is not System.Windows.Shapes.Path mark) return;
        mark.BeginAnimation(UIElement.OpacityProperty, null);
        mark.Opacity = value ? 0 : 1;
        Tween(mark, UIElement.OpacityProperty, value ? 1 : 0, 120);
    }
    private static void SwitchMoved(CheckBox check, bool instant)
    {
        if (!instant && !check.IsLoaded) return;
        if (check.Template?.FindName("SwitchKnob", check) is not FrameworkElement knob || knob.RenderTransform is not TranslateTransform shift) return;
        if (shift.IsFrozen) { shift = shift.Clone(); knob.RenderTransform = shift; }
        double target = check.IsChecked == true ? 18 : 0;
        if (instant || !Enabled) { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = target; }
        else Tween(shift, TranslateTransform.XProperty, target, 280);
        if (check.Template.FindName("SwitchTrack", check) is Border track)
        {
            var color = ((SolidColorBrush)check.FindResource(check.IsChecked == true ? "Accent" : "SwitchOff")).Color;
            var from = (track.Background as SolidColorBrush)?.Color ?? color;
            var brush = new SolidColorBrush(from); track.Background = brush;
            PolishedMotion.Color(brush, color, instant ? 0 : 280);
        }
    }
}

internal sealed class CornerAnimation : AnimationTimeline
{
    public CornerRadius From { get; set; }
    public CornerRadius To { get; set; }
    public override Type TargetPropertyType => typeof(CornerRadius);
    protected override Freezable CreateInstanceCore() => new CornerAnimation { From = From, To = To };
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
    {
        double t = clock.CurrentProgress ?? 1; t = 1 - Math.Pow(1 - t, 3);
        double Blend(double a, double b) => a + (b - a) * t;
        return new CornerRadius(Blend(From.TopLeft, To.TopLeft), Blend(From.TopRight, To.TopRight), Blend(From.BottomRight, To.BottomRight), Blend(From.BottomLeft, To.BottomLeft));
    }
}
