using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace MyNotch;

/// <summary>Windows dark/light mode and accent color. Explorer, the taskbar and other apps follow.</summary>
static class SystemTheme
{
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    const string DwmKey = @"Software\Microsoft\Windows\DWM";

    public static bool IsDark =>
        Registry.CurrentUser.OpenSubKey(Personalize)?.GetValue("AppsUseLightTheme") is int v && v == 0;

    // The original Windows theme is backed up before MyNotch overwrites it and restored when MyNotch quits (see Revert/Reapply).
    static readonly string Dir = AppDirs.Local;
    static readonly string BackupFile = Path.Combine(Dir, "theme-backup.json");
    static readonly string AppliedFile = Path.Combine(Dir, "theme-applied.json");

    static void Write(params (string Key, string Name, object Value)[] values)
    {
        RegBackup.Write(Registry.CurrentUser, BackupFile, values);
        Native.BroadcastSettingChange("ImmersiveColorSet");
    }

    /// <summary>MyNotch off: save MyNotch's theme choice, then restore the original Windows theme.</summary>
    public static void Revert()
    {
        if (!File.Exists(BackupFile)) return; // MyNotch never changed the theme
        RegBackup.Snapshot(Registry.CurrentUser, BackupFile, AppliedFile);
        RegBackup.Restore(Registry.CurrentUser, BackupFile);
        Native.BroadcastSettingChange("ImmersiveColorSet");
    }

    /// <summary>MyNotch on: reapply the previous session's theme choice (the original theme is backed up again).</summary>
    public static void Reapply()
    {
        if (!File.Exists(AppliedFile)) return;
        Write([.. RegBackup.Values(AppliedFile)]);
        File.Delete(AppliedFile);
    }

    public static void SetDark(bool dark) =>
        Write((Personalize, "AppsUseLightTheme", dark ? 0 : 1), (Personalize, "SystemUsesLightTheme", dark ? 0 : 1));

    /// <summary>Accent color on title bars &amp; window borders (Explorer and all apps), plus Start.</summary>
    public static bool AccentOnWindows
    {
        get => Registry.CurrentUser.OpenSubKey(DwmKey)?.GetValue("ColorPrevalence") is int v && v == 1;
        set => Write((DwmKey, "ColorPrevalence", value ? 1 : 0), (Personalize, "ColorPrevalence", value ? 1 : 0));
    }

    /// <summary>Windows transparency effects (Start, Explorer, system panels).</summary>
    public static bool Transparency
    {
        get => Registry.CurrentUser.OpenSubKey(Personalize)?.GetValue("EnableTransparency") is not int v || v == 1;
        set => Write((Personalize, "EnableTransparency", value ? 1 : 0));
    }

    public static Color Accent =>
        Registry.CurrentUser.OpenSubKey(AccentKey)?.GetValue("AccentColorMenu") is int abgr
            ? FromAbgr(abgr) : Color.FromRgb(0x00, 0x78, 0xD4);

    public static void SetAccent(Color c)
    {
        int argb = unchecked((int)(0xC4000000u | (uint)(c.R << 16 | c.G << 8 | c.B)));
        Write(
            (AccentKey, "AccentPalette", Palette(c)),
            (AccentKey, "AccentColorMenu", ToAbgr(c)),
            (AccentKey, "StartColorMenu", ToAbgr(Mix(c, Colors.Black, 0.4))),
            (DwmKey, "AccentColor", ToAbgr(c)),
            (DwmKey, "ColorizationColor", argb),
            (DwmKey, "ColorizationAfterglow", argb),
            // Turn off the automatic accent from the wallpaper so the choice is not overwritten.
            (@"Control Panel\Desktop", "AutoColorization", 0));
    }

    // The Windows registry stores colors as DWORD 0xAABBGGRR.
    public static int ToAbgr(Color c) => unchecked((int)(0xFF000000u | (uint)(c.B << 16 | c.G << 8 | c.R)));
    public static Color FromAbgr(int v) => Color.FromRgb((byte)v, (byte)(v >> 8), (byte)(v >> 16));

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>AccentPalette = 8 RGBA colors (3 lighter, base, 3 darker, complement); used by Start/taskbar.</summary>
    public static byte[] Palette(Color c)
    {
        Color[] shades =
        [
            Mix(c, Colors.White, 0.6), Mix(c, Colors.White, 0.4), Mix(c, Colors.White, 0.2), c,
            Mix(c, Colors.Black, 0.2), Mix(c, Colors.Black, 0.4), Mix(c, Colors.Black, 0.6),
            Color.FromRgb((byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B)),
        ];
        return shades.SelectMany(s => new[] { s.R, s.G, s.B, (byte)0 }).ToArray();
    }
}

/// <summary>Master volume of the default output device, via Core Audio COM.</summary>
static class Audio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(); // unused; only keeps the vtable order
        IMMDevice GetDefaultAudioEndpoint(int dataFlow, int role);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [return: MarshalAs(UnmanagedType.IUnknown)] object Activate(ref Guid iid, int clsCtx, nint activationParams);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        // Order must match the endpointvolume.h vtable exactly.
        void RegisterControlChangeNotify(); void UnregisterControlChangeNotify(); void GetChannelCount();
        void SetMasterVolumeLevel(); void SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        void GetMasterVolumeLevel(); float GetMasterVolumeLevelScalar();
        void SetChannelVolumeLevel(); void SetChannelVolumeLevelScalar(); void GetChannelVolumeLevel(); void GetChannelVolumeLevelScalar();
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [return: MarshalAs(UnmanagedType.Bool)] bool GetMute();
    }

    // Fetch the endpoint every time: the default device can change (headset plugged in, etc.).
    static IAudioEndpointVolume? Endpoint()
    {
        try
        {
            var device = ((IMMDeviceEnumerator)new MMDeviceEnumerator()).GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/);
            var iid = typeof(IAudioEndpointVolume).GUID;
            return (IAudioEndpointVolume)device.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, 0);
        }
        catch (COMException) { return null; } // no output device
    }

    public static double? Volume
    {
        get => Endpoint()?.GetMasterVolumeLevelScalar();
        set { var g = Guid.Empty; if (value is { } v) Endpoint()?.SetMasterVolumeLevelScalar((float)Math.Clamp(v, 0, 1), ref g); }
    }

    public static bool Muted
    {
        get => Endpoint()?.GetMute() ?? false;
        set { var g = Guid.Empty; Endpoint()?.SetMute(value, ref g); }
    }
}

/// <summary>Internal laptop screen brightness via WMI. External monitors are not supported (null).</summary>
static class Brightness
{
    public static int? Get()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch (Exception ex) when (ex is ManagementException or COMException) { }
        return null;
    }

    public static void Set(int percent)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in s.Get())
                o.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)Math.Clamp(percent, 0, 100)]);
        }
        catch (Exception ex) when (ex is ManagementException or COMException) { }
    }
}

/// <summary>Wi-Fi/Bluetooth radio switches (like Quick Settings; not network selection).</summary>
static class Radios
{
    public static async Task<Radio?> FindAsync(RadioKind kind)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return null;
            return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == kind);
        }
        catch (COMException) { return null; }
    }
}
