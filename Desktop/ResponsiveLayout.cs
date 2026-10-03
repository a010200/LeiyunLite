using System;
namespace RazerBatteryTray.Desktop
{
    internal enum LayoutMode { Wide, Medium, Compact }
    internal static class ResponsiveLayout
    {
        internal static LayoutMode ForWidth(double dip)
        {
            if (double.IsNaN(dip) || double.IsInfinity(dip) || dip < 820) return LayoutMode.Compact;
            return dip >= 1100 ? LayoutMode.Wide : LayoutMode.Medium;
        }
        internal static double NavigationWidth(LayoutMode mode) { return mode == LayoutMode.Compact ? 64 : mode == LayoutMode.Medium ? 144 : 176; }
        internal static double PageInset(LayoutMode mode) { return mode == LayoutMode.Wide ? 30 : mode == LayoutMode.Medium ? 22 : 16; }
    }
}
