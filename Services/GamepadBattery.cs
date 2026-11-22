using System.Runtime.InteropServices;

namespace DesktopIsland.Services;

internal static class GamepadBattery
{
    internal static string Describe(byte type, byte level) => type switch
    {
        1 => "По USB",
        2 or 3 => level switch { 0 => "Разряжен", 1 => "Низкий заряд", 2 => "Средний заряд", 3 => "Полный заряд", _ => "Заряд неизвестен" },
        _ => "Заряд не передаётся"
    };
    internal static string Read()
    {
        var result = new List<string>();
        try
        {
            for (uint i = 0; i < 4; i++)
                if (XInputGetBatteryInformation(i, 0, out var battery) == 0 && battery.Type != 0)
                    result.Add("Геймпад " + (i + 1) + ": " + Describe(battery.Type, battery.Level));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return string.Join(" · ", result);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Battery { public byte Type, Level; }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetBatteryInformation(uint user, byte device, out Battery battery);
}
