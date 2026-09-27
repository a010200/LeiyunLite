using System;

namespace RazerBatteryTray.Desktop
{
    // Only explicitly identified combinations are writable. A generic receiver
    // does not identify its paired mouse and must never imply an 8 kHz capability.
    internal sealed class DeviceCapabilities
    {
        public int ProductId, MinimumDpi, MaximumDpi;
        public int[] Rates = new int[0];
        public bool Known, RotationProtocolCandidate;
        public static DeviceCapabilities For(int pid)
        {
            var c = new DeviceCapabilities { ProductId = pid };
            switch (pid)
            {
                case 0x00DE: // Viper V3 Pro SE, cable
                case 0x00DF: // Viper V3 Pro SE, bundled receiver
                    c.Known = true; c.MinimumDpi = 100; c.MaximumDpi = 35000;
                    c.Rates = new[] { 125, 500, 1000 };
                    c.RotationProtocolCandidate = pid == 0x00DF; break;
                case 0x00C0: // Viper V3 Pro, cable
                case 0x00C1: // Viper V3 Pro, dedicated receiver
                    c.Known = true; c.MinimumDpi = 100; c.MaximumDpi = 35000;
                    c.Rates = pid == 0x00C1 ? new[] { 125, 500, 1000, 2000, 4000, 8000 } : new[] { 125, 500, 1000 }; break;
            }
            return c;
        }
        public bool AcceptsDpi(int value) { return Known && value >= MinimumDpi && value <= MaximumDpi; }
        public bool AcceptsRate(int value) { return Known && Array.IndexOf(Rates, value) >= 0; }
        public bool AcceptsRotation(int value) { return RotationProtocolCandidate && value >= RazerProtocol.MinimumRotation && value <= RazerProtocol.MaximumRotation; }
    }
}
