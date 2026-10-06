namespace RazerBatteryTray
{
    internal static partial class RazerProtocol
    {
        internal const int MinimumRotation = -44;
        internal const int MaximumRotation = 44;

        internal static bool TryEncodeRotation(int angle, out byte[] payload)
        {
            if (angle < MinimumRotation || angle > MaximumRotation)
            {
                payload = null;
                return false;
            }
            payload = new byte[] { 1, 1, unchecked((byte)(sbyte)angle) };
            return true;
        }

        internal static bool TryDecodeRotationPayload(byte[] report, int offset, out int angle)
        {
            angle = 0;
            if (report == null || offset < 0 || offset > report.Length - 11 ||
                report[offset + 5] < 3 || report[offset + 8] != 1 || report[offset + 9] != 1) return false;
            angle = unchecked((sbyte)report[offset + 10]);
            return angle >= MinimumRotation && angle <= MaximumRotation;
        }

        internal static int DecodeLegacyPollingRate(byte divisor)
        {
            switch (divisor) { case 1: return 1000; case 2: return 500; case 8: return 125; default: return 0; }
        }
        internal static int DecodePollingRate(byte value)
        {
            switch (value)
            {
                case 0x01: return 8000;
                case 0x02: return 4000;
                case 0x04: return 2000;
                case 0x08: return 1000;
                case 0x10: return 500;
                case 0x40: return 125;
                default: return 0;
            }
        }

        internal static byte EncodePollingRate(int hz)
        {
            byte value;
            if (!TryEncodePollingRate(PollingProtocolKind.HighRate, hz, out value)) throw new System.ArgumentOutOfRangeException("hz");
            return value; // Compatibility helper for existing valid-value fixtures only.
        }

        internal static byte CalculateCrc(byte[] report, int startOffset)
        {
            byte crc = 0;
            for (int i = startOffset + 2; i < startOffset + 88; i++)
            {
                crc ^= report[i];
            }
            return crc;
        }

        internal static byte[] CreateRazerReport(byte transactionId, byte commandClass, byte commandId, byte dataSize, int totalLength, bool prependedZero, byte[] args = null)
        {
            byte[] report = new byte[totalLength];
            int offset = prependedZero ? 1 : 0;

            report[offset + 0] = 0x00;
            report[offset + 1] = transactionId;
            report[offset + 2] = 0x00;
            report[offset + 3] = 0x00;
            report[offset + 4] = 0x00;
            report[offset + 5] = dataSize;
            report[offset + 6] = commandClass;
            report[offset + 7] = commandId;

            if (args != null)
            {
                for (int i = 0; i < args.Length && i < 80; i++)
                {
                    report[offset + 8 + i] = args[i];
                }
            }

            report[offset + 88] = CalculateCrc(report, offset);
            report[offset + 89] = 0x00;

            return report;
        }
    }
}
