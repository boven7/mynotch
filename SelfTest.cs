using System.Windows.Media;

namespace MyNotch;

/// <summary>Logic checks (colors, palette, registry backup, migration). Run: <c>MyNotch.exe --selftest</c>, exit code 0 = pass.</summary>
static class SelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        void Check(bool ok, string name) { if (!ok) failures.Add(name); }

        var c = Color.FromRgb(0x11, 0x22, 0x33);
        Check(SystemTheme.ToAbgr(c) == unchecked((int)0xFF332211), "ToAbgr");
        Check(SystemTheme.FromAbgr(SystemTheme.ToAbgr(c)) == c, "FromAbgr roundtrip");
        // Real registry value from a dev machine: AccentColorMenu 0xffd47800 = #0078D4
        Check(SystemTheme.FromAbgr(unchecked((int)0xFFD47800)) == Color.FromRgb(0x00, 0x78, 0xD4), "FromAbgr registry");
        Check(SystemTheme.Mix(c, Colors.White, 0) == c && SystemTheme.Mix(c, Colors.White, 1) == Colors.White, "Mix endpoints");

        var p = SystemTheme.Palette(c);
        Check(p.Length == 32, "Palette length");
        Check(p[12] == 0x11 && p[13] == 0x22 && p[14] == 0x33 && p[15] == 0, "Palette base at index 3");

        // RegBackup: original value (REG_EXPAND_SZ) comes back exactly, created keys are removed again
        const string testKey = @"Software\MyNotchSelfTest";
        var backup = Path.Combine(Path.GetTempPath(), "mynotch-regtest.json");
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testKey))
            k.SetValue("x", @"%SystemRoot%\a.dll,-1", Microsoft.Win32.RegistryValueKind.ExpandString);
        RegBackup.Write(Microsoft.Win32.Registry.CurrentUser, backup,
            [(testKey, "x", "new"), (testKey + @"\Sub\DefaultIcon", "", "icon")]);
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey))
            Check(k?.GetValue("x") as string == "new", "RegBackup write");
        RegBackup.Restore(Microsoft.Win32.Registry.CurrentUser, backup);
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey))
        {
            Check(k?.GetValueKind("x") == Microsoft.Win32.RegistryValueKind.ExpandString
                  && k.GetValue("x", null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string == @"%SystemRoot%\a.dll,-1",
                  "RegBackup restore exact");
            Check(k?.OpenSubKey("Sub") == null, "RegBackup removes created keys");
        }
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testKey, false);
        Check(!File.Exists(backup), "RegBackup clears backup file");

        // Theme: negative DWORD & binary restored exactly; a snapshot captures MyNotch's choice and can be reapplied
        var applied = Path.Combine(Path.GetTempPath(), "mynotch-regtest-applied.json");
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testKey)) k.SetValue("d", unchecked((int)0xC4112233));
        RegBackup.Write(Microsoft.Win32.Registry.CurrentUser, backup, [(testKey, "d", 5), (testKey, "b", new byte[] { 1, 2, 255 })]);
        RegBackup.Snapshot(Microsoft.Win32.Registry.CurrentUser, backup, applied);
        RegBackup.Restore(Microsoft.Win32.Registry.CurrentUser, backup);
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey))
            Check(k?.GetValue("d") is int d && d == unchecked((int)0xC4112233) && k.GetValue("b") == null, "RegBackup restore dword/binary");
        RegBackup.Write(Microsoft.Win32.Registry.CurrentUser, backup, RegBackup.Values(applied));
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey))
            Check(k?.GetValue("d") is 5 && k.GetValue("b") is byte[] { Length: 3 } b && b[2] == 255
                  && k.GetValueKind("b") == Microsoft.Win32.RegistryValueKind.Binary, "RegBackup snapshot reapply");
        RegBackup.Restore(Microsoft.Win32.Registry.CurrentUser, backup);
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testKey, false);
        File.Delete(applied);

        // .reg file for the icon tasks: backslash escaping, value delete, default value, REG_EXPAND_SZ
        var reg = SystemIconInstaller.RegFile([
            (@"SOFTWARE\X", "3", RegBackup.Entry.Of(@"C:\a b\f.ico,0")),
            (@"SOFTWARE\X", "4", null),
            (@"SOFTWARE\Y", "", new RegBackup.Entry("", "", true, "ExpandString", "%A%")),
        ]);
        Check(reg.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE\\X]\r\n\"3\"=\"C:\\\\a b\\\\f.ico,0\"\r\n\"4\"=-\r\n"), "RegFile sz/delete");
        Check(reg.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE\\Y]\r\n@=hex(2):25,00,41,00,25,00,00,00\r\n"), "RegFile expand_sz");

        // Rename migration: legacy data folder moves once, an existing new folder wins, Program Files keeps the legacy name
        var root = Path.Combine(Path.GetTempPath(), "mynotch-dirtest");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, AppDirs.LegacyName));
        File.WriteAllText(Path.Combine(root, AppDirs.LegacyName, "taskbar-state.txt"), "3");
        Check(SystemIconInstaller.MachineNameFor(root) == AppDirs.LegacyName, "MachineNameFor legacy");
        var moved = AppDirs.Resolve(root);
        Check(moved == Path.Combine(root, AppDirs.Name) && File.ReadAllText(Path.Combine(moved, "taskbar-state.txt")) == "3"
              && !Directory.Exists(Path.Combine(root, AppDirs.LegacyName)), "AppDirs moves legacy folder");
        Directory.CreateDirectory(Path.Combine(root, AppDirs.LegacyName));
        Check(AppDirs.Resolve(root) == moved && Directory.Exists(Path.Combine(root, AppDirs.LegacyName)), "AppDirs keeps existing folder");
        Check(SystemIconInstaller.MachineNameFor(root) == AppDirs.Name, "MachineNameFor new");
        Directory.Delete(root, true);

        var log = Path.Combine(Path.GetTempPath(), "mynotch-selftest.txt");
        File.WriteAllText(log, failures.Count == 0 ? "OK" : "FAIL: " + string.Join(", ", failures));
        return failures.Count == 0 ? 0 : 1;
    }
}
