using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DesktopIsland;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
}

public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Pinned { get; set; }
    public bool ClipboardEnabled { get; set; }
    public bool TelegramNotificationsEnabled { get; set; }
    public bool DiscordPresenceEnabled { get; set; }
    public string DiscordApplicationId { get; set; } = "";
    public bool DiscordShowTrack { get; set; } = true;
    public int ClipboardFeatureVersion { get; set; }
    public bool ClipboardNotifications { get; set; } = true;
    public bool HoldNoticesOnHover { get; set; }
    public bool AutoTranslateClipboard { get; set; } = true;
    public bool DownloadsEnabled { get; set; }
    public string DownloadFolder { get; set; } = "";
    public bool HideInFullscreen { get; set; }
    public bool Topmost { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ReducedMotion { get; set; }
    public bool ExpandOnClick { get; set; }
    public string OpeningTab { get; set; } = "Последняя вкладка";
    public bool MiniMode { get; set; }
    public bool ShowKeyboard { get; set; } = true;
    public bool ShowWeather { get; set; } = true;
    public bool ShowDateOrBattery { get; set; } = true;
    public bool Detached { get; set; }
    public double CompactScale { get; set; } = 100;
    public double CompactWidthOverride { get; set; }
    public double ExpandedScale { get; set; } = 100;
    public double PlayerHeight { get; set; }
    public double Radius { get; set; } = 28;
    public bool ArtworkTint { get; set; } = true;
    public bool IdleGlow { get; set; } = true;
    public string InterfaceFont { get; set; } = "Segoe UI";
    public string AnimationStyle { get; set; } = "Капля";
    public double AnimationSpeed { get; set; } = 100;
    public double SpringStrength { get; set; } = 60;
    public string HideStyle { get; set; } = "Капля вверх";
    public string WeatherCity { get; set; } = "";
    public DesktopIsland.Services.WeatherLocation? WeatherLocation { get; set; }
    public int LastTab { get; set; }
    public string CalendarView { get; set; } = "День";
    public bool LyricsEnabled { get; set; } = true;
    public bool LyricsOnCompact { get; set; } = true;
    public double LyricsAdjustment { get; set; }
    public bool YandexLyricsEnabled { get; set; } = true;
}

public sealed class ShelfItem
{
    public string Path { get; set; } = "";
    public bool IsLink => IsWebLink(Path);
    public string Name => IsLink ? new Uri(Path).Host : System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
    public string Icon => IsLink ? "M 8,13 L 13,8 M 7,10 L 5,12 C 1,16 7,22 11,18 L 14,15 M 10,7 L 12,5 C 16,1 22,7 18,11 L 15,14" : Directory.Exists(Path) ? "M 2,6 L 9,6 L 11,9 L 22,9 L 22,21 L 2,21 Z" : "M 5,2 L 15,2 L 21,8 L 21,23 L 5,23 Z M 15,2 L 15,8 L 21,8";
    public static bool IsWebLink(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    public string Detail => File.Exists(Path) ? "Файл" : Directory.Exists(Path) ? "Папка" : "Путь недоступен";
}

public sealed class TaskItem : Observable
{
    public string Text { get; set; } = "";
    private bool done;
    public bool Done { get => done; set => Set(ref done, value); }
}

public sealed class CalendarEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public bool Reminded { get; set; }
    public string When => StartsAt.ToString("dd MMMM, HH:mm");
}

public sealed class ClipboardEntry : Observable
{
    private string translation = "", translationState = "";
    public string Translation { get => translation; set => Set(ref translation, value); }
    public string TranslationState { get => translationState; set => Set(ref translationState, value); }
    public string Text { get; init; } = "";
    public DateTime At { get; init; } = DateTime.Now;
    public string Preview => Text.Replace('\r', ' ').Replace('\n', ' ');
    public string Detail => $"{At:HH:mm} · {Text.Length:N0} символов";
}

public sealed class ActivityEntry
{
    public DateTime At { get; init; } = DateTime.Now;
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Time => At.ToString("HH:mm");
}

public sealed class DownloadEntry : Observable
{
    public string Path { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    private string status = "Записывается";
    public string Status { get => status; set => Set(ref status, value); }
    private long bytes;
    public long Bytes { get => bytes; set { if (Set(ref bytes, value)) Changed(nameof(Size)); } }
    public string Size => FormatBytes(Bytes);
    public DateTime LastChanged { get; set; } = DateTime.Now;
    public bool Complete { get; set; }
    public static bool IsTemporary(string path) => new[] { ".crdownload", ".part", ".tmp", ".download" }.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));
    public static string FormatBytes(double n) => n >= 1073741824 ? $"{n / 1073741824:0.0} ГБ" : n >= 1048576 ? $"{n / 1048576:0.0} МБ" : n >= 1024 ? $"{n / 1024:0.0} КБ" : $"{n:0} Б";
}
