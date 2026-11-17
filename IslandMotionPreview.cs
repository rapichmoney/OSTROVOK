using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopIsland;

internal sealed class IslandMotionPreview : Border
{
    private readonly Border pill;
    private readonly TextBlock detail;
    private readonly DispatcherTimer returnTimer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private string motionStyle = "Капля";
    private double speed = 100, strength = 60, radius = 28;
    private bool opened;

    internal IslandMotionPreview(TextBlock clock)
    {
        Height = 176; Background = new SolidColorBrush(Color.FromRgb(27, 30, 34)); CornerRadius = new CornerRadius(8);
        clock.FontSize = 13; clock.HorizontalAlignment = HorizontalAlignment.Center; clock.Margin = new Thickness(0, 8, 0, 0);
        var content = new Grid(); content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); content.RowDefinitions.Add(new RowDefinition());
        content.Children.Add(clock);
        detail = new TextBlock { Text = "Текст и управление\nпоявляются после оболочки", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(146, 154, 165)), TextAlignment = TextAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(12, 6, 12, 10), LineHeight = 20 };
        Grid.SetRow(detail, 1); content.Children.Add(detail);
        pill = new Border { Width = 280, Height = 38, Background = new SolidColorBrush(Color.FromRgb(24, 26, 29)), BorderBrush = new SolidColorBrush(Color.FromRgb(53, 59, 67)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = content, ClipToBounds = true };
        Child = new Grid { Children = { pill } };
        MouseEnter += (_, _) => { returnTimer.Stop(); Animate(true); };
        MouseLeave += (_, _) => Animate(false);
        returnTimer.Tick += (_, _) => { returnTimer.Stop(); if (!IsMouseOver) Animate(false); };
        Unloaded += (_, _) => { returnTimer.Stop(); pill.BeginAnimation(WidthProperty, null); pill.BeginAnimation(HeightProperty, null); pill.Width = 280; pill.Height = 38; opened = false; detail.Visibility = Visibility.Collapsed; Motion.ClearContentMotion(detail); };
    }

    internal void Configure(string style, double corner, double tempo, double spring) { motionStyle = style; radius = corner; speed = tempo; strength = spring; }
    internal void Play() { Animate(true, true); returnTimer.Stop(); returnTimer.Start(); }
    private void Animate(bool expand, bool replay = false)
    {
        if (opened == expand && !replay) return;
        opened = expand;
        if (replay) { pill.BeginAnimation(WidthProperty, null); pill.BeginAnimation(HeightProperty, null); pill.Width = 280; pill.Height = 38; }
        double width = expand ? 400 : 280, height = expand ? 118 : 38;
        detail.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        if (expand) Motion.RevealContent(detail, 340, 80);
        pill.CornerRadius = new CornerRadius(expand ? Math.Clamp(radius, 0, 30) : 18);
        var plan = MotionDynamics.Plan(motionStyle, expand, speed, strength);
        Motion.Tween(pill, WidthProperty, width, plan.Width.Duration, plan.Width.Delay, easingOverride: MotionDynamics.SafeGeometry(plan.Width.Easing, pill.Width, width, 200, 460));
        Motion.Tween(pill, HeightProperty, height, plan.Height.Duration, plan.Height.Delay, easingOverride: MotionDynamics.SafeGeometry(plan.Height.Easing, pill.Height, height, 26, 150));
    }
}
