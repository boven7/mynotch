using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace MyNotch;

/// <summary>
/// Installs/removes the MyNotch system icon set in Explorer.
/// Machine (admin, via <c>MyNotch.exe --system-icons-machine on|off</c>): copies icons to Program Files,
/// HKLM "Shell Icons" (folder, drive, generic file, Recycle Bin, ...) and the system drive icon.
/// Per user: shell locations (This PC, Home, Gallery, ...), known folders (registry + desktop.ini)
/// and common file types (ProgID DefaultIcon in HKCU).
/// Every original registry value is backed up (name, kind, data) before being overwritten and restored exactly.
/// </summary>
static class SystemIconInstaller
{
    // Moving Program Files needs admin and Explorer's registry points into it, so an install from before the
    // rename keeps its NotchKu folder and task names until the icons are turned off (which deletes them).
    static readonly string MachineName = MachineNameFor(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
    static readonly string ProgramDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), MachineName);
    public static readonly string MachineDir = Path.Combine(ProgramDir, "icons");
    static readonly string MachineBackup = Path.Combine(ProgramDir, "machine-backup.json");
    static readonly string LocalDir = AppDirs.Local;

    public static string MachineNameFor(string programFiles) =>
        Directory.Exists(Path.Combine(programFiles, AppDirs.LegacyName)) && !Directory.Exists(Path.Combine(programFiles, AppDirs.Name))
            ? AppDirs.LegacyName : AppDirs.Name;
    static readonly string UserIcons = Path.Combine(LocalDir, "icons");
    static readonly string BackupDir = Path.Combine(LocalDir, "icon-backup");
    static readonly string UserBackup = Path.Combine(BackupDir, "registry.json");

    static string Ico(string name) => Path.Combine(MachineDir, name + ".ico") + ",0";

    // ---------- Mapping ----------

    const string ShellIcons = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
    // "Shell Icons" indexes still used by Windows 10/11
    static readonly (string Index, string Icon)[] ShellIconIndexes =
    [
        ("0", "file"), ("1", "file"), ("2", "app"), ("3", "folder"), ("4", "folder"),
        ("7", "drive-usb"), ("8", "drive"), ("9", "drive-network"), ("10", "drive-network"),
        ("11", "disc"), ("12", "drive"), ("15", "computer"), ("17", "network"),
        ("31", "trash-empty"), ("32", "trash-full"), ("34", "desktop"),
    ];

    const string UserClsid = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID";
    const string ClassesClsid = @"Software\Classes\CLSID";
    const string RecycleBin = "{645FF040-5081-101B-9F08-00AA002F954E}";

    // Official per-user override for desktop objects (This PC, Network, Recycle Bin)
    static readonly (string Clsid, string Icon)[] DesktopObjects =
    [
        ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "computer"),
        ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "network"),
    ];

    // Other shell locations: HKCU\Software\Classes\CLSID overrides HKCR for this user
    static readonly (string Clsid, string Icon)[] ShellLocations =
    [
        ("{f874310e-b6b7-47dc-bc84-b9e6b38f5903}", "home"),
        ("{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}", "gallery"),
        ("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", "user"),
        ("{679f85cb-0220-4080-b29b-5540cc05aab6}", "quickaccess"),
        ("{031E4825-7B94-4dc3-B131-E946B44C8DD5}", "libraries"),
        ("{26EE0668-A00A-44D7-9371-BEB064C98683}", "controlpanel"),
    ];

    // Known folders: icon in "This PC" (CLSID) and in the sidebar (desktop.ini)
    static readonly (string Clsid, Func<string> Path, string Icon)[] KnownFolders =
    [
        ("{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}", () => Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "desktop"),
        ("{d3162b92-9365-467a-956b-92703aca08af}", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "documents"),
        ("{088e3905-0323-4b02-9826-5d99428e115f}", DownloadsPath, "downloads"),
        ("{3dfdf296-dbec-4fb4-81d1-6a3438bcf4de}", () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "music"),
        ("{24ad3ad4-a569-4530-98e1-ab02f9417aa8}", () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "pictures"),
        ("{f86fa3ab-70d2-4fc7-9c99-fcbf05467f3a}", () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "videos"),
    ];

    // Common file types. Code files keep their editor's icon.
    static readonly (string Ext, string Icon)[] FileTypes =
    [
        (".txt", "file-text"), (".log", "file-text"), (".md", "file-text"),
        (".png", "file-image"), (".jpg", "file-image"), (".jpeg", "file-image"), (".gif", "file-image"), (".bmp", "file-image"), (".webp", "file-image"),
        (".mp3", "file-audio"), (".wav", "file-audio"), (".flac", "file-audio"), (".m4a", "file-audio"),
        (".mp4", "file-video"), (".mkv", "file-video"), (".avi", "file-video"), (".mov", "file-video"),
        (".zip", "file-archive"), (".rar", "file-archive"), (".7z", "file-archive"),
        (".pdf", "file-pdf"),
    ];

    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, nint a, nint b);

    static string DownloadsPath()
    {
        SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, 0, out var p);
        try { return Marshal.PtrToStringUni(p)!; } finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>ProgID that determines this extension's icon for the current user.</summary>
    static string? ProgIdOf(string ext)
    {
        using var choice = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
        var id = choice?.GetValue("ProgId") as string;
        if (string.IsNullOrEmpty(id)) { using var cls = Registry.ClassesRoot.OpenSubKey(ext); id = cls?.GetValue("") as string; }
        return string.IsNullOrEmpty(id) ? null : id;
    }

    // ---------- Full flow ----------

    // Admin tasks are registered once (one UAC prompt) so MyNotch on/off can apply/remove icons without prompting.
    // Their only action is `reg import` of a file in Program Files (admin-writable only), so a user may run them safely.
    static readonly string TaskOn = MachineName + @"\IconsOn", TaskOff = MachineName + @"\IconsOff";
    static readonly string RegOn = Path.Combine(ProgramDir, "icons-on.reg");
    static readonly string RegOff = Path.Combine(ProgramDir, "icons-off.reg");

    /// <summary>Settings toggle: apply (no UAC if the task exists) / remove completely incl. tasks (UAC). false if UAC is declined.</summary>
    public static bool Set(bool on)
    {
        if (on) SystemIcons.Export(UserIcons);
        if (!(on && RunTask(true)) && !RunElevated($"--system-icons-machine {(on ? "on" : "off")}")) return false;
        ApplyUser(on);
        Refresh();
        return true;
    }

    /// <summary>
    /// MyNotch on/off: apply/restore icons through the admin task, without UAC.
    /// false if the task is not registered (older install) or fails.
    /// </summary>
    public static bool Switch(bool on, bool restartExplorer = true)
    {
        if (!RunTask(on)) return false;
        ApplyUser(on);
        if (restartExplorer) Refresh();
        return true;
    }

    static bool RunTask(bool on)
    {
        if (RunQuiet("schtasks.exe", $"/run /tn \"{(on ? TaskOn : TaskOff)}\"") != 0) return false;
        // schtasks /run does not wait for the task: wait until HKLM has actually changed (max 10 s).
        for (int i = 0; i < 50; i++)
        {
            if (MachineApplied() == on) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    static bool MachineApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(ShellIcons);
        return k?.GetValue("3") as string == Ico("folder");
    }

    static int RunQuiet(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (!p!.WaitForExit(15000)) return -1;
            return p.ExitCode;
        }
        catch (Win32Exception) { return -1; }
    }

    static bool RunElevated(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true, Verb = "runas" });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; } // UAC cancelled
    }

    // ---------- Machine part (elevated process) ----------

    public static int ApplyMachine(bool on)
    {
        if (on)
        {
            Directory.CreateDirectory(MachineDir);
            foreach (var f in Directory.GetFiles(UserIcons, "*.ico")) File.Copy(f, Path.Combine(MachineDir, Path.GetFileName(f)), true);
            var writes = ShellIconIndexes.Select(x => (Key: ShellIcons, Name: x.Index, Value: (object)Ico(x.Icon))).ToList();
            var sysDrive = Path.GetPathRoot(Environment.SystemDirectory)![0];
            writes.Add(($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{sysDrive}\DefaultIcon", "", Ico("drive-system")));
            RegBackup.Write(Registry.LocalMachine, MachineBackup, writes);

            // .reg pair for the tasks: "on" = MyNotch values, "off" = original values from the backup.
            File.WriteAllText(RegOn, RegFile(writes.Select(w => (w.Key, w.Name, (RegBackup.Entry?)RegBackup.Entry.Of(w.Value)))), Encoding.Unicode);
            File.WriteAllText(RegOff, RegFile(RegBackup.Load(MachineBackup).Select(e => (e.Key, e.Name, e.Existed ? e : null))), Encoding.Unicode);
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            if (!RegisterTask(TaskOn, RegOn, user) || !RegisterTask(TaskOff, RegOff, user)) return 3;
        }
        else
        {
            RegBackup.Restore(Registry.LocalMachine, MachineBackup);
            RunQuiet("schtasks.exe", $"/delete /tn \"{TaskOn}\" /f");
            RunQuiet("schtasks.exe", $"/delete /tn \"{TaskOff}\" /f");
            if (Directory.Exists(ProgramDir)) Directory.Delete(ProgramDir, true); // icons + .reg files
        }
        return 0;
    }

    /// <summary>Trigger-less task run on demand as this user with admin rights (S4U: no console window).</summary>
    static bool RegisterTask(string name, string regFile, string userSid)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Principals><Principal id="Author"><UserId>{userSid}</UserId><LogonType>S4U</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author"><Exec><Command>{Path.Combine(Environment.SystemDirectory, "reg.exe")}</Command><Arguments>import "{regFile}"</Arguments></Exec></Actions>
            </Task>
            """;
        var tmp = Path.Combine(Path.GetTempPath(), "mynotch-task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        try { return RunQuiet("schtasks.exe", $"/create /tn \"{name}\" /xml \"{tmp}\" /f") == 0; }
        finally { File.Delete(tmp); }
    }

    /// <summary>
    /// .reg file content for HKLM; Value null = delete the value.
    /// shortcut: keys created by MyNotch are left empty on "off" (no effect on Explorer); delete them if that ever matters.
    /// </summary>
    public static string RegFile(IEnumerable<(string Key, string Name, RegBackup.Entry? Value)> values)
    {
        static string Q(string s) => "\"" + s.Replace(@"\", @"\\").Replace("\"", "\\\"") + "\"";
        var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        foreach (var g in values.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append($"\r\n[HKEY_LOCAL_MACHINE\\{g.Key}]\r\n");
            foreach (var (_, name, v) in g)
            {
                sb.Append(name == "" ? "@" : Q(name)).Append('=');
                sb.Append(v switch
                {
                    null => "-",
                    { Kind: "String" } => Q(v.Data!),
                    { Kind: "ExpandString" } => "hex(2):" + string.Join(",", Encoding.Unicode.GetBytes(v.Data + "\0").Select(b => b.ToString("x2"))),
                    { Kind: "DWord" } => "dword:" + unchecked((uint)int.Parse(v.Data!)).ToString("x8"),
                    _ => throw new NotSupportedException("Unsupported registry value kind: " + v.Kind),
                }).Append("\r\n");
            }
        }
        return sb.ToString();
    }

    // ---------- Per-user part ----------

    static void ApplyUser(bool on)
    {
        if (!on)
        {
            RegBackup.Restore(Registry.CurrentUser, UserBackup);
            foreach (var (_, path, icon) in KnownFolders) SetFolderIcon(path(), icon, false);
            return;
        }

        var writes = new List<(string Key, string Name, object Value)>();
        foreach (var (clsid, icon) in DesktopObjects) writes.Add(($@"{UserClsid}\{clsid}\DefaultIcon", "", Ico(icon)));
        writes.Add(($@"{UserClsid}\{RecycleBin}\DefaultIcon", "", Ico("trash-empty")));
        writes.Add(($@"{UserClsid}\{RecycleBin}\DefaultIcon", "empty", Ico("trash-empty")));
        writes.Add(($@"{UserClsid}\{RecycleBin}\DefaultIcon", "full", Ico("trash-full")));
        foreach (var (clsid, icon) in ShellLocations) writes.Add(($@"{ClassesClsid}\{clsid}\DefaultIcon", "", Ico(icon)));
        foreach (var (clsid, _, icon) in KnownFolders) writes.Add(($@"{ClassesClsid}\{clsid}\DefaultIcon", "", Ico(icon)));
        foreach (var (ext, icon) in FileTypes)
            if (ProgIdOf(ext) is { } progId && !writes.Any(w => w.Key == $@"Software\Classes\{progId}\DefaultIcon"))
                writes.Add(($@"Software\Classes\{progId}\DefaultIcon", "", Ico(icon)));
        RegBackup.Write(Registry.CurrentUser, UserBackup, writes);

        foreach (var (_, path, icon) in KnownFolders) SetFolderIcon(path(), icon, true);
    }

    /// <summary>
    /// Sets IconResource in the folder's desktop.ini (icon in the sidebar/Quick access).
    /// Backup: original desktop.ini content (or its absence) + original folder attributes.
    /// </summary>
    static void SetFolderIcon(string folder, string icon, bool on)
    {
        if (!Directory.Exists(folder)) return;
        var ini = Path.Combine(folder, "desktop.ini");
        var backupIni = Path.Combine(BackupDir, icon + ".ini");
        var backupAttr = Path.Combine(BackupDir, icon + ".attr");
        Directory.CreateDirectory(BackupDir);

        if (on)
        {
            if (!File.Exists(backupAttr)) // back up only once: keep the original state
            {
                File.WriteAllText(backupAttr, ((int)new DirectoryInfo(folder).Attributes).ToString());
                if (File.Exists(ini)) File.Copy(ini, backupIni, true);
            }
            var lines = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : ["[.ShellClassInfo]"];
            lines.RemoveAll(l => l.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase));
            int section = lines.FindIndex(l => l.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase));
            if (section < 0) { lines.Insert(0, "[.ShellClassInfo]"); section = 0; }
            lines.Insert(section + 1, "IconResource=" + Ico(icon));
            WriteIni(ini, lines);
            new DirectoryInfo(folder).Attributes |= FileAttributes.ReadOnly; // Windows only reads desktop.ini of read-only folders
        }
        else if (File.Exists(backupAttr))
        {
            if (File.Exists(backupIni)) WriteIni(ini, File.ReadAllLines(backupIni).ToList());
            else if (File.Exists(ini)) { File.SetAttributes(ini, FileAttributes.Normal); File.Delete(ini); }
            new DirectoryInfo(folder).Attributes = (FileAttributes)int.Parse(File.ReadAllText(backupAttr));
            File.Delete(backupAttr);
            if (File.Exists(backupIni)) File.Delete(backupIni);
        }
    }

    static void WriteIni(string ini, List<string> lines)
    {
        if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
        File.WriteAllLines(ini, lines, Encoding.Unicode); // Windows desktop.ini: UTF-16 LE + BOM
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
    }

    /// <summary>Refreshes the icon cache and restarts Explorer (MyNotch hides the taskbar again on TaskbarCreated).</summary>
    static void Refresh()
    {
        try { Process.Start(new ProcessStartInfo("ie4uinit.exe", "-show") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000); }
        catch (Win32Exception) { }
        SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0, 0, 0);
        foreach (var p in Process.GetProcessesByName("explorer"))
            try { p.Kill(); } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        Thread.Sleep(1500);
        if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
    }
}

/// <summary>
/// Writes registry values after backing up the originals (existed or not, kind, data) to a JSON file,
/// and restores them exactly. Each value is backed up only once, so the original state is always kept.
/// </summary>
static class RegBackup
{
    /// <summary>Data stored as text; Binary as base64.</summary>
    public sealed record Entry(string Key, string Name, bool Existed, string? Kind, string? Data)
    {
        /// <summary>Entry from a .NET value about to be written (string/int/byte[]).</summary>
        public static Entry Of(object value) => value switch
        {
            byte[] b => new("", "", true, nameof(RegistryValueKind.Binary), Convert.ToBase64String(b)),
            int i => new("", "", true, nameof(RegistryValueKind.DWord), i.ToString()),
            _ => new("", "", true, nameof(RegistryValueKind.String), value.ToString()),
        };

        [System.Text.Json.Serialization.JsonIgnore] public object Value => Enum.Parse<RegistryValueKind>(Kind!) switch
        {
            RegistryValueKind.Binary => Convert.FromBase64String(Data!),
            RegistryValueKind.DWord => int.Parse(Data!),
            RegistryValueKind.QWord => long.Parse(Data!),
            _ => Data!,
        };
    }

    public static void Write(RegistryKey hive, string backupFile, IEnumerable<(string Key, string Name, object Value)> writes)
    {
        var list = Load(backupFile);
        foreach (var (key, name, value) in writes)
        {
            if (!list.Any(e => Same(e, key, name)))
            {
                list.Add(Read(hive, key, name));
                Save(backupFile, list); // save before overwriting: safe even if the process dies midway
            }
            using var w = hive.CreateSubKey(key);
            w.SetValue(name, value); // kind follows the .NET type: string=SZ, int=DWORD, byte[]=BINARY
        }
    }

    /// <summary>Saves the CURRENT data of every backed-up value (e.g. MyNotch's theme choice before restoring).</summary>
    public static void Snapshot(RegistryKey hive, string backupFile, string snapshotFile) =>
        Save(snapshotFile, Load(backupFile).Select(e => Read(hive, e.Key, e.Name)).ToList());

    /// <summary>Values from a snapshot, ready to be written again with <see cref="Write"/>.</summary>
    public static List<(string Key, string Name, object Value)> Values(string snapshotFile) =>
        Load(snapshotFile).Where(e => e.Existed).Select(e => (e.Key, e.Name, e.Value)).ToList();

    static Entry Read(RegistryKey hive, string key, string name)
    {
        using var k = hive.OpenSubKey(key);
        if (k?.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase) != true) return new(key, name, false, null, null);
        var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new(key, name, true, k.GetValueKind(name).ToString(), v is byte[] b ? Convert.ToBase64String(b) : v?.ToString());
    }

    public static void Restore(RegistryKey hive, string backupFile)
    {
        var list = Load(backupFile);
        foreach (var e in list)
        {
            using (var k = hive.OpenSubKey(e.Key, writable: true))
            {
                if (k == null) continue;
                if (e.Existed) k.SetValue(e.Name, e.Value, Enum.Parse<RegistryValueKind>(e.Kind!));
                else k.DeleteValue(e.Name, false);
            }
            DeleteEmptyKeys(hive, e.Key);
        }
        if (File.Exists(backupFile)) File.Delete(backupFile);
    }

    /// <summary>Deletes empty keys (no values or subkeys) from the deepest upwards; stops at a non-empty key.</summary>
    static void DeleteEmptyKeys(RegistryKey hive, string key)
    {
        while (key.Contains('\\'))
        {
            using (var k = hive.OpenSubKey(key))
                if (k == null || k.ValueCount > 0 || k.SubKeyCount > 0) return;
            hive.DeleteSubKey(key, false);
            key = key[..key.LastIndexOf('\\')];
        }
    }

    static bool Same(Entry e, string key, string name) =>
        e.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    public static List<Entry> Load(string file) =>
        File.Exists(file) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(file)) ?? [] : [];

    static void Save(string file, List<Entry> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
