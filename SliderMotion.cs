using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DesktopIsland;

internal static class SliderMotion
{
    internal static void Attach(Slider slider)
    {
        var label = new TextBlock { Foreground = Brushes.WhiteSmoke, FontSize = 12 };
        var popup = new Popup { StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Top, VerticalOffset = -8,
            Child = new Border { Background = new SolidColorBrush(Color.FromRgb(53, 61, 68)), BorderBrush = new SolidColorBrush(Color.FromRgb(86, 97, 106)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4), Child = label } };
        Thumb? current = null;
        TextBlock? volumeLabel = null;
        Brush? volumeInk = null;
        void Close()
        {
            popup.IsOpen = false;
            if (volumeLabel != null && volumeInk != null) volumeLabel.Foreground = volumeInk;
            if (current?.RenderTransform is ScaleTransform scale)
            { Motion.Tween(scale, ScaleTransform.ScaleXProperty, 1, 160); Motion.Tween(scale, ScaleTransform.ScaleYProperty, 1, 160); }
        }
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, args) =>
        {
            current = args.OriginalSource as Thumb;
            if (current == null) return;
            if (slider.Name == "Volume" && Window.GetWindow(slider)?.FindName("VolumeText") is TextBlock valueLabel)
            { volumeLabel = valueLabel; volumeInk = valueLabel.Foreground; valueLabel.Foreground = Brushes.WhiteSmoke; }
            current.RenderTransformOrigin = new Point(.5, .5);
            var scale = new ScaleTransform(1, 1); current.RenderTransform = scale;
            Motion.Tween(scale, ScaleTransform.ScaleXProperty, 1.2, 100); Motion.Tween(scale, ScaleTransform.ScaleYProperty, 1.2, 100);
            if (slider.Name == "Timeline") { popup.PlacementTarget = current; label.Text = TimeSpan.FromSeconds(slider.Value).ToString(@"m\:ss"); popup.IsOpen = true; }
        }));
        slider.ValueChanged += (_, _) => { if (popup.IsOpen) { label.Text = TimeSpan.FromSeconds(slider.Value).ToString(@"m\:ss"); popup.HorizontalOffset = popup.HorizontalOffset == 0 ? .01 : 0; } };
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => Close()));
        slider.LostMouseCapture += (_, _) => Close();
        slider.Unloaded += (_, _) => Close();
        slider.IsVisibleChanged += (_, _) => { if (!slider.IsVisible) Close(); };
    }
}
