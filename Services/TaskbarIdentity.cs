using System;
using System.Runtime.InteropServices;

namespace DesktopIsland.Services
{
    // Window-level shell properties are separate from WM_SETICON and package logos.
    internal static class TaskbarIdentity
    {
        internal const string Id = "rapichmoney.OSTROVOK";
        [StructLayout(LayoutKind.Sequential)] private struct PropertyKey
        {
            public Guid Format;
            public uint Id;
            public PropertyKey(uint id) { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); Id = id; }
        }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr Text;
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface PropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out Variant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref Variant value);
            [PreserveSig] int Commit();
        }
        [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out PropertyStore store);
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
        private static PropertyStore Open(IntPtr window)
        {
            Guid iid = typeof(PropertyStore).GUID; PropertyStore store;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out store)); return store;
        }
        private static void Set(PropertyStore store, uint id, string? text)
        {
            var key = new PropertyKey(id); var value = new Variant();
            if (text != null) { value.Type = 31; value.Text = Marshal.StringToCoTaskMemUni(text); }
            try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
            finally { PropVariantClear(ref value); }
        }
        internal static void Apply(IntPtr window, string iconPath)
        {
            var store = Open(window);
            try { Set(store, 3, iconPath); Set(store, 5, Id); }
            finally { Marshal.ReleaseComObject(store); }
        }
        internal static string Read(IntPtr window, uint id)
        {
            var store = Open(window); var key = new PropertyKey(id); var value = new Variant();
            try { Marshal.ThrowExceptionForHR(store.GetValue(ref key, out value)); return value.Type == 31 ? Marshal.PtrToStringUni(value.Text) ?? "" : ""; }
            finally { PropVariantClear(ref value); Marshal.ReleaseComObject(store); }
        }
        internal static void Clear(IntPtr window)
        {
            var store = Open(window);
            try { Set(store, 5, null); Set(store, 3, null); }
            finally { Marshal.ReleaseComObject(store); }
        }
    }
}
