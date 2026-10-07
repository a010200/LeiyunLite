using System;
using System.Linq;
using System.Collections.Generic;

namespace RazerBatteryTray.Tests
{
    internal static partial class OpenRazerCompatibilityTests
    {
        private static void RunControlPathTests()
        {
            Test("Control audit: dynamic complete PID scopes, no global consumer unlock", ControlScopes);
            Test("DA4 both PIDs desktop/consumer any Usage, 90/91 GET-only discovery", Da4Shapes);
            Test("Modern audited candidate set and V4 non-MI03 unique live control", ModernControlMatrix);
            Test("Common resolver unique/zero/two success, independent physical instances", ControlUniqueness);
            Test("Resolver keeps silence, unsupported, malformed and transport errors distinct", ControlFailures);
            Test("V4 control discovery bounded BUSY with single send per known GET", ControlBusy);
            Test("Locked refresh avoids sibling probes; explicit refresh rechecks ambiguity", ControlCaching);
            Test("Sleep/wake fresh discovery, changed candidate pool revokes old lock", ControlWake);
            Test("All descriptor identities and invalidation retire all performance locks", ControlLifetime);
            Test("Native ephemeral handles used only within Visit callback", ControlHandleLifetime);
        }
        private static void ControlScopes()
        {
            foreach(var p in DeviceCapabilityCatalog.All) {
                var d=new HidDescriptor{VendorId=0x1532,ProductId=p.ProductId,Version=0x100,ReportLength=91,UsagePage=0xC,Usage=99,Path="hid#mi_02#control"};
                Check(!d.CanProbe,"Global gate remains narrow");
                Check(p.AcceptsDescriptor(d)==(p.Transport==TransportKind.HidFeature90Or91 && RazerControlPathResolver.HasAuditedDesktopConsumerCandidates(p.ProductId)),"Dynamic whole catalog candidate scope "+p.ProductId);
                d.ReportLength=89;Check(!p.AcceptsDescriptor(d),"No unsupported length");
                d.ReportLength=91;d.VendorId=1;Check(!p.AcceptsDescriptor(d),"No wrong VID");
            }
        }
        private static void Da4Shapes()
        {
            foreach(int pid in new[]{0xbe,0xbf}) foreach(int length in new[]{90,91}) foreach(int shape in new[]{0,1,2}) {
                var f=new ProfileFake(pid,length);f.Descriptor.Path="hid#mi_00#da4";
                f.Descriptor.UsagePage=shape==0?1:0xC;f.Descriptor.Usage=shape==2?9:shape==0?2:1;
                var c=Client(f);var r=c.QueryRazerDeviceInfo();
                Check(r.IsDpiWriteSupported && r.IsPollingWriteSupported && f.Requests.Count>0 && f.Writes==0 && f.Unexpected==0,"Known GET finds DA4 candidate");
                int o=length==91?1:0;Check(f.Requests.All(q=>q[o+7]>=0x80),"Discovery sends no SET");
                Check(!RazerProtocolProfile.SupportsRotation(f.Descriptor),"DA4 no Rotation");
            }
        }
        private static void ModernControlMatrix()
        {
            foreach(var p in DeviceCapabilityCatalog.All.Where(p=>RazerControlPathResolver.HasAuditedDesktopConsumerCandidates(p.ProductId))) {
                if(p.ProductId==0x7a) continue; // Approved FF/3F matrix is independently covered.
                if(p.ProductId==0xe5 || p.ProductId==0xe6) {
                    var f=new SupplementalFake(p.ProductId,91);f.Descriptor.Path="hid#mi_00#v4";f.Descriptor.UsagePage=0xC;f.Descriptor.Usage=8;
                    var c=SupplementalClient(f);var r=c.QueryRazerDeviceInfo();Check(r.IsWriteSupported && f.Writes==0,"Unique native V4 proof");
                    Check(c.SetDpiTransactional(p.ProductId,1600,r.DeviceKey,r.InterfacePath).Success,"Confirmed V4 live transaction");
                } else {
                    var f=new ProfileFake(p.ProductId,91);f.Descriptor.UsagePage=0xC;f.Descriptor.Usage=9;
                    var r=Client(f).QueryRazerDeviceInfo();Check(r.IsWriteSupported && f.Writes==0 && f.Unexpected==0,"Audited consumer PID "+p.ProductId);
                }
            }
        }
        private static void ControlUniqueness()
        {
            foreach(int responders in new[]{0,1,2}) {
                var a=new ControlFaultFake(0xbf,responders>=1?0:1);var b=new ControlFaultFake(0xbf,responders==2?0:1);
                b.Descriptor.ContainerId=a.Descriptor.ContainerId;b.Descriptor.UsagePage=0xC;b.Descriptor.Usage=8;
                var c=CorrectionsClient(a,b);var r=c.QueryRazerDeviceInfo();
                Check(r.IsWriteSupported==(responders==1),"Exactly one proves permission");
                if(responders!=1) Check(!c.SetDpiTransactional(0xbf,1600,r.DeviceKey,r.InterfacePath).WriteAttempted && !c.SetPollingTransactional(0xbf,1000,r.DeviceKey,r.InterfacePath).WriteAttempted,"Ambiguous/zero no SET");
                else Check(c.SetDpiTransactional(0xbf,1600,r.DeviceKey,r.InterfacePath).Success && b.Inner.Writes==0,"Only unique locked path writes");
            }
            var x=new ProfileFake(0xbf,91);var y=new ProfileFake(0xbf,91);var independent=CorrectionsClient(x,y);var live=independent.QueryRazerDeviceInfo();
            Check(live.IsWriteSupported && independent.SetDpiTransactional(0xbf,1600,live.DeviceKey,live.InterfacePath).Success && x.Writes+y.Writes==1,"Different physical keys never pooled");
        }
        private static void ControlFailures()
        {
            foreach(int mode in Enumerable.Range(1,5)) {
                var f=new ControlFaultFake(mode==5?0x46:0xbf,mode);var c=CorrectionsClient(f);var r=c.QueryRazerDeviceInfo();
                var expected=mode==1?ControlProbeOutcome.NoResponse:mode==2?ControlProbeOutcome.Unsupported:mode==4?ControlProbeOutcome.TransportError:ControlProbeOutcome.Malformed;
                Check(c.ControlProbeOutcomes[f.Descriptor.Path]==expected && !r.IsWriteSupported && f.Inner.Writes==0,"Distinct outcome "+mode);
            }
            var silent=new ControlFaultFake(0xbf,1);var malformed=new ControlFaultFake(0xbf,3);var valid=new ProfileFake(0xbf,91);
            silent.Descriptor.ContainerId=malformed.Descriptor.ContainerId=valid.Descriptor.ContainerId;
            var resolver=CorrectionsClient(silent,malformed,valid);var reading=resolver.QueryRazerDeviceInfo();
            Check(reading.IsWriteSupported && resolver.ControlProbeOutcomes[malformed.Descriptor.Path]==ControlProbeOutcome.Malformed,"Bad sibling not mistaken for successful path");
        }
        private static void ControlBusy()
        {
            var f=new SupplementalFake(0xe6,91){Busy=100};var c=SupplementalClient(f);var r=c.QueryRazerDeviceInfo();
            Check(!r.IsWriteSupported && c.ControlProbeOutcomes[f.Descriptor.Path]==ControlProbeOutcome.BusyTimeout && f.Writes==0 && f.Gets==f.Sends*16,"BUSY response-only bounded budget");
            Check(f.Requests.GroupBy(q=>q[7]+":"+q[8]).All(g=>g.Count()==1),"No retransmit during discovery");
        }
        private static void ControlCaching()
        {
            var a=new ControlFaultFake(0xbf,1);var b=new ControlFaultFake(0xbf,0);b.Descriptor.ContainerId=a.Descriptor.ContainerId;b.Descriptor.UsagePage=0xC;
            var c=CorrectionsClient(a,b);Check(c.QueryRazerDeviceInfo().IsWriteSupported,"Initial unique proof");int aCount=a.Count;
            for(int i=0;i<4;i++)Check(c.QueryRazerDeviceInfo().IsWriteSupported,"Stable locked live GET");
            Check(a.Count==aCount,"Background queries never reprobe rejected sibling");
            a.Mode=0;c.InvalidateTarget();var r=c.QueryRazerDeviceInfo();Check(!r.IsWriteSupported && r.ProtocolReason=="ambiguous-control-path","Explicit refresh discovers new responder");
            int count=a.Count+b.Count;c.QueryRazerDeviceInfo();Check(a.Count+b.Count==count,"Ambiguity cached until invalidation");
        }
        private static void ControlWake()
        {
            var a=new ControlFaultFake(0xbf,0);var b=new ControlFaultFake(0xbf,1);b.Descriptor.ContainerId=a.Descriptor.ContainerId;
            var t=new FakeTransport(a,b);var c=new RazerDeviceClient(t,new HardwareCacheStore(null));var r=c.QueryRazerDeviceInfo();Check(r.IsWriteSupported,"Unique initial");
            a.Mode=1;r=c.QueryRazerDeviceInfo();Check(!r.IsWriteSupported,"Sleep revokes writes");
            a.Mode=b.Mode=0;r=c.QueryRazerDeviceInfo();Check(!r.IsWriteSupported && r.ProtocolReason=="ambiguous-control-path","Wake rechecks entire pool before grants");
            t.Devices.Remove(b);r=c.QueryRazerDeviceInfo();Check(r.IsWriteSupported,"Pool change forces rediscovery");
            t.Devices.Add(b);int writes=a.Inner.Writes+b.Inner.Writes;
            Check(!c.SetDpiTransactional(0xbf,1600,r.DeviceKey,r.InterfacePath).WriteAttempted && a.Inner.Writes+b.Inner.Writes==writes,"New sibling between refresh and write revokes topology lock before any SET");
            a=new ControlFaultFake(0xbf,1);b=new ControlFaultFake(0xbf,1);b.Descriptor.ContainerId=a.Descriptor.ContainerId;
            c=CorrectionsClient(a,b);Check(!c.QueryRazerDeviceInfo().IsWriteSupported,"Initially sleeping pool");b.Mode=0;
            bool awake=false;for(int i=0;i<3;i++)awake|=c.QueryRazerDeviceInfo().IsWriteSupported;
            Check(awake,"Bounded single sentinel polling discovers wake on a sibling, then reconfirms uniqueness");
        }
        private static void ControlLifetime()
        {
            foreach(int field in Enumerable.Range(0,8)) {
                var f=new ProfileFake(0xbf,91);var c=Client(f);var r=c.QueryRazerDeviceInfo();var original=RazerControlPathResolver.Signature(f.Descriptor);
                if(field==0)f.Descriptor.ContainerId+="new";if(field==1)f.Descriptor.Path+="new";if(field==2)f.Descriptor.Version++;
                if(field==3)f.Descriptor.ReportLength=90;if(field==4)f.Descriptor.UsagePage=0xC;if(field==5)f.Descriptor.Usage=9;
                if(field==6)f.Descriptor.ProductId=0xbe;if(field==7)c.InvalidateTarget();
                int n=f.Requests.Count;Check(!c.SetDpiTransactional(0xbf,1600,r.DeviceKey,r.InterfacePath).WriteAttempted && !c.SetPollingTransactional(0xbf,1000,r.DeviceKey,r.InterfacePath).WriteAttempted && f.Requests.Count==n,"Stale all locks no traffic");
                Check(original!=RazerControlPathResolver.Signature(f.Descriptor) || field==7,"Every field bound");
            }
        }
        private static void ControlHandleLifetime()
        {
            var t=new ScopedTransport();var c=new RazerDeviceClient(t,new HardwareCacheStore(null));var r=c.QueryRazerDeviceInfo();
            Check(r.IsWriteSupported && t.Gets>0 && c.SetPollingTransactional(0xbf,1000,r.DeviceKey,r.InterfacePath).Success,"No captured native handle outside callback");
        }
        private sealed class ControlFaultFake:IHidDevice,IHidDescriptor
        {
            internal ProfileFake Inner;internal int Mode,Count;
            internal ControlFaultFake(int pid,int mode){Inner=new ProfileFake(pid,91);Mode=mode;}
            public HidDescriptor Descriptor {get{return Inner.Descriptor;}}public int ProductId{get{return Descriptor.ProductId;}}
            public string ProductName{get{return Inner.ProductName;}}public int ReportLength{get{return Inner.ReportLength;}}
            public byte[] Exchange(byte[] q,int delay){Count++;if(Mode==4)throw new InvalidOperationException("Transport fault");if(Mode==1)return null;var r=Inner.Exchange(q,delay);if(r==null)return null;
                if(Mode==2)r[1]=5;if(Mode==3)r[2]^=1;
                if(Mode==5)for(int i=9;i<25;i++)r[i]=0;
                r[89]=RazerProtocol.CalculateCrc(r,1);return r;}
        }
        private sealed class ScopedTransport:IHidTransport
        {
            private readonly ProfileFake fixture=new ProfileFake(0xbf,91);internal int Gets;
            public void Visit(Func<IHidDevice,bool> visit){var d=new ScopedDevice(fixture,this);try{visit(d);}finally{d.Active=false;}}
            private sealed class ScopedDevice:IHidDevice,IHidDescriptor
            {
                internal bool Active=true;private ProfileFake inner;private ScopedTransport owner;
                internal ScopedDevice(ProfileFake f,ScopedTransport o){inner=f;owner=o;}
                public HidDescriptor Descriptor{get{return inner.Descriptor;}}public int ProductId{get{return Descriptor.ProductId;}}
                public int ReportLength{get{return inner.ReportLength;}}public string ProductName{get{return inner.ProductName;}}
                public byte[] Exchange(byte[] q,int delay){if(!Active)throw new Exception("Native handle escaped callback");owner.Gets++;return inner.Exchange(q,delay);}
            }
        }
    }
}
