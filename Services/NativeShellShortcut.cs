using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DesktopIsland.Services;

// Use the Unicode shell interface directly, without Script Host's dispatch layer.
internal sealed class NativeShellShortcut : YandexLaunchSetup.IShortcut
{
    private readonly string path;
    private readonly ShellLink link;
    private bool disposed;
    internal NativeShellShortcut(string path)
    {
        this.path = YandexLaunchSetup.ShellPath(path);
        var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!;
        link = (ShellLink)(Activator.CreateInstance(type) ?? throw new InvalidOperationException("Shell link is unavailable"));
        try { if (File.Exists(this.path)) ((IPersistFile)link).Load(this.path, 0); }
        catch { Marshal.FinalReleaseComObject(link); throw; }
    }
    public string TargetPath
    {
        get { var buffer = new StringBuilder(32768); link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0); return buffer.ToString(); }
        set => link.SetPath(YandexLaunchSetup.ShellPath(value));
    }
    public string Arguments
    {
        get { var buffer = new StringBuilder(32768); link.GetArguments(buffer, buffer.Capacity); return buffer.ToString(); }
        set => link.SetArguments(value);
    }
    public string WorkingDirectory { set => link.SetWorkingDirectory(YandexLaunchSetup.ShellPath(value)); }
    public string IconLocation
    {
        set
        {
            int separator = value.LastIndexOf(',');
            string file = separator >= 0 ? value[..separator] : value;
            int index = separator >= 0 && int.TryParse(value[(separator + 1)..], out int parsed) ? parsed : 0;
            link.SetIconLocation(YandexLaunchSetup.ShellPath(file.Trim('"')), index);
        }
    }
    public void Save() => ((IPersistFile)link).Save(path, true);
    public void Dispose() { if (disposed) return; disposed = true; Marshal.FinalReleaseComObject(link); }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr data, uint flags);
        void GetIDList(out IntPtr id);
        void SetIDList(IntPtr id);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder folder, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string folder);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort key);
        void SetHotkey(ushort key);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string file, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr owner, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
