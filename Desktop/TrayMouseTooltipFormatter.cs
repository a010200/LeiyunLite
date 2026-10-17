using System;
using System.Globalization;

namespace RazerBatteryTray.Desktop
{
    internal static class TrayMouseTooltipFormatter
    {
        internal static string Format(MouseBatteryInfo reading, BatteryTrayVisual visual)
        {
            // Use the same validated visual as the icon. Cached/raw readings must
            // not turn an unknown silhouette into a seemingly live percentage.
            string battery;
            if (reading == null || !reading.IsConnected)
                battery = Ui.T("鼠标电量：未检测到设备", "Mouse battery: no device detected");
            else if (visual.Percent < 0)
                battery = visual.Charging ? Ui.T("鼠标电量：正在充电（电量未知）", "Mouse battery: charging (unknown)") : Ui.T("鼠标电量：未知", "Mouse battery: unknown");
            else {
                string value = visual.Percent.ToString(CultureInfo.InvariantCulture) + "%";
                battery = visual.Charging ? Ui.T("鼠标电量：正在充电（" + value + "）", "Mouse battery: charging (" + value + ")") : Ui.T("鼠标电量：" + value, "Mouse battery: " + value);
            }
            string name = reading == null || string.IsNullOrWhiteSpace(reading.DeviceName) ? "" : reading.DeviceName.Replace('\r',' ').Replace('\n',' ').Trim();
            // .NET Framework NotifyIcon.Text is limited to 63 UTF-16 units.
            // Preserve the entire battery sentence, shortening only the model name.
            int available = 63 - battery.Length - 1;
            if (name.Length == 0 || available <= 0) return battery;
            if (name.Length > available) {
                int count = Math.Max(0, available - 1);
                if (count > 0 && char.IsHighSurrogate(name[count - 1])) count--;
                name = name.Substring(0,count) + "…";
            }
            return name + "\n" + battery;
        }
    }
}
