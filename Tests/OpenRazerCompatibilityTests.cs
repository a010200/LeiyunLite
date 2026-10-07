using System;
using System.Collections.Generic;
using System.Linq;
using RazerBatteryTray;

namespace RazerBatteryTray.Tests
{
    internal static partial class OpenRazerCompatibilityTests
    {
        private static int passed, failed;
        private static bool readOnly;
        [STAThread]
        private static int Main(string[] args)
        {
            readOnly = args.Contains("--read-only");
            Test("Whole catalog: exact selective GET, transport exclusions and trust", CatalogReads);
            Test("Representative byte / XY / stages / high rate / receiver profiles", RepresentativeReads);
            Test("Every protocol family rejects corrupt response fields", CorruptReads);
            Test("DPI-only, polling-only and failed capability isolation", CapabilityIsolation);
            Test("Unknown, descriptor rejection and cached telemetry never grant writes", NegativeReads);
            Test("Whole catalog Rotation exclusion and exact local descriptor guard", RotationExclusion);
            Test("Encoders reject unknown values; legacy byte exact round trips", Encoders);
            if (!readOnly) { RunWriteTests(); RunSupplementalTests(); RunCorrectionTests(); RunControlPathTests(); RunUiTests(); }
            Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
        private static void Test(string name, Action test)
        { try { test(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + name + "\n" + ex); } }
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        private static RazerDeviceClient Client(ProfileFake f) { return new RazerDeviceClient(new FakeTransport(f), new HardwareCacheStore(null)); }
        private static void CatalogReads()
        {
            foreach (var p in OpenRazerCapabilityCatalog.All.Select(x=>DeviceCapabilityCatalog.Find(x.ProductId))) {
                var f = new ProfileFake(p.ProductId,91); var c = Client(f); var r = c.QueryRazerDeviceInfo();
                Check(f.Writes == 0 && f.Unexpected == 0,"No unexpected command or write: " + p.ProductId);
                Check(r.IsConnected && r.DeviceKind == RazerIdentityCatalog.Find(p.ProductId).Kind,"Identity is independent");
                if (p.Transport != TransportKind.HidFeature90Or91) {
                    Check(f.Requests.Count == 0 && r.ProtocolStatus == DeviceProtocolStatus.IdentityOnly && !r.IsWriteSupported,"Unsupported transport never probes"); continue;
                }
                Check(r.DpiKnown == p.GetDpi && r.PollingKnown == p.GetPolling && r.BatteryKnown == p.GetBattery,"Per-capability GET: " + p.ProductId);
                Check(r.ChargingKnown == (p.ChargingProtocol != ChargingProtocolKind.None),"Charging model");
                Check(r.RotationTrust != CapabilityTrust.HardwareVerified || p.ProductId == 0xde || p.ProductId == 0xdf,"No upstream Rotation grant");
                if (readOnly && p.ProductId != 0xc0 && p.ProductId != 0xc1 && p.ProductId != 0xde && p.ProductId != 0xdf)
                    Check(!r.IsWriteSupported && !c.SetDpiVerified(p.ProductId,900,r.DeviceKey) && f.Writes == 0,"Stage B new PID is read only");
            }
        }
        private static void RepresentativeReads()
        {
            foreach (int length in new[] {90,91}) foreach (int pid in new[] {0x15,0x37,0x5c,0xa5,0xa6,0xb6,0xb7,0xb3,0xc0,0xc1,0xde,0xdf}) {
                var f = new ProfileFake(pid,length); var r = Client(f).QueryRazerDeviceInfo();
                Check(r.DpiKnown && r.Dpi == 900 && r.PollingKnown && r.PollingRate == 500,"Live values: " + pid);
                Check(f.Unexpected == 0 && f.Writes == 0,"Exact TID/family");
                if (pid == 0xb3) Check(r.DeviceKind == DeviceKind.GenericReceiver && r.DeviceName.Contains("Dongle") && r.ReceiverTrust == CapabilityTrust.UpstreamVerified,"Proxy retains receiver identity");
            }
        }
        private static void CorruptReads()
        {
            foreach (int pid in new[] {0x15,0x5c,0xa6,0xb3,0xde}) foreach (int length in new[] {90,91}) foreach (int fault in Enumerable.Range(1,10)) {
                var f = new ProfileFake(pid,length) { Corruption = fault };
                var r = Client(f).QueryRazerDeviceInfo();
                Check(!r.DpiKnown && !r.PollingKnown && !r.BatteryKnown && !r.IsWriteSupported && f.Writes == 0,"Corrupt read rejected " + pid + "/" + fault);
            }
        }
        private static void CapabilityIsolation()
        {
            var dpi = Client(new ProfileFake(0x46,91)).QueryRazerDeviceInfo();
            var poll = Client(new ProfileFake(0x42,91)).QueryRazerDeviceInfo();
            Check(dpi.DpiKnown && !dpi.PollingKnown,"DPI-only"); Check(poll.PollingKnown && !poll.DpiKnown,"Polling-only");
            var f = new ProfileFake(0xa6,91) { FailDpi = true };
            var r = Client(f).QueryRazerDeviceInfo();
            Check(r.ProtocolStatus == DeviceProtocolStatus.Ready && r.BatteryKnown && !r.DpiKnown && !r.IsDpiWriteSupported,"Battery live is not a DPI grant");
            f.FailDpi = false; f.FailPolling = true; r = Client(f).QueryRazerDeviceInfo();
            Check(r.DpiKnown && !r.PollingKnown && !r.IsPollingWriteSupported,"Failed polling stays separate");
        }
        private static void NegativeReads()
        {
            foreach (int pid in new[] {0xffff,0xa4,0x13,0x16,0x96,0x99,0xcb}) {
                var f = new ProfileFake(pid,91); var c = Client(f); var r = c.QueryRazerDeviceInfo();
                Check(f.Requests.Count == 0 && !r.IsWriteSupported && !c.SetDpiVerified(pid,900,r.DeviceKey),"Identity-only no protocol");
            }
            foreach (int mode in Enumerable.Range(0,5)) {
                var f = new ProfileFake(0xa6,91);
                if (mode == 0) f.Descriptor.VendorId=0x1234;
                if (mode == 1) f.Descriptor.UsagePage=1;
                if (mode == 1) f.Descriptor.Usage=6;
                if (mode == 2) f.Descriptor.ReportLength=89;
                if (mode == 3) f.Descriptor.ReportLength=90;
                if (mode == 4) f.Descriptor.UsagePage=2;
                var c = Client(f); var r = c.QueryRazerDeviceInfo();
                Check(f.Requests.Count == 0 && !c.SetDpiVerified(0xa6,900,r.DeviceKey),"Descriptor denied");
            }
            var sleeping = new ProfileFake(0xa6,91); var client = Client(sleeping); client.QueryRazerDeviceInfo(); sleeping.Sleeping=true;
            var cached = client.QueryRazerDeviceInfo();
            Check(cached.ProtocolStatus == DeviceProtocolStatus.Cached && !cached.IsDpiWriteSupported && !cached.IsPollingWriteSupported && cached.DpiTrust == CapabilityTrust.IdentityOnly && !client.SetDpiVerified(0xa6,900,cached.DeviceKey),"Cache never authorizes writes");
        }
        private static void RotationExclusion()
        {
            foreach (var p in OpenRazerCapabilityCatalog.All) foreach (int length in new[] {90,91}) {
                var f = new ProfileFake(p.ProductId,length); var c = Client(f); var r = c.QueryRazerDeviceInfo();
                bool permitted = (p.ProductId == 0xde || p.ProductId == 0xdf) && length == 91;
                Check(RazerProtocolProfile.SupportsRotation(f.Descriptor) == permitted && r.IsRotationWriteSupported == permitted,"Rotation matrix");
                if (!permitted) Check(!c.SetRotationVerified(p.ProductId,1,r.DeviceKey).WriteAttempted && f.Writes == 0,"No Rotation write");
            }
        }
        private static void Encoders()
        {
            byte value;
            foreach (int hz in new[] {0,1,126,999,99999}) foreach (var kind in new[] {PollingProtocolKind.Legacy,PollingProtocolKind.HighRate}) Check(!RazerProtocol.TryEncodePollingRate(kind,hz,out value),"Unknown rate rejected");
            Check(!RazerProtocol.TryEncodePollingRate(PollingProtocolKind.Legacy,250,out value),"Legacy 250 unsupported");
            Check(RazerProtocol.TryEncodePollingRate(PollingProtocolKind.HighRate,250,out value) && value == 0x20,"Known high rate 250 wire fact");
            foreach (int dpi in new[] {100,800,1600,6751,-1}) Check(!RazerProtocol.TryEncodeLegacyDpiByte(dpi,out value),"No byte quantization");
            Check(RazerProtocol.TryEncodeLegacyDpiByte(900,out value) && value == 34 && RazerProtocol.DecodeLegacyDpiByte(value) == 900,"Exact byte round trip");
        }
        private static void RunWriteTests()
        {
            Test("Whole catalog writes require live per-capability permission and exact values", CatalogWrites);
            Test("DPI writes preserve inactive stages and exact asymmetric rollback", DpiPreservation);
            Test("All high-rate values and both storage passes use exact transactions", HighRates);
            Test("DPI / polling ACK, readback, exception and rollback fault injection", RollbackFaults);
            Test("Stale instance, interface and changed descriptor cannot receive writes", WriteTargets);
        }
        private static void CatalogWrites()
        {
            foreach(var p in OpenRazerCapabilityCatalog.All.Select(x=>DeviceCapabilityCatalog.Find(x.ProductId))) foreach(int length in new[] {90,91}) {
                var f=new ProfileFake(p.ProductId,length); var c=Client(f); var r=c.QueryRazerDeviceInfo();
                var dpi=c.SetDpiTransactional(p.ProductId,1350,r.DeviceKey,r.InterfacePath);
                Check(dpi.Success==r.IsDpiWriteSupported,"DPI permission " + p.ProductId);
                var rate=c.SetPollingTransactional(p.ProductId,1000,r.DeviceKey,r.InterfacePath);
                Check(rate.Success==r.IsPollingWriteSupported,"Polling permission " + p.ProductId);
                int before=f.Writes;
                Check(!c.SetDpiTransactional(p.ProductId,99999,r.DeviceKey).WriteAttempted && !c.SetPollingTransactional(p.ProductId,999,r.DeviceKey).WriteAttempted && f.Writes==before,"Invalid never writes");
                Check(f.Unexpected==0,"Exact protocol family/TID: " + p.ProductId);
            }
            var failed=new ProfileFake(0xa6,91) { FailDpi=true }; var client=Client(failed); var reading=client.QueryRazerDeviceInfo();
            Check(!client.SetDpiTransactional(0xa6,1350,reading.DeviceKey).WriteAttempted,"Battery does not grant DPI");
            Check(client.SetPollingTransactional(0xa6,1000,reading.DeviceKey).Success,"Independent polling succeeds");
        }
        private static void DpiPreservation()
        {
            foreach(int pid in new[] {0xa6,0xb3,0xde,0xdf}) {
                var f=new ProfileFake(pid,91); byte[] original=(byte[])f.StagePayload.Clone(); var c=Client(f); var r=c.QueryRazerDeviceInfo();
                var result=c.SetDpiTransactional(pid,1250,r.DeviceKey,r.InterfacePath);
                Check(result.Success && result.Before==900 && result.After==1250,"Stage result");
                for(int i=0;i<original.Length;i++) if(i<11 || i>14) Check(original[i]==f.StagePayload[i],"Other stage bytes preserved");
            }
            foreach(int pid in new[] {0x15,0x37,0x5c}) {
                var f=new ProfileFake(pid,91) { DpiY=1350 }; var c=Client(f); var r=c.QueryRazerDeviceInfo(); f.WriteFault=1;
                var result=c.SetDpiTransactional(pid,1800,r.DeviceKey,r.InterfacePath);
                Check(!result.Success && result.RollbackSucceeded && f.Dpi==900 && f.DpiY==1350,"Restore original asymmetric X/Y");
            }
        }
        private static void HighRates()
        {
            foreach(int pid in new[] {0x91,0xb3,0xc1,0xbe}) foreach(int rate in new[] {125,500,1000,2000,4000,8000}) {
                var f=new ProfileFake(pid,91); var c=Client(f); var r=c.QueryRazerDeviceInfo();
                var result=c.SetPollingTransactional(pid,rate,r.DeviceKey,r.InterfacePath);
                Check(result.Success && f.Rate==rate,"High rate target");
                if(rate!=500) {
                    var sets=f.Requests.Where(q=>q[7]==0 && q[8]==0x40).ToArray();
                    Check(sets.Length==2 && sets[0][9]==0 && sets[1][9]==1 && sets[0][2]==f.Profile.PollingSetTransaction && sets[1][2]==f.Profile.PollingSecondTransaction,"Two exact storage/TID passes");
                }
            }
            var old=new ProfileFake(0xc7,91); var client=Client(old); var reading=client.QueryRazerDeviceInfo();
            Check(!client.SetPollingTransactional(0xc7,250,reading.DeviceKey).WriteAttempted,"Upstream 250 absent from exact legacy wire mapper");
        }
        private static void RollbackFaults()
        {
            foreach(int pid in new[] {0x15,0x5c,0xa6,0xb3,0xc1,0xde}) foreach(bool polling in new[] {false,true}) foreach(int fault in Enumerable.Range(1,5)) {
                var f=new ProfileFake(pid,91); var c=Client(f); var r=c.QueryRazerDeviceInfo(); var before=(byte[])f.StagePayload.Clone(); f.WriteFault=fault;
                PerformanceWriteResult result=polling?(PerformanceWriteResult)c.SetPollingTransactional(pid,1000,r.DeviceKey,r.InterfacePath):c.SetDpiTransactional(pid,1350,r.DeviceKey,r.InterfacePath);
                Check(!result.Success && result.WriteAttempted && result.RollbackAttempted,"Failure cannot be success " + pid + "/" + fault);
                Check(result.RollbackSucceeded==(fault!=5),"Rollback outcome " + pid + "/" + fault);
                if(result.RollbackSucceeded) {
                    Check((polling?f.Rate:f.Profile.GetStages?(f.StagePayload[11]<<8|f.StagePayload[12]):f.Dpi)==(polling?500:900),"Restored before");
                    if(!polling && f.Profile.GetStages) Check(before.SequenceEqual(f.StagePayload),"Entire stages restored");
                }
                Check(f.Unexpected==0,"No alternate protocol attempt");
            }
        }
        private static void WriteTargets()
        {
            foreach(int pid in new[] {0x15,0x5c,0xa6,0xb3,0xde}) foreach(int mode in Enumerable.Range(0,7)) {
                var f=new ProfileFake(pid,91); var c=Client(f); var r=c.QueryRazerDeviceInfo(); string key=r.DeviceKey,path=r.InterfacePath;
                if(mode==0) key="old-key";
                if(mode==1) path="old-path";
                if(mode==2) f.Descriptor.Path="new-path";
                if(mode==3) f.Descriptor.Usage=6;
                if(mode==4) f.Descriptor.Version++;
                if(mode==5) f.ChangeAfterRead=true;
                if(mode==6) c.InvalidateTarget();
                Check(!c.SetDpiTransactional(pid,1350,key,path).WriteAttempted && f.Writes==0,"Stale DPI refused: " + mode);
                Check(!c.SetPollingTransactional(pid,1000,key,path).WriteAttempted && f.Writes==0,"Stale polling refused: " + mode);
            }
        }

        internal sealed class FakeTransport : IHidTransport
        {
            internal readonly List<IHidDevice> Devices = new List<IHidDevice>();
            internal FakeTransport(params IHidDevice[] devices) { Devices.AddRange(devices); }
            public void Visit(Func<IHidDevice,bool> visitor) { foreach(var device in Devices) if(visitor(device)) break; }
        }
        internal sealed class ProfileFake : IHidDevice, IHidDescriptor
        {
            internal readonly DeviceCapabilityProfile Profile;
            public HidDescriptor Descriptor { get; private set; }
            public int ProductId { get { return Descriptor.ProductId; } }
            public int ReportLength { get; private set; }
            public string ProductName { get { return "Fake"; } }
            internal readonly List<byte[]> Requests = new List<byte[]>();
            internal int Writes, Unexpected, Corruption, WriteFault, Dpi=900, DpiY=900, Rate=500, Rotation=-9;
            private int primaryWrites, pendingReadFault;
            internal bool ChangeAfterRead;
            internal bool Sleeping, FailDpi, FailPolling;
            internal byte[] StagePayload = new byte[38];
            internal ProfileFake(int pid,int length,DeviceCapabilityProfile effective=null)
            {
                Profile=effective??DeviceCapabilityCatalog.Find(pid); ReportLength=length;
                Descriptor=new HidDescriptor { VendorId=0x1532,ProductId=pid,Version=0x0100,ReportLength=length,UsagePage=1,Usage=2,Path=Guid.NewGuid().ToString(),ContainerId=Guid.NewGuid().ToString() };
                StagePayload[0]=1; StagePayload[1]=2; StagePayload[2]=3;
                for(int i=0;i<3;i++) { int at=3+i*7,value=450*(i+1); StagePayload[at]=(byte)(i+1); StagePayload[at+1]=StagePayload[at+3]=(byte)(value>>8); StagePayload[at+2]=StagePayload[at+4]=(byte)value; }
            }
            public byte[] Exchange(byte[] q,int delay)
            {
                Requests.Add((byte[])q.Clone()); int o=ReportLength==91?1:0; byte cls=q[o+6],cmd=q[o+7],tid=q[o+1];
                if (Sleeping) return null;
                bool isSet = cmd < 0x80;
                if (isSet) Writes++;
                bool primary=isSet && (cls!=0 || cmd==5 || q[o+8]==0);
                if(primary) primaryWrites++;
                if(isSet && WriteFault==5 && primaryWrites>1) { var ignored=(byte[])q.Clone(); ignored[o]=2; ignored[o+88]=RazerProtocol.CalculateCrc(ignored,o); return ignored; }
                bool valid = false; var r=(byte[])q.Clone();
                if(Profile != null) {
                    if(cls==4) {
                        if(FailDpi) return null;
                        if(cmd==0x86 && Profile.GetStages && tid==Profile.DpiStageTransaction) { valid=true; Array.Copy(StagePayload,0,r,o+8,38); }
                        if(cmd==6 && Profile.SetStages && tid==Profile.DpiStageTransaction) { valid=true; Array.Copy(q,o+8,StagePayload,0,38); }
                        if(cmd==0x85 && Profile.GetDpi && Profile.DpiProtocol==DpiProtocolKind.ModernXY && tid==Profile.DpiTransaction) { valid=true; r[o+9]=(byte)(Dpi>>8); r[o+10]=(byte)Dpi; r[o+11]=(byte)(DpiY>>8); r[o+12]=(byte)DpiY; }
                        if(cmd==5 && Profile.SetDpi && tid==Profile.DpiSetTransaction) { valid=true; Dpi=q[o+9]<<8|q[o+10]; DpiY=q[o+11]<<8|q[o+12]; }
                        if(cmd==0x81 && Profile.GetDpi && Profile.DpiProtocol==DpiProtocolKind.LegacyByte && tid==Profile.DpiTransaction) { valid=true; r[o+8]=(byte)(Dpi*255/6750); r[o+9]=(byte)(DpiY*255/6750); }
                        if(cmd==1 && Profile.SetDpi && tid==Profile.DpiSetTransaction) { valid=true; Dpi=RazerProtocol.DecodeLegacyDpiByte(q[o+8]); DpiY=RazerProtocol.DecodeLegacyDpiByte(q[o+9]); }
                    }
                    if(cls==0 && Profile.GetPolling) {
                        if(FailPolling) return null;
                        byte get=Profile.PollingProtocol==PollingProtocolKind.Legacy?(byte)0x85:(byte)0xc0;
                        byte set=Profile.PollingProtocol==PollingProtocolKind.Legacy?(byte)5:(byte)0x40;
                        byte encoded; RazerProtocol.TryEncodePollingRate(Profile.PollingProtocol,Rate,out encoded);
                        if(cmd==get && tid==Profile.PollingTransaction) { valid=true; r[o+(get==0x85?8:9)]=encoded; }
                        if(cmd==set && Profile.SetPolling && (tid==Profile.PollingSetTransaction || q[o+8]==1 && tid==Profile.PollingSecondTransaction)) { valid=true; Rate=RazerProtocol.DecodeRate(Profile.PollingProtocol,q[o+(set==5?8:9)]); }
                    }
                    if(cls==7 && cmd==0x80 && Profile.GetBattery && tid==Profile.BatteryTransaction) { valid=true; r[o+9]=128; }
                    if(cls==7 && cmd==0x84 && Profile.ChargingProtocol==ChargingProtocolKind.Query && tid==Profile.ChargingTransaction) { valid=true; r[o+9]=0; }
                }
                if(cls==0xb && tid==0x1f && RazerProtocolProfile.SupportsRotation(Descriptor)) {
                    if(cmd==0x94) { valid=true; r[o+8]=r[o+9]=1; r[o+10]=unchecked((byte)(sbyte)Rotation); }
                    if(cmd==0x14) { valid=true; Rotation=unchecked((sbyte)q[o+10]); }
                }
                if(!valid) { Unexpected++; return null; }
                r[o]=2;
                if(primary && primaryWrites==1) {
                    if(WriteFault==1 || WriteFault==5) r[o]=5;
                    if(WriteFault==2 || WriteFault==3) pendingReadFault=WriteFault;
                    if(WriteFault==4) throw new InvalidOperationException("Injected after delivery");
                }
                if(!isSet && pendingReadFault!=0) {
                    int fault=pendingReadFault; pendingReadFault=0;
                    if(fault==2) return null;
                    if(cls==0) { byte encoded; RazerProtocol.TryEncodePollingRate(Profile.PollingProtocol,125,out encoded); r[o+(cmd==0x85?8:9)]=encoded; }
                    if(cls==4 && cmd==0x86) r[o+12]^=1;
                    if(cls==4 && cmd==0x85) r[o+10]^=1;
                    if(cls==4 && cmd==0x81) r[o+8]^=1;
                }
                if(!isSet && ChangeAfterRead) { ChangeAfterRead=false; Descriptor.Path="changed-during-read"; }
                if(Corruption==1) r[o+1]^=0x20;
                if(Corruption==2) r[o+6]^=1;
                if(Corruption==3) r[o+7]^=1;
                if(Corruption==4) r[o+2]=1;
                if(Corruption==5) r[o+5]=0;
                if(Corruption==6) r[o]=5;
                if(Corruption==7) return null;
                if(Corruption==8) return r.Take(89).ToArray();
                if(Corruption==10) r[o+4]=1;
                r[o+88]=RazerProtocol.CalculateCrc(r,o);
                if(Corruption==9) r[o+88]^=1;
                return r;
            }
        }
    }
}
