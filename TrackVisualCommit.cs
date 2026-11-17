using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopIsland;

public partial class MainWindow
{
    private bool trackVisualPending, trackCommitQueued;
    private string pendingVisualKey = "", lyricsReadyVisualKey = "";
    private double heldWindowWidth, heldWindowHeight;
    private int trackVisualRevision;
    private string CurrentVisualKey => media.Source + "\n" + media.Title + "\n" + media.Artist;
    private void BeginTrackVisualHold(string key, bool verification = false)
    {
        if ((!verification && App.VerificationMode) || !IsLoaded || HasCompactNotification || Shell.ActualWidth < 1 || Shell.ActualHeight < 1) return;
        if (!trackVisualPending)
        {
            StopCompactTextTransition();
            Motion.ClearContentMotion(Shell);
            var dpi = VisualTreeHelper.GetDpi(this);
            var image = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            image.Render(Shell); image.Freeze();
            TrackVisualSnapshot.BeginAnimation(OpacityProperty, null);
            TrackVisualSnapshot.Source = image; TrackVisualSnapshot.Opacity = 1; TrackVisualSnapshot.Visibility = Visibility.Visible;
            heldWindowWidth = Width; heldWindowHeight = Height;
        }
        ++trackVisualRevision; trackCommitQueued = false;
        trackVisualPending = true; pendingVisualKey = key; lyricsReadyVisualKey = "";
        Shell.BeginAnimation(OpacityProperty, null); Shell.Opacity = 0;
    }
    private void TryCommitTrackVisual()
    {
        if (!trackVisualPending)
        {
            if (media.Available && media.ArtworkReady && (!settings.LyricsEnabled || lyricsReadyVisualKey == CurrentVisualKey)) BeginTrackIntroduction();
            return;
        }
        if (!trackVisualPending || trackCommitQueued || CurrentVisualKey != pendingVisualKey || !media.ArtworkReady || (settings.LyricsEnabled && lyricsReadyVisualKey != pendingVisualKey)) return;
        trackCommitQueued = true;
        int revision = trackVisualRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            trackCommitQueued = false;
            if (revision != trackVisualRevision || !trackVisualPending || CurrentVisualKey != pendingVisualKey || !media.ArtworkReady || (settings.LyricsEnabled && lyricsReadyVisualKey != pendingVisualKey)) return;
            UpdateLayout(); StopCompactTextTransition(); BeginTrackIntroduction(); trackVisualPending = false;
            RefreshIdleContent(); UpdateLayout();
            if (expanded && Navigation.SelectedIndex == 0) AnimateExpansion(true);
            Motion.Enter(Shell, 0, 280);
            Motion.Tween(TrackVisualSnapshot, OpacityProperty, 0, 240, completed: () =>
            {
                if (revision != trackVisualRevision) return;
                TrackVisualSnapshot.Source = null; TrackVisualSnapshot.Visibility = Visibility.Collapsed;
            });
        }));
    }
    private void CancelTrackVisualHold()
    {
        if (!trackVisualPending && TrackVisualSnapshot.Source == null) return;
        ++trackVisualRevision; trackVisualPending = trackCommitQueued = false;
        TrackVisualSnapshot.BeginAnimation(OpacityProperty, null); TrackVisualSnapshot.Source = null; TrackVisualSnapshot.Visibility = Visibility.Collapsed;
        Motion.ClearContentMotion(Shell);
    }
}
