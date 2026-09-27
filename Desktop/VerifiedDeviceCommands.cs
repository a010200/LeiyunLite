using System;
using RazerBatteryTray.Desktop;

namespace RazerBatteryTray
{
    internal sealed class RotationWriteResult
    {
        internal bool Success, WriteAttempted, RollbackAttempted, RollbackSucceeded;
        internal int Before, After;
        internal string Error = "";
    }

    internal sealed partial class RazerDeviceClient
    {
        private bool Matches(IHidDevice device, int pid, string expectedKey)
        {
            var identity = device as IHidIdentity;
            return identity != null && identity.ProductId == pid && IsSelected(device)
                && (expectedKey == null || selected.InstanceKey == expectedKey);
        }
        private static bool Reply(IHidDevice device, byte[] reply, byte cls, byte cmd)
        {
            int o = Offset(device);
            return Success(reply, o) && reply[o + 6] == cls && reply[o + 7] == cmd && reply[o + 1] == 0x1F
                && reply[o + 88] == RazerProtocol.CalculateCrc(reply, o);
        }
        private static bool WriteRotation(IHidDevice device, int angle)
        {
            byte[] payload;
            return RazerProtocol.TryEncodeRotation(angle, out payload) &&
                Reply(device, Send(device, 0x1F, 0x0B, 0x14, 3, payload, 80), 0x0B, 0x14);
        }
        internal bool TryGetRotationVerified(int pid, string expectedKey, out int angle)
        {
            angle = 0;
            lock (hidLock) {
                var p = selected == null ? null : RazerProtocolProfile.For(selected.ProductId);
                if (p == null || !p.RotationReadVerified || !RazerProtocolProfile.SupportsRotation(selected) || string.IsNullOrEmpty(expectedKey)) return false;
                bool ok = false; int value = 0;
                transport.Visit(device => {
                    if (!Matches(device, pid, expectedKey) || !RazerProtocolProfile.SupportsRotation(Describe(device))) return false;
                    ok = TryReadRotation(device, out value); return true;
                });
                angle = value; return ok;
            }
        }
        internal RotationWriteResult SetRotationVerified(int pid, int angle, string expectedKey = null)
        { return SetRotationCore(pid, angle, expectedKey, false); }

        // Called only by isolated hardware research tools and fault-injection tests.
        internal RotationWriteResult SetRotationForResearch(int pid, int angle, string expectedKey)
        { return SetRotationCore(pid, angle, expectedKey, true); }

