using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyNotch;

/// <summary>
/// One dock icon = one app. Pinned shortcuts have a LaunchPath; open windows of the same exe
/// are merged into that icon (no duplicates), like the macOS Dock.
/// </summary>
public sealed class DockItem : INotifyPropertyChanged
{
    /// <summary>Lower-case exe file name (e.g. "chrome.exe"), used to match windows.</summary>
    public string? ExeName { get; init; }
    public string? LaunchPath { get; init; }
    ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set { _icon = value; Notify(nameof(Icon)); } }
    /// <summary>The app's own icon (restored when MyNotch-style icons are turned off).</summary>
    public ImageSource? OriginalIcon { get; init; }

    public List<nint> Windows { get; private set; } = [];

    string _title = "";
    public string Title { get => _title; set { if (_title != value) { _title = value; Notify(nameof(Title)); } } }

    bool _active;
    public bool IsActive { get => _active; set { if (_active != value) { _active = value; Notify(nameof(IndicatorWidth)); } } }

    double _size = DockWindow.BaseSize;
    public double Size { get => _size; set { if (_size != value) { _size = value; Notify(nameof(Size)); } } }

    /// <summary>Long bar = active app, dot = open app, none = pinned only.</summary>
    public double IndicatorWidth => Windows.Count == 0 ? 0 : IsActive ? 16 : 5;

    public void SetWindows(List<nint> windows)
    {
        bool hadWindows = Windows.Count > 0;
        Windows = windows;
        if (hadWindows != (windows.Count > 0)) Notify(nameof(IndicatorWidth));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Taskbar replacement. Registered as an AppBar so maximized windows don't cover it;
/// glass/blur background, follows dark/light mode, icons magnify on hover.
/// </summary>
public partial class DockWindow : Window
{
    public const double BaseSize = 44;
    const double MaxSize = 74, Sigma = 64, ReservedDip = 62, Headroom = MaxSize - BaseSize + 4;
    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    static readonly string PinnedDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    readonly List<DockItem> _pinned;
    readonly ObservableCollection<DockItem> _running = [];
    readonly Dictionary<nint, string> _exeOf = []; // cache hwnd -> exe name
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(700) };
    nint _hwnd, _lastExternal; // _lastExternal: last active window of another app (never the dock)
    uint _callbackMsg, _taskbarCreatedMsg;
    Backdrop? _backdrop;
    bool _animating;

    public DockWindow()
    {
        InitializeComponent();
        _pinned = Directory.Exists(PinnedDir)
            ? Directory.GetFiles(PinnedDir, "*.lnk").Select(p =>
            {
                var target = Shortcut.Target(p);
                return new DockItem
                {
                    LaunchPath = p,
                    ExeName = target != null ? Path.GetFileName(target).ToLowerInvariant() : null,
                    Title = Path.GetFileNameWithoutExtension(p),
                    // icon from the target exe: the .lnk icon carries the shortcut arrow
                    OriginalIcon = (target != null ? Pixels.ShellIcon(target) : null) ?? Pixels.ShellIcon(p) ?? Native.FileIcon(p),
                };
            }).ToList()
            : [];
        Pinned.ItemsSource = _pinned;
        Running.ItemsSource = _running;
        _poll.Tick += (_, _) => Refresh();
        Bar.SizeChanged += (_, _) => UpdateShape(); // the card widens with the icons while zooming
        App.LookChanged += ApplyLook;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW, Native.WS_EX_APPWINDOW);
        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        _callbackMsg = Native.RegisterWindowMessage("MyNotch.AppBar");
        _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated"); // sent when explorer.exe restarts

        _backdrop = new Backdrop(Bar, BackdropImage);
        ApplyLook();
        Taskbar.Hide();
        RegisterAppBar();
        Refresh();
        _poll.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        var d = Native.NewAppBarData(); d.hWnd = _hwnd;
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref d);
        base.OnClosed(e);
        Application.Current.Shutdown(); // closing the dock ends the app; OnExit restores the taskbar
    }

    // ---------- Look ----------

    void ApplyLook()
    {
        var s = Settings.Current;
        bool dark = App.Dark;
        Backdrop.ExcludeFromCapture(this, !s.ShowInScreenshots);
        _backdrop!.Enabled = s.UseBlur;
        Tint.Background = new SolidColorBrush(dark
            ? Color.FromArgb(s.TintAlpha, 0x1C, 0x1C, 0x1C)
            : Color.FromArgb(s.TintAlpha, 0xF5, 0xF5, 0xF5));
        Bar.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0, 0, 0));
        StartIcon.Source = s.CustomIcons ? SystemIcons.Render(SystemIcons.Launcher, 128) : null;
        StartSquares.Visibility = s.CustomIcons ? Visibility.Collapsed : Visibility.Visible;
        foreach (var p in _pinned)
            p.Icon = s.CustomIcons && p.ExeName == "explorer.exe" ? SystemIcons.Render(SystemIcons.ExplorerApp, 128) : p.OriginalIcon;
        UpdateShape();
    }

    void UpdateShape()
    {
        // Rounded, anti-aliased clip on the card only; icons outside Bar are not clipped.
        Bar.Clip = new RectangleGeometry(new Rect(0, 0, Bar.ActualWidth, Bar.ActualHeight), 18, 18);
    }

    // ---------- AppBar ----------

    void RegisterAppBar()
    {
        var d = Native.NewAppBarData(); d.hWnd = _hwnd; d.uCallbackMessage = _callbackMsg;
        Native.SHAppBarMessage(Native.ABM_NEW, ref d);
        PositionAppBar();
    }

    void PositionAppBar()
    {
        // AppBar works in physical pixels, WPF in DIPs. Only ReservedDip is reserved;
        // the window is taller (Headroom) so magnified icons have room.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        int reserved = (int)Math.Round(ReservedDip * scale);
        int total = (int)Math.Round((ReservedDip + Headroom) * scale);
        var d = Native.NewAppBarData();
        d.hWnd = _hwnd;
        d.uEdge = Native.ABE_BOTTOM;
        d.rc = new Native.RECT { Left = 0, Right = Native.GetSystemMetrics(0), Bottom = Native.GetSystemMetrics(1) };
        d.rc.Top = d.rc.Bottom - reserved;
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref d);
        d.rc.Top = d.rc.Bottom - reserved; // the shell may move Bottom; keep the height
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref d);
        Native.SetWindowPos(_hwnd, -1 /*HWND_TOPMOST*/, d.rc.Left, d.rc.Bottom - total, d.rc.Right - d.rc.Left, total, 0x0010 /*SWP_NOACTIVATE*/);
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == _callbackMsg)
        {
            if (wParam == Native.ABN_POSCHANGED) PositionAppBar();
            else if (wParam == Native.ABN_FULLSCREENAPP)
            {
                // Clicking the desktop sometimes fires this by mistake; ignore it.
                var cls = Native.ClassName(Native.GetForegroundWindow());
                bool on = lParam != 0 && cls is not ("Progman" or "WorkerW");
                Visibility = on ? Visibility.Hidden : Visibility.Visible;
                App.RaiseFullscreen(on);
            }
        }
        else if (msg == _taskbarCreatedMsg)
        {
            Taskbar.Hide();
            RegisterAppBar();
        }
        return 0;
    }

    // ---------- App list ----------

    string ExeOf(nint h)
    {
        if (!_exeOf.TryGetValue(h, out var exe))
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            var path = Native.ProcessPath(pid);
            exe = path != null ? Path.GetFileName(path).ToLowerInvariant() : $"hwnd:{h}";
            // All UWP/Store apps run in ApplicationFrameHost: tell them apart by AppUserModelID.
            if (exe == "applicationframehost.exe" && AppId.Of(h) is { } aumid) exe = "app:" + aumid;
            // Script hosts (python, java, node, ...): each process is a different app.
            else if (WindowList.IsScriptHost(exe)) exe = $"{exe}:{pid}";
            _exeOf[h] = exe;
        }
        return exe;
    }

    void Refresh()
    {
        var now = DateTime.Now;
        DockTime.Text = now.ToString("HH:mm", English);
        DockDate.Text = now.ToString("ddd, d MMM", English);

        var fg = Native.GetForegroundWindow();
        if (fg != 0 && fg != _hwnd && WindowList.IsTaskWindow(fg)) _lastExternal = fg;

        var windows = WindowList.Get(); // z-order: most recently used first
        foreach (var dead in _exeOf.Keys.Except(windows).ToList()) _exeOf.Remove(dead);
        var groups = windows.GroupBy(ExeOf).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var p in _pinned)
            p.SetWindows(p.ExeName != null && groups.Remove(p.ExeName, out var list) ? list : []);

        // The rest: open apps that aren't pinned, one icon per app.
        for (int i = _running.Count - 1; i >= 0; i--)
            if (!groups.ContainsKey(_running[i].ExeName!)) _running.RemoveAt(i);
        foreach (var (exe, list) in groups)
        {
            var item = _running.FirstOrDefault(r => r.ExeName == exe);
            if (item == null) _running.Add(item = new DockItem { ExeName = exe, Icon = WindowList.Icon(list[0], exe) });
            item.SetWindows(list);
            var title = Native.WindowText(list[0]);
            item.Title = title.Length > 0 ? title : exe.Split(':')[0];
        }

        foreach (var item in _pinned.Concat(_running)) item.IsActive = item.Windows.Contains(_lastExternal);
        RunningSep.Visibility = _running.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Icon magnification (macOS style) ----------

    Rect BarRect() => new(Bar.TranslatePoint(new Point(0, 0), this), new Size(Bar.ActualWidth, Bar.ActualHeight));

    void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (_animating) return;
        _animating = true;
        CompositionTarget.Rendering += ZoomFrame;
    }

    /// <summary>
    /// Zoom zone = the card plus the space above it (magnified icons). Each icon's target size follows
    /// a Gaussian of its distance to the cursor; the current size moves 30% toward it per frame.
    /// The cursor is read directly because the transparent icon layer doesn't always get MouseMove.
    /// </summary>
    void ZoomFrame(object? sender, EventArgs e)
    {
        Native.GetCursorPos(out var c);
        var m = PointFromScreen(new Point(c.X, c.Y));
        var bar = BarRect();
        bool inZone = m.X >= bar.Left && m.X <= bar.Right && m.Y >= bar.Top - (MaxSize - BaseSize) && m.Y <= ActualHeight;
        bool zoom = inZone && Settings.Current.Magnify;

        bool moving = false;
        foreach (var (item, el) in Containers())
        {
            double target = BaseSize;
            if (zoom)
            {
                double d = el.TranslatePoint(new Point(el.ActualWidth / 2, 0), this).X - m.X;
                target = BaseSize + (MaxSize - BaseSize) * Math.Exp(-d * d / (2 * Sigma * Sigma));
            }
            double next = item.Size + (target - item.Size) * 0.3;
            if (Math.Abs(target - next) < 0.3) next = target; else moving = true;
            item.Size = next;
        }
        if (!moving && !inZone)
        {
            CompositionTarget.Rendering -= ZoomFrame;
            _animating = false;
        }
    }

    IEnumerable<(DockItem, FrameworkElement)> Containers()
    {
        foreach (var ic in new[] { Pinned, Running })
            for (int i = 0; i < ic.Items.Count; i++)
                if (ic.Items[i] is DockItem item && ic.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement el)
                    yield return (item, el);
    }

    // ---------- Clicks ----------

    void Item_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not DockItem item) return;
        var wins = item.Windows;

        if (wins.Count == 0)
        {
            if (item.LaunchPath != null) Process.Start(new ProcessStartInfo(item.LaunchPath) { UseShellExecute = true });
            return;
        }

        int i = wins.IndexOf(_lastExternal);
        if (i >= 0 && !Native.IsIconic(wins[i]))
        {
            if (wins.Count == 1) { Native.ShowWindow(wins[0], Native.SW_MINIMIZE); _lastExternal = 0; }
            else Activate(wins[(i + 1) % wins.Count]); // several windows: cycle to the next one
        }
        else Activate(wins[0]);
        Refresh();
    }

    void Activate(nint h)
    {
        if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
        Native.SetForegroundWindow(h);
        _lastExternal = h;
    }

    void CloseWindows_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DockItem item)
            foreach (var h in item.Windows) WindowList.Close(h);
    }

    void Start_Click(object sender, RoutedEventArgs e) => Native.PressWinKey();

    void TaskbarSettings_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });

    void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}

