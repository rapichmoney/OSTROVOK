using System.Runtime.InteropServices;

namespace DesktopIsland.Services;

// Uses only the documented default render endpoint; no global input simulation.
public sealed class AudioService
{
    public float Volume { get; private set; }
    public bool Muted { get; private set; }
    public bool Available { get; private set; }
    public string Error { get; private set; } = "";
    public string DeviceName { get; private set; } = "Устройство вывода Windows";
    public async Task RefreshDeviceNameAsync()
    {
        try
        {
            string id = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(Windows.Media.Devices.AudioDeviceRole.Default);
            if (!string.IsNullOrWhiteSpace(id)) DeviceName = (await Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(id)).Name;
        }
        catch (Exception) { DeviceName = "Устройство вывода Windows"; }
    }
    public bool Refresh() => WithEndpoint(endpoint =>
    {
        Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var volume));
        Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted)); Volume = volume; Muted = muted;
    });
    public bool SetVolume(float value) => WithEndpoint(endpoint =>
    {
        var context = Guid.Empty;
        Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(Math.Clamp(value, 0, 1), ref context));
    });
    public bool SetMuted(bool value) => WithEndpoint(endpoint =>
    {
        var context = Guid.Empty; Marshal.ThrowExceptionForHR(endpoint.SetMute(value, ref context));
    });
    private bool WithEndpoint(Action<IAudioEndpointVolume> action)
    {
        object? enumerator = null, device = null, endpoint = null;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            var devices = (IMMDeviceEnumerator)enumerator!;
            int status = devices.GetDefaultAudioEndpoint(0, 1, out var output);
            if (status < 0) status = devices.GetDefaultAudioEndpoint(0, 0, out output);
            Marshal.ThrowExceptionForHR(status); device = output;
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(output.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
            action((IAudioEndpointVolume)endpoint); Available = true; Error = ""; return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { Available = false; Error = ex.Message; return false; }
        finally
        {
            if (endpoint != null && Marshal.IsComObject(endpoint)) Marshal.ReleaseComObject(endpoint);
            if (device != null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
        }
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
