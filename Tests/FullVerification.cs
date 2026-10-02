// Test-only entry point. Not included in either application project.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using RazerBatteryTray;
using RazerBatteryTray.Macros;
using RazerBatteryTray.Updates;
using RazerBatteryTray.Desktop;

internal static partial class FullVerification
{
    static string Root;
    static int Pass, Fail;
    static readonly List<string> Rows = new List<string>();
    static readonly Random Random = new Random(123202610);
    [STAThread] static int Main(string[] args)
    {
        Root = Path.GetFullPath(args[0]); Directory.CreateDirectory(Root);
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length > 1 && args[1].StartsWith("fix-", StringComparison.Ordinal)) return FixTests(args[1].Substring(4));
        if (args.Length > 1 && args[1] == "extra") return ExtraTests();
        if (args.Length > 1 && args[1] == "network") return NetworkTests();
        if (args.Length > 1 && args[1] == "hardware-read") return HardwareRead();
        if (args.Length > 1 && args[1] == "hardware-rotation") return HardwareRotation();
        if (args.Length > 1 && args[1] == "stress") return UiStress();
        if (args.Length > 1 && args[1] == "cold") return ColdStart();
        if (args.Length > 1 && args[1] == "hardware") return Hardware();
        ProtocolTests(); DpiTests(); DeviceTests(); RotationTests();
        ValidationTests(); MacroCancellation(); RouterTests(); RecordingTests(); PersistenceTests(); UpdateTests();
        FuzzTests(); ConcurrencyTests(); SchedulerTests();
        File.WriteAllLines(Path.Combine(Root, "results.tsv"), Rows, Encoding.UTF8);
        Console.WriteLine("RESULT: " + Pass + " passed, " + Fail + " failed"); return Fail == 0 ? 0 : 1;
    }
    static void Check(bool ok, string detail) { if (!ok) throw new Exception(detail); }
    static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) {
            if (ex is InvalidOperationException || ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is System.Xml.XmlException || ex is CryptographicException || ex is FormatException || ex is UnauthorizedAccessException) return;
            throw;
        } throw new Exception("Invalid input was accepted");
    }
    static void Test(string name, string module, Action action)
    {
        if (FixFilter != null && !FixFilter(name)) return;
        try { action(); Pass++; Rows.Add(name + "\tPASS\t" + module + "\t0/0\t"); Console.WriteLine("PASS " + name); }
        catch (Exception first) {
            Fail++; Console.WriteLine("FAIL " + name + " " + first); int reproduced = 0;
            for (int i = 1; i <= 3; i++) {
                try { action(); Console.WriteLine("REPRO " + name + " #" + i + " PASS"); }
                catch (Exception ex) { reproduced++; Console.WriteLine("REPRO " + name + " #" + i + " FAIL " + ex); }
            }
            Rows.Add(name + "\tFAIL\t" + module + "\t" + reproduced + "/3\t" + first.GetType().Name + ": " + first.Message.Replace('\t',' ').Replace('\n',' '));
        }
    }
    static object Field(object obj, string name) { return obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj); }
    static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj, args); }
    static string Dir() { string p = Path.Combine(Root, "fixtures", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    internal sealed class Transport : IHidTransport
    {
        internal readonly List<IHidDevice> Devices = new List<IHidDevice>();
        internal Transport(params IHidDevice[] devices) { Devices.AddRange(devices); }
        public void Visit(Func<IHidDevice,bool> visitor) { foreach (var d in Devices.ToArray()) if (visitor(d)) break; }
    }
    internal sealed class Hid : IHidDevice, IHidDescriptor
    {
        internal int[] Values = {400,800,1600,3200,6400};
        internal int Active = 3, Angle = -8, Rate = 1000, Writes, Exchanges, RotationSets, RotationReads;
        internal byte Battery = 128;
        internal bool Sleeping, Charging, IgnoreWrite;
        internal Func<byte[],byte[],byte[]> Filter;
        public HidDescriptor Descriptor { get; private set; }
        public int ProductId { get { return Descriptor.ProductId; } }
        public string ProductName { get { return "Full verification fake"; } }
        public int ReportLength { get { return Descriptor.ReportLength; } }
        internal Hid(int length = 91, int pid = 0x00DF, string key = null) {
            Descriptor = new HidDescriptor { VendorId = 0x1532, ProductId = pid, Version = 0x0100, ReportLength = length, UsagePage = 0xFF00, Path = "fake-" + (key ?? Guid.NewGuid().ToString("N")), ContainerId = key ?? Guid.NewGuid().ToString("N") };
        }
        public byte[] Exchange(byte[] q, int delay) {
            Exchanges++; if (Sleeping) return null;
            int o = ReportLength == 91 ? 1 : 0; var r = (byte[])q.Clone(); r[o] = 2; byte cls = q[o+6], cmd = q[o+7];
            if (cls == 4 && cmd == 6) { Writes++; if (!IgnoreWrite) { Active = q[o+9]; for (int i=0;i<Values.Length;i++) Values[i]=(q[o+12+i*7]<<8)|q[o+13+i*7]; } }
            if (cls == 4 && cmd == 0x86) {
                r[o+8]=1; r[o+9]=(byte)Active; r[o+10]=(byte)Values.Length;
                for(int i=0;i<Math.Min(5,Values.Length);i++) { int a=o+11+i*7; r[a]=(byte)(i+1); r[a+1]=r[a+3]=(byte)(Values[i]>>8); r[a+2]=r[a+4]=(byte)Values[i]; }
            }
            if (cls == 4 && cmd == 0x85) { r[o+9]=6; r[o+10]=64; }
            if (cls == 7 && cmd == 0x80) r[o+9]=Battery;
            if (cls == 7 && cmd == 0x84) r[o+9]=Charging ? (byte)1:(byte)0;
            if (cls == 0 && (cmd == 5 || cmd == 0x40)) { Writes++; if (!IgnoreWrite) Rate=cmd==5 ? RazerProtocol.DecodeLegacyPollingRate(q[o+8]):RazerProtocol.DecodePollingRate(q[o+9]); }
            if (cls == 0 && cmd == 0x85) r[o+8]=(byte)(Rate == 0 ? 0:1000/Rate);
            if (cls == 0 && cmd == 0xC0) r[o+9]=Rate==0 ? (byte)0:RazerProtocol.EncodePollingRate(Rate);
            if (cls == 0x0B && cmd == 0x14) { Writes++; RotationSets++; if (!IgnoreWrite) Angle=unchecked((sbyte)q[o+10]); }
            if (cls == 0x0B && cmd == 0x94) { RotationReads++; r[o+8]=1; r[o+9]=1; r[o+10]=unchecked((byte)(sbyte)Angle); }
            r[o+88]=RazerProtocol.CalculateCrc(r,o); return Filter == null ? r : Filter(q,r);
        }
    }
    static RazerDeviceClient Client(Hid h) { return new RazerDeviceClient(new Transport(h),new HardwareCacheStore(null)); }
    static void ProtocolTests()
    {
        foreach(int length in new[]{89,90,91,92}) { int n=length; Test("HID-report-length-"+n,"Protocol",()=>{
            var h=new Hid(n); var r=Client(h).QueryRazerDeviceInfo(); Check((n==90||n==91) ? r.ProtocolStatus==DeviceProtocolStatus.Ready : h.Exchanges==0&&!r.IsWriteSupported,"Illegal report length entered exchange");
        }); }
        for(int m=0;m<17;m++) { int mode=m; Test("HID-reply-reject-"+mode,"Protocol",()=>{
            var h=new Hid(); h.Filter=(q,r)=> {
                int o=1;
                switch(mode) {
                    case 0:return null; case 1:return new byte[0]; case 2:return r.Take(89).ToArray(); case 3:return r.Take(90).ToArray(); case 4:return r.Concat(new byte[]{0}).ToArray();
                    case 5:r[o+1]=0x3F;break; case 6:r[o+6]^=1;break; case 7:r[o+7]^=1;break; case 8:r[o+6]^=1;r[o+7]^=1;break;
                    case 9:r[o+88]++;return r;case 10:r[o+88]--;return r;case 11:r[o+9]^=1;return r;
                    case 12:r[o]=1;break;case 13:r[o]=3;break;case 14:r[o]=255;break;case 15:r[o+5]=0;break;case 16:r[o+5]=81;break;
                } r[o+88]=RazerProtocol.CalculateCrc(r,o); return r;
            }; var c=Client(h);var read=c.QueryRazerDeviceInfo(); Check(read.ProtocolStatus==DeviceProtocolStatus.PresentUnresponsive&&!read.BatteryKnown&&read.Dpi==0&&read.PollingRate==0,"Malformed reply counted as telemetry"); Check(!c.SetDpiVerified(h.ProductId,1200,read.DeviceKey)&&h.Writes==0,"Write after malformed probe");
        }); }
        Test("HID-stale-previous-command","Protocol",()=>{var h=new Hid();byte[] previous=null;h.Filter=(q,r)=>{var old=previous;previous=r;return old;};var read=Client(h).QueryRazerDeviceInfo();Check(!read.BatteryKnown&&read.Dpi==0&&read.PollingRate==0,"Previous command reply accepted");});
        Test("HID-status-all-256","Protocol",()=>{for(int s=0;s<256;s++){var h=new Hid();int st=s;h.Filter=(q,r)=>{r[1]=(byte)st;return r;};var read=Client(h).QueryRazerDeviceInfo();Check((st==2)==(read.ProtocolStatus==DeviceProtocolStatus.Ready),"status="+st);}});
    }
    static void DpiTests()
    {
        Test("DPI-boundary-math","DPI",()=>{ foreach(int d in new[]{100,150,200,400,800,1600,3200,6400,8000,35000,99,101,149,151,799,801,7999,8001,34999,35001,int.MinValue,int.MaxValue}) {double p=DpiScale.ToPosition(d);int back=DpiScale.FromPosition(p);Check(!double.IsNaN(p)&&p>=0&&p<=1&&back>=100&&back<=8000,"DPI boundary="+d);Check(Math.Abs(back-Math.Max(100,Math.Min(8000,(double)d)))<=25,"Snap error="+d);}Check(DpiScale.ToPosition(100)==0&&DpiScale.ToPosition(8000)==1,"End points");});
        for(int count=1;count<=5;count++) {int n=count;Test("DPI-stage-preserve-"+n,"DPI",()=>{for(int active=1;active<=n;active++){var h=new Hid();h.Values=new[]{400,800,1600,3200,6400}.Take(n).ToArray();h.Active=active;var before=(int[])h.Values.Clone();var c=Client(h);var r=c.QueryRazerDeviceInfo();Check(c.SetDpiVerified(h.ProductId,1250,r.DeviceKey),"Valid Stage write rejected");for(int i=0;i<n;i++)Check(h.Values[i]==(i==active-1?1250:before[i]),"Nonactive stage changed");Check(h.Active==active,"Active stage changed");}});}
        for(int mode=0;mode<12;mode++){int m=mode;Test("DPI-write-fault-"+m,"DPI",()=>{
            var h=new Hid();var c=Client(h);var r=c.QueryRazerDeviceInfo(); int stageReads=0;
            h.Filter=(q,a)=>{int o=1;bool get=q[o+6]==4&&q[o+7]==0x86;if(get)stageReads++;
                if(get) { if(m==0)a[o+10]=0;if(m==1)a[o+10]=6;if(m==2)a[o+18]=a[o+11];if(m==3)a[o+9]=99;if(m==4)a[o+12]=a[o+13]=0;if(m==5)a[o+14]=a[o+15]=0;if(m==6)return null;if(m==8&&h.Writes>0)return null;if(m==9&&h.Writes>0)a[o+12]++; }
                if(m==7&&q[o+6]==4&&q[o+7]==6)a[o]=3;a[o+88]=RazerProtocol.CalculateCrc(a,o);return a;};
            if(m==10)c.InvalidateTarget(); bool ok=c.SetDpiVerified(m==11?0x00DE:h.ProductId,1250,r.DeviceKey);Check(!ok,"Fault counted as successful DPI write");if(m<=6||m>=10)Check(h.Writes==0,"Unsafe DPI write attempted");
        });}
        int[] invalid={0,1,124,126,250,499,501,999,1001,16000,-1,int.MaxValue};
        foreach(int pid in new[]{0x00DE,0x00DF,0x00C0,0x00C1}){int p=pid;Test("Polling-profile-"+p.ToString("X4"),"Polling",()=>{foreach(int hz in DeviceCapabilities.For(p).Rates){var h=new Hid(91,p);var c=Client(h);var r=c.QueryRazerDeviceInfo();Check(c.SetRateVerified(p,hz,r.DeviceKey)&&h.Rate==hz,"Rate="+hz);}foreach(int hz in invalid.Concat(p==0x00C1?new int[0]:new[]{2000,4000,8000})){var h=new Hid(91,p);var c=Client(h);var r=c.QueryRazerDeviceInfo();Check(!c.SetRateVerified(p,hz,r.DeviceKey)&&h.Writes==0,"Unsupported write="+hz);}});}
        for(int mode=0;mode<6;mode++){int m=mode;Test("Polling-fault-"+m,"Polling",()=>{var h=new Hid();var c=Client(h);var r=c.QueryRazerDeviceInfo();if(m==0)h.Rate=0;if(m==1)h.IgnoreWrite=true;h.Filter=(q,a)=>{if(m==2&&h.Writes>0)return null;if(m==3&&h.Writes>0)a[9]=7;if(m==4&&q[8]==5)a[1]=3;a[89]=RazerProtocol.CalculateCrc(a,1);return a;};Check(!c.SetRateVerified(h.ProductId,500,m==5?"other-instance":r.DeviceKey),"Rate fault accepted");if(m==0||m==5)Check(h.Writes==0,"No write expected");});}
    }
    static void DeviceTests()
    {
        foreach(int pid in new[]{0x00A4,0xFFFF,0x0203,0x00B3}){int p=pid;Test("Identity-no-control-"+p,"Device",()=>{var h=new Hid(91,p);var c=Client(h);var r=c.QueryRazerDeviceInfo();Check(!r.IsWriteSupported&&h.Exchanges==0&&!c.SetDpiVerified(p,800,r.DeviceKey),"Identity granted protocol");});}
        Test("Wrong-VID","Device",()=>{var h=new Hid();h.Descriptor.VendorId=0x1234;Check(!Client(h).QueryRazerDeviceInfo().IsConnected&&h.Exchanges==0,"Wrong vendor probed");});
        Test("Hotplug-sleep-wake-A-B","Device",()=>{var a=new Hid();var b=new Hid();b.Battery=255;b.Values=new[]{3200};b.Active=1;var t=new Transport();var cache=new HardwareCacheStore(null);var c=new RazerDeviceClient(t,cache);Check(!c.QueryRazerDeviceInfo().IsConnected,"Empty");t.Devices.Add(a);var first=c.QueryRazerDeviceInfo();a.Sleeping=true;Check(c.QueryRazerDeviceInfo().ProtocolStatus==DeviceProtocolStatus.Cached,"Sleep cache");cache.CachedLastUpdated=DateTime.Now.AddMinutes(-6);Check(c.QueryRazerDeviceInfo().ProtocolStatus==DeviceProtocolStatus.PresentUnresponsive,"Stale cache");cache.CachedLastUpdated=DateTime.Now.AddHours(1);Check(c.QueryRazerDeviceInfo().ProtocolStatus!=DeviceProtocolStatus.Cached,"Future cache");a.Sleeping=false;Check(c.QueryRazerDeviceInfo().ProtocolStatus==DeviceProtocolStatus.Ready,"Wake");t.Devices.Clear();c.InvalidateTarget();Check(!c.QueryRazerDeviceInfo().IsConnected,"Unplug");t.Devices.Add(b);var second=c.QueryRazerDeviceInfo();Check(second.Dpi==3200&&second.BatteryPercent==100&&second.DeviceKey!=first.DeviceKey,"A values leaked to B");Check(!c.SetDpiVerified(b.ProductId,1200,first.DeviceKey)&&b.Writes==0,"Stale target wrote B");});
        foreach(int value in new[]{0,1,127,128,254,255}){int v=value;Test("Battery-raw-"+v,"Battery",()=>{var h=new Hid();h.Battery=(byte)v;var r=Client(h).QueryRazerDeviceInfo();Check(r.BatteryKnown&&r.BatteryPercent==(int)Math.Round(v/255.0*100),"Battery conversion");});}
        for(int m=0;m<2;m++){int mode=m;Test("Battery-partial-"+m,"Battery",()=>{var h=new Hid();h.Charging=true;h.Filter=(q,r)=>q[7]==7&&q[8]==(mode==0?0x80:0x84)?null:r;var info=Client(h).QueryRazerDeviceInfo();Check(mode==0?!info.BatteryKnown&&info.IsCharging:info.BatteryKnown&&!info.IsCharging,"Partial read");});}
    }
    static void RotationTests()
    {
        Test("Rotation-all-89-angles","Rotation",()=>{for(int a=-44;a<=44;a++){byte[] payload;Check(RazerProtocol.TryEncodeRotation(a,out payload),"encode="+a);var h=new Hid();h.Angle=a;var c=Client(h);var r=c.QueryRazerDeviceInfo();Check(r.RotationKnown&&r.RotationAngle==a,"decode="+a);var x=c.SetRotationVerified(h.ProductId,a,r.DeviceKey);Check(x.Success&&!x.WriteAttempted,"Same angle");}foreach(int bad in new[]{-45,45,int.MinValue,int.MaxValue}){byte[] p;Check(!RazerProtocol.TryEncodeRotation(bad,out p),"Invalid angle");}});
        for(int mode=0;mode<8;mode++){int m=mode;Test("Rotation-rollback-matrix-"+m,"Rotation",()=>{
            var h=new Hid();var c=Client(h);var read=c.QueryRazerDeviceInfo();h.RotationReads=0;h.RotationSets=0;
            h.Filter=(q,r)=>{bool get=q[7]==0x0B&&q[8]==0x94,set=q[7]==0x0B&&q[8]==0x14;
                if(m==0&&get)return null;if(m==1&&set&&h.RotationSets==1)r[1]=3;if(m==2&&get&&h.RotationSets==1)return null;
                if(m==3&&get&&h.RotationSets==1)r[11]=9;if(m==4&&set&&h.RotationSets==1)throw new IOException("fake write delivery exception");
                if(m==5){if(set&&h.RotationSets==1)r[1]=3;if(set&&h.RotationSets==2)throw new IOException("fake rollback exception");}
                if(m==6&&get&&h.RotationSets>0)return null;if(m==7&&get&&h.RotationSets==1)r[11]=9;
                r[89]=RazerProtocol.CalculateCrc(r,1);return r;};
            var x=c.SetRotationVerified(h.ProductId,10,read.DeviceKey);Check(!x.Success,"Fault success");if(m==0)Check(!x.WriteAttempted&&h.RotationSets==0,"Initial failure wrote");else{Check(x.WriteAttempted&&x.RollbackAttempted&&h.RotationSets==2,"Rollback not attempted");Check(x.RollbackSucceeded==(m!=6),"Rollback confirmation wrong");if(m==5)Check(h.RotationReads>=3,"No final read after rollback exception");}
        });}
    }
    static MacroLibrary Lib(params MacroStep[] steps){var l=new MacroLibrary();var m=new MacroDefinition{Id="main",Name="test"};m.Steps.AddRange(steps);l.Macros.Add(m);return l;}
    static void ValidationTests()
    {
        foreach(int n in new[]{0,1,500,501}){int count=n;Test("Macro-capacity-"+n,"Validation",()=>{var l=Lib(Enumerable.Range(0,count).Select(i=>new MacroStep{Kind=ActionKind.Delay,Number=0}).ToArray());if(count>500)Reject(()=>MacroValidation.Validate(l));else MacroValidation.Validate(l);});}
        foreach(int n in new[]{-1,0,1,600000,600001,int.MaxValue,int.MinValue}){int v=n;Test("Macro-delay-bound-"+n,"Validation",()=>{var l=Lib(new MacroStep{Kind=ActionKind.Delay,Number=v});if(v<0||v>600000)Reject(()=>MacroValidation.Validate(l));else MacroValidation.Validate(l);});}
        foreach(int depth in new[]{1,8,9}){int d=depth;Test("Macro-loop-depth-"+d,"Validation",()=>{var steps=Enumerable.Repeat(0,d).Select(i=>new MacroStep{Kind=ActionKind.LoopStart,Number=2}).Concat(Enumerable.Repeat(0,d).Select(i=>new MacroStep{Kind=ActionKind.LoopEnd})).ToArray();if(d>8)Reject(()=>MacroValidation.Validate(Lib(steps)));else MacroValidation.Validate(Lib(steps));});}
        foreach(int depth in new[]{2,3,16,17}){int d=depth;Test("Macro-call-depth-cycle-"+d,"Validation",()=>{var l=new MacroLibrary();for(int i=0;i<d;i++){var m=new MacroDefinition{Id=i.ToString(),Name="call"};if(i+1<d)m.Steps.Add(new MacroStep{Kind=ActionKind.CallMacro,Value=(i+1).ToString()});l.Macros.Add(m);}if(d>16)Reject(()=>MacroValidation.Validate(l));else MacroValidation.Validate(l);l.Macros[d-1].Steps.Add(new MacroStep{Kind=ActionKind.CallMacro,Value="0"});Reject(()=>MacroValidation.Validate(l));});}
        Test("Macro-invalid-enums-targets-shortcut","Validation",()=>{foreach(var step in new[]{new MacroStep{Kind=(ActionKind)99},new MacroStep{Kind=ActionKind.Keyboard,KeyCode=0},new MacroStep{Kind=ActionKind.Mouse,Mouse=(MouseAction)99},new MacroStep{Kind=ActionKind.Mouse,Number=0},new MacroStep{Kind=ActionKind.CallMacro,Value="missing"},new MacroStep{Kind=ActionKind.LoopStart,Number=1001},new MacroStep{Kind=ActionKind.LoopEnd}})Reject(()=>MacroValidation.Validate(Lib(step)));var l=Lib(new MacroStep());l.Bindings.Add(new MacroBinding{MacroId="main",Trigger=TriggerKind.WheelUp,Mode=RunMode.WhileHeld});Reject(()=>MacroValidation.Validate(l));l.Bindings[0].Trigger=TriggerKind.Keyboard;l.Bindings[0].KeyCode=123;l.Bindings[0].Modifiers=KeyModifiers.Control|KeyModifiers.Shift;Reject(()=>MacroValidation.Validate(l));});
    }
    internal sealed class Output : IMacroOutput
    {
        internal readonly ConcurrentQueue<string> Events = new ConcurrentQueue<string>(); internal bool BlockText;
        public void Key(int key,bool down){Events.Enqueue("K"+key+(down?"+":"-"));}
        public void MouseButton(MouseAction b,bool down){Events.Enqueue("M"+b+(down?"+":"-"));}
        public void Wheel(int n){Events.Enqueue("W"+n);}
        public void Text(string t,CancellationToken token){Events.Enqueue("T"+t);if(BlockText)token.WaitHandle.WaitOne();token.ThrowIfCancellationRequested();}
        public void Launch(string target,string args,bool command){Events.Enqueue((command?"C":"L")+target);}
    }
    static void Until(Func<bool> f){Check(SpinWait.SpinUntil(f,4000),"Timed out");}
    static void MacroCancellation()
    {
        foreach(MouseAction button in new[]{MouseAction.Left,MouseAction.Right,MouseAction.Middle,MouseAction.X1,MouseAction.X2})foreach(string context in new[]{"Delay","Text","Loop","Call","Repeat","Dispose"}){MouseAction b=button;string kind=context;Test("Cancel-"+kind+"-"+b,"MacroEngine",()=>{
            var l=Lib(new MacroStep{Kind=ActionKind.Keyboard,KeyCode=65,Press=PressMode.Down},new MacroStep{Kind=ActionKind.Mouse,Mouse=b,Press=PressMode.Down,Number=1});
            if(kind=="Text")l.Macros[0].Steps.Add(new MacroStep{Kind=ActionKind.Text,Value="blocked"});
            else if(kind=="Call"){var child=new MacroDefinition{Id="child",Name="child"};child.Steps.Add(new MacroStep{Kind=ActionKind.Delay,Number=600000});l.Macros.Add(child);l.Macros[0].Steps.Add(new MacroStep{Kind=ActionKind.CallMacro,Value="child"});}
            else {if(kind=="Loop")l.Macros[0].Steps.Add(new MacroStep{Kind=ActionKind.LoopStart,Number=1000});l.Macros[0].Steps.Add(new MacroStep{Kind=ActionKind.Delay,Number=600000});if(kind=="Loop")l.Macros[0].Steps.Add(new MacroStep{Kind=ActionKind.LoopEnd});}
            var output=new Output{BlockText=kind=="Text"};using(var e=new MacroEngine(output)){Check(e.Start(l,"main","test",kind=="Repeat",0),"Start rejected");Until(()=>output.Events.Contains("M"+b+"+"));if(kind=="Dispose")e.Dispose();else e.Stop();Until(()=>!e.IsRunning);}Check(output.Events.Contains("K65-")&&output.Events.Contains("M"+b+"-"),"Stuck simulated input");
        });}
        Test("Macro-output-order-all-buttons","MacroEngine",()=>{var steps=new List<MacroStep>();foreach(MouseAction b in Enum.GetValues(typeof(MouseAction)))steps.Add(new MacroStep{Kind=ActionKind.Mouse,Mouse=b,Press=PressMode.Tap,Number=2});steps.Add(new MacroStep{Kind=ActionKind.Keyboard,KeyCode=65});steps.Add(new MacroStep{Kind=ActionKind.Text,Value="中文😀"});steps.Add(new MacroStep{Kind=ActionKind.Launch,Value="fake-target"});steps.Add(new MacroStep{Kind=ActionKind.Command,Value="fake-command"});var l=Lib(steps.ToArray());MacroValidation.Validate(l);var o=new Output();using(var e=new MacroEngine(o)){e.Start(l,"main","test",false,0);Until(()=>!e.IsRunning);}var expected=new List<string>();foreach(MouseAction b in new[]{MouseAction.Left,MouseAction.Right,MouseAction.Middle,MouseAction.X1,MouseAction.X2}){expected.Add("M"+b+"+");expected.Add("M"+b+"-");}expected.AddRange(new[]{"W2","W-2","K65+","K65-","T中文😀","Lfake-target","Cfake-command"});Check(o.Events.SequenceEqual(expected),"Action order mismatch");});
    }
    internal sealed class Runner : IMacroRunner
    {
        public bool IsRunning{get;private set;} public string ActiveBinding{get;private set;} internal int Starts,Stops; internal bool Repeat;
        public bool Start(MacroLibrary l,string m,string b,bool repeat,int delay){if(IsRunning)return false;Starts++;IsRunning=true;ActiveBinding=b;Repeat=repeat;return true;}
        public void Stop(){Stops++;IsRunning=false;ActiveBinding=null;}
    }
    static void RouterTests()
    {
        foreach(RunMode mode in Enum.GetValues(typeof(RunMode)))for(int mods=0;mods<16;mods++){RunMode rm=mode;KeyModifiers modifier=(KeyModifiers)mods;Test("Binding-"+rm+"-modifier-"+mods,"Binding",()=>{var l=Lib(new MacroStep());var b=new MacroBinding{MacroId="main",Trigger=TriggerKind.Middle,Mode=rm,SuppressOriginal=true};l.Bindings.Add(b);var runner=new Runner();var r=new BindingRouter(runner);r.Configure(l);Check(r.Handle(new InputStroke{Trigger=TriggerKind.Middle,Modifiers=modifier,Down=true,RightButtonDown=true}),"Unrelated modifiers blocked");Check(runner.Starts==1,"Wrong start count");r.Handle(new InputStroke{Trigger=TriggerKind.Right,Down=false});Check(runner.IsRunning,"Right release stopped own hold");Check(r.Handle(new InputStroke{Trigger=TriggerKind.Middle,Down=false}),"Up not paired");Check(runner.IsRunning==(rm!=RunMode.WhileHeld),"Mode release behavior");if(rm==RunMode.Toggle){r.Handle(new InputStroke{Trigger=TriggerKind.Middle,Down=true});Check(!runner.IsRunning,"Toggle failed");}runner.Stop();});}
        Test("Binding-explicit-modifier-priority","Binding",()=>{var l=Lib(new MacroStep());foreach(KeyModifiers mod in new[]{KeyModifiers.None,KeyModifiers.Shift,KeyModifiers.Control|KeyModifiers.Shift})l.Bindings.Add(new MacroBinding{MacroId="main",Trigger=TriggerKind.Middle,Modifiers=mod,Mode=RunMode.WhileHeld});var n=new Runner();var r=new BindingRouter(n);r.Configure(l);r.Handle(new InputStroke{Trigger=TriggerKind.Middle,Modifiers=KeyModifiers.Shift|KeyModifiers.Control,Down=true});Check(n.ActiveBinding==l.Bindings[2].Id,"Explicit priority");});
        foreach(bool suppress in new[]{false,true}){bool s=suppress;Test("Binding-delete-between-down-up-"+s,"Binding",()=>{var l=Lib(new MacroStep());l.Bindings.Add(new MacroBinding{MacroId="main",Trigger=TriggerKind.Middle,Mode=RunMode.WhileHeld,SuppressOriginal=s});var runner=new Runner();var r=new BindingRouter(runner);r.Configure(l);Check(r.Handle(new InputStroke{Trigger=TriggerKind.Middle,Down=true})==s,"Down");r.ReplaceAndStop(new MacroLibrary());Check(!runner.IsRunning,"Delete stop");Check(r.Handle(new InputStroke{Trigger=TriggerKind.Middle,Down=false})==s,"Removed binding lost paired Up");Check(!r.Handle(new InputStroke{Trigger=TriggerKind.Middle,Down=true}),"Residual suppression");});}
        Test("Emergency-all-router-states","Binding",()=>{for(int flags=0;flags<8;flags++){var l=Lib(new MacroStep());l.BindingsEnabled=(flags&1)==0;var n=new Runner();var r=new BindingRouter(n);r.Configure(l);r.Suspended=(flags&2)!=0;n.Start(l,"main","test",true,0);Check(r.Handle(new InputStroke{Trigger=TriggerKind.Keyboard,Key=123,Down=true,Modifiers=KeyModifiers.Control|KeyModifiers.Shift,BypassBindings=(flags&4)!=0})&&!n.IsRunning,"Emergency state="+flags);}});
        Test("Injected-all-trigger-kinds","Binding",()=>{foreach(TriggerKind k in Enum.GetValues(typeof(TriggerKind))){var l=Lib(new MacroStep());l.Bindings.Add(new MacroBinding{MacroId="main",Trigger=k,KeyCode=65});var n=new Runner();var r=new BindingRouter(n);r.Configure(l);Check(!r.Handle(new InputStroke{Trigger=k,Key=65,Down=true,Injected=true})&&n.Starts==0,"Injected recursive binding="+k);}});
    }
    static void RecordingTests()
    {
        foreach(int n in new[]{498,499,500,501}){int count=n;Test("Recording-capacity-"+n,"Recorder",()=>{var b=new RecordingBuffer(RecordingDelay.None,0,500);for(int i=0;i<count;i++)b.Accept(new InputStroke{Trigger=TriggerKind.WheelUp,Down=true});b.Accept(new InputStroke{Trigger=TriggerKind.Keyboard,Key=65,Down=true});b.Balance();Check(b.Steps.Count<=500,"Capacity exceeded");var downs=b.Steps.Count(s=>s.Kind==ActionKind.Keyboard&&s.Press==PressMode.Down);var ups=b.Steps.Count(s=>s.Kind==ActionKind.Keyboard&&s.Press==PressMode.Up);Check(downs==ups,"Capacity left unpaired key");});}
        Test("Recording-pause-resume-all-inputs","Recorder",()=>{foreach(TriggerKind trigger in Enum.GetValues(typeof(TriggerKind))){var b=new RecordingBuffer(RecordingDelay.Actual,0,500);b.Accept(new InputStroke{Trigger=trigger,Key=65,Down=true,Timestamp=100});b.Accept(new InputStroke{BreakRecordingTiming=true});b.Accept(new InputStroke{Trigger=trigger,Key=65,Down=false,SkipRecording=true});b.Accept(new InputStroke{Trigger=TriggerKind.Keyboard,Key=66,Down=true,Timestamp=Stopwatch.Frequency*10});b.Balance();Check(b.Steps.All(s=>s.Kind!=ActionKind.Delay),"Pause time recorded");var held=new HashSet<string>();foreach(var s in b.Steps){if(s.Press==PressMode.Down)held.Add(s.Kind+":"+s.KeyCode+":"+s.Mouse);if(s.Press==PressMode.Up)held.Remove(s.Kind+":"+s.KeyCode+":"+s.Mouse);}Check(held.Count==0,"Pause left held input");}});
    }
    static void PersistenceTests()
    {
        Test("Macro-XML-special-character-roundtrip","MacroStore",()=>{string p=Path.Combine(Dir(),"macros.xml");var s=new MacroStore(p);var l=Lib(new MacroStep{Kind=ActionKind.Text,Value="中文😀 <>&\"'\n"});l.Macros[0].Name="中文😀&<>";s.Save(l);Check(s.Load().Macros[0].Steps[0].Value==l.Macros[0].Steps[0].Value,"Unicode loss");s.Save(l);Check(File.Exists(p+".bak"),"Missing backup");});
        Test("Macro-XML-malformed-DTD-XXE","MacroStore",()=>{string p=Path.Combine(Dir(),"macros.xml");foreach(string text in new[]{"","<broken>","<!DOCTYPE x [<!ENTITY external SYSTEM 'file:///not-read'>]><x>&external;</x>","<LeiyunLiteMacros>"}){File.WriteAllText(p,text);Reject(()=>new MacroStore(p).Load());Check(File.ReadAllText(p)==text,"Invalid XML overwritten");}});
        Test("Macro-atomic-locked-file-preserves-original","MacroStore",()=>{string p=Path.Combine(Dir(),"macros.xml");var store=new MacroStore(p);var l=Lib(new MacroStep());store.Save(l);string original=File.ReadAllText(p);l.Macros[0].Name="changed";using(var held=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.None))Reject(()=>store.Save(l));Check(File.ReadAllText(p)==original,"Locked save corrupted original");Check(Directory.GetFiles(Path.GetDirectoryName(p),"*.tmp").Length==0,"Temporary file retained");});
        Test("Desktop-settings-invalid-fields-normalized","Settings",()=>{var p=Dir();File.WriteAllText(Path.Combine(p,"desktop.xml"),"<DesktopPreferences><Language>bad</Language><Theme>bad</Theme><LowBatteryThreshold>99</LowBatteryThreshold><AutoCheckUpdates>false</AutoCheckUpdates><AutoDownloadUpdates>true</AutoDownloadUpdates><AutoInstallUpdates>true</AutoInstallUpdates><Unknown>field</Unknown></DesktopPreferences>");var v=new DesktopSettings(p).Load();Check(v.Language=="system"&&v.Theme=="dark"&&v.LowBatteryThreshold==20&&!v.AutoDownloadUpdates&&!v.AutoInstallUpdates,"Normalization");});
        Test("Desktop-settings-corrupt-load-safe-default","Settings",()=>{var p=Dir();File.WriteAllText(Path.Combine(p,"desktop.xml"),"<DesktopPreferences><Theme>");var v=new DesktopSettings(p).Load();Check(v!=null,"No safe preference result");});
        Test("Registry-isolated-settings-autostart-types","Settings",()=>{string root=@"Software\LeiyunLite.FullVerification\"+Guid.NewGuid().ToString("N");try{using(var k=Registry.CurrentUser.CreateSubKey(root+"\\Run")){}var a=new AutoStartService(@"C:\Fake Space\App.exe",root+"\\Run");a.SetEnabled(true);Check(a.IsCurrentExecutableEnabled(),"Enable");var b=new AutoStartService(@"C:\Moved Space\App.exe",root+"\\Run");b.Sync();Check(b.IsCurrentExecutableEnabled(),"Migration");b.SetEnabled(false);Check(!b.IsEnabled(),"Disable");using(var k=Registry.CurrentUser.CreateSubKey(root+"\\Settings")){k.SetValue("RefreshInterval","bad",RegistryValueKind.String);}var s=new SettingsStore(root+"\\Settings").Load();Check(s.RefreshInterval==60000,"Invalid field destroyed defaults");}finally{Registry.CurrentUser.DeleteSubKeyTree(root,false);}});
        Test("Cache-invalid-numeric-fields-stay-unknown","Cache",()=>{string root=@"Software\LeiyunLite.FullVerification\"+Guid.NewGuid().ToString("N");try{var h=new Hid();var store=new HardwareCacheStore(root);store.SelectDevice(h.Descriptor.InstanceKey);string active=(string)Field(store,"CacheRegistryKey");using(var k=Registry.CurrentUser.CreateSubKey(active)){k.SetValue("CatalogRevision",RazerIdentityCatalog.Revision);k.SetValue("BatteryKnown",1);k.SetValue("BatteryPercent",999);k.SetValue("Dpi",-1);k.SetValue("PollingRate",-1);k.SetValue("LastUpdated",DateTime.Now.ToString("o"));}h.Sleeping=true;var r=new RazerDeviceClient(new Transport(h),new HardwareCacheStore(root)).QueryRazerDeviceInfo();Check(!r.BatteryKnown&&r.Dpi==0&&r.PollingRate==0,"Corrupt cache shown: known="+r.BatteryKnown+", battery="+r.BatteryPercent+", DPI="+r.Dpi+", Hz="+r.PollingRate);}finally{Registry.CurrentUser.DeleteSubKeyTree(root,false);}});
    }
    static void UpdateTests()
    {
        Test("Updater-transfer-size-hash-interruption-matrix","Updates",()=>{byte[] data=Encoding.UTF8.GetBytes("test data");string hash;using(var s=new MemoryStream(data))hash=UpdatePackage.Hash(s);foreach(long length in new long[]{0,1,data.Length-1,data.Length+1,UpdatePackage.MaxBytes+1,long.MaxValue}){string p=Path.Combine(Dir(),"download.part");Reject(()=>{using(var s=new MemoryStream(data))ReleaseUpdateService.CopyVerified(s,p,length,hash,null,CancellationToken.None).GetAwaiter().GetResult();});}foreach(string digest in new[]{"","bad",new string('0',64)})Reject(()=>{using(var s=new MemoryStream(data))ReleaseUpdateService.CopyVerified(s,Path.Combine(Dir(),"bad.part"),data.Length,digest,null,CancellationToken.None).GetAwaiter().GetResult();});using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool rejected=false;try{using(var s=new MemoryStream(data))ReleaseUpdateService.CopyVerified(s,Path.Combine(Dir(),"cancel.part"),data.Length,hash,null,cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){rejected=true;}Check(rejected,"Cancellation ignored");}});
        Test("Updater-safety-all-64-state-combinations","Updates",()=>{for(int i=0;i<64;i++)Check((UpdateSafety.Block((i&1)!=0,(i&2)!=0,(i&4)!=0,(i&8)!=0,(i&16)!=0,(i&32)!=0)==null)==(i==0),"Unsafe handoff="+i);});
        Test("Updater-invalid-envelope-Base64-signature","Updates",()=>{foreach(string json in new[]{"", "{}", "null", "{", "{\"Payload\":\"!\",\"Signature\":\"!\"}","{\"Payload\":\"e30=\",\"Signature\":\"AA==\"}"})Reject(()=>UpdatePackage.Verify(json,UpdateTrust.PublicKey));});
        Test("Updater-version-boundary","Updates",()=>{foreach(string s in new[]{"1.2.3","v1.2.3","1.2.3-r1","0.0.0"})Check(ReleaseVersion.Parse(s)!=null,"Valid version="+s);foreach(string s in new[]{null,"","01.2.3","-1.2.3","1.2","1.2.3.4","1.2.3-beta","1.2.3-r0","99999999999.2.3","1.2.3\n"})Check(ReleaseVersion.Parse(s)==null,"Malformed version accepted="+s);});
    }
    static string RandomString(int max){const string chars="<>{}[]\"'&;/:\\\n\t0123456789abcXYZ中文😀";int len=Random.Next(max+1);return new string(Enumerable.Range(0,len).Select(i=>chars[Random.Next(chars.Length)]).ToArray());}
    static void FuzzTests()
    {
        Test("FUZZ-DpiScale-10000","Fuzz",()=>{int prior=100;for(int i=0;i<10000;i++){double p=i/9999.0;int v=DpiScale.FromPosition(p);Check(v>=prior&&v>=100&&v<=8000,"Position reversed");prior=v;int d=(int)(Random.NextDouble()*4294967295.0-2147483648.0);double pos=DpiScale.ToPosition(d);Check(!double.IsNaN(pos)&&!double.IsInfinity(pos)&&pos>=0&&pos<=1,"Invalid random position");}});
        Test("FUZZ-RotationMath-10000","Fuzz",()=>{for(int i=0;i<10000;i++){double a=Random.NextDouble()*88-44;var v=new System.Windows.Vector(Random.Next(-10000,10000),Random.Next(-10000,10000));var r=RotationMath.Rotate(v,a);Check(!double.IsNaN(r.X)&&Math.Abs(r.Length-v.Length)<0.00001,"Rotation invariant");var points=Enumerable.Range(0,Random.Next(0,30)).Select(j=>new System.Windows.Point(Random.Next(500),Random.Next(500))).ToList();double angle;bool ok=RotationMath.TryStroke(points,out angle);Check(!ok||!double.IsNaN(angle)&&Math.Abs(angle)<=44,"Invalid stroke accepted");}});
        Test("FUZZ-Protocol-10000","Fuzz",()=>{for(int i=0;i<10000;i++){var h=new Hid();int index=Random.Next(2,90);byte flip=(byte)(1<<Random.Next(8));h.Filter=(q,r)=>{r[index]^=flip;return r;};var read=Client(h).QueryRazerDeviceInfo();Check(read.ProtocolStatus==DeviceProtocolStatus.PresentUnresponsive,"CRC mutation accepted byte="+index);}});
        Test("FUZZ-MacroValidation-10000","Fuzz",()=>{for(int i=0;i<10000;i++){var l=Lib();for(int j=0;j<Random.Next(1,25);j++)l.Macros[0].Steps.Add(new MacroStep{Kind=(ActionKind)Random.Next(-1,11),Press=(PressMode)Random.Next(-1,5),Mouse=(MouseAction)Random.Next(-1,10),KeyCode=Random.Next(-10,270),Number=Random.Next(-1,600002),Value=Random.Next(3)==0?null:RandomString(20)});try{MacroValidation.Validate(l);}catch(InvalidOperationException){} }});
        Test("FUZZ-MacroXML-10000","Fuzz",()=>{string p=Path.Combine(Dir(),"fuzz.xml");for(int i=0;i<10000;i++){File.WriteAllBytes(p,Encoding.UTF8.GetBytes(RandomString(200)));try{new MacroStore(p).Load();}catch(InvalidOperationException){}catch(System.Xml.XmlException){}catch(InvalidDataException){} }});
        Test("FUZZ-UpdateManifest-10000","Fuzz",()=>{for(int i=0;i<10000;i++){string text=RandomString(150);try{UpdatePackage.Verify(text,UpdateTrust.PublicKey);}catch(ArgumentException){}catch(InvalidOperationException){}catch(InvalidDataException){}catch(FormatException){}catch(CryptographicException){} }});
        Test("FUZZ-Version-10000","Fuzz",()=>{for(int i=0;i<10000;i++){string s=RandomString(60);var v=ReleaseVersion.Parse(s);if(v!=null)Check(v.CompareTo(v)==0,"Version self-compare");bool stable=UpdatePackage.IsVersion(s);if(stable)Check(Version.Parse(s)!=null,"Stable parse mismatch");}});
        Test("FUZZ-DeviceSelection-10000","Fuzz",()=>{var a=new Hid(91,0x00DF,"unit-a");var b=new Hid(91,0x00DF,"unit-b");var sibling=new Hid(90,0x00DF,"unit-a");sibling.Descriptor.Path="fake-sibling";var wrong=new Hid(91,0xFFFF,"unknown");var t=new Transport(a,b,sibling,wrong);var c=new RazerDeviceClient(t,new HardwareCacheStore(null));string key=c.QueryRazerDeviceInfo().DeviceKey;for(int i=0;i<10000;i++){t.Devices.Sort((x,y)=>string.CompareOrdinal(((IHidDescriptor)x).Descriptor.Path,((IHidDescriptor)y).Descriptor.Path));if(Random.Next(2)==0)t.Devices.Reverse();var r=c.QueryRazerDeviceInfo();Check(r.DeviceKey==key&&r.ProtocolStatus==DeviceProtocolStatus.Ready,"Order changed selected unit");}});
    }
    sealed class MonitorFake : IRazerDeviceClient
    {
        internal int Queries; public int CachedBatteryPercent{get{return 0;}}
        public MouseBatteryInfo QueryRazerDeviceInfo(){Interlocked.Increment(ref Queries);return new MouseBatteryInfo();}
        public bool FastQueryDpi(out int dpi,out int stage,out int count){dpi=800;stage=count=1;return true;}
        public bool SetRazerDpi(int d){throw new Exception("Real setting forbidden");}public bool SetRazerDpiStage(int s){throw new Exception("Forbidden");}public bool SetRazerPollingRate(int r){throw new Exception("Forbidden");}
    }
    static void ConcurrencyTests()
    {
        Test("DpiMonitor-1000-start-refresh-dispose","Concurrency",()=>{for(int i=0;i<1000;i++){var f=new MonitorFake();var m=new DpiMonitor(f,r=>{});m.Start();f.QueryRazerDeviceInfo();m.Dispose();Reject(()=>m.Start());}Thread.Sleep(500);});
        Test("HID-10000-concurrent-operations","Concurrency",()=>{var h=new Hid();var c=Client(h);string key=c.QueryRazerDeviceInfo().DeviceKey;int operations=0;var tasks=Enumerable.Range(0,8).Select(worker=>Task.Run(()=>{for(int i=0;i<1250;i++){switch(worker){case 0:c.QueryRazerDeviceInfo();break;case 1:int d,s,n;c.FastQueryDpi(out d,out s,out n);break;case 2:c.SetDpiVerified(h.ProductId,1200,key);break;case 3:c.SetRateVerified(h.ProductId,500,key);break;case 4:int a;c.TryGetRotationVerified(h.ProductId,key,out a);break;case 5:c.SetRotationVerified(h.ProductId,10,key);break;case 6:c.InvalidateTarget();break;case 7:Check(!c.SetDpiVerified(h.ProductId,800,"wrong-instance"),"Stale instance write");break;}Interlocked.Increment(ref operations);}})).ToArray();Check(Task.WaitAll(tasks,60000)&&operations==10000,"Concurrency deadlock/incomplete");});
    }
    static void SchedulerTests()
    {
        foreach(int ms in new[]{1,2,5,10,50,100}){int duration=ms;Test("Scheduler-"+ms+"ms-500","Timing",()=>{var timings=new List<double>();using(var scheduler=new PrecisionScheduler(CancellationToken.None)){for(int i=0;i<500;i++){long start=Stopwatch.GetTimestamp();scheduler.Wait(duration);timings.Add((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency);}}var sorted=timings.OrderBy(x=>x).ToArray();Console.WriteLine("TIMING ms="+duration+" min="+sorted[0]+" P50="+sorted[249]+" P95="+sorted[474]+" P99="+sorted[494]+" max="+sorted[499]+" mean="+sorted.Average());File.WriteAllLines(Path.Combine(Root,"scheduler-"+duration+"ms.csv"),timings.Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));Check(sorted[0]>=duration*0.8,"Burst/short wait");});}
        foreach(int stall in new[]{20,50,100}){int delay=stall;Test("Scheduler-stall-"+stall,"Timing",()=>{using(var s=new PrecisionScheduler(CancellationToken.None)){s.Wait(1);Thread.Sleep(delay);var sw=Stopwatch.StartNew();for(int i=0;i<20;i++)s.Wait(1);Check(sw.Elapsed.TotalMilliseconds>=16,"Stall replay burst");}});}
    }
}
