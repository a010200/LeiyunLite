using System;

namespace RazerBatteryTray
{
    internal enum ReadProbeOutcome { Success, NoResponse, UnsupportedOrTimeout, Malformed }

    internal sealed partial class RazerDeviceClient
    {
        private DeviceCapabilityProfile selectedPerformance;
        private string selectedPerformanceTarget;
        private static string TargetSignature(HidDescriptor d)
        {
            return d==null?null:d.VendorId+"|"+d.ProductId+"|"+d.InstanceKey+"|"+(d.Path??"").ToLowerInvariant()+"|"+
                d.Version+"|"+d.ReportLength+"|"+d.UsagePage+"|"+d.Usage;
        }
        private DeviceCapabilityProfile SelectedPerformanceProfile
        { get { return selected!=null && selectedPerformanceTarget==TargetSignature(selected)?selectedPerformance:null; } }
        private void ClearPerformanceSession() { selectedPerformance=null; selectedPerformanceTarget=null; }
        private void RetireStalePerformanceSession(PerformanceWriteResult result)
        {
            if(!result.WriteAttempted && result.Error=="device-changed" && selectedPerformance!=null &&
                selectedPerformance.TransactionProbePolicy==TransactionProbePolicy.ReadOnlySessionFallback) {
                ClearPerformanceSession(); selectedDpiReady=selectedPollingReady=selectedStagesReady=false;
            }
        }
        private static HidDescriptor SnapshotDescriptor(HidDescriptor d)
        {
            return new HidDescriptor { VendorId=d.VendorId,ProductId=d.ProductId,Version=d.Version,ReportLength=d.ReportLength,
                UsagePage=d.UsagePage,Usage=d.Usage,Path=d.Path,Serial=d.Serial,ContainerId=d.ContainerId,ProductString=d.ProductString };
        }
        private static ReadProbeOutcome ProbeReadTransaction(IHidDevice device,DeviceCapabilityProfile p)
        {
            // Only the reviewed Ultimate profiles reach this code; no SET or
            // enumeration of transaction IDs is allowed in the resolver.
            var request=RazerProtocol.BuildModernDpiGet(p.DpiTransaction,p.DpiGetStorage,device.ReportLength);
            byte[] reply;
            try { reply=device.Exchange(request,p.ReadDelayMs); } catch(Exception) { return ReadProbeOutcome.Malformed; }
            if(reply==null) return ReadProbeOutcome.NoResponse;
            if(!ValidPerformanceFrame(device,request,reply)) return ReadProbeOutcome.Malformed;
            int o=Offset(device);
            if(reply[o]==4 || reply[o]==5) return ReadProbeOutcome.UnsupportedOrTimeout;
            if(reply[o]!=2) return ReadProbeOutcome.Malformed;
            int x=reply[o+9]<<8|reply[o+10], y=reply[o+11]<<8|reply[o+12];
            return ValidDpiRead(p,x) && ValidDpiRead(p,y)?ReadProbeOutcome.Success:ReadProbeOutcome.Malformed;
        }
        private bool ResolvePerformanceProfile(IHidDevice device,HidDescriptor descriptor,out DeviceCapabilityProfile effective)
        {
            effective=DeviceCapabilityCatalog.Find(descriptor.ProductId);
            if(effective==null || !effective.AcceptsDescriptor(descriptor) || device.ReportLength!=descriptor.ReportLength) return false;
            if(effective.TransactionProbePolicy==TransactionProbePolicy.None) return true;
            if(descriptor.ProductId!=0x007A && descriptor.ProductId!=0x007B) return false;
            string signature=TargetSignature(descriptor);
            if(selectedPerformanceTarget==signature && selectedPerformance!=null) { effective=selectedPerformance; return true; }
            ReadProbeOutcome outcome=ProbeReadTransaction(device,effective);
            if(signature!=TargetSignature(Describe(device))) return false;
            if(outcome==ReadProbeOutcome.Success) return true;
            if(outcome!=ReadProbeOutcome.NoResponse && outcome!=ReadProbeOutcome.UnsupportedOrTimeout) return false;
            foreach(byte tid in effective.AlternatePerformanceTransactions) {
                if(tid!=0x3F) return false; // Approved list is intentionally closed.
                var alternate=effective.Clone();
                alternate.DpiTransaction=alternate.DpiSetTransaction=alternate.DpiStageTransaction=tid;
                alternate.PollingTransaction=alternate.PollingSetTransaction=tid;
                alternate.BatteryTransaction=alternate.ChargingTransaction=tid;
                outcome=ProbeReadTransaction(device,alternate);
                if(signature!=TargetSignature(Describe(device))) return false;
                if(outcome==ReadProbeOutcome.Success) { effective=alternate; return true; }
                if(outcome==ReadProbeOutcome.Malformed) return false;
            }
            return false;
        }
    }
}
