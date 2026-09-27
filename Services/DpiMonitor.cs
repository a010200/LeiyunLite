using System;
using System.Threading;

namespace RazerBatteryTray
{
    internal sealed class DpiReading
    {
        public int Dpi;
        public int Stage;
        public int Count;
        public bool Changed;
        public string DeviceKey;
    }

    // Owns polling and shutdown, while MainForm marshals readings onto its UI thread.
    internal sealed class DpiMonitor : IDisposable
    {
        private readonly IRazerDeviceClient device;
        private readonly Action<DpiReading> onReading;
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private Thread thread;
        private bool disposed;
        private int lastDpi = -1, lastStage = -1;

        internal DpiMonitor(IRazerDeviceClient device, Action<DpiReading> onReading)
        {
            this.device = device;
            this.onReading = onReading;
        }
        public void Start()
        {
            if (disposed) throw new ObjectDisposedException("DpiMonitor");
            if (thread != null) return;
            thread = new Thread(Run) { IsBackground = true, Name = "RazerDpiMonitorWorker" };
            thread.Start();
        }
        private void Run()
        {
            try
            {
                do
                {
                    if (stop.WaitOne(0)) break;
                    try
                    {
                        int dpi, stage, count; DpiReading reading;
                        var concrete = device as RazerDeviceClient;
                        bool ok;
                        if (concrete != null) ok = concrete.FastQueryDpiReading(out reading);
                        else { ok = device.FastQueryDpi(out dpi, out stage, out count); reading = new DpiReading { Dpi = dpi, Stage = stage, Count = count }; }
                        dpi = reading.Dpi; stage = reading.Stage; count = reading.Count;
                        if (ok && !stop.WaitOne(0))
                        {
                            bool changed = dpi > 0 && lastDpi != -1 && (dpi != lastDpi || stage != lastStage);
                            reading.Changed = changed; onReading(reading);
                            if (dpi > 0) { lastDpi = dpi; lastStage = stage; }
                        }
                    }
                    catch { }
                } while (!stop.WaitOne(200));
            }
            finally { stop.Dispose(); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (thread == null) { stop.Dispose(); return; }
            // The worker owns the wait handle; do not dispose it during a HID call.
            try { stop.Set(); } catch (ObjectDisposedException) { }
        }
    }
}
