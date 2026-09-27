using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RazerBatteryTray.Desktop
{
    internal static class RotationMath
    {
        public static Vector Rotate(Vector delta, double degrees)
        {
            double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            return new Vector(c * delta.X - s * delta.Y, s * delta.X + c * delta.Y);
        }
        // Undirected line angle: swiping right-to-left must give the same result.
        public static bool TryStroke(IList<Point> points, out double degrees)
        {
            degrees = 0;
            if (points == null || points.Count < 6) return false;
            double mx = points.Average(p => p.X), my = points.Average(p => p.Y), xx = 0, yy = 0, xy = 0;
            foreach (var p in points) { double x = p.X - mx, y = p.Y - my; xx += x * x; yy += y * y; xy += x * y; }
            if (points.Max(p => p.X) - points.Min(p => p.X) < 100) return false;
            double major = (xx + yy + Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy)) / 2;
            double minor = xx + yy - major;
            if (major <= 0 || minor / major > 0.02) return false; // reject strongly curved strokes
            degrees = Math.Atan2(2 * xy, xx - yy) * 90 / Math.PI;
            return Math.Abs(degrees) <= 44;
        }
        public static bool TryCorrection(IList<double> angles, double alreadyApplied, out double correction)
        {
            correction = 0;
            if (angles == null || angles.Count < 10 || double.IsNaN(alreadyApplied) || Math.Abs(alreadyApplied) > 44) return false;
            var sorted = angles.Where(a => !double.IsNaN(a) && !double.IsInfinity(a) && Math.Abs(a) <= 44).OrderBy(a => a).ToArray();
            if (sorted.Length < 10) return false;
            double median = Median(sorted);
            var inliers = sorted.Where(a => Math.Abs(a - median) <= 5).ToArray();
            if (inliers.Length < 10) return false;
            // Measurements are already rotated by the current applied angle.
            // New absolute correction = old correction - observed residual.
            correction = Math.Round(alreadyApplied - Median(inliers), 0);
            return Math.Abs(correction) <= 44;
        }
        private static double Median(double[] a) { return (a[(a.Length - 1) / 2] + a[a.Length / 2]) / 2; }
    }
}
