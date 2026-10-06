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
                var p = RazerProtocolProfile.For(selected);
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
            if (string.IsNullOrEmpty(expectedKey)) return result;
            lock (hidLock) {
                if (selected == null || selected.ProductId != pid || !DeviceCapabilities.For(selected).AcceptsRotation(angle)) return result;
                var p = RazerProtocolProfile.For(selected);
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
    }
}
