using System;
namespace RazerBatteryTray.Desktop
{
    // Position is not DPI: a quadratic curve expands the commonly used low range.
    internal static class DpiScale
    {
        internal const int Minimum = 100, Maximum = 8000, Step = 50;
        internal static int FromPosition(double position)
        {
            double p = Math.Max(0, Math.Min(1, position));
            return Math.Max(Minimum, Math.Min(Maximum, Minimum + (int)Math.Round((Maximum - Minimum) * p * p / Step, MidpointRounding.AwayFromZero) * Step));
        }
        internal static double ToPosition(int dpi) { return Math.Sqrt((Math.Max(Minimum, Math.Min(Maximum, dpi)) - Minimum) / (double)(Maximum - Minimum)); }
    }
}
