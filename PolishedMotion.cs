using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DesktopIsland;

internal static class PolishedMotion
{
    // Take the current rendered value when interrupting a transition.
    internal static void Color(SolidColorBrush brush, Color target, double duration = 280)
    {
        var from = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = target;
        if (Motion.Enabled) brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(from, target, TimeSpan.FromMilliseconds(duration)) { FillBehavior = FillBehavior.Stop });
    }

    internal static void Swap(FrameworkElement element, Action change, double duration = 280, double distance = 6)
    {
        if (!Motion.Enabled || !element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1)
        { change(); return; }
        var layer = AdornerLayer.GetAdornerLayer(element);
        Snapshot? old = null;
        if (layer != null)
        {
            var dpi = VisualTreeHelper.GetDpi(element);
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(element); bitmap.Freeze();
            old = new Snapshot(element, bitmap); layer.Add(old);
            element.Unloaded += Remove;
            Motion.Tween(old, UIElement.OpacityProperty, 0, duration >= 300 ? 120 : Math.Min(180, duration), completed: () => Remove(element, new RoutedEventArgs()));
            void Remove(object sender, RoutedEventArgs e) { layer.Remove(old); element.Unloaded -= Remove; }
        }
        change(); Motion.Enter(element, distance, duration);
        if (duration >= 300) Motion.Tween(element, UIElement.OpacityProperty, 1, duration - 65, 65);
    }
    internal static void FillFromBottom(FrameworkElement element, Action change)
    {
        Swap(element, change, 300, 0);
        if (!Motion.Enabled || element.ActualWidth < 1 || element.ActualHeight < 1) return;
        var clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        element.Clip = clip;
        var reveal = new RectAnimation(new Rect(0, element.ActualHeight, element.ActualWidth, 0), clip.Rect, TimeSpan.FromMilliseconds(300))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        reveal.Completed += (_, _) => { if (ReferenceEquals(element.Clip, clip)) element.Clip = null; };
        clip.BeginAnimation(RectangleGeometry.RectProperty, reveal);
    }
    internal static Action? Hold(FrameworkElement element)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer == null || !element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1) return null;
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element); bitmap.Freeze();
        var snapshot = new Snapshot(element, bitmap); layer.Add(snapshot);
        bool released = false;
        return () => { if (released) return; released = true; Motion.Enter(element, 6, 420); Motion.Tween(snapshot, UIElement.OpacityProperty, 0, 420, completed: () => layer.Remove(snapshot)); };
    }
    private sealed class Snapshot(UIElement element, ImageSource bitmap) : Adorner(element)
    {
        protected override void OnRender(DrawingContext drawing) => drawing.DrawImage(bitmap, new Rect(AdornedElement.RenderSize));
        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;
    }

    internal static void Menu(ContextMenu menu)
    {
        Point screenPosition = new();
        DpiScale menuDpi = new(1, 1);
        menu.Opened += (_, _) =>
        {
            screenPosition = menu.PointToScreen(new Point()); menuDpi = VisualTreeHelper.GetDpi(menu);
            Motion.ClearContentMotion(menu);
            var scale = new ScaleTransform(.98, .98); var shift = new TranslateTransform(0, -6);
            var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(shift);
            menu.RenderTransformOrigin = new Point(1, 0); menu.RenderTransform = group;
            menu.Opacity = Motion.Enabled ? 0 : 1;
            Motion.Tween(menu, UIElement.OpacityProperty, 1, 220);
            Motion.Tween(shift, TranslateTransform.YProperty, 0, 340);
            Motion.Tween(scale, ScaleTransform.ScaleXProperty, 1, 340);
            Motion.Tween(scale, ScaleTransform.ScaleYProperty, 1, 340);
        };
        bool dismissing = false;
        menu.PreviewMouseLeftButtonUp += async (_, e) =>
        {
            if (!Motion.Enabled || dismissing) return;
            var node = e.OriginalSource as DependencyObject;
            while (node != null && node is not MenuItem) node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            if (node is not MenuItem item || item.HasItems || !item.IsEnabled || item.IsCheckable) return;
            e.Handled = true; dismissing = true;
            Motion.Tween(menu, UIElement.OpacityProperty, 0, 180);
            await Task.Delay(180);
            if (menu.IsOpen) { menu.IsOpen = false; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)); }
            dismissing = false;
        };
        menu.Closed += (_, _) =>
        {
            // Outside clicks and Escape close the native popup immediately. A non-interactive
            // snapshot finishes the visual exit without delaying focus or the clicked action.
            if (Motion.Enabled && menu.Opacity > .1 && menu.ActualWidth > 0 && menu.ActualHeight > 0 && !menu.Dispatcher.HasShutdownStarted)
                MenuExit(menu, screenPosition, menuDpi);
            Motion.ClearContentMotion(menu);
        };
    }

    private static void MenuExit(ContextMenu menu, Point position, DpiScale dpi)
    {
        try
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(menu.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(menu); bitmap.Freeze();
            var ghost = new Window { Width = menu.ActualWidth, Height = menu.ActualHeight, Left = position.X / dpi.DpiScaleX, Top = position.Y / dpi.DpiScaleY,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Focusable = false, IsHitTestVisible = false, Topmost = true,
                Content = new Image { Source = bitmap, IsHitTestVisible = false } };
            ghost.SourceInitialized += (_, _) =>
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(ghost).Handle;
                Services.WindowInterop.HideFromAltTab(handle);
                int style = GetWindowLong(handle, -20);
                SetWindowLong(handle, -20, style | 0x08000000 | 0x20); // NOACTIVATE | TRANSPARENT
            };
            ghost.Show(); Motion.Tween(ghost, UIElement.OpacityProperty, 0, 180, completed: ghost.Close);
        }
        catch (InvalidOperationException) { /* Native owner can disappear during shutdown. */ }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
}
