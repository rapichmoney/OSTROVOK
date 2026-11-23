using System.Runtime.InteropServices;
using DesktopIsland.Services;

namespace DesktopIsland;

public partial class App
{
    internal static DiagnosticLog? Diagnostics { get; private set; }
    private readonly bool diagnosticProbe;
    public App()
    {
        var args = Environment.GetCommandLineArgs();
        diagnosticProbe = args.Contains("--diagnostics-probe");
        bool isolated = args.Any(x => x is "--self-test" or "--repair-yandex" || x.StartsWith("--verify-", StringComparison.Ordinal) || x.StartsWith("--showcase", StringComparison.Ordinal));
        if (isolated && !diagnosticProbe) return;
        string root = diagnosticProbe ? args.SkipWhile(x => x != "--diagnostics-root").Skip(1).First() : DiagnosticLog.DefaultRoot;
        Diagnostics = new DiagnosticLog(root);
        DispatcherUnhandledException += (_, e) =>
        {
            Diagnostics.Fatal(DiagnosticArea.Dispatcher, e.Exception);
            if (diagnosticProbe) { e.Handled = true; Shutdown(41); }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.IsTerminating) Diagnostics.Fatal(DiagnosticArea.BackgroundThread, e.ExceptionObject as Exception);
            else if (e.ExceptionObject is Exception error) Diagnostics.Error(DiagnosticArea.BackgroundThread, error);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Diagnostics.Error(DiagnosticArea.UnobservedTask, e.Exception);
    }
    private bool RunDiagnosticProbe()
    {
        if (!diagnosticProbe) return false;
        SetErrorMode(3); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Diagnostics!.BeginSession();
        Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("Synthetic private text must not be logged.")));
        return true;
    }
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
