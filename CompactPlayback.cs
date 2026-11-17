using System.Windows.Media;
using System.Globalization;

namespace DesktopIsland;

public partial class MainWindow
{
    private string measuredCompactLine = "";
    private double measuredCompactLineWidth, measuredCompactDpi;
    private readonly RectangleGeometry compactLyricClip = new();
    private bool compactPlaybackPlaying, equalizerAnimating;
    private string CompactTrackCaption => string.IsNullOrWhiteSpace(media.Artist) ? media.Title : media.Title + " · " + media.Artist;
    private bool HasTimedCompactLyrics => !IsTrackIntroduction && settings.LyricsOnCompact && !settings.MiniMode && lyricsResult?.Synced == true && currentLyricIndex >= 0 && currentLyricIndex < lyricsResult.Lines.Length && !string.IsNullOrWhiteSpace(lyricsResult.Lines[currentLyricIndex].Text);

    private void ApplyCompactPlayback(bool active, bool playing, ImageSource? artwork)
    {
        compactPlaybackPlaying = active && playing;
        CompactLyricFill.Effect ??= new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, ShadowDepth = 0, BlurRadius = 4, Opacity = .6 };
        CompactArtworkFrame.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        CompactPlaybackBars.Visibility = active && playing ? Visibility.Visible : Visibility.Collapsed;
        UpdateCompactEqualizer();
        Clock.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        CompactClockGroup.Visibility = Clock.Visibility;
        IdleClockDial.Visibility = Visibility.Collapsed;
        CompactArtwork.Source = artwork;
        UpdateArtworkMaterial(active, artwork);
        CompactTrack.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        CompactTrack.Foreground = (Brush)FindResource(active && HasTimedCompactLyrics ? "Muted" : active ? "Ink" : "Secondary");
        if (active)
        {
            KeyboardBadge.Visibility = Battery.Visibility = CompactWeatherGroup.Visibility = Visibility.Collapsed;
            IdleDateBadge.Visibility = Visibility.Collapsed;
        }
        if (!active || !settings.LyricsOnCompact || lyricsResult?.Synced != true)
            CompactLyricFill.Visibility = Visibility.Collapsed;
        else UpdateCompactLyricProgress(media.Position, currentLyricIndex);
    }

    private void UpdateCompactEqualizer()
    {
        bool animate = compactPlaybackPlaying && Header.IsVisible && !expanded && !closing && Motion.Enabled;
        if (animate == equalizerAnimating) return;
        equalizerAnimating = animate;
        double[] speeds = [420, 610, 470, 540];
        int index = 0;
        foreach (var bar in CompactPlaybackBars.Children.OfType<System.Windows.Shapes.Rectangle>())
        {
            var transform = (ScaleTransform)bar.RenderTransform;
            if (transform.IsFrozen) { transform = transform.Clone(); bar.RenderTransform = transform; }
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transform.ScaleY = index % 2 == 0 ? .5 : .8;
            if (animate) transform.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(index % 2 == 0 ? .25 : 1, index % 2 == 0 ? 1 : .3, TimeSpan.FromMilliseconds(speeds[index]))
            {
                AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            });
            index++;
        }
    }

    private void RenderCompactLyrics(object? sender, EventArgs e)
    {
        UpdateTrackIntroduction();
        if (!expanded && Header.IsVisible && CompactClockGroup.IsVisible) UpdateIdleClockHands(DateTime.Now);
        if (!closing && expanded && Navigation.SelectedIndex == 0 && media.Available && lyricsResult?.Synced == true)
        {
            double expandedPosition = media.Position;
            int expandedIndex = Services.LyricsService.LineAt(lyricsResult.Lines, expandedPosition, settings.LyricsAdjustment);
            if (expandedIndex != currentLyricIndex) UpdateLyricPosition(expandedPosition);
            else UpdateExpandedLyricProgress(expandedPosition);
            return;
        }
        if (closing || expanded || !Header.IsVisible || !compactMusicActive || !HasTimedCompactLyrics) return;
        double position = media.Position;
        int index = Services.LyricsService.LineAt(lyricsResult!.Lines, position, settings.LyricsAdjustment);
        if (index != currentLyricIndex) UpdateLyricPosition(position);
        else UpdateCompactLyricProgress(position, index);
    }

    private void UpdateCompactLyricProgress(double position, int index)
    {
        if (IsTrackIntroduction) { CompactLyricFill.Visibility = Visibility.Collapsed; return; }
        if (!settings.LyricsOnCompact || settings.MiniMode || lyricsResult?.Synced != true || index < 0 || index >= lyricsResult.Lines.Length || CompactArtworkFrame.Visibility != Visibility.Visible)
        { CompactLyricFill.Visibility = Visibility.Collapsed; return; }
        var line = lyricsResult.Lines[index];
        if (string.IsNullOrWhiteSpace(line.Text)) { CompactLyricFill.Visibility = Visibility.Collapsed; return; }
        double end = line.EndSeconds ?? (index + 1 < lyricsResult.Lines.Length ? lyricsResult.Lines[index + 1].Seconds : media.Duration);
        double fraction = end > line.Seconds ? Math.Clamp((position + settings.LyricsAdjustment - line.Seconds) / (end - line.Seconds), 0, 1) : 1;
        CompactTrack.Foreground = (Brush)FindResource("Muted");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (line.Text != measuredCompactLine || dpi != measuredCompactDpi)
        {
            measuredCompactLine = line.Text; measuredCompactDpi = dpi;
            var text = new FormattedText(line.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, Brushes.White, dpi);
            measuredCompactLineWidth = text.WidthIncludingTrailingWhitespace;
            CompactLyricFill.Text = line.Text;
        }
        CompactLyricFill.Visibility = Visibility.Visible;
        CompactLyricFill.ToolTip = "Приблизительное заполнение по таймкодам строк";
        compactLyricClip.Rect = new Rect(0, 0, Math.Max(0, measuredCompactLineWidth * fraction), 32);
        if (!ReferenceEquals(CompactLyricFill.Clip, compactLyricClip)) CompactLyricFill.Clip = compactLyricClip;
    }
}
