using Windows.Media.Control;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading;

namespace DesktopIsland.Services;

public sealed partial class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private string lastTrack = "";
    private bool updating;
    private readonly PlaybackClock clock = new();
    private readonly SynchronizedPlaybackClock externalClock = new();
    private bool externalPositionAvailable;
    private bool nativeAvailable;
    private bool windowVideoAvailable;
    private readonly YouTubeWindowProbe youTube = new();
    private long nativeUpdatedAt;
    private string nativeTrackId = "";
    private YandexLyricsBridge.PlaybackState? lastNativeState;
    private long nativeMetadataAt;
    public bool ArtworkReady { get; private set; } = true;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool disposed, refreshPending;
    private int refreshQueued;
    public string Title { get; private set; } = "Ничего не играет";
    public string Artist { get; private set; } = "Откройте музыку или видео в плеере";
    public string Source { get; private set; } = "";
    public string Album { get; private set; } = "";
    public bool IsVideo { get; private set; }
    public static bool VideoSource(string source, string artist, Windows.Media.MediaPlaybackType? type) =>
        type == Windows.Media.MediaPlaybackType.Video || artist.Contains("YouTube", StringComparison.OrdinalIgnoreCase) || source.Contains("youtube", StringComparison.OrdinalIgnoreCase) ||
        (type != Windows.Media.MediaPlaybackType.Music && new[] { "chrome", "msedge", "firefox", "browser" }.Any(x => source.Contains(x, StringComparison.OrdinalIgnoreCase)));
    public static int SessionPriority(bool playing, bool video) => playing ? video ? 2 : 3 : 0;
    public string ArtworkUrl => Source == "ru.yandex.desktop.music" && lastNativeState is { } native && native.TrackId == nativeTrackId && native.Title == Title ? native.ArtworkUrl : "";
    public string TrackPageUrl => Source == "ru.yandex.desktop.music" && nativeTrackId.Length > 0 && nativeTrackId.All(char.IsAsciiDigit) ? "https://music.yandex.ru/track/" + nativeTrackId : "";
    public bool Playing { get; private set; }
    public bool Available => session != null || nativeAvailable || windowVideoAvailable;
    public bool UpdateNativePlayback(YandexLyricsBridge.PlaybackState state)
    {
        if (session != null && Source != "ru.yandex.desktop.music" && (!IsVideo || !state.Playing && Playing)) return false;
        if (string.IsNullOrWhiteSpace(state.Title) || state.Duration <= 0) return false;
        bool sourceChanged = Source != "ru.yandex.desktop.music";
        bool changed = sourceChanged || !Available || nativeTrackId != state.TrackId || Title != state.Title || Artist != state.Artist || Playing != state.Playing;
        bool trackChanged = sourceChanged || nativeTrackId != state.TrackId;
        if (sourceChanged) requestedArtwork = "";
        if (trackChanged) { ArtworkReady = string.IsNullOrEmpty(state.ArtworkUrl); if (ArtworkReady) Artwork = null; Album = ""; }
        lastNativeState = state; nativeMetadataAt = Environment.TickCount64;
        if (IsVideo) DisconnectSession();
        windowVideoAvailable = false;
        IsVideo = false;
        nativeAvailable = session == null; nativeUpdatedAt = Environment.TickCount64; nativeTrackId = state.TrackId;
        Source = "ru.yandex.desktop.music"; Title = state.Title; Artist = state.Artist; Duration = state.Duration; Playing = state.Playing;
        CanPlay = CanSeek = true; CanNext = CanPrevious = true;
        UpdateExternalPosition(state.Position, trackChanged, state.Playing);
        LoadNativeArtwork(state);
        return changed;
    }
    public bool CanPlay { get; private set; }
    public bool CanNext { get; private set; }
    public bool CanPrevious { get; private set; }
    public bool CanSeek { get; private set; }
    public double Duration { get; private set; }
    public double Position => externalPositionAvailable && Source == "ru.yandex.desktop.music" ? externalClock.At(DateTimeOffset.UtcNow) : clock.At(DateTimeOffset.UtcNow);
    public void UpdateExternalPosition(double? position, bool discontinuity = false, bool? isPlaying = null)
    {
        bool first = !externalPositionAvailable;
        externalPositionAvailable = position.HasValue;
        if (position.HasValue)
        {
            nativeUpdatedAt = Environment.TickCount64;
            externalClock.Sample(position.Value, Duration, isPlaying ?? Playing, DateTimeOffset.UtcNow, discontinuity || first);
        }
    }
    public BitmapImage? Artwork { get; private set; }
    public string? Error { get; private set; }
    public event Action? Updated;
    public IReadOnlyList<GlobalSystemMediaTransportControlsSession> Sessions { get; private set; } = [];
    public string? SelectedSource { get; set; }
    public async Task InitializeAsync()
    {
        if (manager != null) { await RefreshAsync(); return; }
        try { manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); manager.CurrentSessionChanged += ManagerChanged; manager.SessionsChanged += ManagerChanged; }
        catch (Exception ex) { Error = "Windows не предоставила доступ к плеерам: " + ex.Message; }
        await RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        if (disposed || manager == null) return;
        if (updating) { refreshPending = true; return; }
        updating = true;
        try
        {
            Sessions = manager.GetSessions().ToArray();
            var candidates = new List<(GlobalSystemMediaTransportControlsSession Session, int Priority)>();
            foreach (var candidate in Sessions)
            {
                try
                {
                    var metadata = await candidate.TryGetMediaPropertiesAsync();
                    bool video = VideoSource(candidate.SourceAppUserModelId, metadata.Artist, metadata.PlaybackType);
                    candidates.Add((candidate, SessionPriority(candidate.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, video)));
                }
                catch (Exception) { /* One closing browser tab must not hide the other players. */ }
            }
            var next = candidates.OrderByDescending(x => x.Priority)
                .ThenByDescending(x => x.Session.SourceAppUserModelId == SelectedSource)
                .ThenByDescending(x => ReferenceEquals(x.Session, manager.GetCurrentSession()))
                .Select(x => x.Session).FirstOrDefault();
            bool nativeMusicPlaying = Source == "ru.yandex.desktop.music" && lastNativeState is { Playing: true } && Environment.TickCount64 - nativeUpdatedAt < 8000;
            if (!nativeMusicPlaying && !candidates.Any(x => x.Priority > 0))
            {
                var video = await youTube.ReadAsync();
                if (video != null)
                {
                    DisconnectSession(); nativeAvailable = false; windowVideoAvailable = true;
                    Source = "youtube.window"; IsVideo = true; Title = video.Title; Artist = "YouTube"; Album = "";
                    Playing = video.Playing; Artwork = null; ArtworkReady = true; Duration = 0;
                    CanPlay = CanNext = CanPrevious = CanSeek = false;
                    clock.Sample(0, 0, false, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                    Updated?.Invoke(); return;
                }
            }
            windowVideoAvailable = false;
            if (Source == "ru.yandex.desktop.music" && lastNativeState is { Playing: true } && Environment.TickCount64 - nativeUpdatedAt < 8000 &&
                !candidates.Any(x => x.Priority == 3))
            {
                DisconnectSession(); nativeAvailable = true; IsVideo = false; return;
            }
            if (next == null)
            {
                if ((nativeAvailable || externalPositionAvailable && Source == "ru.yandex.desktop.music") && Environment.TickCount64 - nativeUpdatedAt < 8000)
                {
                    DisconnectSession(); session = null; nativeAvailable = true; return;
                }
                nativeAvailable = false;
                DisconnectSession();
                session = null; Title = "Ничего не играет"; Artist = "Откройте музыку или видео в плеере";
                Artwork = null; Source = ""; Playing = false; IsVideo = false; CanPlay = CanNext = CanPrevious = CanSeek = false;
                Duration = 0; Album = ""; clock.Sample(0, 0, false, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); lastTrack = ""; Updated?.Invoke(); return;
            }
            if (!ReferenceEquals(session, next))
            {
                DisconnectSession(); session = next;
                session.MediaPropertiesChanged += SessionChanged; session.PlaybackInfoChanged += SessionChanged; session.TimelinePropertiesChanged += SessionChanged;
            }
            var properties = await next.TryGetMediaPropertiesAsync();
            nativeAvailable = false;
            var playback = next.GetPlaybackInfo();
            var timeline = next.GetTimelineProperties();
            var native = next.SourceAppUserModelId == "ru.yandex.desktop.music" && Environment.TickCount64 - nativeMetadataAt < 8000 ? lastNativeState : null;
            Title = native?.Title ?? (string.IsNullOrWhiteSpace(properties.Title) ? "Без названия" : properties.Title);
            Artist = native?.Artist ?? (string.IsNullOrWhiteSpace(properties.Artist) ? properties.AlbumArtist : properties.Artist);
            Album = properties.AlbumTitle;
            Source = next.SourceAppUserModelId;
            IsVideo = VideoSource(Source, Artist, properties.PlaybackType);
            Playing = native?.Playing ?? playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            CanPlay = playback.Controls.IsPlayPauseToggleEnabled;
            CanNext = playback.Controls.IsNextEnabled;
            CanPrevious = playback.Controls.IsPreviousEnabled;
            CanSeek = playback.Controls.IsPlaybackPositionEnabled;
            Duration = native?.Duration ?? Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
            clock.Sample(Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds), Duration, Playing, playback.PlaybackRate ?? 1, timeline.LastUpdatedTime, DateTimeOffset.UtcNow);
            var track = Source + "\n" + Title + "\n" + Artist;
            if (track != lastTrack || (Artwork == null && properties.Thumbnail != null))
            {
                if (track != lastTrack && native == null) externalPositionAvailable = false;
                lastTrack = track;
                if (properties.Thumbnail != null && (native == null || string.Equals(properties.Title, native.Title, StringComparison.OrdinalIgnoreCase)))
                {
                    ArtworkReady = false;
                    try
                    {
                        using var input = await properties.Thumbnail.OpenReadAsync();
                        using var stream = input.AsStreamForRead();
                        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 240; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Artwork = bitmap; ArtworkReady = true;
                    }
                    catch (Exception) { ArtworkReady = true; }
                }
                else if (Source != "ru.yandex.desktop.music") { Artwork = null; ArtworkReady = true; }
            }
            Error = null; Updated?.Invoke();
        }
        catch (Exception ex) { Error = "Плеер недоступен: " + ex.Message; Updated?.Invoke(); }
        finally { updating = false; if (refreshPending && !disposed) { refreshPending = false; RequestRefresh(); } }
    }
    private void ManagerChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args) => RequestRefresh();
    private void SessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => RequestRefresh();
    private void RequestRefresh()
    {
        if (disposed || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () => { Interlocked.Exchange(ref refreshQueued, 0); if (!disposed) await RefreshAsync(); }));
    }
    private void DisconnectSession()
    {
        if (session == null) return;
        session.MediaPropertiesChanged -= SessionChanged; session.PlaybackInfoChanged -= SessionChanged; session.TimelinePropertiesChanged -= SessionChanged; session = null;
    }
    public void Dispose()
    {
        disposed = true; DisconnectSession();
        if (manager != null) { manager.CurrentSessionChanged -= ManagerChanged; manager.SessionsChanged -= ManagerChanged; }
    }
    public async Task<bool> CommandAsync(string command, double seconds = 0)
    {
        var current = session;
        if (current == null) return false;
        try
        {
            return command switch
            {
                "toggle" => await current.TryTogglePlayPauseAsync(),
                "next" => await current.TrySkipNextAsync(),
                "previous" => await current.TrySkipPreviousAsync(),
                "seek" => await current.TryChangePlaybackPositionAsync(current.GetTimelineProperties().StartTime.Ticks + TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Duration)).Ticks),
                _ => false
            };
        }
        catch (Exception ex) { Error = ex.Message; return false; }
    }
}
