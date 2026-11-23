using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopIsland.Services;

internal enum DiagnosticEvent { AppStarted, AppStopped, Error, Crash, UnexpectedExit, LyricsRequested, LyricsReady, LyricsMissing, UpdateCheckStarted, UpdateInstallStarted, YandexShortcutSkipped, UsbConnectionCount, UsbNoticeShown }
internal enum DiagnosticArea { Application, Dispatcher, BackgroundThread, UnobservedTask, Storage, UiTick, Lyrics, Updates, Connections, Devices }

internal sealed class DiagnosticLog
{
    internal const int MaxJournalBytes = 1024 * 1024, MaxJournals = 14, MaxCrashes = 30;
    private readonly object gate = new();
    private readonly Queue<JournalEntry> recent = new();
    private readonly Guid session = Guid.NewGuid();
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private bool begun, ended, fatalRecorded;
    private string? journal;
    internal string Root { get; }
    internal string CrashDirectory => Path.Combine(Root, "Crashes");
    private string MarkerPath => Path.Combine(Root, "active-session.json");
    private static readonly UTF8Encoding utf8 = new(false);
    private static readonly JsonSerializerOptions json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter() } };
    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopIsland", "Logs");
    private sealed record JournalEntry(DateTimeOffset Time, Guid Session, DiagnosticEvent Event, DiagnosticArea Area, int? Code, ErrorInfo? Error);
    private sealed record FrameInfo(string Method, int Offset, int Line);
    private sealed record ErrorInfo(string Type, int HResult, int? NativeCode, int? HttpStatus, FrameInfo[] Frames, ErrorInfo? Inner);
    private sealed record SessionMarker(Guid Session, int Process, DateTimeOffset ProcessStarted, DateTimeOffset Started, string Version);
    internal DiagnosticLog(string root) => Root = Path.GetFullPath(root);
    private static string Version => typeof(App).Assembly.GetName().Version!.ToString(3);
    internal void BeginSession()
    {
        lock (gate)
        {
            if (begun) return;
            begun = true;
            try
            {
                Directory.CreateDirectory(Root); Directory.CreateDirectory(CrashDirectory);
                if (File.Exists(MarkerPath) && new FileInfo(MarkerPath).Length < 32768)
                {
                    try
                    {
                        var previous = JsonSerializer.Deserialize<SessionMarker>(File.ReadAllText(MarkerPath), json);
                        if (previous != null && !PreviousStillRunning(previous))
                            WriteCrash(DiagnosticArea.Application, null, "unexpected-exit", previous);
                    }
                    catch (JsonException error) { Error(DiagnosticArea.Storage, error); }
                }
                using var process = Process.GetCurrentProcess();
                WriteDurable(MarkerPath, JsonSerializer.Serialize(new SessionMarker(session, process.Id, process.StartTime.ToUniversalTime(), started, Version), json), overwrite: true);
                Prune();
                Event(DiagnosticEvent.AppStarted);
            }
            catch (Exception) { /* Diagnostics must not prevent startup. */ }
        }
    }
    private static bool PreviousStillRunning(SessionMarker previous)
    {
        try { using var process = Process.GetProcessById(previous.Process); return !process.HasExited && Math.Abs((process.StartTime.ToUniversalTime() - previous.ProcessStarted.UtcDateTime).TotalSeconds) < 2; }
        catch (Exception) { return false; }
    }
    internal void EndSession(int code)
    {
        lock (gate)
        {
            if (!begun || ended) return;
            ended = true; Event(DiagnosticEvent.AppStopped, code: code);
            DeleteOwnMarker();
        }
    }
    internal void Event(DiagnosticEvent name, DiagnosticArea area = DiagnosticArea.Application, int? code = null)
        => Append(new(DateTimeOffset.UtcNow, session, name, area, code, null));
    internal void Error(DiagnosticArea area, Exception error)
    {
        try { Append(new(DateTimeOffset.UtcNow, session, DiagnosticEvent.Error, area, error.HResult, Describe(error))); }
        catch (Exception) { }
    }
    internal string? Fatal(DiagnosticArea area, Exception? error)
    {
        lock (gate)
        {
            if (fatalRecorded) return null;
            fatalRecorded = true;
            try
            {
                Append(new(DateTimeOffset.UtcNow, session, DiagnosticEvent.Crash, area, error?.HResult, error == null ? null : Describe(error)));
                string path = WriteCrash(area, error, "unhandled-exception", null);
                DeleteOwnMarker(); // The persisted report already explains this exit.
                return path;
            }
            catch (Exception) { return null; }
        }
    }
    private void DeleteOwnMarker()
    {
        try
        {
            if (File.Exists(MarkerPath) && JsonSerializer.Deserialize<SessionMarker>(File.ReadAllText(MarkerPath), json)?.Session == session)
                File.Delete(MarkerPath);
        }
        catch (Exception) { }
    }
    private void Append(JournalEntry entry)
    {
        lock (gate)
        {
            recent.Enqueue(entry); while (recent.Count > 24) recent.Dequeue();
            try
            {
                Directory.CreateDirectory(Root);
                string day = DateTime.UtcNow.ToString("yyyyMMdd");
                if (journal == null || !File.Exists(journal) || !Path.GetFileName(journal).StartsWith("app-" + day, StringComparison.Ordinal) || new FileInfo(journal).Length >= MaxJournalBytes)
                {
                    int part = 0;
                    do { journal = Path.Combine(Root, $"app-{day}-{part++:00}.jsonl"); }
                    while (File.Exists(journal) && new FileInfo(journal).Length >= MaxJournalBytes);
                }
                File.AppendAllText(journal, JsonSerializer.Serialize(entry, json) + "\n", utf8);
                Prune();
            }
            catch (Exception) { }
        }
    }
    private string WriteCrash(DiagnosticArea area, Exception? error, string kind, SessionMarker? previous)
    {
        Directory.CreateDirectory(CrashDirectory);
        string path = Path.Combine(CrashDirectory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
        var report = new
        {
            Schema = 1, Time = DateTimeOffset.UtcNow, Session = previous?.Session ?? session, Kind = kind,
            Version = previous?.Version ?? Version, Os = Environment.OSVersion.Version.ToString(), Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Started = previous?.Started ?? started, Area = area,
            Error = error == null ? null : Describe(error), Recent = previous == null ? recent.Select(x => new { x.Time, x.Event, x.Area, x.Code, ErrorType = x.Error?.Type }).ToArray() : [],
            Previous = previous != null ? "Previous session did not close normally; no managed exception was captured." : null
        };
        WriteDurable(path, JsonSerializer.Serialize(report, json), overwrite: false); Prune(); return path;
    }
    private static ErrorInfo Describe(Exception error, int depth = 0)
    {
        // Exception.Message, Data, full file paths and memory dumps may contain user content.
        var frames = (new StackTrace(error, true).GetFrames() ?? []).Take(64).Select(frame =>
        {
            var method = frame.GetMethod();
            return new FrameInfo((method?.DeclaringType?.FullName ?? "") + "." + method?.Name, frame.GetILOffset(), frame.GetFileLineNumber());
        }).ToArray();
        int? native = error is System.ComponentModel.Win32Exception win32 ? win32.NativeErrorCode : null;
        int? http = error is System.Net.Http.HttpRequestException request && request.StatusCode is { } status ? (int)status : null;
        return new(error.GetType().FullName ?? error.GetType().Name, error.HResult, native, http, frames, depth < 4 && error.InnerException != null ? Describe(error.InnerException, depth + 1) : null);
    }
    private static void WriteDurable(string path, string content, bool overwrite)
    {
        using var stream = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        byte[] bytes = utf8.GetBytes(content); stream.Write(bytes); stream.Flush(true);
    }
    private void Prune()
    {
        KeepNewest(Root, "app-*.jsonl", MaxJournals);
        KeepNewest(CrashDirectory, "crash-*.json", MaxCrashes);
    }
    private static void KeepNewest(string directory, string pattern, int count)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles(pattern).OrderByDescending(x => x.LastWriteTimeUtc).Skip(count))
                try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception) { }
    }
}
