namespace DesktopIsland;

public partial class MainWindow
{
    private Services.YandexLyricsBridge.PlaybackState? yandexPlayback;
    private bool pollingYandex, writingYandexVolume, ratingYandex;
    private double? pendingYandexVolume;
    private double lastYandexVolume = .5;
    private long yandexStateAt, yandexCommandRevision;
    private bool IsYandexSelected => media.Source == "ru.yandex.desktop.music";
    private bool CanSeekSelectedPlayer => media.CanSeek || IsYandexSelected && yandexPlayback != null;

    private async Task RefreshYandexPlaybackAsync()
    {
        if (closing || pollingYandex || (!IsYandexSelected && !media.IsVideo && media.Source.Length != 0) || !settings.YandexLyricsEnabled || writingYandexVolume || seeking || ratingYandex) return;
        pollingYandex = true;
        long revision = yandexCommandRevision;
        try
        {
            yandexLyrics.PrefetchEnabled = settings.LyricsEnabled && settings.YandexLyricsEnabled;
            var state = await yandexLyrics.PlaybackAsync();
            if (closing || (!IsYandexSelected && !media.IsVideo && media.Source.Length != 0) || seeking || writingYandexVolume || ratingYandex || revision != yandexCommandRevision) return;
            if (state == null && Environment.TickCount64 - yandexStateAt < 8000) return;
            yandexPlayback = state;
            if (state != null)
            {
                yandexStateAt = Environment.TickCount64;
                if (media.UpdateNativePlayback(state)) UpdateMedia();
            }
            if (IsYandexSelected) media.UpdateExternalPosition(state?.Position, isPlaying: state?.Playing);
            if (state?.Volume > .001) lastYandexVolume = state.Volume;
            Timeline.IsEnabled = media.Duration > 0 && CanSeekSelectedPlayer;
            RefreshVolume(); UpdateYandexRating();
        }
        finally { pollingYandex = false; }
    }
    private void UpdateYandexRating()
    {
        var visible = IsYandexSelected ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        YandexLikeButton.Visibility = YandexDislikeButton.Visibility = visible;
        YandexLikeButton.IsEnabled = !ratingYandex && yandexPlayback?.CanLike == true;
        YandexDislikeButton.IsEnabled = !ratingYandex && yandexPlayback?.CanDislike == true;
        bool liked = yandexPlayback?.Liked == true, disliked = yandexPlayback?.Disliked == true;
        YandexLikeIcon.Data = (System.Windows.Media.Geometry)FindResource(liked ? "Yandex_liked_xs" : "Yandex_like_xs");
        YandexDislikeIcon.Data = (System.Windows.Media.Geometry)FindResource(disliked ? "Yandex_disliked_xs" : "Yandex_dislike_xs");
        YandexLikeIcon.Fill = (System.Windows.Media.Brush)FindResource(liked ? "Ink" : "Secondary");
        YandexDislikeIcon.Fill = (System.Windows.Media.Brush)FindResource(disliked ? "Ink" : "Secondary");
        YandexLikeButton.ToolTip = liked ? "Убрать из «Мне нравится»" : "Нравится — добавить в «Мне нравится»";
        System.Windows.Automation.AutomationProperties.SetItemStatus(YandexLikeButton, liked ? "Нравится" : "Без оценки");
        System.Windows.Automation.AutomationProperties.SetItemStatus(YandexDislikeButton, disliked ? "Не нравится" : "Без оценки");
    }
    private async void RateYandexTrack(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!IsYandexSelected || ratingYandex || yandexPlayback == null) return;
        string? command = (sender as System.Windows.Controls.Button)?.Tag as string;
        if (command is not ("like" or "dislike")) return;
        ratingYandex = true; UpdateYandexRating();
        yandexCommandRevision++;
        try
        {
            var state = await yandexLyrics.PlaybackAsync(command, expectedTrackId: yandexPlayback.TrackId);
            if (closing || !IsYandexSelected) return;
            if (state == null) Status.Text = "Яндекс Музыка не приняла оценку. Проверьте подключение и повторите.";
            else { yandexPlayback = state; await media.RefreshAsync(); }
        }
        finally { ratingYandex = false; if (!closing) { UpdateYandexRating(); await RefreshYandexPlaybackAsync(); } }
    }
    private async Task<bool> SeekSelectedPlayerAsync(double seconds)
    {
        yandexCommandRevision++;
        if (IsYandexSelected)
        {
            var state = await yandexLyrics.PlaybackAsync("seek", Math.Clamp(seconds, 0, media.Duration));
            if (state != null) { yandexPlayback = state; media.UpdateExternalPosition(state.Position, discontinuity: true, isPlaying: state.Playing); return true; }
        }
        return await media.CommandAsync("seek", seconds);
    }
    private async void QueueYandexVolume(double value)
    {
        pendingYandexVolume = Math.Clamp(value, 0, 1);
        yandexCommandRevision++;
        if (writingYandexVolume) return;
        writingYandexVolume = true;
        try
        {
            while (pendingYandexVolume is double next && !closing && IsYandexSelected)
            {
                pendingYandexVolume = null;
                var state = await yandexLyrics.PlaybackAsync("volume", next);
                if (state == null) { Status.Text = "Восстанавливается связь с Яндекс Музыкой…"; break; }
                yandexPlayback = state;
                if (state.Volume > .001) lastYandexVolume = state.Volume;
            }
        }
        finally { writingYandexVolume = false; pendingYandexVolume = null; if (!closing) RefreshVolume(); }
    }
    private async Task<bool> CommandSelectedPlayerAsync(string command)
    {
        if (IsYandexSelected)
        {
            yandexCommandRevision++;
            var state = await yandexLyrics.PlaybackAsync(command);
            if (state != null) { yandexPlayback = state; media.UpdateNativePlayback(state); UpdateMedia(); return true; }
        }
        return await media.CommandAsync(command);
    }
}
