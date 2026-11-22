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
    private Action? releaseTrackIdentity;
    private string CurrentVisualKey => media.Source + "\n" + media.Title + "\n" + media.Artist;
    private void BeginTrackVisualHold(string key, bool verification = false)
    {
        if ((!verification && App.VerificationMode) || !IsLoaded || HasCompactNotification || Shell.ActualWidth < 1 || Shell.ActualHeight < 1) return;
        if (!trackVisualPending)
        {
            heldWindowWidth = Width; heldWindowHeight = Height;
            releaseTrackIdentity = PolishedMotion.Hold(TrackIdentity);
        }
        ++trackVisualRevision; trackCommitQueued = false;
        trackVisualPending = true; pendingVisualKey = key; lyricsReadyVisualKey = "";
        BeginTrackIntroduction();
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
            UpdateLayout(); trackVisualPending = false;
            releaseTrackIdentity?.Invoke(); releaseTrackIdentity = null;
            RefreshIdleContent(); UpdateLayout();
            if (expanded && Navigation.SelectedIndex == 0) AnimateExpansion(true);
        }));
    }
    private void CancelTrackVisualHold()
    {
        if (!trackVisualPending && TrackVisualSnapshot.Source == null) return;
        ++trackVisualRevision; trackVisualPending = trackCommitQueued = false;
        releaseTrackIdentity?.Invoke(); releaseTrackIdentity = null;
        TrackVisualSnapshot.BeginAnimation(OpacityProperty, null); TrackVisualSnapshot.Source = null; TrackVisualSnapshot.Visibility = Visibility.Collapsed;
        Motion.ClearContentMotion(Shell);
    }
}
