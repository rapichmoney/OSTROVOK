using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopIsland;

public partial class MainWindow
{
    private int shellRevision;
    private bool shellMoving;
    private bool hiding;
    private string displayedTrack = "";

    private void AnimateExpansion(bool value)
    {
        if (trackVisualPending && value != expanded) CancelTrackVisualHold();
        idleGlow?.Hide();
        bool opening = value && !expanded;
        bool compactResize = !value && !expanded && Math.Abs(Height - CompactHeight) < 1 && !HasCompactNotification;
        var revision = ++shellRevision;
        var center = Left + Width / 2;
        var area = Services.WindowInterop.WorkArea(handle);
        double targetWidth = value ? ExpandedWidth : Math.Min(CompactPresentationWidth, area.Width);
        double targetHeight = value ? Math.Min(ExpandedHeight, area.Height) : Math.Min(CompactPresentationHeight, area.Height);
        double targetLeft = Math.Clamp(center - targetWidth / 2, area.Left, Math.Max(area.Left, area.Right - targetWidth));
        double targetTop = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - targetHeight));
        expanded = value;
        if (value) StopCompactTransition();
        HeaderRow.Height = new GridLength(value ? 64 : HasCompactNotification ? 62 : 34);
        compactNoticeGeometryActive = HasCompactNotification;
        MoveHandle.Visibility = HeightHandle.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        CompactLeftHandle.Visibility = CompactRightHandle.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        UpdateClipboardNoticeVisibility(false);
        MaxHeight = area.Height;
        Header.Visibility = value || HasCompactNotification ? Visibility.Collapsed : Visibility.Visible;
        TopNavigation.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        ToolbarIndicatorLayer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (value) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateToolbar));
        UpdateClock();
        var cornerFrom = Shell.CornerRadius;
        Shell.BeginAnimation(System.Windows.Controls.Border.CornerRadiusProperty, null);
        ApplyShellAppearance();
        var plan = MotionDynamics.Plan(settings.AnimationStyle, targetWidth > Width + .1 || targetHeight > Height + .1, settings.AnimationSpeed, settings.SpringStrength);
        if (compactResize)
        {
            var axis = new MotionAxis(360 * 100 / Math.Clamp(settings.AnimationSpeed, 70, 140), 0, new CubicEase { EasingMode = EasingMode.EaseInOut });
            plan = new ShellMotionPlan(axis, axis);
        }
        double duration = plan.Duration;
        if (!Motion.Enabled)
        {
            StopShellMotion();
            Width = targetWidth; Height = targetHeight; Left = targetLeft; Top = targetTop;
            Body.Visibility = Footer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Body.Opacity = Footer.Opacity = 1;
            return;
        }
        if (!shellMoving) CompositionTarget.Rendering += SyncAnimatedBounds;
        shellMoving = true;
        Body.Width = 386; Body.Height = Math.Max(0, Math.Min(ExpandedHeight, area.Height) / (Math.Clamp(settings.ExpandedScale, 80, 150) / 100) - 104);
        Body.HorizontalAlignment = HorizontalAlignment.Left; Body.VerticalAlignment = VerticalAlignment.Top;
        if (value && Body.Visibility != Visibility.Visible)
        {
            Body.Opacity = Footer.Opacity = 0;
            Body.Visibility = Footer.Visibility = Visibility.Visible;
        }
        Shell.BeginAnimation(System.Windows.Controls.Border.CornerRadiusProperty, new CornerAnimation
        {
            From = cornerFrom, To = Shell.CornerRadius, Duration = TimeSpan.FromMilliseconds(duration), FillBehavior = FillBehavior.Stop
        });
        var widthEase = MotionDynamics.SafeGeometry(plan.Width.Easing, Width, targetWidth, Math.Min(targetWidth, CompactWidth * .65), area.Width);
        var heightEase = MotionDynamics.SafeGeometry(plan.Height.Easing, Height, targetHeight, Math.Min(targetHeight, CompactHeight * .72), Math.Max(targetHeight, area.Bottom - targetTop));
        Action complete = () =>
        {
            if (revision != shellRevision || closing) return;
            StopShellMotion();
            Width = targetWidth; Height = targetHeight; Left = targetLeft; Top = targetTop;
            Services.WindowInterop.SetAnimatedBounds(handle, Left, Top, targetWidth, targetHeight, VisualTreeHelper.GetDpi(this));
            FinishExpansionLayout(value);
        };
        Motion.Tween(this, LeftProperty, targetLeft, plan.Width.Duration, plan.Width.Delay, easingOverride: widthEase);
        Motion.Tween(this, TopProperty, targetTop, plan.Height.Duration, plan.Height.Delay, easingOverride: new CubicEase { EasingMode = EasingMode.EaseOut });
        Motion.Tween(this, WidthProperty, targetWidth, plan.Width.Duration, plan.Width.Delay, completed: plan.Width.End >= plan.Height.End ? complete : null, easingOverride: widthEase);
        Motion.Tween(this, HeightProperty, targetHeight, plan.Height.Duration, plan.Height.Delay, completed: plan.Height.End > plan.Width.End ? complete : null, easingOverride: heightEase);
        if (opening) Motion.RevealContent(Body, Math.Min(420, duration - 60), 60);
        else Motion.Tween(Body, OpacityProperty, value ? 1 : 0, value ? 180 : 110);
        Motion.Tween(Footer, OpacityProperty, value ? 1 : 0, value ? 240 : 100, value ? 80 : 0);
    }
    private void FinishExpansionLayout(bool value)
    {
        Body.HorizontalAlignment = HorizontalAlignment.Stretch; Body.VerticalAlignment = VerticalAlignment.Stretch;
        Body.Visibility = Footer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Body.BeginAnimation(OpacityProperty, null); Footer.BeginAnimation(OpacityProperty, null);
        Body.Opacity = Footer.Opacity = 1;
        if (value) QueueCurrentLyricScroll();
        if (!shellMoving) UpdateIdleGlow();
    }
    private void SyncAnimatedBounds(object? sender, EventArgs e)
    {
        if (shellMoving && !closing)
        {
            var area = Services.WindowInterop.WorkArea(handle);
            double width = Math.Min(Width, area.Width), height = Math.Min(Height, area.Height);
            double left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - width));
            double top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - height));
            Services.WindowInterop.SetAnimatedBounds(handle, left, top, width, height, VisualTreeHelper.GetDpi(this));
        }
    }
    private void StopShellMotion(bool settle = false)
    {
        var center = Left + Width / 2;
        ++shellRevision; shellMoving = false;
        CompositionTarget.Rendering -= SyncAnimatedBounds;
        foreach (var property in new[] { WidthProperty, HeightProperty, LeftProperty, TopProperty })
        {
            var current = (double)GetValue(property); BeginAnimation(property, null); SetValue(property, current);
        }
        Shell.BeginAnimation(System.Windows.Controls.Border.CornerRadiusProperty, null);
        Body.Width = Body.Height = double.NaN;
        Motion.ClearContentMotion(Body);
        FinishExpansionLayout(expanded);
        if (settle)
        {
            Width = expanded ? ExpandedWidth : CompactPresentationWidth; Height = expanded ? Math.Min(ExpandedHeight, Services.WindowInterop.WorkArea(handle).Height) : CompactPresentationHeight;
            Left = center - Width / 2; ClampToMonitor();
        }
    }
    private void HideAnimated()
    {
        int revision = ++hideRevision;
        if (!Motion.Enabled || settings.HideStyle == "Без анимации") { hiding = false; Hide(); return; }
        if (shellMoving) StopShellMotion(true);
        StopPageTransition(); hiding = true; IsHitTestVisible = false;
        Motion.ClearContentMotion(Shell);
        var scale = new ScaleTransform(1, 1); var shift = new TranslateTransform(); var transforms = new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(shift);
        Shell.RenderTransformOrigin = new Point(.5, 0); Shell.RenderTransform = transforms;
        bool drop = settings.HideStyle == "Капля вверх", fold = settings.HideStyle == "Схлопывание";
        double duration = drop ? 420 : fold ? 330 : 300;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        Motion.Tween(scale, ScaleTransform.ScaleXProperty, drop ? .12 : fold ? .84 : .95, drop ? 260 : duration, easingOverride: ease);
        Motion.Tween(scale, ScaleTransform.ScaleYProperty, drop ? .18 : fold ? .025 : .95, duration, easingOverride: ease);
        if (drop) Motion.Tween(shift, TranslateTransform.YProperty, -34, duration, easingOverride: ease);
        Motion.Tween(this, OpacityProperty, 0, drop ? 180 : duration, drop ? 240 : 0, completed: () =>
        {
            if (revision != hideRevision) return;
            Hide(); hiding = false; Opacity = 1; Motion.ClearContentMotion(Shell); IsHitTestVisible = true;
        });
    }
    private void AnimateRevealShell()
    {
        if (!Motion.Enabled || settings.HideStyle == "Без анимации") return;
        bool drop = settings.HideStyle == "Капля вверх", fold = settings.HideStyle == "Схлопывание";
        var scale = new ScaleTransform(drop ? .16 : fold ? .84 : .95, drop ? .22 : fold ? .03 : .95);
        var shift = new TranslateTransform(0, drop ? -24 : 0); var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(shift);
        Shell.RenderTransformOrigin = new Point(.5, 0); Shell.RenderTransform = group;
        var spring = new MotionDynamics.PhysicalResponse(drop ? .7 : .84, 12);
        Motion.Tween(scale, ScaleTransform.ScaleXProperty, 1, drop ? 620 : 460, drop ? 100 : 0, easingOverride: spring);
        Motion.Tween(scale, ScaleTransform.ScaleYProperty, 1, drop ? 580 : 480, easingOverride: spring);
        Motion.Tween(shift, TranslateTransform.YProperty, 0, 340);
        Opacity = 0; Motion.Tween(this, OpacityProperty, 1, 180);
        Motion.RevealContent(Body, 300, drop ? 180 : 70);
    }
    private void ReducedMotionChanged(object sender, RoutedEventArgs e)
    {
        settings.ReducedMotion = LessMotion.IsChecked == true;
        Motion.Reduced = settings.ReducedMotion;
        if (!Motion.Enabled)
        {
            StopPageTransition();
            StopShellMotion(); AnimateExpansion(expanded);
            ContentTabs.BeginAnimation(OpacityProperty, null); ContentTabs.Opacity = 1; ContentTabs.RenderTransform = Transform.Identity;
        }
        SaveSettings();
    }
}
