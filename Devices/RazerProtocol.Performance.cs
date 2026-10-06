namespace RazerBatteryTray
{
    internal static partial class RazerProtocol
    {
        internal static bool TryEncodePollingRate(PollingProtocolKind kind, int hz, out byte value)
        {
            value = 0;
            if (kind == PollingProtocolKind.Legacy) {
                switch (hz) { case 125: value = 8; break; case 500: value = 2; break; case 1000: value = 1; break; default: return false; }
            } else if (kind == PollingProtocolKind.HighRate || kind == PollingProtocolKind.V4HighRate) {
                if(kind == PollingProtocolKind.V4HighRate && hz == 250) return false;
                switch (hz) { case 125: value = 0x40; break; case 250: value = 0x20; break; case 500: value = 0x10; break;
                    case 1000: value = 8; break; case 2000: value = 4; break; case 4000: value = 2; break; case 8000: value = 1; break; default: return false; }
            } else return false;
            return true;
        }
        internal static int DecodeRate(PollingProtocolKind kind, byte value)
        { return kind == PollingProtocolKind.Legacy ? DecodeLegacyPollingRate(value) : kind == PollingProtocolKind.HighRate ? (value == 0x20 ? 250 : DecodePollingRate(value)) : kind == PollingProtocolKind.V4HighRate ? DecodePollingRate(value) : 0; }
        internal static int DecodeLegacyDpiByte(byte value) { return value * 6750 / 255; }
        internal static bool TryEncodeLegacyDpiByte(int dpi, out byte value)
        {
            value = 0;
            // Only exact integer round trips under the pinned daemon scale.
            if (dpi < 100 || dpi > 6750 || dpi * 255 % 6750 != 0) return false;
            value = (byte)(dpi * 255 / 6750);
            return DecodeLegacyDpiByte(value) == dpi;
        }
        internal static byte[] BuildModernDpiGet(byte tid, byte storage, int length)
        { return CreateRazerReport(tid, 4, 0x85, 7, length, length == 91, new byte[] { storage }); }
        internal static byte[] BuildModernDpiSet(byte tid, byte storage, int x, int y, int length)
        { return CreateRazerReport(tid, 4, 5, 7, length, length == 91, new byte[] { storage, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0, 0 }); }
        internal static byte[] BuildLegacyDpiByteGet(byte tid, int length)
        { return CreateRazerReport(tid, 4, 0x81, 3, length, length == 91); }
        internal static byte[] BuildLegacyDpiByteSet(byte tid, byte x, byte y, int length)
        { return CreateRazerReport(tid, 4, 1, 3, length, length == 91, new byte[] { x, y, 0 }); }
        internal static byte[] BuildDpiStagesGet(byte tid, int length)
        { return CreateRazerReport(tid, 4, 0x86, 0x26, length, length == 91, new byte[] { 1 }); }
        internal static byte[] BuildDpiStagesSet(byte tid, byte[] payload, int length)
        { return CreateRazerReport(tid, 4, 6, 0x26, length, length == 91, payload); }
        internal static byte[] BuildV4DpiStagesGet(int length)
        { return CreateRazerReport(0x1F,4,0x86,80,length,length==91); }
        internal static byte[] BuildV4DpiStagesSet(byte[] payload,int length)
        {
            if(payload==null || payload.Length<10 || payload.Length>80) throw new System.ArgumentException("Invalid V4 stage payload");
            return CreateRazerReport(0x1F,4,6,(byte)payload.Length,length,length==91,payload);
        }
        internal static byte[] BuildPollingGet(PollingProtocolKind kind, byte tid, int length)
        { return kind==PollingProtocolKind.V4HighRate ? CreateRazerReport(tid,0,0xC0,2,length,length==91,new byte[]{1,0}) : CreateRazerReport(tid, 0, kind == PollingProtocolKind.Legacy ? (byte)0x85 : (byte)0xC0, 1, length, length == 91); }
        internal static byte[] BuildPollingSet(PollingProtocolKind kind, byte tid, byte storage, byte rate, int length)
        { var args = kind == PollingProtocolKind.Legacy ? new byte[] { rate } : new byte[] { kind==PollingProtocolKind.V4HighRate?(byte)1:storage, rate };
            return CreateRazerReport(tid, 0, kind == PollingProtocolKind.Legacy ? (byte)5 : (byte)0x40, (byte)args.Length, length, length == 91, args); }
        internal static byte[] BuildBatteryGet(byte tid, int length)
        { return CreateRazerReport(tid, 7, 0x80, 2, length, length == 91); }
        internal static byte[] BuildChargingGet(byte tid, int length)
        { return CreateRazerReport(tid, 7, 0x84, 2, length, length == 91); }
    }
}
