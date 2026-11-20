using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace DesktopIsland.Services;

internal sealed class ClipboardPasteTarget
{
    private readonly IntPtr window;
    private readonly int[] identity;
    private readonly TextPatternRange selection;
    private readonly string original;
    private ClipboardPasteTarget(IntPtr window, int[] identity, TextPatternRange selection, string original)
    { this.window = window; this.identity = identity; this.selection = selection; this.original = original; }
    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private static TextPatternRange? SelectedEditableText(AutomationElement element, string text)
    {
        if (element.Current.IsPassword || !element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern)) return null;
        var ranges = ((TextPattern)pattern).GetSelection();
        if (ranges == null || ranges.Length != 1 || ranges[0].GetAttributeValue(TextPattern.IsReadOnlyAttribute) is not false) return null;
        return Normalize(ranges[0].GetText(4001)) == Normalize(text) ? ranges[0] : null;
    }
    internal static async Task<ClipboardPasteTarget?> CaptureAsync(IntPtr window, string original)
    {
        try { return await Task.Run(() => Capture(window, original)).WaitAsync(TimeSpan.FromMilliseconds(500)); }
        catch (TimeoutException) { return null; }
    }
    private static ClipboardPasteTarget? Capture(IntPtr window, string original)
    {
        try
        {
            if (DoubleCopyHook.ForegroundWindow() != window) return null;
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;
            var identity = focused.GetRuntimeId();
            if (identity == null || identity.Length == 0) return null;
            var selected = SelectedEditableText(focused, original);
            return selected == null ? null : new(window, identity, selected.Clone(), original);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or ExternalException or NotSupportedException or ArgumentException) { return null; }
    }
    private bool MatchesFocus()
    {
        if (DoubleCopyHook.ForegroundWindow() != window) return false;
        var focused = AutomationElement.FocusedElement;
        if (focused?.GetRuntimeId()?.SequenceEqual(identity) != true) return false;
        var current = SelectedEditableText(focused, original);
        return current != null && current.CompareEndpoints(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.Start) == 0
            && current.CompareEndpoints(TextPatternRangeEndpoint.End, selection, TextPatternRangeEndpoint.End) == 0
            && AutomationElement.FocusedElement?.GetRuntimeId()?.SequenceEqual(identity) == true;
    }
    internal async Task<bool> TryPasteAsync(string translation, CancellationToken token)
    {
        uint sequence = DoubleCopyHook.ClipboardSequence();
        try
        {
            bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
            bool ModifiersHeld() => new[] { 0x11, 0x10, 0x12, 0x5B, 0x5C, 0x43, 0x56 }.Any(Held);
            for (int attempt = 0; attempt < 75 && ModifiersHeld(); attempt++) await Task.Delay(20, token);
            token.ThrowIfCancellationRequested();
            if (ModifiersHeld() || !await Task.Run(MatchesFocus).WaitAsync(TimeSpan.FromMilliseconds(500), token)
                || DoubleCopyHook.ForegroundWindow() != window || DoubleCopyHook.ClipboardSequence() != sequence
                || !System.Windows.Clipboard.ContainsText() || System.Windows.Clipboard.GetText() != translation) return false;
            if (DoubleCopyHook.ForegroundWindow() != window || DoubleCopyHook.ClipboardSequence() != sequence) return false;
            var input = new[] { Key(0x11), Key(0x56), Key(0x56, true), Key(0x11, true) };
            uint sent = SendInput((uint)input.Length, input, Marshal.SizeOf<NativeInput>());
            if (sent == input.Length) return true;
            SendInput(2, [Key(0x56, true), Key(0x11, true)], Marshal.SizeOf<NativeInput>());
            return false;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or ExternalException or NotSupportedException or TimeoutException or ArgumentException) { return false; }
    }
    private static NativeInput Key(ushort key, bool up = false) => new() { Type = 1, Key = key, Flags = up ? 2u : 0u };
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Key;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(16)] public uint Time;
        [FieldOffset(24)] public UIntPtr Extra;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
