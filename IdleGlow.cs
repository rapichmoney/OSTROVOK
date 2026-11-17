using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopIsland;

public partial class MainWindow
{
    private IdleGlowWindow? idleGlow;
    private void InitializeIdleGlow()
    {
        if (App.VerificationMode) return;
        LocationChanged += (_, _) => UpdateIdleGlow();
        SizeChanged += (_, _) => UpdateIdleGlow();
        IsVisibleChanged += (_, _) => UpdateIdleGlow();
        UpdateIdleGlow();
    }
    private void UpdateIdleGlow()
    {
        if (App.VerificationMode || !IsLoaded || handle == IntPtr.Zero) return;
        bool visible = !closing && IsVisible && !expanded && !shellMoving && settings.IdleGlow;
        if (!visible) { idleGlow?.Hide(); return; }
        idleGlow ??= new IdleGlowWindow { Owner = this };
        idleGlow.Follow(this);
        if (!idleGlow.IsVisible) idleGlow.Show();
    }
}

internal sealed class IdleGlowWindow : Window
{
    private readonly Grid surface = new();
    private readonly Border halo = new() { Background = Brushes.White, CornerRadius = new CornerRadius(22), Margin = new Thickness(20), Effect = new DropShadowEffect { Color = Colors.White, ShadowDepth = 0, BlurRadius = 22, Opacity = .68 } };
    public IdleGlowWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; IsHitTestVisible = false; ResizeMode = ResizeMode.NoResize;
        surface.Children.Add(halo); Content = surface;
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x20 | 0x80 | 0x08000000); };
    }
    internal void Follow(Window owner)
    {
        double width = owner.ActualWidth + 40, height = owner.ActualHeight + 40;
        Left = owner.Left - 20; Top = owner.Top - 20; Topmost = owner.Topmost;
        if (Width == width && Height == height) return;
        Width = width; Height = height;
        surface.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(new Rect(20, 20, owner.ActualWidth, owner.ActualHeight), 22, 22));
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
