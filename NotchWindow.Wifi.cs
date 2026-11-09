using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Security.Credentials;

namespace MyNotch;

/// <summary>One network row in the Wi-Fi panel.</summary>
public sealed class WifiRow(WiFiAvailableNetwork network, bool connected)
{
    public WiFiAvailableNetwork Network { get; } = network;
    public string Ssid => Network.Ssid;
    public bool Connected { get; } = connected;
    public bool Secured => Network.SecuritySettings.NetworkAuthenticationType
        is not (Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211 or Windows.Networking.Connectivity.NetworkAuthenticationType.None);
    public string SignalGlyph => Network.SignalBars switch { <= 1 => "\uE871", 2 => "\uE872", 3 => "\uE873", _ => "\uE701" };
    public Visibility LockVisibility => Secured ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CheckVisibility => Connected ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Wi-Fi panel in the notch: power switch, network list, connect (with password when needed).</summary>
public partial class NotchWindow
{
    WiFiAdapter? _wifiAdapter;
    WifiRow? _pendingNetwork; // network waiting for a password

    async void Wifi_Click(object sender, RoutedEventArgs e)
    {
        WifiToggle.IsChecked = _wifi?.State == RadioState.On; // the chip only opens the panel; it is not a switch
        ShowSettings(false);
        MainPanel.Visibility = Visibility.Collapsed;
        WifiPanel.Visibility = Visibility.Visible;
        await RefreshWifiAsync(scan: true);
    }

    void WifiBack_Click(object sender, RoutedEventArgs e) => HideWifi();

    void HideWifi()
    {
        if (WifiPanel.Visibility != Visibility.Visible) return;
        HidePassword();
        WifiPanel.Visibility = Visibility.Collapsed;
        if (SettingsPanel.Visibility != Visibility.Visible) MainPanel.Visibility = Visibility.Visible;
    }

    async void WifiRefresh_Click(object sender, RoutedEventArgs e) => await RefreshWifiAsync(scan: true);

    async void WifiPower_Click(object sender, RoutedEventArgs e)
    {
        if (_wifi == null) return;
        await _wifi.SetStateAsync(WifiPower.IsChecked == true ? RadioState.On : RadioState.Off);
        WifiToggle.IsChecked = _wifi.State == RadioState.On;
        await Task.Delay(1500); // the adapter needs time before it can scan
        await RefreshWifiAsync(scan: true);
    }

    void WifiStatusText(string? text, bool showLocationButton = false)
    {
        WifiStatus.Text = text ?? "";
        WifiStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        WifiLocationBtn.Visibility = showLocationButton ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task RefreshWifiAsync(bool scan)
    {
        _wifi ??= await Radios.FindAsync(RadioKind.WiFi);
        WifiPower.IsEnabled = _wifi != null;
        WifiPower.IsChecked = _wifi?.State == RadioState.On;
        if (_wifi?.State != RadioState.On)
        {
            WifiList.ItemsSource = null;
            WifiStatusText(_wifi == null ? "No Wi-Fi adapter found" : "Wi-Fi is off");
            return;
        }

        try
        {
            if (_wifiAdapter == null)
            {
                // Windows 11: desktop apps need location permission to scan Wi-Fi
                if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed)
                {
                    WifiStatusText("Allow location access for desktop apps to list Wi-Fi networks", showLocationButton: true);
                    return;
                }
                _wifiAdapter = (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
                if (_wifiAdapter == null) { WifiStatusText("No Wi-Fi adapter found"); return; }
            }

            if (scan) { WifiStatusText("Scanning…"); await _wifiAdapter.ScanAsync(); }
            var profile = await _wifiAdapter.NetworkAdapter.GetConnectedProfileAsync();
            var connected = profile?.WlanConnectionProfileDetails?.GetConnectedSsid() ?? profile?.ProfileName;

            var rows = _wifiAdapter.NetworkReport.AvailableNetworks
                .Where(n => !string.IsNullOrEmpty(n.Ssid))          // skip hidden networks
                .GroupBy(n => n.Ssid)
                .Select(g => g.OrderByDescending(n => n.SignalBars).First())
                .Select(n => new WifiRow(n, n.Ssid == connected))
                .OrderByDescending(r => r.Connected).ThenByDescending(r => r.Network.SignalBars)
                .ToList();
            WifiList.ItemsSource = rows;
            WifiStatusText(rows.Count == 0 ? "No networks found" : null);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            WifiStatusText("Scan failed: " + ex.Message);
        }
    }

    async void WifiNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not WifiRow row || _wifiAdapter == null) return;
        HidePassword();
        if (row.Connected)
        {
            _wifiAdapter.Disconnect();
            await RefreshWifiAsync(scan: false);
            return;
        }

        WifiStatusText($"Connecting to {row.Ssid}…");
        // Try the saved profile first; a new secured network fails here, so ask for the password
        var result = await _wifiAdapter.ConnectAsync(row.Network, WiFiReconnectionKind.Automatic);
        if (result.ConnectionStatus == WiFiConnectionStatus.Success) { await RefreshWifiAsync(scan: false); return; }
        if (row.Secured) { AskPassword(row); return; }
        WifiStatusText($"Could not connect ({result.ConnectionStatus})");
    }

    void AskPassword(WifiRow row)
    {
        _pendingNetwork = row;
        WifiStatusText($"Password for {row.Ssid}");
        WifiPassword.Password = "";
        WifiPasswordRow.Visibility = Visibility.Visible;
        // The notch normally refuses keyboard focus; allow it while typing
        Native.AddExStyle(new WindowInteropHelper(this).Handle, 0, Native.WS_EX_NOACTIVATE);
        Activate();
        WifiPassword.Focus();
        Keyboard.Focus(WifiPassword);
    }

    void HidePassword()
    {
        if (WifiPasswordRow.Visibility != Visibility.Visible) return;
        WifiPasswordRow.Visibility = Visibility.Collapsed;
        WifiPassword.Password = "";
        _pendingNetwork = null;
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_NOACTIVATE);
    }

    void WifiPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) WifiConnect_Click(sender, e);
        else if (e.Key == Key.Escape) HidePassword();
    }

    async void WifiConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingNetwork is not { } row || _wifiAdapter == null) return;
        var credential = new PasswordCredential { Password = WifiPassword.Password };
        WifiStatusText($"Connecting to {row.Ssid}…");
        var result = await _wifiAdapter.ConnectAsync(row.Network, WiFiReconnectionKind.Automatic, credential);
        if (result.ConnectionStatus == WiFiConnectionStatus.Success)
        {
            HidePassword();
            await RefreshWifiAsync(scan: false);
        }
        else WifiStatusText(result.ConnectionStatus == WiFiConnectionStatus.InvalidCredential
            ? "Wrong password" : $"Could not connect ({result.ConnectionStatus})");
    }

    void WifiLocation_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true });
}
