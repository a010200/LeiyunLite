using System;
using System.Linq;
using System.Collections.Generic;

namespace RazerBatteryTray.Tests
{
    internal static partial class OpenRazerCompatibilityTests
    {
        private static void RunCorrectionTests()
        {
            Test("Reviewed owned clones preserve original catalog and evidence", CorrectionClones);
            Test("DeathAdder V3 exact 30K ceiling, rejected values issue no write", CorrectionDpi);
            Test("004C and 00A6 exact polling wire families, bounds and rollback", CorrectionPolling);
            Test("Ultimate canonical/approved read-only winner and transaction restoration", UltimateWinner);
            Test("Ultimate malformed canonical, no response and stage grants fail closed", UltimateMalformed);
            Test("Ultimate winner cannot survive stale descriptor or invalidation", UltimateLifetime);
            Test("Naga Windows 91-byte paths, ambiguity, sleep/wake and exclusions", NagaControlPaths);
            Test("All effective profiles retain exact Rotation exclusion", EffectiveRotationMatrix);
        }
        private static void CorrectionClones()
        {
            Check(OpenRazerCapabilityCatalog.All.Count()==117 && SupplementalCapabilityCatalog.All.Count()==4 && DeviceCapabilityCatalog.All.Count()==121,"Counts");
            foreach(var source in OpenRazerCapabilityCatalog.All.Concat(SupplementalCapabilityCatalog.All)) {
                var p=DeviceCapabilityCatalog.Find(source.ProductId);
                Check(!ReferenceEquals(p,source) && !ReferenceEquals(p.PollRates,source.PollRates) && !ReferenceEquals(p.AvailableDpi,source.AvailableDpi) && !ReferenceEquals(p.UpstreamPollRates,source.UpstreamPollRates),"Owned deep arrays");
                Check(p.RotationTrust==CapabilityTrust.IdentityOnly,"No correction hardware Rotation grant");
            }
            Check(OpenRazerCapabilityCatalog.Find(0x4c).PollingProtocol==PollingProtocolKind.None && DeviceCapabilityCatalog.Find(0x4c).PollingProtocol==PollingProtocolKind.Legacy,"Source/effective 004C");
            Check(OpenRazerCapabilityCatalog.Find(0xa6).PollingProtocol==PollingProtocolKind.Legacy && DeviceCapabilityCatalog.Find(0xa6).PollingProtocol==PollingProtocolKind.HighRate,"Source/effective A6");
            var copy=DeviceCapabilityCatalog.Find(0xa6).Clone(); copy.PollRates[0]=250; copy.UpstreamPollRates[0]=250;
            Check(DeviceCapabilityCatalog.Find(0xa6).PollRates[0]==125 && OpenRazerCapabilityCatalog.Find(0xa6).PollRates[0]==125,"Clone mutation isolated");
            foreach(var p in DeviceCapabilityCatalog.All) Check((p.TransactionProbePolicy==TransactionProbePolicy.ReadOnlySessionFallback)==(p.ProductId==0x7a || p.ProductId==0x7b),"Closed resolver list");
        }
        private static void CorrectionDpi()
        {
            foreach(int pid in new[]{0xb6,0xb7,0xc2,0xc3}) {
                Check(OpenRazerCapabilityCatalog.Find(pid).MaximumDpi==35000 && DeviceCapabilityCatalog.Find(pid).MaximumDpi==30000,"Original max preserved");
                var f=new ProfileFake(pid,91);var c=Client(f);var r=c.QueryRazerDeviceInfo();
                Check(c.SetDpiTransactional(pid,30000,r.DeviceKey,r.InterfacePath).Success,"30K succeeds"); int count=f.Writes;
                foreach(int dpi in new[]{30001,35000}) Check(!c.SetDpiTransactional(pid,dpi,r.DeviceKey).WriteAttempted && f.Writes==count,"No out of range SET");
            }
        }
        private static void CorrectionPolling()
        {
            foreach(int pid in new[]{0x4c,0xa6}) foreach(int length in new[]{90,91}) {
                var f=new ProfileFake(pid,length); var c=Client(f); var r=c.QueryRazerDeviceInfo(); int o=length==91?1:0;
                foreach(int hz in new[]{125,500,1000}) {
                    f.Rate=hz==500?125:500; int writes=f.Writes;
                    Check(c.SetPollingTransactional(pid,hz,r.DeviceKey,r.InterfacePath).Success && f.Writes-writes==1,"Single target SET");
                    var get=f.Requests.Last(q=>q[o+6]==0 && q[o+7]>=0x80); var set=f.Requests.Last(q=>q[o+6]==0 && q[o+7]<0x80);
                    Check(get[o+7]==(pid==0x4c?0x85:0xc0) && set[o+7]==(pid==0x4c?5:0x40) && get[o+1]==(pid==0x4c?0xff:0x1f) && set[o+1]==get[o+1],"Exact family and TID");
                    if(pid==0xa6) Check(set[o+8]==0 && set[o+9]==(hz==125?0x40:hz==500?0x10:8),"Exact high-family divisor, selector zero only");
                }
                int before=f.Writes; foreach(int hz in new[]{250,2000,4000,8000}) Check(!c.SetPollingTransactional(pid,hz,r.DeviceKey).WriteAttempted && f.Writes==before,"1K ceiling");
                foreach(int fault in new[]{1,2,3,4,5}) {
                    f=new ProfileFake(pid,length); c=Client(f); r=c.QueryRazerDeviceInfo(); f.WriteFault=fault;
                    var result=c.SetPollingTransactional(pid,1000,r.DeviceKey,r.InterfacePath);
                    Check(!result.Success && result.RollbackAttempted && result.RollbackSucceeded==(fault!=5),"Polling fault independently restored");
                }
            }
        }
        private static RazerDeviceClient CorrectionsClient(params IHidDevice[] devices)
        { return new RazerDeviceClient(new ControlTransport(devices),new HardwareCacheStore(null)); }
        private static void UltimateWinner()
        {
            foreach(int pid in new[]{0x7a,0x7b}) foreach(int mode in new[]{0,1,2,3}) {
                var f=new UltimateFake(pid,mode); var c=CorrectionsClient(f); var r=c.QueryRazerDeviceInfo(); byte tid=mode==3?(byte)0xff:(byte)0x3f;
                Check(r.IsDpiWriteSupported && r.IsPollingWriteSupported && f.Writes==0,"Read-only resolver");
                Check(f.Requests[0][2]==0xff && f.Requests.All(q=>q[8]>=0x80),"Canonical first, only GET");
                Check(mode==3?!f.Requests.Any(q=>q[2]==0x3f):f.Requests[1][2]==0x3f,"Approved winner");
                f.Requests.Clear();
                Check(c.SetDpiTransactional(pid,1600,r.DeviceKey,r.InterfacePath).Success && c.SetPollingTransactional(pid,1000,r.DeviceKey,r.InterfacePath).Success,"Winner writes/readback");
                Check(f.Requests.All(q=>q[2]==tid),"No SET-time probing or another TID");
                f.Requests.Clear(); c.QueryRazerDeviceInfo(); Check(f.Requests.All(q=>q[2]==tid),"Winner retained for exact live target");
                foreach(bool polling in new[]{false,true}) foreach(int fault in new[]{1,2,3,4,5}) {
                    f=new UltimateFake(pid,mode); c=CorrectionsClient(f); r=c.QueryRazerDeviceInfo(); f.Inner.WriteFault=fault; f.Requests.Clear();
                    var result=polling?(PerformanceWriteResult)c.SetPollingTransactional(pid,1000,r.DeviceKey,r.InterfacePath):c.SetDpiTransactional(pid,1600,r.DeviceKey,r.InterfacePath);
                    Check(!result.Success && result.RollbackAttempted && result.RollbackSucceeded==(fault!=5) && f.Requests.All(q=>q[2]==tid),"Winner survives ACK/readback/delivery faults through restore");
                }
            }
        }
        private static void UltimateMalformed()
        {
            foreach(int fault in Enumerable.Range(1,14)) {
                var f=new UltimateFake(0x7b,4){Fault=fault}; var c=CorrectionsClient(f);var r=c.QueryRazerDeviceInfo();
                Check(!r.IsWriteSupported && !f.Requests.Any(q=>q[2]==0x3f) && !c.SetDpiTransactional(0x7b,1600,r.DeviceKey).WriteAttempted && f.Writes==0,"Malformed canonical cannot fallback");
            }
            var none=new UltimateFake(0x7b,0){AlternateSilent=true}; var client=CorrectionsClient(none);var reading=client.QueryRazerDeviceInfo();
            Check(!reading.IsWriteSupported && none.Writes==0 && none.Requests.Count==2,"Both approved reads fail");
            var stage=new UltimateFake(0x7b,0){FailStages=true}; client=CorrectionsClient(stage); reading=client.QueryRazerDeviceInfo();
            Check(reading.DpiKnown && !reading.IsDpiWriteSupported && reading.IsPollingWriteSupported && !client.SetDpiTransactional(0x7b,1600,reading.DeviceKey).WriteAttempted,"XY success not a stages write grant");
        }
        private static void UltimateLifetime()
        {
            foreach(int field in Enumerable.Range(0,8)) {
                var f=new UltimateFake(0x7b,0);var c=CorrectionsClient(f);var r=c.QueryRazerDeviceInfo();
                string container=f.Descriptor.ContainerId, path=f.Descriptor.Path;
                int version=f.Descriptor.Version, page=f.Descriptor.UsagePage, usage=f.Descriptor.Usage;
                if(field==0) f.Descriptor.ContainerId+="different";
                if(field==1) f.Descriptor.Path+="different";
                if(field==2) f.Descriptor.Version++;
                if(field==3) f.Descriptor.ReportLength=90;
                if(field==4) f.Descriptor.UsagePage=0xff00;
                if(field==5) f.Descriptor.Usage=1;
                if(field==6) f.Length=90;
                if(field==7) c.InvalidateTarget();
                f.Requests.Clear();
                Check(!c.SetDpiTransactional(0x7b,1600,r.DeviceKey,r.InterfacePath).WriteAttempted && !c.SetPollingTransactional(0x7b,1000,r.DeviceKey,r.InterfacePath).WriteAttempted && f.Requests.Count==0,"Changed descriptor/session: no request");
                f.Descriptor.ContainerId=container; f.Descriptor.Path=path; f.Descriptor.Version=version;
                f.Descriptor.ReportLength=91; f.Descriptor.UsagePage=page; f.Descriptor.Usage=usage; f.Length=91;
                Check(!c.SetDpiTransactional(0x7b,1600,r.DeviceKey,r.InterfacePath).WriteAttempted && f.Requests.Count==0,"Restoring old descriptor does not resurrect retired winner");
                f.Mode=3; var fresh=c.QueryRazerDeviceInfo();
                Check(f.Requests[0][2]==0xff && fresh.IsDpiWriteSupported,"Fresh discovery re-confirms canonical winner");
                f.Requests.Clear(); Check(c.SetDpiTransactional(0x7b,1600,fresh.DeviceKey,fresh.InterfacePath).Success && f.Requests.All(q=>q[2]==0xff),"New lifetime uses newly confirmed winner only");
            }
        }
        private static void NagaControlPaths()
        {
            foreach(int pid in new[]{0xe7,0xe8}) foreach(int usage in new[]{1,2,3}) foreach(string path in new[]{"hid#mi_03#control","hid#mi_00#control","hid#control"}) {
                var f=new SupplementalFake(pid,91); f.Descriptor.Usage=usage; f.Descriptor.Path=path;
                var c=CorrectionsClient(f); var r=c.QueryRazerDeviceInfo(); Check(r.IsDpiWriteSupported && r.IsPollingWriteSupported,"Unique valid Windows path");
                f.Sleeping=true; r=c.QueryRazerDeviceInfo(); Check(!r.IsWriteSupported && !c.SetDpiTransactional(pid,1600,r.DeviceKey).WriteAttempted,"Cached cannot write");
                f.Sleeping=false; r=c.QueryRazerDeviceInfo(); Check(r.IsWriteSupported,"Wake needs fresh GET");
            }
            foreach(int pid in new[]{0xe7,0xe8}) {
                var a=new SupplementalFake(pid,91);var b=new SupplementalFake(pid,91); b.Descriptor.ContainerId=a.Descriptor.ContainerId;
                a.Descriptor.Usage=b.Descriptor.Usage=3; var c=CorrectionsClient(a,b);var r=c.QueryRazerDeviceInfo();
                Check(r.DpiKnown && r.ProtocolReason=="ambiguous-control-path" && !r.IsWriteSupported && !c.SetDpiTransactional(pid,1600,r.DeviceKey,r.InterfacePath).WriteAttempted && !c.SetPollingTransactional(pid,1000,r.DeviceKey,r.InterfacePath).WriteAttempted && a.Writes+b.Writes==0,"Two valid paths same instance: no write");
                b.ReadFault=7; r=c.QueryRazerDeviceInfo(); Check(r.IsWriteSupported,"Only strict valid response selects unique path");
                var bad=new SupplementalFake(pid,90); r=CorrectionsClient(bad).QueryRazerDeviceInfo();Check(bad.Requests.Count==0 && !r.IsWriteSupported,"90 bytes no Windows grant");
                bad=new SupplementalFake(pid,91);bad.Descriptor.ProductId=0x7fff;r=CorrectionsClient(bad).QueryRazerDeviceInfo();Check(bad.Requests.Count==0,"Wrong PID no command");
            }
        }
        private static void EffectiveRotationMatrix()
        {
            foreach(var p in DeviceCapabilityCatalog.All) foreach(int length in new[]{90,91}) foreach(int usage in new[]{1,2,3}) {
                var d=new HidDescriptor{VendorId=0x1532,ProductId=p.ProductId,Version=0x0100,ReportLength=length,UsagePage=1,Usage=usage};
                Check(RazerProtocolProfile.SupportsRotation(d)==((p.ProductId==0xde || p.ProductId==0xdf) && length==91 && usage==2),"Exact Rotation only");
            }
        }
        private sealed class ControlTransport:IHidTransport
        {
            private readonly IHidDevice[] devices; internal ControlTransport(params IHidDevice[] values){devices=values;}
            public void Visit(Func<IHidDevice,bool> visit){foreach(var d in devices)if(visit(d))break;}
        }
        private sealed class UltimateFake:IHidDevice,IHidDescriptor
        {
            internal readonly ProfileFake Inner; internal readonly List<byte[]> Requests=new List<byte[]>();
            internal int Mode,Fault,Length=91; internal bool AlternateSilent,FailStages;
            public HidDescriptor Descriptor {get{return Inner.Descriptor;}}
            public int ProductId {get{return Descriptor.ProductId;}}
            public int ReportLength {get{return Length;}}
            public string ProductName {get{return "Ultimate fixture";}}
            internal int Writes {get{return Inner.Writes;}}
            internal UltimateFake(int pid,int mode){Mode=mode;var p=DeviceCapabilityCatalog.Find(pid).Clone();p.DpiTransaction=p.DpiSetTransaction=p.DpiStageTransaction=p.PollingTransaction=p.PollingSetTransaction=p.BatteryTransaction=p.ChargingTransaction=0x3f;Inner=new ProfileFake(pid,91,p);}
            public byte[] Exchange(byte[] q,int delay)
            {
                Requests.Add((byte[])q.Clone());int o=1;byte tid=q[2];
                if(tid==0xff && Mode!=3) {
                    if(Mode==0)return null;
                    var r=(byte[])q.Clone();r[o]=(byte)(Mode==1?5:Mode==2?4:2);r[o+9]=r[o+11]=3;r[o+10]=r[o+12]=0x84;
                    if(Fault==1)r[o+1]=0x1f;if(Fault==2)r[o+6]^=1;if(Fault==3)r[o+7]^=1;
                    if(Fault==4)r[o+2]=1;if(Fault==5)r[o+3]=1;if(Fault==6)r[o+4]=1;
                    if(Fault==7)r[o+5]=0;if(Fault==8)r[o+5]=81;if(Fault==9)r[o+89]=1;
                    if(Fault==10)r[0]=1;if(Fault==11)return r.Take(90).ToArray();if(Fault==12)r[o]=1;
                    if(Fault==14)r[o+9]=r[o+10]=0;
                    r[o+88]=RazerProtocol.CalculateCrc(r,o);if(Fault==13)r[o+88]^=1;return r;
                }
                if(tid==0x3f && AlternateSilent)return null;
                if(FailStages && q[o+6]==4 && q[o+7]==0x86)return null;
                var translated=(byte[])q.Clone();translated[2]=0x3f;translated[o+88]=RazerProtocol.CalculateCrc(translated,o);
                var reply=Inner.Exchange(translated,delay);if(reply!=null){reply[2]=tid;reply[o+88]=RazerProtocol.CalculateCrc(reply,o);}return reply;
            }
        }
    }
}
