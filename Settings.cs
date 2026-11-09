using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyNotch;

// Stored in settings.json as a number: keep the member order.
public enum IslandStyle { Solid, Transparent, Blur }

/// <summary>Per-user data folders (%APPDATA%\MyNotch, %LOCALAPPDATA%\MyNotch).</summary>
static class AppDirs
{
    public const string Name = "MyNotch", LegacyName = "NotchKu";

    public static readonly string Roaming = Resolve(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    public static readonly string Local = Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// Folder under <paramref name="root"/>. Data from before the rename (NotchKu) is moved once; it holds the
    /// original taskbar, theme and icon backups, so if the move fails (files in use) the old folder keeps being used.
    /// </summary>
    public static string Resolve(string root)
    {
        var dir = Path.Combine(root, Name);
        var legacy = Path.Combine(root, LegacyName);
        if (Directory.Exists(dir) || !Directory.Exists(legacy)) return dir;
        try { Directory.Move(legacy, dir); return dir; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return legacy; }
    }
}

/// <summary>Display settings, stored in %APPDATA%\MyNotch\settings.json.</summary>
public sealed class Settings
{
    public IslandStyle Style { get; set; } = IslandStyle.Blur;
    /// <summary>Notch &amp; dock background opacity in percent (ignored for the Solid style).</summary>
    public int Opacity { get; set; } = 45;
    /// <summary>true = floats below the top edge (Dynamic Island); false = attached to the top edge.</summary>
    public bool Floating { get; set; } = true;
    /// <summary>Dock icons magnify under the cursor.</summary>
    public bool Magnify { get; set; } = true;
    /// <summary>
    /// Show the notch &amp; dock in screenshots/recordings. Glass blur needs the window excluded from
    /// capture, so while this is on the Blur style renders as Transparent.
    /// </summary>
    public bool ShowInScreenshots { get; set; }
    /// <summary>MyNotch macOS-style system icons (Explorer + the dock's Start/Explorer buttons).</summary>
    public bool CustomIcons { get; set; }

    /// <summary>Blur only works while the window is excluded from screen capture.</summary>
    [JsonIgnore] public bool UseBlur => Style == IslandStyle.Blur && !ShowInScreenshots;

    static readonly string FilePath = Path.Combine(AppDirs.Roaming, "settings.json");

    public static Settings Current { get; } = Load();
    public static event Action? Changed;

    [JsonIgnore] public byte TintAlpha => Style == IslandStyle.Solid ? (byte)255 : (byte)(Math.Clamp(Opacity, 0, 100) * 255 / 100);

    static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { } // corrupt/locked file: use defaults
        return new();
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        Changed?.Invoke();
    }
}
