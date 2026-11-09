using System.Windows;
using Windows.System.Power;

namespace MyNotch;

// Battery in the notch: percentage + green bolt while plugged in. Hidden on PCs without a battery.
public partial class NotchWindow
{
    string _batteryHeader = "";

    void InitBattery()
    {
        // PowerManager events arrive on the thread pool; touch the UI only through the Dispatcher.
        PowerManager.RemainingChargePercentChanged += (_, _) => Dispatcher.InvokeAsync(UpdateBattery);
        PowerManager.PowerSupplyStatusChanged += (_, _) => Dispatcher.InvokeAsync(UpdateBattery);
        PowerManager.BatteryStatusChanged += (_, _) => Dispatcher.InvokeAsync(UpdateBattery);
        UpdateBattery();
    }

    void UpdateBattery()
    {
        var status = PowerManager.BatteryStatus;
        bool present = status != BatteryStatus.NotPresent;
        bool plugged = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
        int pct = PowerManager.RemainingChargePercent;

        CompactBattery.Visibility = present ? Visibility.Visible : Visibility.Collapsed;
        ChargingIcon.Visibility = plugged ? Visibility.Visible : Visibility.Collapsed;
        BatteryPct.Text = $"{pct}%";
        // Plugged in at 100% reports Idle, not Charging: don't claim "charging".
        _batteryHeader = !present ? ""
            : status == BatteryStatus.Charging ? $" · {pct}% · charging"
            : $" · {pct}%";
        UpdateClock();
    }
}
