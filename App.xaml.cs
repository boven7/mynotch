using System.Windows;
using System.Windows.Media;

namespace MyNotch;

public partial class App : Application
{
    Mutex? _singleInstance;
    bool _running, _sessionEnding;

    /// <summary>Raised by the dock when a fullscreen app (game/video) appears or goes away.</summary>
    public static event Action<bool>? FullscreenChanged;
    public static void RaiseFullscreen(bool on) => FullscreenChanged?.Invoke(on);

    public static bool Dark { get; private set; } = true;
    /// <summary>Raised when dark/light mode, the accent color or display settings change.</summary>
    public static event Action? LookChanged;

    readonly System.Windows.Threading.DispatcherTimer _themeWatch = new() { Interval = TimeSpan.FromMilliseconds(700) };
    Color _accent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--restore")) { Taskbar.Restore(); Shutdown(); return; }
        if (e.Args.Contains("--selftest")) { Shutdown(SelfTest.Run()); return; }
        // Theme settings from the command line (scripts/automation): --accent RRGGBB, --window-accent on|off
        if (Arg(e.Args, "--accent") is { } hex)
        {
            try { SystemTheme.SetAccent((Color)ColorConverter.ConvertFromString("#" + hex.TrimStart('#'))); Shutdown(); }
            catch (FormatException) { Shutdown(2); } // not a valid hex color
            return;
        }
        if (Arg(e.Args, "--export-icons") is { } iconDir) // writes the system icon set (.ico) + preview.png
        {
            SystemIcons.Export(iconDir);
            Shutdown(); return;
        }
        if (Arg(e.Args, "--system-icons-machine") is { } machine) // run elevated by SystemIconInstaller
        {
            Shutdown(SystemIconInstaller.ApplyMachine(machine == "on"));
            return;
        }
        if (Arg(e.Args, "--window-accent") is { } onOff)
        {
            SystemTheme.AccentOnWindows = onOff == "on";
            Shutdown(); return;
        }

        _singleInstance = new Mutex(true, "MyNotch.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        // The taskbar must come back however the app ends (except a forced kill).
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Taskbar.Restore();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Taskbar.Restore();
        DispatcherUnhandledException += (_, _) => Taskbar.Restore();
        SessionEnding += (_, _) => { _sessionEnding = true; Taskbar.Restore(); };

        // MyNotch on: reapply the chosen theme and Explorer icons (reverted to the originals on exit, see OnExit).
        _running = true;
        SystemTheme.Reapply();
        // Without the admin task (older install), Set asks for it once via UAC.
        if (Settings.Current.CustomIcons) Task.Run(() => SystemIconInstaller.Switch(true) || SystemIconInstaller.Set(true));

        ApplyTheme(SystemTheme.IsDark);
        _accent = SystemTheme.Accent;
        RefreshAccent();
        Settings.Changed += () => LookChanged?.Invoke();
        // Poll the registry: catches changes made by MyNotch and by Windows Settings.
        _themeWatch.Tick += (_, _) =>
        {
            if (SystemTheme.IsDark != Dark) ApplyTheme(!Dark);
            if (SystemTheme.Accent != _accent) { _accent = SystemTheme.Accent; RefreshAccent(); }
        };
        _themeWatch.Start();

        new NotchWindow().Show();
        new DockWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Main instance only: CLI, elevated or second-instance processes exiting must not turn off
        // the running instance's taskbar auto-hide (that pushes the dock up, see Fix #12).
        if (_running)
        {
            Taskbar.Restore();
            // MyNotch off = Explorer back to normal. On logoff/shutdown Explorer is not restarted.
            SystemTheme.Revert();
            if (Settings.Current.CustomIcons) SystemIconInstaller.Switch(false, restartExplorer: !_sessionEnding);
        }
        base.OnExit(e);
    }

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Swaps notch &amp; dock brushes for dark/light mode, then asks windows to redraw their backgrounds.</summary>
    static void ApplyTheme(bool dark)
    {
        Dark = dark;
        var r = Current.Resources;
        static SolidColorBrush B(long argb) => new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        r["IslandFg"] = B(dark ? 0xFFFFFFFF : 0xFF1C1C1C);
        r["IslandFgDim"] = B(dark ? 0xB0FFFFFF : 0x99000000);
        r["IslandSoft"] = B(dark ? 0x26FFFFFF : 0x14000000);
        r["IslandTrack"] = B(dark ? 0x40FFFFFF : 0x26000000);
        r["DockFg"] = B(dark ? 0xFFFFFFFF : 0xFF1C1C1C);
        r["DockHover"] = B(dark ? 0x22FFFFFF : 0x14000000);
        LookChanged?.Invoke();
    }

    public static void RefreshAccent() => Current.Resources["AccentBrush"] = new SolidColorBrush(SystemTheme.Accent);
}
