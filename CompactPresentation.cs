using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopIsland;

public partial class MainWindow
{
    private string compactMeasuredTitle = "";
    private double compactTitleWidth;
    private bool compactVisualNotice;
    private int compactTransitionRevision;
    private bool HasCompactTrack => compactMusicActive;
    private readonly Services.StableMusicWidth stableMusicWidth = new();
    private double StandbyCompactWidth
    {
        get
        {
            double scale = Math.Clamp(settings.CompactScale, 75, 150) / 100;
            return stableMusicWidth.Resolve(NaturalCompactWidth / scale, CompactWidth / scale, HasCompactTrack && !settings.MiniMode && settings.LyricsOnCompact) * scale;
        }
    }
    private double NaturalCompactWidth
    {
        get
        {
            double scale = Math.Clamp(settings.CompactScale, 75, 150) / 100;
            if (settings.MiniMode) return CompactWidth;
            if (!HasCompactTrack) return Math.Min(CompactWidth, 318 * scale);
            if (lyricsLoading && lyricsPendingWidth > 0) return Math.Min(CompactWidth, lyricsPendingWidth * scale);
            if (settings.LyricsOnCompact && lyricsResult?.Synced == true) return CompactWidth;
            if (compactMeasuredTitle != CompactTrackCaption)
            {
                compactMeasuredTitle = CompactTrackCaption;
                compactTitleWidth = new FormattedText(CompactTrackCaption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
            }
            double maximum = Math.Min(CompactWidth / scale, 560);
            return Math.Clamp(Math.Ceiling(compactTitleWidth + 116), Math.Min(220, maximum), maximum) * scale;
        }
    }

    private void CaptureCompactTransition(bool active)
    {
        int revision = ++compactTransitionRevision;
        if (expanded || !IsLoaded || !Motion.Enabled) { StopCompactTransition(); return; }
        var previous = active ? (FrameworkElement)Header : CompactCard;
        if (!previous.IsVisible || previous.ActualWidth < 1 || previous.ActualHeight < 1) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(previous.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(previous.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(previous); bitmap.Freeze();
        CompactSnapshot.Source = bitmap; CompactSnapshot.Visibility = Visibility.Visible;
        CompactSnapshot.BeginAnimation(OpacityProperty, null); CompactSnapshot.Opacity = 1;
        Motion.Tween(CompactSnapshot, OpacityProperty, 0, 150, completed: () =>
        {
            if (revision != compactTransitionRevision) return;
            CompactSnapshot.Source = null; CompactSnapshot.Visibility = Visibility.Collapsed;
        });
    }

    private void StopCompactTransition()
    {
        StopCompactTextTransition();
        ++compactTransitionRevision;
        CompactSnapshot.BeginAnimation(OpacityProperty, null);
        CompactSnapshot.Source = null; CompactSnapshot.Visibility = Visibility.Collapsed;
        Motion.ClearContentMotion(Header); Motion.ClearContentMotion(CompactCard);
    }
}
