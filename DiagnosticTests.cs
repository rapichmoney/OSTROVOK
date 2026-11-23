using System.Diagnostics;
using System.Text.Json;
using DesktopIsland.Services;

namespace DesktopIsland;

internal static class DiagnosticTests
{
    internal static async Task RunAsync(string root, Action<string, bool> check)
    {
        string folder = Path.Combine(root, "diagnostics");
        var log = new DiagnosticLog(folder); log.BeginSession();
        check("diagnostic session marker is created", File.Exists(Path.Combine(folder, "active-session.json")));
        Exception sample;
        try { ThrowFixture(); throw new UnreachableException(); } catch (InvalidOperationException error) { sample = error; }
        sample.Data["private"] = "PRIVATE-DIAGNOSTIC-SENTINEL";
        log.Error(DiagnosticArea.Storage, sample);
        string? report = log.Fatal(DiagnosticArea.Dispatcher, sample);
        check("crash report is written synchronously", report != null && File.Exists(report));
        check("persisted crash does not leave a duplicate-report marker", !File.Exists(Path.Combine(folder,"active-session.json")));
        using (var data = JsonDocument.Parse(File.ReadAllText(report!)))
        {
            var value = data.RootElement;
            check("crash includes version exception code and stack", value.GetProperty("Version").GetString() == typeof(App).Assembly.GetName().Version!.ToString(3) && value.GetProperty("Error").GetProperty("Type").GetString() == typeof(InvalidOperationException).FullName && value.GetProperty("Error").GetProperty("Frames").GetArrayLength() > 0);
        }
        log.Fatal(DiagnosticArea.BackgroundThread, sample);
        check("dispatcher and domain handlers do not duplicate one crash", Directory.GetFiles(log.CrashDirectory, "crash-*.json").Length == 1);
        string combined = string.Join("", Directory.GetFiles(folder, "app-*.jsonl").Select(File.ReadAllText)) + File.ReadAllText(report!);
        check("diagnostics exclude exception message data and profile path", !combined.Contains("PRIVATE-DIAGNOSTIC-SENTINEL", StringComparison.Ordinal) && !combined.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase));
        log.EndSession(0);
        check("normal exit clears only its session marker", !File.Exists(Path.Combine(folder, "active-session.json")));
        var restart = new DiagnosticLog(folder); restart.BeginSession(); restart.EndSession(0);
        check("normal restart does not invent a crash", Directory.GetFiles(log.CrashDirectory, "crash-*.json").Length == 1);

        string interrupted = Path.Combine(root, "diagnostics-interrupted"); Directory.CreateDirectory(interrupted);
        File.WriteAllText(Path.Combine(interrupted,"active-session.json"), JsonSerializer.Serialize(new { Session = Guid.NewGuid(), Process = 0, ProcessStarted = DateTimeOffset.UtcNow.AddDays(-1), Started = DateTimeOffset.UtcNow.AddMinutes(-3), Version = "0.0.1" }));
        var recovered = new DiagnosticLog(interrupted); recovered.BeginSession();
        var previousReport = Directory.GetFiles(recovered.CrashDirectory,"crash-*.json").Single();
        using (var data=JsonDocument.Parse(File.ReadAllText(previousReport)))
            check("abnormal exit is labelled without claiming a captured exception",data.RootElement.GetProperty("Kind").GetString()=="unexpected-exit" && data.RootElement.GetProperty("Version").GetString()=="0.0.1" && !data.RootElement.TryGetProperty("Error",out _));
        recovered.EndSession(0);
        File.WriteAllText(Path.Combine(interrupted,"active-session.json"),"unfinished JSON");
        var corrupt = new DiagnosticLog(interrupted); corrupt.BeginSession();
        using (JsonDocument.Parse(File.ReadAllText(Path.Combine(interrupted,"active-session.json"))))
            check("corrupt session marker does not disable logging",true);
        corrupt.EndSession(0);

        string retention = Path.Combine(root,"diagnostics-retention"); Directory.CreateDirectory(retention); Directory.CreateDirectory(Path.Combine(retention,"Crashes"));
        for(int i=0;i<20;i++) { string path=Path.Combine(retention,$"app-20000101-{i:00}.jsonl"); File.WriteAllText(path,"old fixture\n"); File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-100).AddMinutes(i)); }
        for(int i=0;i<35;i++) { string path=Path.Combine(retention,"Crashes",$"crash-old-{i:00}.json"); File.WriteAllText(path,"{}"); File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-100).AddMinutes(i)); }
        File.WriteAllText(Path.Combine(retention,"keep-user-file.txt"),"keep");
        var bounded = new DiagnosticLog(retention); bounded.BeginSession();
        check("diagnostic retention bounds only application logs",Directory.GetFiles(retention,"app-*.jsonl").Length<=DiagnosticLog.MaxJournals && Directory.GetFiles(bounded.CrashDirectory,"crash-*.json").Length<=DiagnosticLog.MaxCrashes && File.Exists(Path.Combine(retention,"keep-user-file.txt")));
        string active = Directory.GetFiles(retention,"app-*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).First();
        using(var stream=File.Open(active,FileMode.Append,FileAccess.Write)) stream.Write(new byte[DiagnosticLog.MaxJournalBytes]);
        bounded.Event(DiagnosticEvent.LyricsReady,DiagnosticArea.Lyrics,3);
        check("large journal rotates to another bounded file",Directory.GetFiles(retention,"app-*.jsonl").Any(path=>path!=active && File.ReadAllText(path).Contains("LyricsReady")));
        bounded.EndSession(0);
        string blocked = Path.Combine(root,"diagnostics-blocked"); File.WriteAllText(blocked,"not a directory");
        var unavailable=new DiagnosticLog(blocked); unavailable.BeginSession(); unavailable.Event(DiagnosticEvent.AppStarted); unavailable.Error(DiagnosticArea.Application,sample);
        check("unwritable logging cannot throw into the application",unavailable.Fatal(DiagnosticArea.Dispatcher,sample)==null); unavailable.EndSession(0);

        string probe = Path.Combine(root,"diagnostics-child");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden };
        foreach(string argument in new[]{"--diagnostics-probe","--diagnostics-root",probe}) start.ArgumentList.Add(argument);
        using(var process=Process.Start(start)!)
        {
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch(OperationCanceledException) { process.Kill(); throw new TimeoutException("Diagnostic probe timed out"); }
            check("isolated dispatcher failure exits the probe process",process.ExitCode==41);
        }
        var files=Directory.GetFiles(Path.Combine(probe,"Crashes"),"crash-*.json");
        check("real dispatcher hook captures exactly one isolated crash",files.Length==1);
        using(var data=JsonDocument.Parse(File.ReadAllText(files.Single())))
            check("probe report comes from dispatcher without private text",data.RootElement.GetProperty("Area").GetString()=="Dispatcher" && !data.RootElement.GetRawText().Contains("Synthetic private text"));
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowFixture() => throw new InvalidOperationException("PRIVATE-DIAGNOSTIC-SENTINEL C:\\Users\\private\\secret.txt");
}
