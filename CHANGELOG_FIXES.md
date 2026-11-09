# CHANGELOG_FIXES — MyNotch

MyNotch was previously called NotchKu; entries before the rename use paths such as %LOCALAPPDATA%\NotchKu.

Fix log, newest first. Before fixing a bug, search first with `grep -n -i "<symptom>" CHANGELOG_FIXES.md`.

### Fix #12 — Dock moves up 48 px when Explorer opens: taskbar auto-hide turned off by another MyNotch process

| | |
|---|---|
| Date | 2026-11-06 |
| File | `App.xaml.cs` (OnExit) |
| Problem | After opening an Explorer window, the dock is pushed up by the taskbar height (bottom 1032, should be 1080) |
| Root cause | `OnExit` called `Taskbar.Restore()` in EVERY MyNotch process that exits: CLI commands (`--window-accent`, `--accent`), the elevated `--system-icons-machine` process, and a second instance that loses the mutex. Restore turns auto-hide off (state 1→0) and deletes the main instance's `taskbar-state.txt` → the taskbar reserves 48 px → the next `ABN_POSCHANGED` (e.g. opening Explorer) makes `ABM_QUERYPOS` place the dock above it. Made worse by Fix #11 (the elevated process can now run at startup) and triggered directly by the CLI tests in Fix #11 |
| Fix | `Taskbar.Restore()` in `OnExit` only for the main instance (`_running`) |
| Verification | Before: taskbar state 0, work area bottom 970, dock bottom 1032, `taskbar-state.txt` gone. After: following CLI `--window-accent off`, a second instance via Start, and opening Explorer at `C:\` → state stays 1, dock bottom 1080, state file still present, only 1 MyNotch process |
| Lesson | "Cleanup on exit" code in `OnExit` must be limited to the main instance; every CLI/elevated/second-instance mode also passes through `OnExit` |
| Log keywords | dock moves up, dock pushed up, auto-hide lost, ABN_POSCHANGED, ABM_QUERYPOS, Taskbar.Restore, OnExit, second instance |
| Deploy | Local (Debug build), app restarted |

### Fix #11 — Turning MyNotch off does not restore Explorer (icons & theme keep the MyNotch style)

| | |
|---|---|
| Date | 2026-11-06 |
| File | `SystemIconInstaller.cs` (Set/Switch/RunTask/RegisterTask/RegFile, RegBackup), `SystemControls.cs` (SystemTheme), `App.xaml.cs`, `SelfTest.cs` |
| Problem | System icons & theme (dark mode, accent, transparency) were written permanently to the registry; closing MyNotch only restored the taskbar |
| Root cause | No apply/revert cycle tied to the process lifetime. HKLM icons need admin → cannot be reverted automatically without UAC |
| Fix | Icons: on first install (1× UAC) create `icons-on.reg`/`icons-off.reg` in Program Files + 2 scheduled tasks (`NotchKu\IconsOn/Off`, S4U, HighestAvailable, action `reg.exe import`). OnExit → `Switch(false)`, OnStartup → `Switch(true)` (fallback `Set(true)` + UAC if the task does not exist yet). Theme: all writes go through `RegBackup` (`theme-backup.json`); OnExit `Snapshot`s the MyNotch choices → `theme-applied.json` then `Restore`; OnStartup `Reapply`. `RegBackup` now supports DWORD & binary (base64) |
| Verification | Self-test exit 0 (+ tests for negative DWORD, binary, snapshot/reapply, .reg format). Real: Off/On tasks run from a non-admin process → HKLM `Shell Icons\3` removed/restored without UAC. E2E: title bar accent 0→1 via MyNotch; MyNotch OFF → 0, HKLM empty, This PC = `imageres.dll,-109`; ON → 1 + MyNotch icons back within ±3 s |
| Lesson | (1) The self-test immediately caught that the `Entry.Value` property was serialized to JSON → crash; derived properties on records must be `[JsonIgnore]`. (2) An elevated task must run a file only admins can write (not an exe in a user folder) — otherwise it is a privilege-escalation hole. (3) Task XML defaults to `DisallowStartIfOnBatteries=true` → must be false on laptops |
| Log keywords | Explorer not restored, system icons, theme, schtasks, IconsOn, IconsOff, RegBackup, theme-backup, OnExit |
| Deploy | Local (Debug build), tasks registered, app running |

### Fix #10 — Bluetooth chip toggles on/off directly, cannot pick a device like Wi-Fi

| | |
|---|---|
| Date | 2026-11-06 |
| File | `NotchWindow.Bluetooth.cs` (new), `NotchWindow.xaml`, `NotchWindow.xaml.cs` |
| Problem | Clicking the Bluetooth chip = radio switch (`SetRadio`), whereas the Wi-Fi chip opens a network list panel |
| Root cause | Bluetooth had no panel yet; `Bt_Click` called `Radio.SetStateAsync` directly |
| Fix | The chip opens `BtPanel`: power switch + list of paired devices (classic + LE, via AEP `IsConnected`/`ContainerId`). Clicking an audio device = `IOCTL_KS_PROPERTY` `KSPROPSETID_BtAudio` ONESHOT_RECONNECT(0)/DISCONNECT(1) to the KS audio filter with the same `ContainerId`. Non-audio devices & "+" open `ms-settings:bluetooth` |
| Verification | Radio turned on temporarily: 3 devices (65" QLED, BT Speaker, realme C51) found, each with 2 filters (A2DP + HFP). IOCTL accepted (`ok=True`) for connect & disconnect of BT Speaker. **PENDING verification**: status stays `conn=False` (speaker probably off/out of range), so a real connection is not yet proven. Self-test exit 0 |
| Lesson | `HFP` (BTHHFENUM) does not include the MAC in its path; match via `ContainerId`, not MAC. Never `.GetAwaiter().GetResult()` on WinRT async in `OnStartup` (UI thread) — deadlock; wrap in `Task.Run` |
| Log keywords | bluetooth, BtPanel, KSPROPSETID_BtAudio, ONESHOT_RECONNECT, ContainerId, deadlock OnStartup |
| Deploy | Local (Debug build), app restarted |

### Fix #9 — System icon installer would delete built-in Windows registry values on removal (caught before it ran)

| | |
|---|---|
| Date | 2026-11-06 |
| File | `SystemIconInstaller.cs`, `SelfTest.cs` |
| Problem | The first version removed icons with `DeleteSubKeyTree` on `HKCU\...\Explorer\CLSID\{This PC/Network/Recycle Bin}`. Those keys turned out to ALREADY EXIST with built-in Windows values (`imageres.dll,-109/-25/-55/-54`, REG_EXPAND_SZ) → they would have been deleted too |
| Root cause | The initial check was wrong: registry paths in a bash loop were double-escaped (`\\{clsid}`) so `reg query` reported "not found" |
| Fix | `RegBackup`: before writing, save each original value (exists or not, kind, unexpanded data) to JSON; on removal, restore it exactly, then delete only keys that are empty. Used for HKCU and HKLM |
| Verification | Self-test: REG_EXPAND_SZ value `%SystemRoot%\a.dll,-1` comes back with the same kind & data, created subkeys deleted, backup file cleaned up (exit 0). Rechecking with PowerShell `Test-Path` found the 3 keys previously reported as missing |
| Lesson | Never "remove = delete key"; always back up the original values. Verify registry key existence with PowerShell, not `reg query` inside a bash loop |
| Log keywords | registry, DefaultIcon, Recycle Bin, backup, restore, system icons |
| Deploy | Local (Debug build), never ran with the old version |

### Fix #8 — GHOST icon pixelated when the dock zooms

| | |
|---|---|
| Date | 2026-11-06 |
| File | `DockWindow.xaml.cs` (WindowList.Icon, ProjectIcon) |
| Problem | After Fix #7, the GHOST icon came from `WM_GETICON` (32 px) → pixelated at 74 px |
| Root cause | Windows only provides a 32 px window icon; the host exe's icon (pythonw) is not the app's icon |
| Fix | Script hosts: read the command line (WMI `Win32_Process`, `CommandLineToArgvW`), look for an `.ico` in the script folder up to 2 levels (names similar to the script/folder first), render at 128 px; window icon as fallback |
| Verification | `pythonw app.py` → `<project>\public\app.ico` (16–256 px); 74 px zoom sharp, text on the icon readable |
| Lesson | Follow-up to Fix #7: always check icon sharpness at the maximum zoom size, not only the base size |
| Log keywords | pixelated icon, GHOST, pythonw, ico, script host |
| Deploy | Local (Debug build) |

### Fix #7 — GHOST app (pythonw/WinForms) open but not shown in the dock

| | |
|---|---|
| Date | 2026-11-06 |
| File | `DockWindow.xaml.cs` (WindowList.IsTaskWindow, ExeOf, Icon) |
| Problem | The GHOST window (`pythonw.exe` pid 11932, class `WindowsForms10.Window…`, ex 0xD0100, empty title, minimized) did not show |
| Root cause | The dock filter rejected windows without a title; the Windows taskbar does not require a title. Also: all Python apps would be merged under the `pythonw.exe` key with the Python icon |
| Fix | Untitled windows are accepted if `WS_EX_APPWINDOW`; script hosts (python/java/node/mshta/rundll32/…) are keyed per process (`exe:pid`) and use their window icon; the tooltip uses the exe name when the title is empty |
| Verification | Dock screenshot ("In screenshots" mode temporarily on): GHOST icon shown after Terminal |
| Lesson | Mirror the Windows taskbar rules exactly; do not add requirements (title) the taskbar does not use |
| Log keywords | GHOST, pythonw, not shown, empty title, APPWINDOW |
| Deploy | Local (Debug build) |

### Fix #6 — UWP/Store apps shown wrongly in the dock: all merged as ApplicationFrameHost

| | |
|---|---|
| Date | 2026-11-06 |
| File | `DockWindow.xaml.cs` (ExeOf, WindowList.Icon), `Native.cs` (AppId) |
| Problem | Store apps (Settings, Calculator, etc.) shared one `applicationframehost.exe` key, so they all became one generic icon |
| Root cause | UWP windows are owned by the `ApplicationFrameHost.exe` process, not the app's own process |
| Fix | For ApplicationFrameHost windows, key = AppUserModelID (`SHGetPropertyStoreForWindow`, PKEY_AppUserModel_ID); icon from `shell:AppsFolder\<AUMID>` |
| Verification | Calculator opened → dock shows the real Calculator icon in the open-apps section (screenshot d2c). Settings with `cloaked=2` (closed/suspended) still not shown, same as the Windows taskbar |
| Lesson | `cloaked` windows must be filtered; do not "fix" a missing app without checking the DWMWA_CLOAKED attribute first |
| Log keywords | ApplicationFrameHost, AUMID, cloaked, UWP |
| Deploy | Local (Debug build) |

### Fix #5 — Dock icons pixelated when magnified, and Laragon/UltraViewer shown inside a box

| | |
|---|---|
| Date | 2026-11-06 |
| File | `Native.cs` (Pixels.ShellIcon), `DockWindow.xaml.cs` |
| Problem | (a) 32 px icons scaled up to 74 px → pixelated. (b) After switching to `IShellItemImageFactory` at 128 px, exes that only have small icons (Laragon, UltraViewer) were framed in a box by the shell |
| Root cause | (a) `SHGetFileInfo` only returns 32 px icons. (b) The shell wraps small icons in a "jumbo" frame |
| Fix | Exe: `PrivateExtractIcons(path, 0, 128, 128)` (scaled directly, no frame); non-exe: `IShellItemImageFactory` + `SIIGBF_ICONONLY` |
| Verification | Dock snapshot: Chrome/HeidiSQL/Burp sharp at 74 px; Laragon & UltraViewer without a box |
| Lesson | A high-resolution icon from the shell ≠ the real icon; for exes, take it from the resources |
| Log keywords | pixelated icon, icon in box, PrivateExtractIcons, SIIGBF_ICONONLY |
| Deploy | Local (Debug build) |

### Fix #4 — Dock card keeps growing & dock content not centered

| | |
|---|---|
| Date | 2026-11-06 |
| File | `Backdrop.cs`, `DockWindow.xaml`, `NotchWindow.xaml` |
| Problem | The dock card grew up to 1920 px; icons pushed to the left |
| Root cause | The backdrop `Image` (screen capture of Pad + card width) took part in layout sizing → each capture made the card +64 px → feedback loop |
| Fix | Backdrop image placed in a `Canvas` (does not contribute size), Width/Height set explicitly |
| Verification | Snapshot: card 770 px, centered at x=960 (1920 screen) |
| Lesson | Decorative content whose size is derived from its host must not take part in the host's layout |
| Log keywords | dock growing, not centered, backdrop layout |
| Deploy | Local (Debug build) |

### Fix #3 — Acrylic rectangle visible around the notch, faint band above the dock

| | |
|---|---|
| Date | 2026-11-06 |
| File | `Backdrop.cs` (new), `Native.cs` (Pixels.CaptureScreen), notch & dock; `Glass.cs` and `DockCard.cs` removed |
| Problem | DWM blur showed as a rectangle the size of the window (480×240 on the notch, 1920×96 on the dock), not pill-shaped |
| Root cause | DWM effects (`SetWindowCompositionAttribute` acrylic & `DWMWA_SYSTEMBACKDROP_TYPE`) always fill the whole window rectangle; the window region does NOT clip them. Proven on Windows 11 build 26200 through 5 experiments: region (rgnType=3 applied correctly), re-applying the effect after the region, `DwmEnableBlurBehindWindow(hRgnBlur)` (broke hover instead), layered window, and system backdrop (turned solid grey + frame) |
| Fix | Same technique as Poni (REA: `SetWindowDisplayAffinity` + `BitBlt` in poni.exe): layered window excluded from capture (`WDA_EXCLUDEFROMCAPTURE`), the screen behind captured at ~15 fps with BitBlt, blurred with WPF `BlurEffect`, clipped with a rounded `Clip` (anti-aliased) |
| Verification | `RenderTargetBitmap` render of the notch (200×34 & 480×240) and dock: clean pill/card shape, no rectangle/halo |
| Lesson | Do not use DWM effects for non-rectangular shapes. Consequence of this technique: the notch & dock are not recorded in screenshots → "In screenshots" option (blur becomes transparent while enabled) |
| Log keywords | acrylic, transparent rectangle, halo, blur, SetWindowDisplayAffinity |
| Deploy | Local (Debug build) |

### Fix #2 — Original taskbar reappears when the cursor touches the bottom edge (overlapping the dock)

| | |
|---|---|
| Date | 2026-11-06 |
| File | `Taskbar.cs` |
| Problem | The Windows taskbar hidden with `SW_HIDE` reappeared over the dock when the cursor was on the bottom pixel row |
| Root cause | Auto-hide mode (used to free the work area) shows the taskbar when the cursor touches the edge |
| Fix | The taskbar gets an empty region (cross-process `SetWindowRgn`: not drawn, not clickable) + `SetWinEventHook` (SHOW..LOCATIONCHANGE, explorer process) that hides it again; both are restored in `Restore()` |
| Verification | Cursor held at (960,1079) for 1.5 s → screenshot shows only the dock, taskbar does not appear |
| Lesson | Hiding a window owned by another process is not enough; Explorer will show it again |
| Log keywords | taskbar reappears, auto-hide, overlap, Shell_TrayWnd |
| Deploy | Local (Debug build) |

### Fix #1 — Derived property `TintAlpha` saved to settings.json

| | |
|---|---|
| Date | 2026-11-06 |
| File | `Settings.cs` |
| Problem | settings.json contained `"TintAlpha": 188` (a derived value, not a setting) |
| Root cause | Public read-only properties are serialized by System.Text.Json |
| Fix | `[JsonIgnore]` on `TintAlpha` and `UseBlur` |
| Verification | After saving again, settings.json only contains Style/Opacity/Floating/Magnify/ShowInScreenshots |
| Lesson | Mark derived properties with `[JsonIgnore]` from the start |
| Log keywords | settings.json, TintAlpha, JsonIgnore |
| Deploy | Local (Debug build) |
