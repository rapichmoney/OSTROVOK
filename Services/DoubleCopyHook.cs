using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DesktopIsland.Services;

internal sealed class DoubleCopyHook : IDisposable
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DoubleCopyGesture gesture = new();
    private readonly HookProcedure callback;
    private IntPtr hook;
    internal event Action<DoubleCopyRequest>? DoubleCopied;
    internal bool IsActive => hook != IntPtr.Zero;
    internal DoubleCopyHook() => callback = OnKeyboard;
    internal bool Start()
    {
        if (IsActive) return true;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        return IsActive;
    }
    internal void Stop()
    {
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        gesture.Reset();
    }
    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0)
            {
                var key = Marshal.PtrToStructure<KeyboardData>(data);
                if ((key.Flags & 0x10) == 0)
                {
                    if (message.ToInt64() is 0x101 or 0x105) gesture.KeyUp((int)key.Key);
                    else if (message.ToInt64() is 0x100 or 0x104)
                    {
                        bool Held(int value) => (GetAsyncKeyState(value) & 0x8000) != 0;
                        var request = gesture.KeyDown((int)key.Key, Held(0x11), Held(0x10) || Held(0x12) || Held(0x5B) || Held(0x5C), Environment.TickCount64, ForegroundWindow(), ClipboardSequence());
                        if (request != null && !dispatcher.HasShutdownStarted)
                            dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => DoubleCopied?.Invoke(request)));
                    }
                }
            }
        }
        catch { /* The observer must never interrupt keyboard input. */ }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() => Stop();
    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint Key, ScanCode, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, HookProcedure procedure, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")] internal static extern uint ClipboardSequence();
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] internal static extern IntPtr ForegroundWindow();
}
