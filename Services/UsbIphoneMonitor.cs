using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DesktopIsland.Services;

// Identities remain in memory and are never written to the activity or crash logs.
internal sealed class UsbIphoneConnections
{
    private HashSet<string>? connected;
    internal int Count => connected?.Count ?? 0;

    internal int Observe(IEnumerable<string> identities)
    {
        var current = identities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int arrived = connected == null ? current.Count : current.Count(id => !connected.Contains(id));
        connected = current;
        return arrived;
    }

    internal static bool IsIphone(string description, string usbAncestor) =>
        description.Contains("iPhone", StringComparison.OrdinalIgnoreCase) &&
        usbAncestor.StartsWith(@"USB\VID_05AC&", StringComparison.OrdinalIgnoreCase);
}

internal static class UsbIphoneMonitor
{
    private static readonly PropertyKey busDescription = new(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);

    internal static string[] ReadConnected(CancellationToken cancellation)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr devices = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 0x6); // Present, all classes.
        if (devices == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (uint i = 0; ; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var device = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(devices, i, ref device))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                string name = ReadName(devices, ref device);
                if (!name.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) continue;
                string ancestor = UsbAncestor(device.Node);
                if (UsbIphoneConnections.IsIphone(name, ancestor)) result.Add(ancestor);
            }
            return result.ToArray();
        }
        finally { SetupDiDestroyDeviceInfoList(devices); }
    }

    private static string ReadName(IntPtr devices, ref DeviceInfo device)
    {
        var data = new byte[8192];
        var key = busDescription;
        string name = SetupDiGetDeviceProperty(devices, ref device, ref key, out uint type, data, (uint)data.Length, out _, 0) && type == 18
            ? Encoding.Unicode.GetString(data).TrimEnd('\0') : "";
        foreach (uint property in new uint[] { 12, 0 })
            if (SetupDiGetDeviceRegistryProperty(devices, ref device, property, out _, data, (uint)data.Length, out uint required))
                name += " " + Encoding.Unicode.GetString(data, 0, (int)Math.Min(required, data.Length)).TrimEnd('\0');
        return name;
    }

    private static string UsbAncestor(uint node)
    {
        string root = "";
        for (int depth = 0; depth < 16; depth++)
        {
            var id = new StringBuilder(1024);
            if (CM_Get_Device_ID(node, id, id.Capacity, 0) == 0 && id.ToString().StartsWith(@"USB\VID_05AC&", StringComparison.OrdinalIgnoreCase))
                root = id.ToString();
            if (CM_Get_Parent(out uint parent, node, 0) != 0 || parent == node) break;
            node = parent;
        }
        return root;
    }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public int Size; public Guid Class; public uint Node; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct PropertyKey(Guid format, uint id) { public readonly Guid Format = format; public readonly uint Id = id; }
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr devices, uint index, ref DeviceInfo device);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)] private static extern bool SetupDiGetDeviceProperty(IntPtr devices, ref DeviceInfo device, ref PropertyKey key, out uint type, byte[] data, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr devices, ref DeviceInfo device, uint property, out uint type, byte[] data, uint size, out uint required);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID(uint node, StringBuilder id, int length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
}
