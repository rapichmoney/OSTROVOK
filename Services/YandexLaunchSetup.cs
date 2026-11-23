using System.Runtime.InteropServices;

namespace DesktopIsland.Services;

internal static class YandexLaunchSetup
{
    internal enum Step { FindPlayer, FindShortcuts, CreateShell, OpenShortcut, ReadTarget, ReadArguments, Backup, SetTarget, SetArguments, SetDirectory, SetIcon, Save, Release }
    internal interface IShortcut : IDisposable
    {
        string TargetPath { get; set; }
        string Arguments { get; set; }
        string WorkingDirectory { set; }
        string IconLocation { set; }
        void Save();
    }
    internal sealed class ShortcutShell : IDisposable
    {
        private readonly object shell;
        internal ShortcutShell()
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new NotSupportedException("WScript.Shell is unavailable");
            shell = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Cannot create shortcut shell");
        }
        internal IShortcut Open(string path) => new Shortcut(((dynamic)shell).CreateShortcut(path));
        public void Dispose() { if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
    }
    private sealed class Shortcut(object instance) : IShortcut
    {
        public string TargetPath { get => (string)((dynamic)instance).TargetPath; set => ((dynamic)instance).TargetPath = value; }
        public string Arguments { get => (string)((dynamic)instance).Arguments; set => ((dynamic)instance).Arguments = value; }
        public string WorkingDirectory { set => ((dynamic)instance).WorkingDirectory = value; }
        public string IconLocation { set => ((dynamic)instance).IconLocation = value; }
        public void Save() => ((dynamic)instance).Save();
        public void Dispose() { if (Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }
    internal static bool IsRecoverable(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or COMException
        or InvalidOperationException or NotSupportedException or System.Security.SecurityException or System.ComponentModel.Win32Exception
        or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;
    private static void Report(Step step, Exception error)
    {
        App.Diagnostics?.Event(DiagnosticEvent.YandexShortcutSkipped, DiagnosticArea.Connections, (int)step);
        App.Diagnostics?.Error(DiagnosticArea.Connections, error);
    }
    internal static string? PlayerPath()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcesses().Where(x => x.ProcessName.Equals("Яндекс Музыка", StringComparison.OrdinalIgnoreCase) || x.ProcessName.Equals("YandexMusic", StringComparison.OrdinalIgnoreCase)))
                using (process) { try { var path = process.MainModule?.FileName; if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path; } catch (System.ComponentModel.Win32Exception) { } }
        }
        catch (InvalidOperationException) { }
        return new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "YandexMusic", "Яндекс Музыка.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "YandexMusic", "Яндекс Музыка.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "YandexMusic", "Яндекс Музыка.exe")
        }.FirstOrDefault(File.Exists);
    }
    internal static int Install()
    {
        ShortcutShell? shell = null;
        Step step = Step.FindPlayer;
        try
        {
            string? exe = PlayerPath();
            if (exe == null) return 0;
            step = Step.FindShortcuts;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
            string backup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopIsland", "YandexShortcutBackup");
            var links = new List<string>();
            foreach (string root in new[] { desktop, programs, pinned })
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                    var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = root == programs, AttributesToSkip = FileAttributes.ReparsePoint };
                    links.AddRange(Directory.EnumerateFiles(root, "*.lnk", options).Where(x => Path.GetFileName(x).Contains("Яндекс", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).Contains("Yandex", StringComparison.OrdinalIgnoreCase)));
                }
                catch (Exception error) when (IsRecoverable(error)) { Report(Step.FindShortcuts, error); }
            }
            if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop)) links.Add(Path.Combine(desktop, "Яндекс Музыка — остров.lnk"));
            step = Step.CreateShell; shell = new ShortcutShell();
            return InstallLinks(exe, links, backup, shell.Open, Report);
        }
        catch (Exception error) when (IsRecoverable(error)) { Report(step, error); return 0; }
        finally { try { shell?.Dispose(); } catch (Exception error) when (IsRecoverable(error)) { Report(Step.Release, error); } }
    }
    internal static int InstallLinks(string exe, IEnumerable<string> paths, string backup, Func<string, IShortcut> open, Action<Step, Exception> failed)
    {
        int changed = 0;
        foreach (string candidate in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IShortcut? link = null; Step step = Step.OpenShortcut;
            try
            {
                string path = Path.GetFullPath(candidate);
                link = open(path); bool exists = File.Exists(path);
                step = Step.ReadTarget;
                if (exists && !string.Equals(link.TargetPath, exe, StringComparison.OrdinalIgnoreCase)) continue;
                step = Step.ReadArguments;
                string args = link.Arguments ?? "";
                string clean = System.Text.RegularExpressions.Regex.Replace(args, @"(?<!\S)--remote-debugging-(?:port|address)=\S+", "").Trim();
                string replacement = (clean + " --remote-debugging-address=127.0.0.1 --remote-debugging-port=9226").Trim();
                if (exists && args.Trim() == replacement) continue;
                if (exists)
                {
                    step = Step.Backup; Directory.CreateDirectory(backup);
                    string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..16];
                    string copy = Path.Combine(backup, key + ".lnk"); if (!File.Exists(copy)) File.Copy(path, copy);
                }
                // Existing shortcut targets, icons and working directories are valid already.
                // Reassigning those COM properties can reject an otherwise usable shortcut.
                if (!exists) { step = Step.SetTarget; link.TargetPath = exe; }
                step = Step.SetArguments; link.Arguments = replacement;
                if (!exists)
                {
                    step = Step.SetDirectory; link.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
                    step = Step.SetIcon; link.IconLocation = "\"" + exe + "\",0";
                }
                step = Step.Save; link.Save(); ++changed;
            }
            catch (Exception error) when (IsRecoverable(error)) { failed(step, error); }
            finally { try { link?.Dispose(); } catch (Exception error) when (IsRecoverable(error)) { failed(Step.Release, error); } }
        }
        return changed;
    }
}
