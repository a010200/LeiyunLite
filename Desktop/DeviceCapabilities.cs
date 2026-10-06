using System;

namespace RazerBatteryTray.Desktop
{
    // Only explicitly identified combinations are writable. A generic receiver
    // does not identify its paired mouse and must never imply an 8 kHz capability.
    internal sealed class DeviceCapabilities
    {
        public int ProductId, MinimumDpi, MaximumDpi;
        public int[] Rates = new int[0];
        public int[] AvailableDpi = new int[0];
        public bool Known, RotationProtocolCandidate;
        public static DeviceCapabilities For(int pid)
        {
            var c = new DeviceCapabilities { ProductId = pid };
            var p = DeviceCapabilityCatalog.Find(pid);
            if (p != null && p.Transport == TransportKind.HidFeature90Or91) {
                c.Known = p.GetDpi || p.GetPolling; c.MinimumDpi = p.MinimumDpi; c.MaximumDpi = p.MaximumDpi;
                c.AvailableDpi = (int[])p.AvailableDpi.Clone(); c.Rates = (int[])p.PollRates.Clone();
            }
            // Preserve the old static receiver query for compatibility. Actual
            // command and UI permissions still use the exact live descriptor.
            c.RotationProtocolCandidate = pid == 0x00DF;
            return c;
        }
        public bool AcceptsDpi(int value) { return Known && MinimumDpi > 0 && value >= MinimumDpi && value <= MaximumDpi && (AvailableDpi.Length == 0 || Array.IndexOf(AvailableDpi,value) >= 0); }
        public bool AcceptsRate(int value) { return Known && Array.IndexOf(Rates, value) >= 0; }
        public bool AcceptsRotation(int value) { return RotationProtocolCandidate && value >= RazerProtocol.MinimumRotation && value <= RazerProtocol.MaximumRotation; }
        internal static DeviceCapabilities For(HidDescriptor descriptor)
        {
            var c = For(descriptor == null ? 0 : descriptor.ProductId);
            c.RotationProtocolCandidate = RazerProtocolProfile.SupportsRotation(descriptor);
            return c;
        }
        internal static DeviceCapabilities For(MouseBatteryInfo reading)
        {
            var c = For(reading == null ? 0 : reading.ProductId);
            // Probe mints these flags only after the shared descriptor gate and
            // a successful live GET. The command path rechecks its pinned descriptor.
            c.RotationProtocolCandidate = reading != null &&
                (reading.ProductId == 0x00DE || reading.ProductId == 0x00DF) &&
                reading.ProtocolStatus == DeviceProtocolStatus.Ready && reading.RotationKnown &&
                reading.IsRotationWriteSupported && reading.IsRotationHardwareVerified;
            return c;
        }
    }
}
