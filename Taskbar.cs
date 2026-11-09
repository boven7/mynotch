using System.Runtime.InteropServices;

namespace MyNotch;

/// <summary>
/// Hides the Windows taskbar while MyNotch runs and restores it on exit.
/// Auto-hide frees the work area, but Explorer shows the taskbar again when the cursor
/// touches the bottom edge. So the taskbar also gets an empty window region (not drawn,
/// not clickable) and a WinEvent hook hides it again whenever it reappears.
/// The original auto-hide state is saved to a file, so it can be restored even after a crash:
/// run <c>MyNotch.exe --restore</c>.
/// </summary>
static class Taskbar
{
    delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern nint SetWinEventHook(uint min, uint max, nint hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(nint h, int cmd);
    [DllImport("user32.dll")] static extern int SetWindowRgn(nint h, nint rgn, bool redraw);
    [DllImport("gdi32.dll")] static extern nint CreateRectRgn(int x1, int y1, int x2, int y2);

    const uint EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_LOCATIONCHANGE = 0x800B, WINEVENT_OUTOFCONTEXT = 0;

    static readonly string StateFile = Path.Combine(AppDirs.Local, "taskbar-state.txt");

    static WinEventProc? _proc; // kept so the delegate isn't garbage-collected while the hook is active
    static nint _hook;
    static HashSet<nint> _bars = [];

    static List<nint> Bars()
    {
        var list = new List<nint>();
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != 0) list.Add(main);
        for (nint h = 0; (h = Native.FindWindowEx(0, h, "Shell_SecondaryTrayWnd", null)) != 0;) list.Add(h);
        return list;
    }

    static int GetState()
    {
        var d = Native.NewAppBarData();
        return (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d);
    }

    static void SetState(int state)
    {
        var d = Native.NewAppBarData();
        d.hWnd = Native.FindWindow("Shell_TrayWnd", null);
        d.lParam = state;
        Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d);
    }

    /// <summary>Called at startup and when explorer.exe restarts (TaskbarCreated message).</summary>
    public static void Hide()
    {
        // Never overwrite an existing file: it holds the original state from a session that crashed.
        if (!File.Exists(StateFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, GetState().ToString());
        }
        SetState(Native.ABS_AUTOHIDE);

        var bars = Bars();
        _bars = bars.ToHashSet();
        foreach (var h in bars)
        {
            SetWindowRgn(h, CreateRectRgn(0, 0, 0, 0), true); // the system owns the region after this
            Native.ShowWindow(h, Native.SW_HIDE);
        }

        if (_hook != 0) UnhookWinEvent(_hook); // new explorer = new PID
        if (bars.Count == 0) return;
        Native.GetWindowThreadProcessId(bars[0], out var explorerPid);
        _proc = OnWinEvent;
        _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_LOCATIONCHANGE, 0, _proc, explorerPid, 0, WINEVENT_OUTOFCONTEXT);
    }

    static void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == 0 /*OBJID_WINDOW*/ && _bars.Contains(hwnd) && Native.IsWindowVisible(hwnd))
            ShowWindowAsync(hwnd, Native.SW_HIDE);
    }

    /// <summary>Idempotent: safe to call repeatedly (OnExit, ProcessExit, crash handlers, --restore).</summary>
    public static void Restore()
    {
        if (_hook != 0) { UnhookWinEvent(_hook); _hook = 0; }
        _bars = [];
        if (File.Exists(StateFile) && int.TryParse(File.ReadAllText(StateFile), out var original))
        {
            SetState(original);
            File.Delete(StateFile);
        }
        foreach (var h in Bars())
        {
            SetWindowRgn(h, 0, true); // remove the empty region
            Native.ShowWindow(h, Native.SW_SHOWNA);
        }
    }
}
