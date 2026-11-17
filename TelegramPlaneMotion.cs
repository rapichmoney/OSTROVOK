using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopIsland;

public partial class MainWindow
{
    private int planeFlightRevision;
    private bool planeFlying;

    private void StopTelegramPlaneFlight()
    {
        ++planeFlightRevision; planeFlying = false;
        Motion.ClearContentMotion(FlyingTelegramPlane);
        FlyingTelegramPlane.Visibility = Visibility.Collapsed;
        Motion.ClearContentMotion(CompactCardIcon);
    }

    private void FlyTelegramPlane()
    {
        StopTelegramPlaneFlight();
        if (!Motion.Enabled || !CompactCard.IsVisible || !telegramNoticeActive) return;
        int revision = planeFlightRevision; planeFlying = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
        if (revision != planeFlightRevision || !CompactCard.IsVisible || !telegramNoticeActive) return;
        CompactCard.UpdateLayout();
        var root = (UIElement)Shell.Child;
        Point landing = CompactCardIcon.TranslatePoint(new Point(0, 0), root);
        double initialX = Math.Min(landing.X + 180, Math.Max(landing.X, Shell.ActualWidth - 72));
        var scale = new ScaleTransform(1.15, 1.15);
        var angle = new RotateTransform(-135);
        var shift = new TranslateTransform(initialX, landing.Y + 8);
        var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(angle); group.Children.Add(shift);
        FlyingTelegramPlane.Data = CompactCardIcon.Data;
        FlyingTelegramPlane.Fill = CompactCardIcon.Fill; FlyingTelegramPlane.Stroke = CompactCardIcon.Stroke;
        FlyingTelegramPlane.RenderTransformOrigin = new Point(.5, .5); FlyingTelegramPlane.RenderTransform = group;
        FlyingTelegramPlane.Opacity = 0; FlyingTelegramPlane.Visibility = Visibility.Visible; CompactCardIcon.Opacity = 0;
        foreach (var text in new[] { CompactCardTitle, CompactCardText }) { text.BeginAnimation(OpacityProperty, null); text.Opacity = 0; }
        Motion.Tween(CompactCardTitle, OpacityProperty, 1, 260, 600);
        Motion.Tween(CompactCardText, OpacityProperty, 1, 260, 680);
        Motion.Tween(FlyingTelegramPlane, OpacityProperty, 1, 120, 100);
        Motion.Tween(shift, TranslateTransform.XProperty, landing.X, 850, 100);
        Motion.Tween(angle, RotateTransform.AngleProperty, 0, 450, 500);
        Motion.Tween(scale, ScaleTransform.ScaleXProperty, 1, 650, 100);
        Motion.Tween(scale, ScaleTransform.ScaleYProperty, 1, 650, 100);
        shift.Y = landing.Y;
        var arc = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1050), FillBehavior = FillBehavior.Stop };
        arc.KeyFrames.Add(new DiscreteDoubleKeyFrame(landing.Y + 8, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        arc.KeyFrames.Add(new DiscreteDoubleKeyFrame(landing.Y + 8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
        arc.KeyFrames.Add(new EasingDoubleKeyFrame(landing.Y - 9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        arc.KeyFrames.Add(new EasingDoubleKeyFrame(landing.Y, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1050)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        arc.Completed += (_, _) =>
        {
            if (revision != planeFlightRevision) return;
            planeFlying = false; FlyingTelegramPlane.Visibility = Visibility.Collapsed;
            Motion.ClearContentMotion(FlyingTelegramPlane); Motion.Tween(CompactCardIcon, OpacityProperty, 1, 100);
        };
        shift.BeginAnimation(TranslateTransform.YProperty, arc);
        }));
    }
}
