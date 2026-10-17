using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RazerBatteryTray.Desktop
{
    internal struct BatteryTrayVisual : IEquatable<BatteryTrayVisual>
    {
        internal int Percent; // -1 means unknown, never a cached battery percentage.
        internal bool Charging, Low;
        internal int Size;
        internal BatteryTrayVisual(int percent, bool charging, bool low, int size)
        { Percent = percent; Charging = charging; Low = low; Size = size; }
        public bool Equals(BatteryTrayVisual other)
        { return Percent == other.Percent && Charging == other.Charging && Low == other.Low && Size == other.Size; }
        public override bool Equals(object obj) { return obj is BatteryTrayVisual && Equals((BatteryTrayVisual)obj); }
        public override int GetHashCode() { return Percent ^ (Charging ? 256 : 0) ^ (Low ? 512 : 0) ^ Size << 10; }
    }

    internal static class BatteryTrayIconRenderer
    {
        internal static BatteryTrayVisual State(MouseBatteryInfo reading, int threshold, int size, DateTime now, TimeSpan maximumAge)
        {
            bool fresh = reading.IsConnected && !reading.IsSleeping && reading.ProtocolStatus == DeviceProtocolStatus.Ready &&
                reading.LastUpdated != DateTime.MinValue && reading.LastUpdated <= now && now - reading.LastUpdated <= maximumAge;
            int percent = fresh && reading.BatteryKnown && reading.BatteryPercent >= 0 && reading.BatteryPercent <= 100 ? reading.BatteryPercent : -1;
            bool charging = fresh && reading.ChargingKnown && reading.IsCharging;
            return new BatteryTrayVisual(percent, charging, percent >= 0 && percent <= threshold && !charging, Math.Max(16, size));
        }

        // Compatibility entry point for state selection and native HICON lifetime.
        // All tray visuals now use the ordinary mouse silhouette renderer.
        private static readonly Color GreenColor = Color.FromArgb(26, 212, 81);
        internal static Bitmap Render(BatteryTrayVisual state)
        { return TrayMouseBatteryIconRenderer.Render(state, GreenColor); }
        internal static void PlugLayout(int size, out int cx, out int cy, out int diameter)
        { TrayMouseBatteryIconRenderer.PlugLayout(size, out cx, out cy, out diameter); }
        internal static Icon Create(BatteryTrayVisual state)
        {
            using (var bitmap = Render(state))
            {
                IntPtr handle = TrayMouseBatteryIconRenderer.CreateNativeIcon(bitmap);
                try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    }
}
