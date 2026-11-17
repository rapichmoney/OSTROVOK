using System.Runtime.InteropServices;

namespace DesktopIsland.Services;

internal static class YandexLaunchSetup
{
    internal static int Install()
    {
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "YandexMusic", "Яндекс Музыка.exe");
        if (!File.Exists(exe)) return 0;
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        string backup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopIsland", "YandexShortcutBackup");
        var links = new List<string>();
        foreach (string root in new[] { desktop, programs, pinned })
            if (Directory.Exists(root)) links.AddRange(Directory.EnumerateFiles(root, "*.lnk", root == programs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Where(x => Path.GetFileName(x).Contains("Яндекс", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).Contains("Yandex", StringComparison.OrdinalIgnoreCase)));
        links.Add(Path.Combine(desktop, "Яндекс Музыка — остров.lnk"));
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) return 0;
        dynamic shell = Activator.CreateInstance(type)!;
        int changed = 0;
        try
        {
            foreach (string path in links.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                dynamic link = shell.CreateShortcut(path);
                try
                {
                    bool exists = File.Exists(path);
                    if (exists && !string.Equals((string)link.TargetPath, exe, StringComparison.OrdinalIgnoreCase)) continue;
                    string args = (string)link.Arguments;
                    if (args.Contains("--remote-debugging-port=9226", StringComparison.Ordinal)) continue;
                    if (exists)
                    {
                        Directory.CreateDirectory(backup);
                        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..16];
                        string copy = Path.Combine(backup, key + ".lnk"); if (!File.Exists(copy)) File.Copy(path, copy);
                    }
                    args = System.Text.RegularExpressions.Regex.Replace(args, @"--remote-debugging-(?:port|address)=\S+", "").Trim();
                    link.TargetPath = exe; link.Arguments = args + " --remote-debugging-address=127.0.0.1 --remote-debugging-port=9226";
                    link.WorkingDirectory = Path.GetDirectoryName(exe); link.IconLocation = exe + ",0"; link.Save(); changed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException) { }
                finally { Marshal.FinalReleaseComObject(link); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return changed;
    }
}
