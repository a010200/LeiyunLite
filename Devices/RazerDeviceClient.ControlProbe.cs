using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    internal sealed partial class RazerDeviceClient
    {
        private readonly RazerControlPathResolver controlPaths=new RazerControlPathResolver();
        internal readonly Dictionary<string,ControlProbeOutcome> ControlProbeOutcomes=new Dictionary<string,ControlProbeOutcome>(StringComparer.OrdinalIgnoreCase);
        private bool VerifyLockedTopology()
        {
            if(!controlPaths.IsLocked(selected)) return false;
            var descriptors=new List<HidDescriptor>();
            try {
                transport.Visit(device=> { var d=Describe(device); if(d!=null && d.IsRazer) descriptors.Add(SnapshotDescriptor(d)); return false; });
                if(controlPaths.PoolUnchanged(selected,descriptors)) return true;
            } catch(Exception) { }
            controlPaths.Clear(); ClearPerformanceSession(); selectedDpiReady=selectedPollingReady=selectedStagesReady=false;
            return false;
        }
        // Native handles never escape Visit. This adapter prohibits SET during
        // discovery and records transport/frame failures separately from silence.
        private sealed class ControlProbeDevice:IHidDevice,IHidDescriptor,ISequencedHidFeatureDevice
        {
            private readonly IHidDevice inner;
            private byte[] pending;
            internal ControlProbeOutcome Outcome=ControlProbeOutcome.NoResponse;
            internal ControlProbeDevice(IHidDevice device) { inner=device; }
            public HidDescriptor Descriptor { get { return Describe(inner); } }
            public int ProductId { get { return Descriptor.ProductId; } }
            public string ProductName { get { return inner.ProductName; } }
            public int ReportLength { get { return inner.ReportLength; } }
            private void Guard(byte[] request)
            {
                int o=Offset(inner);
                if(request==null || request.Length!=ReportLength || request[o+7]<0x80)
                    throw new InvalidOperationException("Control discovery permits GET only");
            }
            private byte[] Inspect(byte[] request,byte[] reply)
            {
                ControlProbeOutcome next;
                if(reply==null) next=ControlProbeOutcome.NoResponse;
                else if(!ValidPerformanceFrame(inner,request,reply)) next=ControlProbeOutcome.Malformed;
                else switch(reply[Offset(inner)]) {
                    case 2: next=ControlProbeOutcome.Success; break;
                    case 1: next=ControlProbeOutcome.BusyTimeout; break;
                    case 4: case 5: next=ControlProbeOutcome.Unsupported; break;
                    default: next=ControlProbeOutcome.Malformed; break;
                }
                // Preserve a concrete failure instead of replacing it by a later timeout.
                if(next!=ControlProbeOutcome.NoResponse || Outcome==ControlProbeOutcome.NoResponse) Outcome=next;
                return reply;
            }
            public byte[] Exchange(byte[] request,int delay)
            {
                Guard(request);
                try { return Inspect(request,inner.Exchange(request,delay)); }
                catch(Exception) { Outcome=ControlProbeOutcome.TransportError; throw; }
            }
            public bool SendFeature(byte[] request)
            {
                Guard(request); pending=request;
                var sequence=inner as ISequencedHidFeatureDevice;
                if(sequence==null) { Outcome=ControlProbeOutcome.TransportError; return false; }
                try { bool ok=sequence.SendFeature(request); if(!ok) Outcome=ControlProbeOutcome.TransportError; return ok; }
                catch(Exception) { Outcome=ControlProbeOutcome.TransportError; throw; }
            }
            public byte[] GetFeature(int delay)
            {
                try { return Inspect(pending,((ISequencedHidFeatureDevice)inner).GetFeature(delay)); }
                catch(Exception) { Outcome=ControlProbeOutcome.TransportError; throw; }
            }
        }
        private MouseBatteryInfo ProbeControl(IHidDevice device,HidDescriptor descriptor,out DeviceCapabilityProfile effective)
        {
            var guarded=new ControlProbeDevice(device);
            var result=Probe(guarded,descriptor,RazerIdentityCatalog.Find(descriptor.ProductId),out effective);
            bool live=result.DpiKnown || result.PollingKnown || result.BatteryKnown ||
                effective!=null && effective.ChargingProtocol==ChargingProtocolKind.Query && result.ChargingKnown;
            if(TargetSignature(descriptor)!=TargetSignature(Describe(device)) || device.ReportLength!=descriptor.ReportLength) {
                live=false; guarded.Outcome=ControlProbeOutcome.Malformed;
            }
            var outcome=live?ControlProbeOutcome.Success:guarded.Outcome==ControlProbeOutcome.Success?ControlProbeOutcome.Malformed:guarded.Outcome;
            ControlProbeOutcomes[descriptor.Path]=outcome;
            if(!live) {
                result.ProtocolStatus=DeviceProtocolStatus.PresentUnresponsive;
                result.IsWriteSupported=result.IsDpiWriteSupported=result.IsPollingWriteSupported=result.IsRotationWriteSupported=false;
            }
            return result;
        }
    }
}
