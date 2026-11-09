using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyNotch;

/// <summary>Win32 P/Invoke used by the notch, the dock and the taskbar controller.</summary>
static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
    public const int SW_HIDE = 0, SW_MINIMIZE = 6, SW_SHOWNA = 8, SW_RESTORE = 9;
    const uint WM_GETICON = 0x7F, WM_SETTINGCHANGE = 0x1A, SMTO_ABORTIFHUNG = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint h, uint cmd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint h, int idx);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint h, int idx, nint v);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern nint GetClassLongPtr(nint h, int idx);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] static extern nint SendMessageTimeout(nint h, uint msg, nint w, nint l, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] static extern nint SendMessageTimeout(nint h, uint msg, nint w, string l, uint flags, uint timeout, out nint result);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(nint h, uint affinity);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint h, StringBuilder sb, int max);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint h, int attr, out int value, int size);
    [DllImport("kernel32.dll")] static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(nint h, int flags, StringBuilder sb, ref int size);

    // --- AppBar (shell32) ---
    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA { public int cbSize; public nint hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public nint lParam; }
    [DllImport("shell32.dll")] public static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);
    public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10, ABE_BOTTOM = 3;
    public const int ABS_AUTOHIDE = 1, ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;
    public static APPBARDATA NewAppBarData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>() };

    // --- File icons (shell32) ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public nint hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);

    public static string WindowText(nint h) { var sb = new StringBuilder(256); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
    public static string ClassName(nint h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }

    /// <summary>"Cloaked" = hidden by DWM (suspended UWP apps, windows on other virtual desktops).</summary>
    public static bool IsCloaked(nint h) => DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out var v, sizeof(int)) == 0 && v != 0;

    public static void AddExStyle(nint h, long add, long remove = 0) =>
        SetWindowLongPtr(h, GWL_EXSTYLE, (nint)((GetWindowLongPtr(h, GWL_EXSTYLE) & ~remove) | add));

    public static string? ProcessPath(uint pid)
    {
        var h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
        if (h == 0) return null;
        try
        {
            var sb = new StringBuilder(1024); int n = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>The window's own icon (owned by that window; don't destroy it).</summary>
    public static ImageSource? WindowIcon(nint h)
    {
        SendMessageTimeout(h, WM_GETICON, 1 /*ICON_BIG*/, 0, SMTO_ABORTIFHUNG, 50, out var icon);
        if (icon == 0) SendMessageTimeout(h, WM_GETICON, 2 /*ICON_SMALL2*/, 0, SMTO_ABORTIFHUNG, 50, out icon);
        if (icon == 0) icon = GetClassLongPtr(h, -14 /*GCLP_HICON*/);
        return icon == 0 ? null : ToImage(icon);
    }

    /// <summary>File/shortcut icon via the shell; we own the HICON, so it's destroyed after conversion.</summary>
    public static ImageSource? FileIcon(string path)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 /*SHGFI_ICON*/) == 0 || info.hIcon == 0) return null;
        try { return ToImage(info.hIcon); }
        finally { DestroyIcon(info.hIcon); }
    }

    static ImageSource? ToImage(nint hIcon)
    {
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException) { return null; }
    }

    public static void PressWinKey()
    {
        const byte VK_LWIN = 0x5B; const uint KEYUP = 2;
        keybd_event(VK_LWIN, 0, 0, 0);
        keybd_event(VK_LWIN, 0, KEYUP, 0);
    }

    /// <summary>Tells Explorer and all apps that the theme/accent changed.</summary>
    public static void BroadcastSettingChange(string area) =>
        SendMessageTimeout(0xFFFF /*HWND_BROADCAST*/, WM_SETTINGCHANGE, 0, area, SMTO_ABORTIFHUNG, 1000, out _);
}

/// <summary>Sharp bitmaps: screen captures (for blur) and high-res icons from the shell.</summary>
static class Pixels
{
    [DllImport("user32.dll")] static extern nint GetDC(nint h);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint h, nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern int GetObject(nint h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] static extern int GetDIBits(nint dc, nint bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);

    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public nint bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { void GetImage(SIZE size, int flags, out nint hbmp); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, nint bindCtx,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    /// <summary>Copies an HBITMAP into a top-down BitmapSource (GetDIBits normalizes row order).</summary>
    static BitmapSource? FromHBitmap(nint hbmp, int w, int h, PixelFormat format)
    {
        var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        var dc = GetDC(0);
        try { if (GetDIBits(dc, hbmp, 0, (uint)h, bits, ref bi, 0) == 0) return null; }
        finally { ReleaseDC(0, dc); }
        var src = BitmapSource.Create(w, h, 96, 96, format, null, bits, w * 4);
        src.Freeze();
        return src;
    }

    /// <summary>
    /// Captures a screen rectangle (physical pixels). MyNotch windows are excluded from capture
    /// (SetWindowDisplayAffinity), so this returns what is BEHIND the notch/dock.
    /// </summary>
    public static BitmapSource? CaptureScreen(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return null;
        var screen = GetDC(0);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 /*SRCCOPY*/);
        SelectObject(mem, old); // GetDIBits needs a bitmap that isn't selected into a DC
        DeleteDC(mem);
        ReleaseDC(0, screen);
        try { return FromHBitmap(bmp, w, h, PixelFormats.Bgr32); }
        finally { DeleteObject(bmp); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int PrivateExtractIcons(string file, int index, int cx, int cy, nint[] icons, int[] ids, int count, int flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint h);

    /// <summary>
    /// High-res icon (128 px by default) without the shortcut arrow; null on failure.
    /// Exe: read straight from its resources and scaled (the shell frames exes that only have small icons).
    /// </summary>
    public static ImageSource? ShellIcon(string path, int px = 128)
    {
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var icons = new nint[1];
            if (PrivateExtractIcons(path, 0, px, px, icons, new int[1], 1, 0) == 1 && icons[0] != 0)
            {
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                catch (Exception ex) when (ex is COMException or ArgumentException) { }
                finally { DestroyIcon(icons[0]); }
            }
        }
        try
        {
            SHCreateItemFromParsingName(path, 0, typeof(IShellItemImageFactory).GUID, out var factory);
            factory.GetImage(new SIZE { cx = px, cy = px }, 0x4 /*SIIGBF_ICONONLY*/, out var hbmp);
            try
            {
                GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out var bm);
                return FromHBitmap(hbmp, bm.bmWidth, Math.Abs(bm.bmHeight), PixelFormats.Pbgra32);
            }
            finally { DeleteObject(hbmp); }
        }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>A window's AppUserModelID: the app identity the taskbar uses (key for UWP/Store apps).</summary>
static class AppId
{
    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt; ushort r1, r2, r3; public nint p; nint p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);

    public static string? Of(nint hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0) return null;
        try
        {
            var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 }; // PKEY_AppUserModel_ID
            if (store.GetValue(ref key, out var v) != 0) return null;
            try { return v.vt == 31 /*VT_LPWSTR*/ ? Marshal.PtrToStringUni(v.p) : null; }
            finally { PropVariantClear(ref v); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }
}
