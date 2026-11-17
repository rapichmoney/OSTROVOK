using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DesktopIsland.Services;

public sealed class SystemMonitor
{
    private ulong previousIdle, previousKernel, previousUser;
    private long previousReceived, previousSent;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private double previousSeconds;
    private bool initialized;
    public double Cpu { get; private set; }
    public double MemoryPercent { get; private set; }
    public ulong MemoryUsed { get; private set; }
    public ulong MemoryTotal { get; private set; }
    public string Battery { get; private set; } = "Нет данных";
    public string BatteryDetail { get; private set; } = "";
    public string Network { get; private set; } = "Нет соединения";
    public double ReceiveRate { get; private set; }
    public double SendRate { get; private set; }
    public string Disk { get; private set; } = "";
    public void Refresh()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = (kernel - previousKernel) + (user - previousUser);
            if (initialized && total > 0) Cpu = Math.Clamp((1.0 - (double)(idle - previousIdle) / total) * 100, 0, 100);
            previousIdle = idle; previousKernel = kernel; previousUser = user;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory)) { MemoryTotal = memory.TotalPhysical; MemoryUsed = memory.TotalPhysical - memory.AvailablePhysical; MemoryPercent = memory.Load; }
        if (GetSystemPowerStatus(out var power))
        {
            Battery = (power.Flag & 128) != 0 ? "От сети" : power.Percent <= 100 ? power.Percent + "%" : "Нет данных";
            BatteryDetail = (power.Flag & 128) != 0 ? "Батарея не установлена" : power.Ac == 1 ? "Питание от сети" : "Питание от батареи";
        }
        try
        {
            long received = 0, sent = 0;
            var names = new List<string>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var stats = adapter.GetIPStatistics(); received += stats.BytesReceived; sent += stats.BytesSent;
                names.Add(adapter.Name);
            }
            Network = names.Count > 0 ? string.Join(", ", names.Take(2)) + (names.Count > 2 ? $" · ещё {names.Count - 2}" : "") : "Нет соединения";
            var seconds = stopwatch.Elapsed.TotalSeconds;
            if (initialized && seconds > previousSeconds)
            {
                ReceiveRate = Math.Max(0, received - previousReceived) / (seconds - previousSeconds);
                SendRate = Math.Max(0, sent - previousSent) / (seconds - previousSeconds);
            }
            previousReceived = received; previousSent = sent; previousSeconds = seconds;
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            Disk = $"{drive.Name}  {DownloadEntry.FormatBytes(drive.AvailableFreeSpace)} свободно из {DownloadEntry.FormatBytes(drive.TotalSize)}";
        }
        catch (Exception ex) when (ex is NetworkInformationException or IOException or UnauthorizedAccessException) { Network = "Данные сети недоступны"; }
        initialized = true;
    }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte Ac, Flag, Percent, Reserved; public uint Life, FullLife; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended; }
}
