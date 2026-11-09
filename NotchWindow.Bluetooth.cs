using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace MyNotch;

/// <summary>One paired Bluetooth device in the Bluetooth panel.</summary>
public sealed class BtRow(DeviceInformation info, bool connected, string[] audioFilters)
{
    public string Name => string.IsNullOrWhiteSpace(info.Name) ? "Unnamed device" : info.Name;
    public bool Connected { get; } = connected;
    /// <summary>KS audio filter paths of this device; empty = not an audio device.</summary>
    public string[] AudioFilters { get; } = audioFilters;
    public string Status => Connected ? "Connected" : AudioFilters.Length > 0 ? "Not connected" : "Not connected · connect from the device";
    public string Glyph => AudioFilters.Length > 0 ? "\uE7F6" : "\uE702"; // headphone / bluetooth
    public Visibility CheckVisibility => Connected ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// Connects/disconnects paired Bluetooth audio devices the same way Quick Settings does:
/// the KS property <c>KSPROPSETID_BtAudio</c> ONESHOT_RECONNECT/DISCONNECT sent to its audio filters.
/// </summary>
static class BtAudio
{
    const string KsCategoryAudio = "{6994AD04-93EF-11D0-A3CC-00A0C9223196}";
    const string ContainerIdKey = "System.Devices.Aep.ContainerId";
    const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    const uint IoctlKsProperty = 0x002F0003, KsPropertyTypeGet = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct KsProperty { public Guid Set; public uint Id; public uint Flags; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint sa, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref KsProperty input, int inSize, nint output, int outSize, out int returned, nint overlapped);

    /// <summary>Paired classic + LE Bluetooth devices, connected ones first.</summary>
    public static async Task<List<BtRow>> PairedAsync()
    {
        string[] props = [ContainerIdKey, IsConnectedKey];
        var devices = (await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), props, DeviceInformationKind.AssociationEndpoint))
            .Concat(await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), props, DeviceInformationKind.AssociationEndpoint));

        var rows = new List<BtRow>();
        foreach (var d in devices)
        {
            var filters = d.Properties.TryGetValue(ContainerIdKey, out var c) && c is Guid container
                ? await AudioFiltersAsync(container) : [];
            rows.Add(new BtRow(d, d.Properties.TryGetValue(IsConnectedKey, out var v) && v is true, filters));
        }
        return rows.GroupBy(r => r.Name).Select(g => g.OrderByDescending(r => r.Connected).First()) // classic & LE duplicates
                   .OrderByDescending(r => r.Connected).ThenBy(r => r.Name).ToList();
    }

    // KS audio filters (A2DP & Hands-Free) sharing the BT device's ContainerId.
    static async Task<string[]> AudioFiltersAsync(Guid container) =>
        (await DeviceInformation.FindAllAsync(
            $"System.Devices.InterfaceClassGuid:=\"{KsCategoryAudio}\" AND System.Devices.ContainerId:=\"{{{container}}}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True"))
        .Select(f => f.Id).ToArray();

    /// <summary>True if at least one filter accepted the command.</summary>
    public static bool Send(IEnumerable<string> filters, bool connect)
    {
        bool any = false;
        foreach (var path in filters)
        {
            using var h = CreateFile(path, 0xC0000000 /*GENERIC_READ|WRITE*/, 3 /*share R|W*/, 0, 3 /*OPEN_EXISTING*/, 0, 0);
            if (h.IsInvalid) continue;
            var p = new KsProperty { Set = KsPropSetIdBtAudio, Id = connect ? 0u : 1u, Flags = KsPropertyTypeGet };
            any |= DeviceIoControl(h, IoctlKsProperty, ref p, Marshal.SizeOf<KsProperty>(), 0, 0, out _, 0);
        }
        return any;
    }
}

/// <summary>Bluetooth panel in the notch: power switch + paired devices, click to connect/disconnect.</summary>
public partial class NotchWindow
{
    async void Bt_Click(object sender, RoutedEventArgs e)
    {
        BtToggle.IsChecked = _bt?.State == RadioState.On; // the chip only opens the panel; it is not a switch
        ShowSettings(false);
        MainPanel.Visibility = Visibility.Collapsed;
        BtPanel.Visibility = Visibility.Visible;
        await RefreshBtAsync();
    }

    void BtBack_Click(object sender, RoutedEventArgs e) => HideBt();

    void HideBt()
    {
        if (BtPanel.Visibility != Visibility.Visible) return;
        BtPanel.Visibility = Visibility.Collapsed;
        if (SettingsPanel.Visibility != Visibility.Visible) MainPanel.Visibility = Visibility.Visible;
    }

    async void BtRefresh_Click(object sender, RoutedEventArgs e) => await RefreshBtAsync();

    async void BtPower_Click(object sender, RoutedEventArgs e)
    {
        if (_bt == null) return;
        await _bt.SetStateAsync(BtPower.IsChecked == true ? RadioState.On : RadioState.Off);
        BtToggle.IsChecked = _bt.State == RadioState.On;
        await Task.Delay(1500); // paired devices take a moment to reappear
        await RefreshBtAsync();
    }

    void BtStatusText(string? text)
    {
        BtStatus.Text = text ?? "";
        BtStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    async Task RefreshBtAsync()
    {
        _bt ??= await Radios.FindAsync(RadioKind.Bluetooth);
        BtPower.IsEnabled = _bt != null;
        BtPower.IsChecked = _bt?.State == RadioState.On;
        BtToggle.IsChecked = _bt?.State == RadioState.On;
        if (_bt?.State != RadioState.On)
        {
            BtList.ItemsSource = null;
            BtStatusText(_bt == null ? "No Bluetooth adapter found" : "Bluetooth is off");
            return;
        }
        try
        {
            var rows = await BtAudio.PairedAsync();
            BtList.ItemsSource = rows;
            BtStatusText(rows.Count == 0 ? "No paired devices" : null);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            BtStatusText("Could not read devices: " + ex.Message);
        }
    }

    async void BtDevice_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not BtRow row) return;
        if (row.AudioFilters.Length == 0)
        {
            // Mice, keyboards and phones reconnect on their own; Windows has no manual-connect API for them.
            BtAdd_Click(sender, e);
            return;
        }
        BtStatusText(row.Connected ? $"Disconnecting {row.Name}…" : $"Connecting to {row.Name}…");
        if (!await Task.Run(() => BtAudio.Send(row.AudioFilters, connect: !row.Connected)))
        {
            BtStatusText($"Could not {(row.Connected ? "disconnect" : "connect")} {row.Name}");
            return;
        }
        await Task.Delay(row.Connected ? 1500 : 4000); // wait for Windows to update the connection state
        await RefreshBtAsync();
    }

    void BtAdd_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
}
