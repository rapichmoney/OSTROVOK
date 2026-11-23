using System.Runtime.InteropServices;
using DesktopIsland.Services;
using Step = DesktopIsland.Services.YandexLaunchSetup.Step;

namespace DesktopIsland;

internal static class YandexLaunchSetupTests
{
    internal static void Run(string root, Action<string, bool, string> report)
    {
        void check(string name, bool passed, string detail = "") => report(name, passed, detail);
        string folder = Path.Combine(root, "shortcut-setup"); Directory.CreateDirectory(folder);
        string exe = Environment.ProcessPath!, backup = Path.Combine(folder, "backup");
        string existing = Path.Combine(folder, "existing.lnk"); File.WriteAllText(existing, "original shortcut fixture");
        var kept = new Fixture(exe, "--user-option"); kept.FailAt = Step.SetTarget;
        var errors = new List<(Step Step, Exception Error)>();
        int changed = YandexLaunchSetup.InstallLinks(exe, [existing], backup, _ => kept, (step, error) => errors.Add((step, error)));
        check("existing shortcut is repaired without reassigning rejected COM target", changed == 1 && errors.Count == 0 && kept.TargetWrites == 0 && kept.DirectoryWrites == 0 && kept.IconWrites == 0 && kept.Saves == 1);
        check("shortcut arguments preserve custom switches and use loopback", kept.Arguments.Contains("--user-option") && kept.Arguments.Contains("--remote-debugging-address=127.0.0.1") && kept.Arguments.Contains("--remote-debugging-port=9226"));
        check("existing shortcut is backed up before any change", Directory.GetFiles(backup, "*.lnk").Length == 1 && File.ReadAllText(Directory.GetFiles(backup, "*.lnk").Single()) == "original shortcut fixture");
        kept.FailAt = null;
        check("correct shortcut setup does not save a second time", YandexLaunchSetup.InstallLinks(exe, [existing], backup, _ => kept, (step,error) => errors.Add((step,error))) == 0 && kept.Saves == 1);

        foreach (Step failure in new[] { Step.ReadTarget, Step.ReadArguments, Step.SetTarget, Step.SetArguments, Step.SetDirectory, Step.SetIcon, Step.Save })
        {
            string bad = Path.Combine(folder, "bad-" + failure + ".lnk"), good = Path.Combine(folder, "good-" + failure + ".lnk");
            if (failure is Step.ReadTarget or Step.ReadArguments or Step.SetArguments) File.WriteAllText(bad, "unchanged fixture");
            var rejected = new Fixture(exe, "") { FailAt = failure }; var next = new Fixture(exe, ""); errors.Clear();
            changed = YandexLaunchSetup.InstallLinks(exe, [bad,good], backup, path => path == bad ? rejected : next, (step,error) => errors.Add((step,error)));
            check("COM ArgumentException at " + failure + " skips only one shortcut", changed == 1 && errors.Count == 1 && errors[0].Step == failure && errors[0].Error is ArgumentException && errors[0].Error.HResult == unchecked((int)0x80070057) && next.Saves == 1 && rejected.Disposed);
        }
        var healthy = new Fixture(exe, ""); errors.Clear();
        changed = YandexLaunchSetup.InstallLinks(exe, [Path.Combine(folder,"open-failure.lnk"),Path.Combine(folder,"open-next.lnk")], backup,
            path => path.EndsWith("open-failure.lnk",StringComparison.Ordinal) ? throw new ArgumentException("COM fixture") : healthy, (step,error) => errors.Add((step,error)));
        check("CreateShortcut rejection is inside the per-link failure boundary", changed == 1 && errors.Single().Step == Step.OpenShortcut && healthy.Saves == 1);
        healthy = new Fixture(exe, ""); errors.Clear();
        changed = YandexLaunchSetup.InstallLinks(exe, ["bad\0shortcut.lnk",Path.Combine(folder,"valid-after-path.lnk")], backup, _ => healthy, (step,error) => errors.Add((step,error)));
        check("malformed shortcut path cannot abort remaining setup", changed == 1 && errors.Single().Error is ArgumentException && healthy.Saves == 1);

        string unrelatedPath = Path.Combine(folder,"YandexBrowser.lnk"); File.WriteAllText(unrelatedPath,"browser fixture");
        var unrelated = new Fixture(Path.Combine(folder,"browser.exe"),"--browser-option"); errors.Clear();
        changed = YandexLaunchSetup.InstallLinks(exe,[unrelatedPath],backup,_=>unrelated,(step,error)=>errors.Add((step,error)));
        check("Yandex browser and other target shortcuts are not rewritten", changed == 0 && errors.Count == 0 && unrelated.Saves == 0 && unrelated.TargetWrites == 0);
        string blocked = Path.Combine(folder,"blocked-backup"); File.WriteAllText(blocked,"file not directory");
        var protectedLink = new Fixture(exe, ""); healthy = new Fixture(exe, ""); errors.Clear();
        changed = YandexLaunchSetup.InstallLinks(exe,[existing,Path.Combine(folder,"after-backup.lnk")],blocked,path=>path==existing?protectedLink:healthy,(step,error)=>errors.Add((step,error)));
        check("backup failure leaves existing shortcut untouched and allows new links", changed == 1 && errors.Single().Step == Step.Backup && protectedLink.Saves == 0 && healthy.Saves == 1);
        var releaseFault = new Fixture(exe, "") { FailAt = Step.Release }; healthy = new Fixture(exe, ""); errors.Clear();
        changed = YandexLaunchSetup.InstallLinks(exe,[Path.Combine(folder,"release.lnk"),Path.Combine(folder,"release-next.lnk")],backup,path=>path.EndsWith("release-next.lnk",StringComparison.Ordinal)?healthy:releaseFault,(step,error)=>errors.Add((step,error)));
        check("COM cleanup failure cannot hide successful saves or stop setup", changed == 2 && errors.Single().Step == Step.Release && healthy.Saves == 1);
        var stalePort = new Fixture(exe,"--custom --remote-debugging-port=92260 --remote-debugging-address=0.0.0.0"); errors.Clear();
        changed = YandexLaunchSetup.InstallLinks(exe,[existing,existing],backup,_=>stalePort,(step,error)=>errors.Add((step,error)));
        check("wrong port and non-loopback address are replaced once", changed == 1 && stalePort.Saves == 1 && !stalePort.Arguments.Contains("92260") && !stalePort.Arguments.Contains("0.0.0.0") && stalePort.Arguments.StartsWith("--custom",StringComparison.Ordinal));

        // Exercise the real Windows COM layer only inside the temporary test folder.
        using var shell = new YandexLaunchSetup.ShortcutShell();
        string native = Path.Combine(folder,"Яндекс Музыка — тест.lnk"); errors.Clear();
        changed = YandexLaunchSetup.InstallLinks(exe,[native],backup,shell.Open,(step,error)=>errors.Add((step,error)));
        string Details() => "changed=" + changed + ", exists=" + File.Exists(native) + ", errors=" + string.Join("; ", errors.Select(x => x.Step + ":" + x.Error.GetType().Name + ":0x" + x.Error.HResult.ToString("X8")));
        check("real WScript shortcut supports Unicode paths and debug arguments", changed == 1 && errors.Count == 0 && File.Exists(native), Details());
        using(var link = shell.Open(native))
        {
            check("real shortcut reopens with the expected target and loopback port", link.TargetPath.Equals(exe,StringComparison.OrdinalIgnoreCase) && link.Arguments.Contains("--remote-debugging-port=9226") && link.Arguments.Contains("--remote-debugging-address=127.0.0.1"), Details() + ", targetLength=" + link.TargetPath.Length + ", exeLength=" + exe.Length);
        }
        changed = YandexLaunchSetup.InstallLinks(exe,[native],backup,shell.Open,(step,error)=>errors.Add((step,error)));
        check("real shortcut setup is idempotent", changed == 0 && errors.Count == 0, Details());
    }
    private sealed class Fixture(string initialTarget, string initialArguments) : YandexLaunchSetup.IShortcut
    {
        private string target = initialTarget, arguments = initialArguments;
        internal Step? FailAt;
        internal int TargetWrites, DirectoryWrites, IconWrites, Saves;
        internal bool Disposed;
        private void Fail(Step step) { if (FailAt == step) throw new ArgumentException("Synthetic COM failure", "fixture"); }
        public string TargetPath { get { Fail(Step.ReadTarget); return target; } set { Fail(Step.SetTarget); ++TargetWrites; target = value; } }
        public string Arguments { get { Fail(Step.ReadArguments); return arguments; } set { Fail(Step.SetArguments); arguments = value; } }
        public string WorkingDirectory { set { Fail(Step.SetDirectory); ++DirectoryWrites; } }
        public string IconLocation { set { Fail(Step.SetIcon); ++IconWrites; } }
        public void Save() { Fail(Step.Save); ++Saves; }
        public void Dispose() { Disposed = true; Fail(Step.Release); }
    }
}
