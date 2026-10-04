using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace RazerBatteryTray.Macros
{
    // DIAGNOSTIC COPY ONLY. No reference from product source/build manifest.
    internal static class AuditMetrics
    {
        internal const int Capacity = 262144;
        internal static readonly string[] Names = { "MouseMove.total", "MouseMove.local", "Keyboard.total", "MouseButton.total", "Wheel.total", "CallNextHookEx", "RouteInput", "ProtectOwnWindow", "BindingRouter.Handle", "Handle.lockWait", "FindMatch", "MacroEngine.Start", "MacroEngine.Stop", "ReplaceAndStop", "Replace.lockWait", "RecordTiming", "SuppressWheel", "Wheel.lockWait", "Keyboard.local", "MouseButton.local", "Wheel.local", "Probe.overhead" };
        private static readonly long[][] samples = new long[Names.Length][];
        private static readonly int[] counts = new int[Names.Length];
        [ThreadStatic] internal static long ChainTicks;
        private static readonly long[] moveEntry = new long[Capacity];
        private static readonly long[] moveExit = new long[Capacity];
        private static int moveCount;
        internal static bool Enabled;
        static AuditMetrics() { for (int i = 0; i < samples.Length; i++) samples[i] = new long[Capacity]; }
        internal static void Record(int metric, long ticks)
        {
            if (!Enabled) return;
            int slot = Interlocked.Increment(ref counts[metric]) - 1;
            if (slot < Capacity) samples[metric][slot] = ticks;
        }
        internal static void Motion(long entry, long exit)
        {
            if (!Enabled) return;
            int slot = Interlocked.Increment(ref moveCount) - 1;
            if (slot < Capacity) { moveEntry[slot] = entry; moveExit[slot] = exit; }
        }
        // Only reset/aggregate after all measured producers have stopped.
        internal static void Reset() { Enabled = false; Array.Clear(counts, 0, counts.Length); moveCount = 0; ChainTicks = 0; Enabled = true; }
        internal static int Count(int metric) { return counts[metric]; }
        internal static string Number(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        internal static double Ms(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
        internal static double Percentile(long[] sorted, double percentile)
        { return sorted.Length == 0 ? double.NaN : Ms(sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1)]); }
        internal static void Row(StreamWriter writer, string scenario, string name, long[] values, int attempted, double expectedMs)
        {
            Array.Sort(values); double sum = 0, square = 0; int gap2 = 0, gap5 = 0, gap10 = 0;
            foreach (long ticks in values) { double ms = Ms(ticks); sum += ms; square += ms * ms; if (expectedMs > 0) { if (ms > 2 * expectedMs) gap2++; if (ms > 5 * expectedMs) gap5++; if (ms > 10 * expectedMs) gap10++; } }
            double avg = values.Length == 0 ? double.NaN : sum / values.Length;
            writer.WriteLine(string.Join(",", new[] { scenario, name, values.Length.ToString(), Math.Max(0, attempted-values.Length).ToString(), Number(avg), Number(Percentile(values,.5)), Number(Percentile(values,.95)), Number(Percentile(values,.99)), Number(Percentile(values,.999)), Number(values.Length == 0 ? double.NaN : Ms(values[values.Length-1])), Number(values.Length == 0 ? double.NaN : Math.Sqrt(Math.Max(0, square/values.Length-avg*avg))), expectedMs > 0 ? gap2.ToString() : "NA", expectedMs > 0 ? gap5.ToString() : "NA", expectedMs > 0 ? gap10.ToString() : "NA" }));
        }
        internal const string Header = "scenario,metric,count,dropped,avg_ms,p50_ms,p95_ms,p99_ms,p999_ms,max_ms,stddev_ms,gap_gt2x,gap_gt5x,gap_gt10x";
        internal static void Save(string directory, string scenario, double expectedMs, bool saveMotion)
        {
            Enabled = false; Directory.CreateDirectory(directory);
            using (var writer = new StreamWriter(Path.Combine(directory, scenario+"-metrics.csv")))
            {
                writer.WriteLine(Header);
                for (int i=0;i<samples.Length;i++) { int n=Math.Min(Capacity,counts[i]); var values=new long[n]; Array.Copy(samples[i],values,n); Row(writer,scenario,Names[i],values,counts[i],0); }
                int count=Math.Min(Capacity,moveCount); var intervals=new long[Math.Max(0,count-1)];
                for(int i=1;i<count;i++) intervals[i-1]=moveEntry[i]-moveEntry[i-1];
                Row(writer,scenario,"MouseMove.arrivalInterval",intervals,Math.Max(0,moveCount-1),expectedMs);
            }
            if (saveMotion) using(var writer=new StreamWriter(Path.Combine(directory,scenario+"-motion.csv")))
            {
                writer.WriteLine("entry_relative_ticks,exit_relative_ticks"); int n=Math.Min(Capacity,moveCount); long origin=n==0?0:moveEntry[0];
                for(int i=0;i<n;i++) writer.WriteLine((moveEntry[i]-origin)+","+(moveExit[i]-origin));
            }
        }
    }
}
