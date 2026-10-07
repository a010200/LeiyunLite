using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using RazerBatteryTray.Desktop;

namespace RazerBatteryTray.Tests
{
    internal static partial class OpenRazerCompatibilityTests
    {
        private static void RunSupplementalTests()
        {
            Test("Supplemental catalog union, evidence and duplicate fail closed", SupplementalCatalog);
            Test("Supplemental descriptor gates and unchanged alternate exclusions", SupplementalDescriptors);
            Test("V4 independent packet bytes, stages, exact DPI and no XY fallback", V4Packets);
            Test("V4 all six polling values, single selector and RF settle", V4Polling);
            Test("Single send, BUSY response-only reads, bounds and malformed frames", V4Busy);
            Test("Supplemental readback, delivery exceptions and raw rollback faults", SupplementalRollback);
            Test("Naga V3 both transports, limits, stages, battery and charging", NagaReadsWrites);
            Test("Supplemental per-capability, cache, sleep/wake and stale targets", SupplementalSafety);
            Test("Combined Rotation exact negative matrix", CombinedRotation);
        }
        private static RazerDeviceClient SupplementalClient(SupplementalFake f)
        { return new RazerDeviceClient(new SupplementalTransport(f),new HardwareCacheStore(null)); }
        private static void SupplementalCatalog()
        {
            Check(OpenRazerCapabilityCatalog.All.Count()==117 && SupplementalCapabilityCatalog.All.Count()==4 && DeviceCapabilityCatalog.All.Count()==121,"Union count");
            Check(RazerIdentityCatalog.Count==123,"Identity count");
            foreach(var p in SupplementalCapabilityCatalog.All) {
                Check(p.EvidenceKind==ProtocolEvidenceKind.SupplementalCommunity && p.RotationTrust==CapabilityTrust.IdentityOnly,"Evidence and rotation");
                Check(RazerIdentityCatalog.Find(p.ProductId).Evidence.Contains("community") && p.MaximumDpi==50000,"Identity source and range");
            }
            bool rejected=false; try { DeviceCapabilityCatalog.Combine(OpenRazerCapabilityCatalog.All,new[]{OpenRazerCapabilityCatalog.Find(0xdf)}); } catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Duplicate refuses union");
        }
        private static void SupplementalDescriptors()
        {
            foreach(int pid in new[]{0xe5,0xe6,0xe7,0xe8}) foreach(int length in new[]{90,91}) {
                var f=new SupplementalFake(pid,length); var r=SupplementalClient(f).QueryRazerDeviceInfo();
                if(pid>=0xe7 && length==90) { Check(f.Sends==0 && !r.IsWriteSupported,"Naga Windows requires 91-byte collection"); continue; }
                Check(r.ProtocolStatus==DeviceProtocolStatus.Ready && r.DpiTrust==CapabilityTrust.UpstreamVerified,"Correct interface");
                foreach(int field in new[]{0,1,2,3,4,5}) {
                    var bad=new SupplementalFake(pid,length);
                    if(field==0) bad.Descriptor.VendorId=1;
                    if(field==1) bad.Descriptor.ReportLength=89;
                    if(field==2) bad.Descriptor.UsagePage=0x0C;
                    if(field==2) bad.Descriptor.Usage=2;
                    if(field==3) bad.Descriptor.Path="hid#vid_1532&pid_00e6&mi_02#control";
                    if(field==4) bad.Descriptor.Path="hid#vid_1532&pid_00e6#control";
                    if(field==5) bad.Descriptor.Path="hid#vid_1532&mi_03&mi_03#control";
                    if(field>=3 && pid>=0xe7) continue;
                    var client=SupplementalClient(bad); var rejected=client.QueryRazerDeviceInfo();
                    if(pid<0xe7 && field>=2 && field<=4) {
                        Check(rejected.IsWriteSupported && bad.Writes==0,"Audited V4 native collection requires live GET, no absolute MI/Usage restriction"); continue;
                    }
                    Check(!rejected.IsWriteSupported && bad.Sends==0 && !client.SetDpiTransactional(pid,1001,rejected.DeviceKey).WriteAttempted,"Invalid descriptor no command");
                }
            }
            foreach(int pid in new[]{0x96,0x99,0xcb}) { var f=new ProfileFake(pid,91); Client(f).QueryRazerDeviceInfo(); Check(f.Requests.Count==0,"No global alternate unlock"); }
        }
        private static void V4Packets()
        {
            foreach(int pid in new[]{0xe5,0xe6}) foreach(int length in new[]{90,91}) {
                var f=new SupplementalFake(pid,length); var c=SupplementalClient(f); var r=c.QueryRazerDeviceInfo(); int o=length==91?1:0;
                var get=f.Requests.Single(q=>q[o+6]==4 && q[o+7]==0x86);
                Check(get[o+1]==0x1f && get[o+5]==80 && get[o+8]==0 && get[o+88]==RazerProtocol.CalculateCrc(get,o),"Exact V4 GET");
                byte[] original=(byte[])f.Payload.Clone(); var result=c.SetDpiTransactional(pid,50000,r.DeviceKey,r.InterfacePath);
                Check(result.Success && result.After==50000 && f.Payload[1]==2 && f.Payload[3]==0 && f.Payload[10]==1,"50K and slot conversion");
                var set=f.Requests.Last(q=>q[o+6]==4 && q[o+7]==6);
                Check(set[o+5]==original.Length && set[o+1]==0x1f && set[o+88]==RazerProtocol.CalculateCrc(set,o),"Dynamic SET exact CRC");
                for(int i=0;i<original.Length;i++) if(i<11 || i>14) Check(original[i]==f.Payload[i],"Unknown/inactive bytes preserved");
                Check(c.SetDpiTransactional(pid,1001,r.DeviceKey,r.InterfacePath).Success && f.Dpi==1001,"1 DPI exact integer");
                Check(!f.Requests.Any(q=>q[o+6]==4 && (q[o+7]==0x85 || q[o+7]==5)),"V4 never XY");
            }
            foreach(int fault in new[]{1,2,3,4,5,6,7,8,9,10,11,12,13}) {
                var f=new SupplementalFake(0xe5,91){ReadFault=fault}; var c=SupplementalClient(f); var r=c.QueryRazerDeviceInfo();
                Check(!r.DpiKnown && !c.SetDpiTransactional(0xe5,1001,r.DeviceKey).WriteAttempted,"Corrupt V4 stages refused");
                Check(!f.Requests.Any(q=>q[7]==4 && q[8]==0x85),"Failure does not XY fallback");
            }
        }
        private static void V4Polling()
        {
            foreach(int pid in new[]{0xe5,0xe6}) foreach(int hz in new[]{125,500,1000,2000,4000,8000}) {
                var f=new SupplementalFake(pid,91); var c=SupplementalClient(f); var r=c.QueryRazerDeviceInfo(); f.Rate=hz==500?125:500;
                int before=f.Writes; var clock=System.Diagnostics.Stopwatch.StartNew(); var result=c.SetPollingTransactional(pid,hz,r.DeviceKey,r.InterfacePath);
                Check(result.Success && f.Writes-before==1 && clock.ElapsedMilliseconds>=150,"One SET and settle");
                var set=f.Requests.Last(q=>q[7]==0 && q[8]==0x40); byte encoded; RazerProtocol.TryEncodePollingRate(PollingProtocolKind.V4HighRate,hz,out encoded);
                Check(set[6]==2 && set[9]==1 && set[10]==encoded,"Selector=1 exact code");
                Check(f.Requests.Where(q=>q[7]==0 && q[8]==0xc0).All(q=>q[6]==2 && q[9]==1 && q[10]==0),"Exact V4 poll GET");
                Check(!c.SetPollingTransactional(pid,250,r.DeviceKey).WriteAttempted && f.Unexpected==0,"No 250 or second pass");
            }
        }
        private static byte[] RawExchange(SupplementalFake f,byte[] request)
        { return (byte[])typeof(RazerDeviceClient).GetMethod("ExchangeValidated",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{f,request,35}); }
        private static void V4Busy()
        {
            var f=new SupplementalFake(0xe6,91){Busy=2}; var reply=RawExchange(f,RazerProtocol.BuildPollingSet(PollingProtocolKind.V4HighRate,0x1f,1,8,91));
            Check(reply!=null && f.Sends==1 && f.Gets==3 && f.Writes==1 && f.Delays.All(d=>d==35),"BUSY BUSY OK: single send, three reads");
            f=new SupplementalFake(0xe6,91){Busy=100}; Check(RawExchange(f,RazerProtocol.BuildPollingSet(PollingProtocolKind.V4HighRate,0x1f,1,8,91))==null && f.Sends==1 && f.Gets==16 && f.Writes==1,"Bounded always BUSY");
            foreach(int fault in new[]{4,5,6,7,8,9,10,11,12,13}) {
                f=new SupplementalFake(0xe6,91){Busy=1,ReadFault=fault,FaultAfterBusy=true};
                Check(RawExchange(f,RazerProtocol.BuildV4DpiStagesGet(91))==null && f.Sends==1,"Malformed BUSY refuses frame");
            }
            var noSequence=new ProfileFake(0xe6,91); noSequence.Descriptor.Path="hid#vid_1532&pid_00e6&mi_03#control";
            var request=RazerProtocol.BuildV4DpiStagesGet(91);
            var missing=(byte[])typeof(RazerDeviceClient).GetMethod("ExchangeValidated",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{noSequence,request,35});
            Check(missing==null && noSequence.Requests.Count==0,"No sequence support: no repeated Exchange fallback");
        }
        private static void SupplementalRollback()
        {
            foreach(int pid in new[]{0xe5,0xe6,0xe7,0xe8}) foreach(bool polling in new[]{false,true}) foreach(int fault in new[]{1,2,3,4,5}) {
                var f=new SupplementalFake(pid,91); var c=SupplementalClient(f); var r=c.QueryRazerDeviceInfo(); byte[] before=(byte[])f.Payload.Clone(); f.WriteFault=fault;
                PerformanceWriteResult result=polling?(PerformanceWriteResult)c.SetPollingTransactional(pid,1000,r.DeviceKey,r.InterfacePath):c.SetDpiTransactional(pid,50000,r.DeviceKey,r.InterfacePath);
                Check(!result.Success && result.WriteAttempted && result.RollbackAttempted,"Fault attempts restore");
                Check(result.RollbackSucceeded==(fault!=5),"Restore independently confirmed PID="+pid+" polling="+polling+" fault="+fault+" error="+result.Error);
                if(polling && (pid==0xe5 || pid==0xe6)) Check(f.MinimumPollingGapMs>=150,"V4 settle precedes readback or restore even after delivery exception");
                if(fault!=5) Check(polling?f.Rate==500:before.SequenceEqual(f.Payload),"Full raw restoration");
            }
        }
        private static void NagaReadsWrites()
        {
            foreach(int pid in new[]{0xe7,0xe8}) foreach(int length in new[]{91}) {
                var f=new SupplementalFake(pid,length); var c=SupplementalClient(f); var r=c.QueryRazerDeviceInfo();
                Check(r.DpiKnown && r.PollingKnown && r.BatteryKnown && r.ChargingKnown,"Independent GETs");
                Check(c.SetDpiTransactional(pid,50000,r.DeviceKey,r.InterfacePath).Success,"50K stages");
                foreach(int hz in new[]{125,500,1000}) Check(c.SetPollingTransactional(pid,hz,r.DeviceKey,r.InterfacePath).Success,"Naga allowed rate");
                foreach(int hz in new[]{250,2000,4000,8000}) Check(!c.SetPollingTransactional(pid,hz,r.DeviceKey).WriteAttempted,"Naga no dongle inference");
                Check(f.Delays.All(d=>d==(pid==0xe8?100:31)) && f.Unexpected==0,"Evidence backed waits/commands");
                var fallback=new SupplementalFake(pid,length){FailStages=true}; var xy=SupplementalClient(fallback).QueryRazerDeviceInfo();
                Check(xy.DpiKnown && !xy.IsDpiWriteSupported && fallback.Requests.Any(q=>q[(length==91?1:0)+7]==0x85 && q[(length==91?1:0)+6]==4),"Declared Naga XY fallback remains read-only");
            }
        }
        private static void SupplementalSafety()
        {
            foreach(int pid in new[]{0xe5,0xe6,0xe7,0xe8}) {
                var f=new SupplementalFake(pid,91){FailDpi=true}; var c=SupplementalClient(f); var r=c.QueryRazerDeviceInfo();
                Check(r.BatteryKnown && !r.IsDpiWriteSupported && !c.SetDpiTransactional(pid,1001,r.DeviceKey).WriteAttempted,"Battery/polling no DPI grant");
                f=new SupplementalFake(pid,91){FailPolling=true}; c=SupplementalClient(f); r=c.QueryRazerDeviceInfo();
                Check(r.DpiKnown && !r.IsPollingWriteSupported && !c.SetPollingTransactional(pid,1000,r.DeviceKey).WriteAttempted,"DPI no polling grant");
                f=new SupplementalFake(pid,91); c=SupplementalClient(f); r=c.QueryRazerDeviceInfo();
                Check(!c.SetDpiTransactional(pid,1001,r.DeviceKey+"stale").WriteAttempted && !c.SetPollingTransactional(pid,1000,r.DeviceKey,"wrong-path").WriteAttempted,"Stale targets");
                f.Sleeping=true; r=c.QueryRazerDeviceInfo(); Check(r.ProtocolStatus==DeviceProtocolStatus.Cached && !c.SetDpiTransactional(pid,1001,r.DeviceKey).WriteAttempted && !c.SetPollingTransactional(pid,1000,r.DeviceKey).WriteAttempted,"Sleep revokes grants, cache no SET");
                f.Sleeping=false; r=c.QueryRazerDeviceInfo(); Check(r.IsDpiWriteSupported && r.IsPollingWriteSupported,"Wake live GET restores grants");
                f.ChangeAfterRead=true; Check(!c.SetDpiTransactional(pid,1001,r.DeviceKey,r.InterfacePath).WriteAttempted,"Descriptor changes between read and write");
            }
        }
        private static void CombinedRotation()
        {
            foreach(var p in DeviceCapabilityCatalog.All) {
                var f=new SupplementalFake(p.ProductId,91); var d=f.Descriptor;
                d.Path="normal"; d.UsagePage=1; d.Usage=2; d.Version=0x0100;
                Check(RazerProtocolProfile.SupportsRotation(d)==(p.ProductId==0xde || p.ProductId==0xdf),"Exact only SE matrix");
            }
            foreach(int pid in new[]{0xe5,0xe6,0xe7,0xe8}) {
                var f=new SupplementalFake(pid,91); var c=SupplementalClient(f); var r=c.QueryRazerDeviceInfo(); int writes=f.Writes;
                Check(!r.IsRotationWriteSupported && r.RotationTrust==CapabilityTrust.IdentityOnly && !c.SetRotationVerified(pid,-8,r.DeviceKey).WriteAttempted && writes==f.Writes,"New PID zero Rotation SET");
            }
        }
        private static void SupplementalUi()
        {
            foreach(int pid in new[]{0xe5,0xe6,0xe7,0xe8}) {
                var f=new SupplementalFake(pid,91);var client=SupplementalClient(f);var window=DirectFixture(client);var page=Field<DevicePage>(window,"devicePage");
                try {
                    var r=window.Reading;var cap=DeviceCapabilities.For(r);
                    Check(cap.AcceptsDpi(50000) && cap.AcceptsDpi(1001) && !cap.AcceptsDpi(50001),"UI exact DPI range");
                    Check(Descendants<Button>(Field<WrapPanel>(page,"ratePanel")).Select(b=>b.Content as string).SequenceEqual(cap.Rates.Select(h=>h+" Hz")),"UI exact rates");
                    Check(Field<TextBlock>(page,"performanceInfo").Text=="" && Field<TextBlock>(page,"performanceInfo").Visibility==Visibility.Collapsed && !Field<Button>(page,"applyRotation").IsEnabled,"No ordinary protocol prose/no Rotation");
                    PrepareDpi(page,r,1001);AwaitDirect(Commit(page,"CommitDpi"),window);Check(window.Reading.Dpi==1001,"Supplemental DPI direct exact input");
                    AwaitDirect(Commit(page,"CommitRate",1000),window);Check(window.Reading.PollingRate==1000,"Supplemental polling direct readback");
                    int writes=f.Writes;PrepareDpi(page,window.Reading,1600);SetField(page,"dpiEditPath","stale");AwaitDirect(Commit(page,"CommitDpi"),window);Check(f.Writes==writes,"Stale supplemental edit path zero SET");
                    window.Reading.InterfacePath="changed";PrepareDpi(page,window.Reading,1600);AwaitDirect(Commit(page,"CommitDpi"),window);Check(f.Writes==writes,"Backend-bound supplemental path zero SET");
                    window.Reading.IsDpiWriteSupported=window.Reading.IsPollingWriteSupported=false;PrepareDpi(page,window.Reading,1600);AwaitDirect(Commit(page,"CommitDpi"),window);AwaitDirect(Commit(page,"CommitRate",500),window);Check(f.Writes==writes,"Revoked supplemental permissions zero SET");
                } finally {CloseDirectFixture(window);}
            }
        }
        private sealed class SupplementalTransport:IHidTransport
        { private readonly SupplementalFake device; internal SupplementalTransport(SupplementalFake f){device=f;} public void Visit(Func<IHidDevice,bool> visitor){visitor(device);} }
        private sealed class SupplementalFake:IHidDevice,IHidDescriptor,ISequencedHidFeatureDevice
        {
            public HidDescriptor Descriptor {get;private set;}
            public int ProductId {get{return Descriptor.ProductId;}}
            public int ReportLength {get;private set;}
            public string ProductName {get{return "Supplemental Fake";}}
            internal readonly List<byte[]> Requests=new List<byte[]>(); internal readonly List<int> Delays=new List<int>();
            internal int Sends,Gets,Writes,Unexpected,Busy,ReadFault,WriteFault,Rate=500; private int attempt,writeNumber,pendingFault;
            internal bool Sleeping,FailDpi,FailPolling,ChangeAfterRead,FaultAfterBusy,FailStages;
            internal long MinimumPollingGapMs=long.MaxValue; private long lastPollingSet;
            internal byte[] Payload; private byte[] request; private bool v4; private int offset;
            internal int Dpi {get{return Payload[11]<<8|Payload[12];}}
            internal SupplementalFake(int pid,int length)
            {
                v4=pid==0xe5 || pid==0xe6; ReportLength=length; offset=length==91?1:0;
                Descriptor=new HidDescriptor{VendorId=0x1532,ProductId=pid,Version=0x0101,UsagePage=v4?0x0c:1,Usage=v4?1:2,ReportLength=length,ContainerId=Guid.NewGuid().ToString(),Path="hid#vid_1532&pid_"+pid.ToString("x4")+"&mi_03#"+Guid.NewGuid().ToString()};
                Payload=new byte[v4?26:38]; Payload[0]=1; Payload[1]=2; Payload[2]=3;
                for(int i=0;i<3;i++){int at=3+i*7,n=800*(i+1);Payload[at]=(byte)(v4?i:i+1);Payload[at+1]=Payload[at+3]=(byte)(n>>8);Payload[at+2]=Payload[at+4]=(byte)n;Payload[at+5]=(byte)(60+i);Payload[at+6]=(byte)(70+i);}
                Payload[24]=0x7a; Payload[25]=0x6f;
            }
            public byte[] Exchange(byte[] q,int delay){return SendFeature(q)?GetFeature(delay):null;}
            public bool SendFeature(byte[] q)
            {
                request=(byte[])q.Clone();Requests.Add((byte[])q.Clone());Sends++;attempt=0;
                if(lastPollingSet!=0) {
                    long gap=(System.Diagnostics.Stopwatch.GetTimestamp()-lastPollingSet)*1000/System.Diagnostics.Stopwatch.Frequency;
                    MinimumPollingGapMs=Math.Min(MinimumPollingGapMs,gap); lastPollingSet=0;
                }
                byte cls=q[offset+6],cmd=q[offset+7];if(cmd>=0x80)return true;
                Writes++;writeNumber++;
                if(v4 && cls==0 && cmd==0x40) lastPollingSet=System.Diagnostics.Stopwatch.GetTimestamp();
                if(WriteFault==5 && writeNumber>1)return true;
                if(cls==4 && cmd==6) {Payload=new byte[q[offset+5]];Array.Copy(q,offset+8,Payload,0,Payload.Length);}
                else if(cls==0 && (cmd==5 || cmd==0x40)) Rate=RazerProtocol.DecodeRate(v4?PollingProtocolKind.V4HighRate:PollingProtocolKind.Legacy,q[offset+(v4?9:8)]);
                else Unexpected++;
                if(writeNumber==1 && (WriteFault==2 || WriteFault==3))pendingFault=WriteFault;
                if(WriteFault==4 && writeNumber==1)throw new InvalidOperationException("Injected after delivery");
                return true;
            }
            public byte[] GetFeature(int delay)
            {
                Gets++;Delays.Add(delay);if(Sleeping)return null;var q=request;int o=offset;byte cls=q[o+6],cmd=q[o+7];bool set=cmd<0x80;
                if(!set && (cls==4 && (FailDpi || cmd==0x86 && FailStages) || cls==0 && FailPolling))return null;
                var r=(byte[])q.Clone();r[o]=attempt++<Busy?(byte)1:(byte)2;
                if(!set) {
                    if(cls==4 && cmd==0x86) {r[o+5]=(byte)Payload.Length;Array.Copy(Payload,0,r,o+8,Payload.Length);if(ChangeAfterRead)Descriptor.Version++;}
                    else if(cls==4 && cmd==0x85 && !v4) {r[o+9]=Payload[11];r[o+10]=Payload[12];r[o+11]=Payload[13];r[o+12]=Payload[14];}
                    else if(cls==0 && cmd==(v4?0xc0:0x85)){byte encoded;RazerProtocol.TryEncodePollingRate(v4?PollingProtocolKind.V4HighRate:PollingProtocolKind.Legacy,Rate,out encoded);r[o+(v4?9:8)]=encoded;}
                    else if(cls==7 && cmd==0x80)r[o+9]=200;
                    else if(cls==7 && cmd==0x84)r[o+9]=1;
                    else {Unexpected++;return null;}
                }
                if(set && (WriteFault==1 || WriteFault==5) && writeNumber==1)r[o]=5;
                if(!set && pendingFault!=0){int f=pendingFault;pendingFault=0;if(f==2)return null;if(cls==4)r[o+19]^=1;else r[o+(v4?9:8)]^=1;}
                int corruption=FaultAfterBusy && attempt<=Busy?0:ReadFault;
                if(corruption==1 && cls==4)r[o+10]=6;
                if(corruption==2 && cls==4)r[o+9]=0;
                if(corruption==3 && cls==4)r[o+11]=9;
                if(corruption==4)r[o+1]^=1;
                if(corruption==5)r[o+6]^=1;
                if(corruption==6)r[o+7]^=1;
                if(corruption==8)r[o+5]=0;
                if(corruption==9)r[o+2]=1;
                if(corruption==10)r[o+89]=1;
                if(corruption==11 && o==1)r[0]=1;
                if(corruption==12)r[o+4]=1;
                if(corruption==13)return r.Take(89).ToArray();
                r[o+88]=RazerProtocol.CalculateCrc(r,o);if(corruption==7)r[o+88]^=1;return r;
            }
        }
    }
}
