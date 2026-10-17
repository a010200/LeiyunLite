using System;

namespace RazerBatteryTray.Desktop
{
    // Display-only transaction state. Never changes MouseBatteryInfo or write permission.
    internal sealed class PerformanceVisualState
    {
        internal readonly bool Dpi;
        internal readonly int ProductId, Requested;
        internal readonly string DeviceKey, InterfacePath;
        internal readonly long Generation;
        internal int Displayed;
        internal bool Pending = true, Unconfirmed;
        internal long ReadBarrier = long.MaxValue;
        internal PerformanceVisualState(bool dpi, int requested, MouseBatteryInfo reading, long generation)
        {
            Dpi = dpi; Requested = Displayed = requested; ProductId = reading.ProductId;
            DeviceKey = reading.DeviceKey; InterfacePath = reading.InterfacePath; Generation = generation;
        }
        internal bool Matches(MouseBatteryInfo reading)
        {
            return reading.IsConnected && reading.ProductId == ProductId &&
                reading.DeviceKey == DeviceKey && reading.InterfacePath == InterfacePath;
        }
        internal void Complete(PerformanceWriteResult result, long readBarrier)
        {
            Pending = false; ReadBarrier = readBarrier;
            Unconfirmed = result == null || (!result.Success && !result.RollbackSucceeded);
            Displayed = Unconfirmed ? 0 : result.After;
        }
        internal bool HasFreshReading(MouseBatteryInfo reading, long appliedSequence)
        {
            return !Pending && appliedSequence > ReadBarrier && reading.ProtocolStatus == DeviceProtocolStatus.Ready &&
                !reading.IsSleeping && (Dpi ? reading.DpiKnown && reading.Dpi > 0 : reading.PollingKnown && reading.PollingRate > 0);
        }
    }
}