        private RotationWriteResult SetRotationCore(int pid, int angle, string expectedKey, bool research)
        {
            var result = new RotationWriteResult { Error = "unsupported" };
            if (!DeviceCapabilities.For(pid).AcceptsRotation(angle) || string.IsNullOrEmpty(expectedKey)) return result;
            lock (hidLock) {
                var p = selected == null ? null : RazerProtocolProfile.For(selected.ProductId);
                if (p == null || !p.RotationReadVerified || !p.RotationWriteCandidate || !RazerProtocolProfile.SupportsRotation(selected)) return result;
                if (!research && !p.RotationWriteVerified) return result;
                transport.Visit(device => {
                    if (!Matches(device, pid, expectedKey) || !RazerProtocolProfile.SupportsRotation(Describe(device))) { result.Error = "device-changed"; return false; }
                    int before;
                    if (!TryReadRotation(device, out before)) { result.Error = "initial-read-failed"; return true; }
                    result.Before = result.After = before;
                    if (before == angle) { result.Success = true; result.Error = ""; return true; }
                    result.WriteAttempted = true;
                    try {
                        bool acknowledged = WriteRotation(device, angle);
                        int after; bool readBack = TryReadRotation(device, out after);
                        if (readBack) result.After = after;
                        if (acknowledged && readBack && after == angle) {
                            result.Success = true; result.Error = ""; return true;
                        }
                        result.Error = !acknowledged ? "write-not-acknowledged" : !readBack ? "readback-failed" : "readback-mismatch";
                    }
                    catch (Exception) { result.Error = "transport-exception"; }
                    result.RollbackAttempted = true;
                    // A transport may throw after delivering a report. Restoration
                    // is still attempted once and verified independently of its ACK.
                    try { WriteRotation(device, before); } catch (Exception) { }
                    try {
                        int restored;
                        result.RollbackSucceeded = TryReadRotation(device, out restored) && restored == before;
                        if (result.RollbackSucceeded) result.After = restored;
                    } catch (Exception) { result.RollbackSucceeded = false; }
                    if (!result.RollbackSucceeded) result.Error += ";rollback-failed";
                    return true;
                });
            }
            return result;
        }
        // Read / modify / read the active stage; do not replace the user's other stages.
        internal bool SetDpiVerified(int pid, int value, string expectedKey = null)
        {
            if (!DeviceCapabilities.For(pid).AcceptsDpi(value)) return false;
            return Write(device => {
                if (!Matches(device, pid, expectedKey)) return false;
                int validDpi, validStage; int[] validStages;
                if (!ReadStages(device, out validDpi, out validStage, out validStages)) return false;
                int o = Offset(device);
                var before = Send(device, 0x1F, 4, 0x86, 0x26, new byte[] { 1 });
                if (!Reply(device, before, 4, 0x86)) return false;
                int count = before[o + 10], active = before[o + 9], index = -1;
                if (count < 1 || count > 5) return false;
                var payload = new byte[0x26]; Array.Copy(before, o + 8, payload, 0, payload.Length);
                for (int i = 0; i < count; i++) if (payload[3 + i * 7] == active) index = 3 + i * 7;
                if (index < 0) return false;
                payload[0] = 1;
                payload[index + 1] = payload[index + 3] = (byte)(value >> 8);
                payload[index + 2] = payload[index + 4] = (byte)value;
                if (!Reply(device, Send(device, 0x1F, 4, 6, 0x26, payload, 25), 4, 6)) return false;
                var after = Send(device, 0x1F, 4, 0x86, 0x26, new byte[] { 1 });
                if (!Reply(device, after, 4, 0x86)) return false;
                // Storage byte may differ between request and reply. Everything
                // else in the used stage payload must equal what we wrote.
                for (int i = 1; i < 3 + count * 7; i++) if (payload[i] != after[o + 8 + i]) return false;
                return true;
            });
        }
        internal bool SetRateVerified(int pid, int hz, string expectedKey = null)
        {
            if (!DeviceCapabilities.For(pid).AcceptsRate(hz)) return false;
            return Write(device => {
                if (!Matches(device, pid, expectedKey)) return false;
                bool legacy = pid == 0x00DE || pid == 0x00DF || pid == 0x00C0;
                byte get = legacy ? (byte)0x85 : (byte)0xC0, set = legacy ? (byte)0x05 : (byte)0x40;
                // Reject unknown firmware/transport before attempting any write.
                var before = Send(device, 0x1F, 0, get, 1);
                if (!Reply(device, before, 0, get)) return false;
                int old = legacy ? RazerProtocol.DecodeLegacyPollingRate(before[Offset(device) + 8]) : RazerProtocol.DecodePollingRate(before[Offset(device) + 9]);
                if (old == 0) return false;
                byte[] args = legacy ? new byte[] { (byte)(1000 / hz) } : new byte[] { 0, RazerProtocol.EncodePollingRate(hz) };
                var written = Send(device, 0x1F, 0, set, (byte)args.Length, args, 150);
                if (!Reply(device, written, 0, set)) return false;
                var read = Send(device, 0x1F, 0, get, 1);
                if (!Reply(device, read, 0, get)) return false;
                return (legacy ? RazerProtocol.DecodeLegacyPollingRate(read[Offset(device) + 8]) : RazerProtocol.DecodePollingRate(read[Offset(device) + 9])) == hz;
            });
        }
    }
}
