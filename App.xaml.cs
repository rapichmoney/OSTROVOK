using System.Threading;
namespace DesktopIsland;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private EventWaitHandle? showSignal;
    private RegisteredWaitHandle? wait;
    internal static bool VerificationMode { get; private set; }
    internal static bool SelfTestMode { get; private set; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WindowBrand.InitializeProcess();
        if (e.Args.Contains("--repair-yandex"))
        {
            int count = Services.YandexLaunchSetup.Install();
            string? report = e.Args.SkipWhile(x => x != "--report").Skip(1).FirstOrDefault();
            if (report != null) File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { changedShortcuts = count }));
            Shutdown(); return;
        }
        if (e.Args.Contains("--self-test"))
        {
            SelfTestMode = true;
            Shutdown(await SelfTests.RunAsync(e.Args)); return;
        }
        VerificationMode = e.Args.Contains("--showcase-readalong") || e.Args.Contains("--showcase-weather") || e.Args.Contains("--showcase-update") || e.Args.Contains("--verify-motion") || e.Args.Contains("--verify-ui") || e.Args.Contains("--showcase");
        if (!VerificationMode)
        {
            instance = new Mutex(true, "Local\\DesktopIsland-" + Environment.UserName, out bool first);
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopIsland-Show-" + Environment.UserName);
            if (!first) { showSignal.Set(); Shutdown(); return; }
        }
        var window = new MainWindow(); MainWindow = window; window.Show();
        if (showSignal != null) wait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(window.Reveal), null, Timeout.Infinite, false);
    }
    protected override void OnExit(ExitEventArgs e) { wait?.Unregister(null); showSignal?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}