/// <summary>Which windows appear in the taskbar, mimicking the Windows taskbar rules.</summary>
static class WindowList
{
    static readonly uint Me = (uint)Environment.ProcessId;

    public static List<nint> Get()
    {
        var list = new List<nint>();
        Native.EnumWindows((h, _) => { if (IsTaskWindow(h)) list.Add(h); return true; }, 0);
        return list;
    }

    public static bool IsTaskWindow(nint h)
    {
        if (!Native.IsWindowVisible(h) || Native.IsCloaked(h)) return false;
        long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        bool appWindow = (ex & Native.WS_EX_APPWINDOW) != 0;
        if (!appWindow && ((ex & Native.WS_EX_TOOLWINDOW) != 0 || Native.GetWindow(h, 4 /*GW_OWNER*/) != 0)) return false;
        // Untitled windows still show if they set APPWINDOW (e.g. WinForms/Python apps), like the Windows taskbar.
        if (!appWindow && Native.WindowText(h).Length == 0) return false;
        Native.GetWindowThreadProcessId(h, out var pid);
        return pid != Me && Native.ClassName(h) is not ("Progman" or "WorkerW" or "Shell_TrayWnd");
    }

    public static ImageSource? Icon(nint h, string key)
    {
        // UWP/Store apps: the official icon from the Windows app list.
        if (key.StartsWith("app:") && Pixels.ShellIcon(@"shell:AppsFolder\" + key[4..]) is { } appIcon) return appIcon;
        // Script hosts: the host exe icon (e.g. Python) isn't the app's. Use the script project's .ico
        // (high resolution), falling back to the window icon (32 px).
        if (IsScriptHost(key.Split(':')[0]))
        {
            Native.GetWindowThreadProcessId(h, out var hostPid);
            if ((ProjectIcon(hostPid) is { } ico ? Pixels.ShellIcon(ico) : null) is { } sharp) return sharp;
            if (Native.WindowIcon(h) is { } own) return own;
        }
        // Otherwise prefer the high-res exe icon (sharp when magnified), falling back to the 32 px window icon.
        Native.GetWindowThreadProcessId(h, out var pid);
        return (Native.ProcessPath(pid) is { } exe ? Pixels.ShellIcon(exe) : null) ?? Native.WindowIcon(h);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint CommandLineToArgvW(string cmd, out int argc);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint mem);

    /// <summary>
    /// The .ico of the script run by a host process (e.g. <c>pythonw ghost_app.py</c> →
    /// <c>public\ghost.ico</c>). Searched up to 2 levels under the script folder; names matching
    /// the script or folder name win.
    /// </summary>
    static string? ProjectIcon(uint pid)
    {
        string? cmd = null;
        try
        {
            using var q = new System.Management.ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={pid}");
            foreach (var o in q.Get()) cmd = o["CommandLine"] as string;
        }
        catch (System.Management.ManagementException) { }
        if (string.IsNullOrEmpty(cmd)) return null;

        var argv = CommandLineToArgvW(cmd, out int argc);
        if (argv == 0) return null;
        var args = new List<string>();
        try { for (int i = 1; i < argc; i++) args.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!); }
        finally { LocalFree(argv); }

        var script = args.FirstOrDefault(File.Exists);
        var dir = script != null ? Path.GetDirectoryName(Path.GetFullPath(script)) : null;
        if (dir == null) return null;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
        var icons = Directory.EnumerateFiles(dir, "*.ico", opts).Take(50).ToList();
        string stem = Path.GetFileNameWithoutExtension(script!).Split('_', '-')[0], folder = Path.GetFileName(dir);
        return icons.FirstOrDefault(i => Path.GetFileNameWithoutExtension(i).Contains(stem, StringComparison.OrdinalIgnoreCase)
                                      || Path.GetFileNameWithoutExtension(i).Contains(folder, StringComparison.OrdinalIgnoreCase))
               ?? icons.FirstOrDefault();
    }

    static readonly HashSet<string> ScriptHosts =
        ["python.exe", "pythonw.exe", "java.exe", "javaw.exe", "node.exe", "mshta.exe", "rundll32.exe", "wscript.exe", "autohotkey64.exe", "autohotkey.exe"];
    public static bool IsScriptHost(string exe) => ScriptHosts.Contains(exe);

    public static void Close(nint h) => Native.PostMessage(h, 0x10 /*WM_CLOSE*/, 0, 0);
}

/// <summary>Reads .lnk targets via WScript.Shell (built-in Windows COM).</summary>
static class Shortcut
{
    /// <summary>Full path of the shortcut's target exe, or null if it doesn't point to an exe.</summary>
    public static string? Target(string lnkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            string target = shell.CreateShortcut(lnkPath).TargetPath;
            if (!string.IsNullOrEmpty(target)) return target;
            // shortcut: shell shortcuts without a TargetPath (PIDL) are only recognized for File Explorer;
            // use IShellLink.GetIDList if other shell shortcuts need support.
            return Path.GetFileNameWithoutExtension(lnkPath).Contains("Explorer", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") : null;
        }
        catch (COMException) { return null; }
    }
}
