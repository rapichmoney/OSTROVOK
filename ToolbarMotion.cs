using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopIsland;

public partial class MainWindow
{
    private int animatedTab = -1;
    private string animatedCaption = "", animatedFont = "";
    private void AnimateToolbarSelection()
    {
        if (!IsLoaded || !TopNavigation.IsVisible) return;
        int selected = Navigation.SelectedIndex;
        if (!toolbar.ContainsKey(selected)) { ToolbarIndicator.Visibility = Visibility.Collapsed; return; }
        bool changed = animatedTab != selected || animatedCaption != toolbar[selected].Label.Text || animatedFont != FontFamily.Source;
        animatedCaption = toolbar[selected].Label.Text; animatedFont = FontFamily.Source;
        int previous = animatedTab; animatedTab = selected;
        double targetX = Math.Max(TopNavigation.Margin.Left, TopNavigation.TranslatePoint(new Point(0, 0), ToolbarIndicatorLayer).X);
        double duration = 360;
        if (previous >= 0 && toolbar.TryGetValue(previous, out var old))
            duration = Math.Clamp(330 + Math.Abs(old.Button.TranslatePoint(new Point(), TopNavigation).X - toolbar[selected].Button.TranslatePoint(new Point(), TopNavigation).X) * .5, 360, 450);
        var palette = CompactMaterialColors();
        ToolbarIndicator.Background = new SolidColorBrush(Color.FromRgb((byte)(palette.Top.R * .7 + 255 * .12), (byte)(palette.Top.G * .7 + 255 * .12), (byte)(palette.Top.B * .7 + 255 * .12)));
        ToolbarIndicator.BorderBrush = new SolidColorBrush(Color.FromArgb(90, palette.Rim.R, palette.Rim.G, palette.Rim.B));
        double selectedWidth = 26;
        foreach (var (index, item) in toolbar)
        {
            var label = item.Label; var button = item.Button;
            if (index == selected) label.Visibility = Visibility.Visible;
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = index == selected ? 26 + 8 + label.DesiredSize.Width : 26;
            if (index == selected) selectedWidth = width;
            if (toolbar.Keys.TakeWhile(x => x != selected).Contains(index)) targetX += width;
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.ClipToBounds = true;
            button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent;
            Motion.RefreshButtonBackground(button);
            if (index == selected) { label.Visibility = Visibility.Visible; label.Opacity = 1; }
            if (changed)
            {
                if (double.IsNaN(button.Width)) button.Width = button.ActualWidth;
                Motion.Tween(button, WidthProperty, width, previous < 0 ? 0 : duration, completed: () =>
                { if (animatedTab != index) label.Visibility = Visibility.Collapsed; });
            }
        }
        ToolbarIndicatorLayer.Visibility = Visibility.Visible; ToolbarIndicator.Visibility = Visibility.Visible;
        ToolbarIndicator.Height = toolbar[selected].Button.ActualHeight;
        Canvas.SetTop(ToolbarIndicator, TopNavigation.TranslatePoint(new Point(), ToolbarIndicatorLayer).Y);
        if (!changed) return;
        if (double.IsNaN(Canvas.GetLeft(ToolbarIndicator))) Canvas.SetLeft(ToolbarIndicator, targetX);
        if (double.IsNaN(ToolbarIndicator.Width)) ToolbarIndicator.Width = selectedWidth;
        Motion.Tween(ToolbarIndicator, Canvas.LeftProperty, targetX, previous < 0 ? 0 : duration);
        Motion.Tween(ToolbarIndicator, WidthProperty, selectedWidth, previous < 0 ? 0 : duration);
    }
}
