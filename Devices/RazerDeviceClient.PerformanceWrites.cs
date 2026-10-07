using System;

namespace RazerBatteryTray
{
    internal class PerformanceWriteResult
    {
        internal bool Success, WriteAttempted, RollbackAttempted, RollbackSucceeded;
        internal int Before, After;
        internal string Error = "unsupported";
    }
    internal sealed class DpiWriteResult : PerformanceWriteResult { }
    internal sealed class PollingWriteResult : PerformanceWriteResult { }

    internal sealed partial class RazerDeviceClient
    {
        private bool PerformanceTarget(IHidDevice device, int pid, string key, string path)
        { return !string.IsNullOrEmpty(key) && Matches(device,pid,key) && (path == null || string.Equals(path,selected.Path,StringComparison.OrdinalIgnoreCase)); }

        private bool WriteDpiPayload(IHidDevice device, DeviceCapabilityProfile p, DpiSnapshot before, byte[] payload, int pid, string key, string path)
        {
            if (!PerformanceTarget(device,pid,key,path)) return false;
            byte[] report = before.UsesStages ? p.DpiProtocol==DpiProtocolKind.V4Stages?RazerProtocol.BuildV4DpiStagesSet(payload,device.ReportLength):RazerProtocol.BuildDpiStagesSet(p.DpiStageTransaction,payload,device.ReportLength) :
                p.DpiProtocol == DpiProtocolKind.ModernXY ? RazerProtocol.BuildModernDpiSet(p.DpiSetTransaction,p.DpiSetStorage,payload[1]<<8|payload[2],payload[3]<<8|payload[4],device.ReportLength) :
                RazerProtocol.BuildLegacyDpiByteSet(p.DpiSetTransaction,payload[0],payload[1],device.ReportLength);
            return ExchangeValidated(device,report,p.WriteDelayMs) != null;
        }
        private static bool DpiPayloadMatches(DpiSnapshot snapshot, byte[] expected)
        {
            if (snapshot == null || snapshot.Payload.Length != expected.Length) return false;
            int start = snapshot.V4?0:snapshot.UsesStages || expected.Length == 7 ? 1 : 0;
            int end = snapshot.UsesStages && !snapshot.V4 ? 3 + snapshot.Count*7 : expected.Length;
            for(int i=start;i<end;i++) if(snapshot.Payload[i]!=expected[i]) return false;
            return true;
        }
        internal DpiWriteResult SetDpiTransactional(int pid, int value, string expectedKey, string expectedPath = null)
        { return SetDpiCore(pid,value,0,expectedKey,expectedPath); }
        private DpiWriteResult SetDpiCore(int pid, int value, int targetStage, string key, string path)
        {
            var result = new DpiWriteResult();
            if(string.IsNullOrEmpty(key)) return result;
            lock(hidLock) {
                if(!VerifyLockedTopology()) return result;
                var p = SelectedPerformanceProfile;
                if(p == null || selected.ProductId != pid || !selectedDpiReady || !p.SetDpi || p.Transport != TransportKind.HidFeature90Or91 ||
                    targetStage == 0 && !p.AcceptsDpi(value) || targetStage != 0 && (!p.SetStages || !selectedStagesReady)) return result;
                try { transport.Visit(device => {
                    if(!PerformanceTarget(device,pid,key,path)) { result.Error="device-changed"; return false; }
                    DpiSnapshot before;
                    if(!ReadDpiSnapshot(device,p,p.GetStages,out before)) { result.Error="initial-read-failed"; return true; }
                    result.Before=result.After=before.X;
                    byte[] original=(byte[])before.Payload.Clone(), target=(byte[])original.Clone();
                    if(before.UsesStages) {
                        if(!p.SetStages || !selectedStagesReady) return true;
                        int index=-1;
                        for(int i=0;i<before.Count;i++) if(target[3+i*7]+(p.DpiProtocol==DpiProtocolKind.V4Stages?1:0) == (targetStage == 0 ? before.Active : targetStage)) index=3+i*7;
                        if(index<0) { result.Error="invalid-stage"; return true; }
                        if(!before.V4) target[0]=1;
                        if(targetStage != 0) target[1]=(byte)targetStage;
                        else {
                            target[index+1]=target[index+3]=(byte)(value>>8); target[index+2]=target[index+4]=(byte)value;
                        }
                    } else if(p.DpiProtocol == DpiProtocolKind.LegacyByte) {
                        byte encoded; if(!RazerProtocol.TryEncodeLegacyDpiByte(value,out encoded)) return true;
                        target[0]=target[1]=encoded;
                    } else {
                        target[0]=p.DpiSetStorage; target[1]=target[3]=(byte)(value>>8); target[2]=target[4]=(byte)value;
                    }
                    if(DpiPayloadMatches(before,target)) { result.Success=true; result.Error=""; return true; }
                    if(!PerformanceTarget(device,pid,key,path)) { result.Error="device-changed"; return true; }
                    result.WriteAttempted=true;
                    try {
                        bool ack=WriteDpiPayload(device,p,before,target,pid,key,path);
                        DpiSnapshot after=null;
                        bool live=PerformanceTarget(device,pid,key,path) && ReadDpiSnapshot(device,p,before.UsesStages,out after);
                        if(live) result.After=after.X;
                        if(ack && live && DpiPayloadMatches(after,target)) { result.Success=true; result.Error=""; return true; }
                        result.Error=!ack?"write-not-acknowledged":!live?"readback-failed":"readback-mismatch";
                    } catch(Exception) { result.Error="transport-exception"; }
                    // An exception may occur after delivery. Restore once, using
                    // the original raw bytes, and independently confirm readback.
                    result.RollbackAttempted=true;
                    try { WriteDpiPayload(device,p,before,original,pid,key,path); } catch(Exception) { }
                    try {
                        DpiSnapshot restored=null;
                        result.RollbackSucceeded=PerformanceTarget(device,pid,key,path) && ReadDpiSnapshot(device,p,before.UsesStages,out restored) && DpiPayloadMatches(restored,original);
                        if(result.RollbackSucceeded) result.After=restored.X;
                    } catch(Exception) { result.RollbackSucceeded=false; }
                    if(!result.RollbackSucceeded) result.Error+=";rollback-failed";
                    return true;
                }); } catch(Exception) { result.Error="transport-exception"; }
                RetireStalePerformanceSession(result);
            }
            return result;
        }
        private bool WritePolling(IHidDevice device, DeviceCapabilityProfile p, int rate, int pid, string key, string path)
        {
            byte encoded;
            if(!p.AcceptsRate(rate) || !RazerProtocol.TryEncodePollingRate(p.PollingProtocol,rate,out encoded) || !PerformanceTarget(device,pid,key,path)) return false;
            try {
                bool ack=ExchangeValidated(device,RazerProtocol.BuildPollingSet(p.PollingProtocol,p.PollingSetTransaction,0,encoded,device.ReportLength),p.WriteDelayMs)!=null;
                if(ack && p.PollingSecondTransaction != 0) {
                    if(!PerformanceTarget(device,pid,key,path)) return false;
                    // Some profiles have a distinct TID for the second storage pass.
                    bool second=ExchangeValidated(device,RazerProtocol.BuildPollingSet(p.PollingProtocol,p.PollingSecondTransaction,1,encoded,device.ReportLength),p.WriteDelayMs)!=null;
                    ack=ack && second;
                }
                return ack;
            } finally {
                // Delivery may precede a transport exception. Let RF settle
                // before any readback or restoration in that path as well.
                if(p.PostPollingSettleMs>0) System.Threading.Thread.Sleep(p.PostPollingSettleMs);
            }
        }
        internal PollingWriteResult SetPollingTransactional(int pid,int value,string key,string path = null)
        {
            var result=new PollingWriteResult(); if(string.IsNullOrEmpty(key)) return result;
            lock(hidLock) {
                if(!VerifyLockedTopology()) return result;
                var p=SelectedPerformanceProfile;
                if(p==null || selected.ProductId!=pid || !selectedPollingReady || !p.SetPolling || !p.AcceptsRate(value) || p.Transport!=TransportKind.HidFeature90Or91) return result;
                try { transport.Visit(device => {
                    if(!PerformanceTarget(device,pid,key,path)) { result.Error="device-changed"; return false; }
                    int before;
                    if(!ReadPolling(device,p,out before)) { result.Error="initial-read-failed"; return true; }
                    result.Before=result.After=before;
                    if(before==value) { result.Success=true; result.Error=""; return true; }
                    if(!PerformanceTarget(device,pid,key,path)) { result.Error="device-changed"; return true; }
                    result.WriteAttempted=true;
                    try {
                        bool ack=WritePolling(device,p,value,pid,key,path); int after=0;
                        bool live=PerformanceTarget(device,pid,key,path) && ReadPolling(device,p,out after);
                        if(live) result.After=after;
                        if(ack && live && after==value) { result.Success=true; result.Error=""; return true; }
                        result.Error=!ack?"write-not-acknowledged":!live?"readback-failed":"readback-mismatch";
                    } catch(Exception) { result.Error="transport-exception"; }
                    result.RollbackAttempted=true;
                    try { WritePolling(device,p,before,pid,key,path); } catch(Exception) { }
                    try { int restored=0; result.RollbackSucceeded=PerformanceTarget(device,pid,key,path) && ReadPolling(device,p,out restored) && restored==before; if(result.RollbackSucceeded) result.After=restored; }
                    catch(Exception) { result.RollbackSucceeded=false; }
                    if(!result.RollbackSucceeded) result.Error+=";rollback-failed";
                    return true;
                }); } catch(Exception) { result.Error="transport-exception"; }
                RetireStalePerformanceSession(result);
            }
            return result;
        }
        // Legacy unkeyed entry points are confined to the existing four-PID
        // callers. New upstream profiles require an explicit expected key.
        private string LegacyWriteKey(int pid,string key)
        { return key ?? (selected != null && selected.ProductId==pid && (pid==0xc0 || pid==0xc1 || pid==0xde || pid==0xdf) ? selected.InstanceKey : null); }
        internal bool SetDpiVerified(int pid,int value,string expectedKey = null)
        { lock(hidLock) return SetDpiTransactional(pid,value,LegacyWriteKey(pid,expectedKey)).Success; }
        internal bool SetRateVerified(int pid,int value,string expectedKey = null)
        { lock(hidLock) return SetPollingTransactional(pid,value,LegacyWriteKey(pid,expectedKey)).Success; }
    }
}
