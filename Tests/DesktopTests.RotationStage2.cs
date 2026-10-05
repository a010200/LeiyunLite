using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RazerBatteryTray.Desktop
{
    internal static partial class DesktopTests
    {
        internal static int RunRotationStage2(string directory)
        {
            artifacts = directory; Directory.CreateDirectory(artifacts);
            Test("Existing capability bounds; PID-only wired grant still rejected", CapabilityChecks);
            Test("Existing 00DF protocol, readback, instance safety and rollback", RotationCommands);
            Test("Existing exception / lost-reply / rollback / descriptor gates", RotationFailures);
            Test("00DE and 00DF verified descriptors plus negative gate matrix", RotationDescriptorMatrix);
            Test("00DE live Probe, bound GET, SET/readback, stale key and bounds", WiredRotationCommands);
            Test("00DE malformed / non-success readback and rollback faults", WiredRotationFailures);
            Test("Dual active wired route and return to wireless, existing ordering", RotationDualRoute);
            Test("Device UI gets live wired angle; pending-only Apply from verified model", RotationWiredUi);
            string summary = "RESULT: " + passed + " passed, " + failed + " failed";
            results.Add(summary); Console.WriteLine(summary);
            File.WriteAllLines(Path.Combine(artifacts,"rotation-tests.txt"),results);
            return failed == 0 ? 0 : 1;
        }

        private static void RotationDescriptorMatrix()
        {
            foreach (int pid in new[] { 0x00DE, 0x00DF }) {
                var valid = new DeviceFake(91); valid.Descriptor.ProductId = pid;
                var profile = RazerProtocolProfile.For(valid.Descriptor);
                Check(profile.RotationReadVerified && profile.RotationWriteCandidate && profile.RotationWriteVerified &&
                    RazerProtocolProfile.SupportsRotation(valid.Descriptor),"Verified profile " + pid);
                var cap = DeviceCapabilities.For(valid.Descriptor);
                Check(cap.AcceptsRotation(-44) && cap.AcceptsRotation(44) && !cap.AcceptsRotation(-45) && !cap.AcceptsRotation(45),"Descriptor bounds");
                for (int mode=0; mode<8; mode++) {
                    var fake = new DeviceFake(mode==1?90:91); fake.Descriptor.ProductId=pid;
                    if(mode==0)fake.Descriptor.Version=0x0101;
                    if(mode==2)fake.Descriptor.Usage=1;
                    if(mode==3)fake.Descriptor.UsagePage=0xFF00;
                    if(mode==4)fake.Descriptor.VendorId=0x9999;
                    if(mode==5)fake.Descriptor.ProductId=0x00C0;
                    if(mode==6)fake.Descriptor.ProductId=0x1234;
                    if(mode==7)fake.Descriptor.Usage=6;
                    var p=RazerProtocolProfile.For(fake.Descriptor);
                    Check(!RazerProtocolProfile.SupportsRotation(fake.Descriptor) && (p==null || !p.RotationReadVerified && !p.RotationWriteVerified),"Profile negative " + mode);
                    Check(!DeviceCapabilities.For(fake.Descriptor).AcceptsRotation(0),"Capability negative " + mode);
                    var c=new RazerDeviceClient(new Transport(fake),new HardwareCacheStore(null));
                    var r=c.QueryRazerDeviceInfo();
                    Check(!r.RotationKnown && !r.IsRotationHardwareVerified && !r.IsRotationWriteSupported &&
                        !c.SetRotationVerified(fake.ProductId,0,r.DeviceKey).WriteAttempted && fake.Writes==0,"No negative-gate SET " + mode);
                }
            }
        }

        private static void WiredRotationCommands()
        {
            var f=new DeviceFake(91); f.Descriptor.ProductId=0x00DE; f.Rotation=-9;
            var c=new RazerDeviceClient(new Transport(f),new HardwareCacheStore(null)); var r=c.QueryRazerDeviceInfo();
            Check(r.ProductId==0x00DE && r.RotationKnown && r.RotationAngle==-9 && r.IsRotationWriteSupported && r.IsRotationHardwareVerified,"Wired Probe");
            Check(DeviceCapabilities.For(r).AcceptsRotation(-8),"Verified reading capability");
            int angle;Check(c.TryGetRotationVerified(0x00DE,r.DeviceKey,out angle)&&angle==-9,"Bound wired GET");
            Check(!c.SetRotationVerified(0x00DE,-8).WriteAttempted && !c.SetRotationVerified(0x00DE,-8,"stale").WriteAttempted &&
                !c.SetRotationVerified(0x00DF,-8,r.DeviceKey).WriteAttempted && !c.SetRotationVerified(0x00DE,45,r.DeviceKey).WriteAttempted && f.Writes==0,"Unsafe arguments denied");
            var target=c.SetRotationVerified(0x00DE,-8,r.DeviceKey);
            Check(target.Success && target.Before==-9 && target.After==-8 && f.Writes==1,"Wired target");
            var restore=c.SetRotationVerified(0x00DE,-9,r.DeviceKey);
            Check(restore.Success && restore.After==-9 && f.Writes==2 && f.Rotation==-9,"Wired restore");
        }

        private sealed class WiredReplyFault : IHidDevice, IHidDescriptor
        {
            internal readonly DeviceFake Inner=new DeviceFake(91); internal int Mode;
            internal WiredReplyFault(){Inner.Descriptor.ProductId=0x00DE;}
            public HidDescriptor Descriptor {get{return Inner.Descriptor;}}
            public int ProductId {get{return Inner.ProductId;}}
            public string ProductName {get{return Inner.ProductName;}}
            public int ReportLength {get{return Inner.ReportLength;}}
            public byte[] Exchange(byte[] request,int delay){
                var r=Inner.Exchange(request,delay);
                if(request[7]==0x0B && request[8]==0x94){
                    if(Mode==0)r[1]=4;
                    if(Mode==1)r[11]=45;
                    if(Mode==2)r[89]^=1;
                    if(Mode==3)r[6]=2;
                    if(Mode!=2)r[89]=RazerProtocol.CalculateCrc(r,1);
                }
                return r;
            }
        }
        private static void WiredRotationFailures()
        {
            for(int mode=1;mode<=7;mode++) {
                var f=new RotationFault();f.Inner.Descriptor.ProductId=0x00DE;
                var c=new RazerDeviceClient(new Transport(f),new HardwareCacheStore(null));var r=c.QueryRazerDeviceInfo();f.Mode=mode;
                var result=c.SetRotationVerified(0x00DE,10,r.DeviceKey);
                Check(!result.Success,"Wired fault cannot pass " + mode);
                Check(mode<=4 ? result.RollbackAttempted && f.SetCount==2 : !result.WriteAttempted && f.SetCount==0,"Wired bounded recovery " + mode);
                if(mode<=4)Check(result.RollbackSucceeded==(mode!=4),"Wired restore truth " + mode);
            }
            for(int mode=0;mode<4;mode++) {
                var f=new WiredReplyFault{Mode=mode};var c=new RazerDeviceClient(new Transport(f),new HardwareCacheStore(null));var r=c.QueryRazerDeviceInfo();
                Check(!r.RotationKnown && !r.IsRotationWriteSupported && !c.SetRotationVerified(0x00DE,0,r.DeviceKey).WriteAttempted && f.Inner.Writes==0,"Malformed/non-success wired GET " + mode);
            }
        }

        private sealed class RouteTransport : IHidTransport
        {
            internal readonly DeviceFake Wired=new DeviceFake(91), Wireless=new DeviceFake(91);
            internal bool Dual=true;
            internal RouteTransport(){Wired.Descriptor.ProductId=0x00DE;Wired.Rotation=Wireless.Rotation=-9;}
            private sealed class Inactive : IHidDevice, IHidDescriptor {
                private readonly DeviceFake d;internal Inactive(DeviceFake value){d=value;}
                public int ProductId{get{return d.ProductId;}}public HidDescriptor Descriptor{get{return d.Descriptor;}}
                public int ReportLength{get{return d.ReportLength;}}public string ProductName{get{return d.ProductName;}}
                public byte[] Exchange(byte[] r,int delay){var reply=d.Exchange(r,delay);reply[1]=4;reply[89]=RazerProtocol.CalculateCrc(reply,1);return reply;}
            }
            public void Visit(Func<IHidDevice,bool> visit){if(Dual){if(visit(new Inactive(Wireless)))return;visit(Wired);}else visit(Wireless);}
        }
        private static void RotationDualRoute()
        {
            var t=new RouteTransport();var c=new RazerDeviceClient(t,new HardwareCacheStore(null));var r=c.QueryRazerDeviceInfo();
            Check(r.ProductId==0x00DE && r.ProtocolStatus==DeviceProtocolStatus.Ready && r.RotationKnown && r.RotationAngle==-9,"Active wired selected");
            Check(!c.SetRotationVerified(0x00DE,-8,t.Wireless.Descriptor.InstanceKey).WriteAttempted && t.Wired.Writes==0,"Wrong instance denied");
            t.Dual=false;r=c.QueryRazerDeviceInfo();Check(r.ProductId==0x00DF && r.RotationKnown && r.RotationAngle==-9 && r.IsRotationHardwareVerified,"Wireless recovers");
        }

        private static void RotationWiredUi()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};DesktopApp.LoadTheme(app);
            var w=new ShellWindow(true);
            try {
                var f=new DeviceFake(91);f.Descriptor.ProductId=0x00DE;f.Rotation=-9;
                w.Reading=new RazerDeviceClient(new Transport(f),new HardwareCacheStore(null)).QueryRazerDeviceInfo();
                var page=Field<DevicePage>(w,"devicePage");page.UpdateReading();
                Check(Field<TextBox>(page,"angleValue").Text=="-9" && !Field<Button>(page,"applyRotation").IsEnabled,"Real readback; same angle no write");
                Field<Slider>(page,"angle").Value=-8;Check(Field<Button>(page,"applyRotation").IsEnabled,"Pending verified wired Apply");
                Invoke(page,"RestoreRotationPreview");Check(Field<TextBox>(page,"angleValue").Text=="-9" && !Field<Button>(page,"applyRotation").IsEnabled,"Undo preview");
                Check(f.Writes==0 && w.Macros==null,"UI test no hardware/Hook initialization");
            } finally {w.ClosePreview();app.Shutdown();}
        }
    }
    internal static class RotationStage2TestRunner
    {
        [STAThread] private static int Main(string[] args){return DesktopTests.RunRotationStage2(args[0]);}
    }
}
