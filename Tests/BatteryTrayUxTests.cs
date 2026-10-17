using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RazerBatteryTray.Desktop;
using Drawing = System.Drawing;

namespace RazerBatteryTray.Tests
{
    internal static class BatteryTrayUxTestRunner
    {
        [STAThread] private static int Main(string[] args) { return OpenRazerCompatibilityTests.RunBatteryTrayUxTests(args[0]); }
    }
    internal static partial class OpenRazerCompatibilityTests
    {
        private static string trayArtifacts;
        internal static int RunBatteryTrayUxTests(string directory)
        {
            trayArtifacts = directory; Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory,"screenshots"));
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; DesktopApp.LoadTheme(app);
            Test("DPI immediate target, stale monitor/refresh, duplicate events, silent readback and resumed telemetry", () => PendingVisual(true));
            Test("Polling immediate selection, stable buttons, stale refresh, duplicate click and silent readback", () => PendingVisual(false));
            Test("DPI confirmed rollback restores visual original and preserves error", () => RollbackVisual(true));
            Test("Polling unconfirmed rollback disables selection until a fresh query", () => RollbackVisual(false));
            Test("Key, PID, path and disconnection cancel old display and delayed completion", ChangedVisual);
            Test("Disposed/rebuilt page ignores outstanding result and notices", DisposedVisual);
            Test("Pre-write refresh completion and queued monitor generations cannot retire or overwrite confirmed target", RefreshBarrierAndEpoch);
            Test("Same/invalid target and Demo retain idempotent transaction semantics", SameAndDemo);
            Test("Same displayed value still reads live hardware and corrects a different physical setting", SameDisplayChangedHardware);
            Test("Fresh charging, threshold, unknown, invalid, sleep/cache/disconnect/resume state selection", TrayStates);
            Test("Native mouse silhouettes, charging plug and every 0..100 fill remain transparent and bounded", TrayPixels);
            Test("Mouse fill height is linear, 25/10 colors exact and unknown never retains color", TrayProgress);
            Test("Every size 16..256 keeps RGBA bounds and rejects invalid render inputs", TrayFallback);
            Test("Tooltip text matches validated battery state in Chinese/English and preserves long-name limits", TrayTooltipText);
            Test("Native NotifyIcon tooltip and icon update together on model/charge/sleep/disconnect changes", TrayTooltipUpdates);
            Test("Native HICON alpha composites once and matches renderer on a known background", TrayNativeAlpha);
            Test("500 NotifyIcon replacements, repeated unchanged update and disposal keep GDI/USER bounded", TrayResources);
            Console.WriteLine("TRAY UX RESULT: " + passed + " passed, " + failed + " failed");
            app.Shutdown(); return failed == 0 ? 0 : 1;
        }
        private sealed class PausedDevice : IHidDevice, IHidDescriptor, IDisposable
        {
            internal readonly ProfileFake Fake = new ProfileFake(0xa6,91);
            internal readonly ManualResetEvent Entered = new ManualResetEvent(false), Release = new ManualResetEvent(false);
            internal bool Armed;
            internal PausedDevice()
            {
                Fake.Dpi=Fake.DpiY=800;
                Fake.StagePayload[11]=Fake.StagePayload[13]=3; Fake.StagePayload[12]=Fake.StagePayload[14]=32;
            }
            public HidDescriptor Descriptor { get { return Fake.Descriptor; } }
            public int ProductId { get { return Fake.ProductId; } }
            public string ProductName { get { return Fake.ProductName; } }
            public int ReportLength { get { return Fake.ReportLength; } }
            public byte[] Exchange(byte[] q,int delay)
            {
                if (Armed && q[8] < 0x80) { Armed = false; Entered.Set(); if (!Release.WaitOne(10000)) throw new Exception("Fake release timeout"); }
                return Fake.Exchange(q,delay);
            }
            public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
        }
        private static void WaitFor(WaitHandle handle)
        { var timer=Stopwatch.StartNew(); while(!handle.WaitOne(0)) { Check(timer.ElapsedMilliseconds<5000,"Fake send did not enter"); Pump(); Thread.Sleep(1); } }
        private static Button RateButton(DevicePage p,int rate)
        { return Field<WrapPanel>(p,"ratePanel").Children.Cast<Button>().Single(b=>(string)b.Content==rate+" Hz"); }
        private static void Display(DevicePage p,bool dpi,int expected)
        {
            if(dpi) Check(Field<TextBox>(p,"dpiValue").Text==(expected>0?expected.ToString():"—"),"DPI display differs from "+expected);
            else Check(Field<WrapPanel>(p,"ratePanel").Children.Cast<Button>().Where(b=>b.Tag is bool && (bool)b.Tag).Select(b=>(string)b.Content).SequenceEqual(expected>0?new[]{expected+" Hz"}:new string[0]),"Polling visual selection differs from "+expected);
        }
        private static void CapturePerformance(DevicePage page,string label)
        {
            page.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1,(int)page.ActualWidth),Math.Max(1,(int)page.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(page); var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var stream=File.Create(Path.Combine(trayArtifacts,"screenshots",label+".png")))encoder.Save(stream);
        }
        private static void PendingVisual(bool isDpi)
        {
            using(var gate=new PausedDevice())
            {
                var w=DirectFixture(new RazerDeviceClient(new FakeTransport(gate),new HardwareCacheStore(null))); var p=Field<DevicePage>(w,"devicePage");
                try
                {
                    var buttons=Field<WrapPanel>(p,"ratePanel").Children.Cast<Button>().ToArray(); gate.Fake.Requests.Clear(); gate.Armed=true;
                    int original=w.Reading.Dpi, target=isDpi?1600:1000;
                    if(isDpi) PrepareDpi(p,w.Reading,target);
                    Task task=isDpi?Commit(p,"CommitDpi"):Commit(p,"CommitRate",target);
                    Display(p,isDpi,target); Check(w.Reading.Dpi==original && w.Reading.PollingRate==500,"No optimistic hardware reading mutation"); WaitFor(gate.Entered);
                    CapturePerformance(p,isDpi?"dpi-pending-1600":"polling-pending-1000");
                    File.AppendAllText(Path.Combine(trayArtifacts,"ui-transitions.tsv"),(isDpi?"DPI":"Polling")+"\tpending\t"+target+"\t"+(isDpi?w.Reading.Dpi:w.Reading.PollingRate)+Environment.NewLine);
                    for(int i=0;i<3;i++)
                    {
                        w.Reading.Dpi=800; w.Reading.PollingRate=500; p.UpdateReading(); p.UpdateDpi();
                        Display(p,isDpi,target); Check(buttons.SequenceEqual(Field<WrapPanel>(p,"ratePanel").Children.Cast<Button>()),"Polling buttons recreated during write");
                        File.AppendAllText(Path.Combine(trayArtifacts,"ui-transitions.tsv"),(isDpi?"DPI":"Polling")+"\tstale-"+i+"\t"+target+"\t"+(isDpi?w.Reading.Dpi:w.Reading.PollingRate)+Environment.NewLine);
                    }
                    if(isDpi) PrepareDpi(p,w.Reading,target);
                    Check((isDpi?Commit(p,"CommitDpi"):Commit(p,"CommitRate",target)).IsCompleted,"Duplicate event entered write");
                    gate.Release.Set(); AwaitDirect(task,w); Display(p,isDpi,target);
                    Check(gate.Fake.Writes==1 && Toast(w)=="","One SET and silent success");
                    CapturePerformance(p,isDpi?"dpi-confirmed-1600":"polling-confirmed-1000");
                    File.AppendAllText(Path.Combine(trayArtifacts,"ui-transitions.tsv"),(isDpi?"DPI":"Polling")+"\tconfirmed\t"+target+"\t"+(isDpi?w.Reading.Dpi:w.Reading.PollingRate)+Environment.NewLine);
                    byte[] commands=gate.Fake.Requests.Select(q=>q[8]).Take(3).ToArray();
                    Check(commands.SequenceEqual(isDpi?new byte[]{0x86,6,0x86}:new byte[]{0xc0,0x40,0xc0}),"GET / SET / readback changed");
                    SetField(p,"pendingDpi",false);
                    typeof(ShellWindow).GetMethod("OnDpi",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,new object[]{new DpiReading {DeviceKey=w.Reading.DeviceKey,Dpi=2400,Stage=1,Count=3}});
                    if(isDpi) Display(p,true,2400);
                    Check(Field<PerformanceVisualState>(p,"visual")==null,"Visual override did not terminate after fresh query");
                }
                finally { gate.Release.Set(); CloseDirectFixture(w); }
            }
        }
        private static void RollbackVisual(bool isDpi)
        {
            using(var gate=new PausedDevice())
            {
                var w=DirectFixture(new RazerDeviceClient(new FakeTransport(gate),new HardwareCacheStore(null))); var p=Field<DevicePage>(w,"devicePage");
                try
                {
                    int original=isDpi?w.Reading.Dpi:w.Reading.PollingRate; gate.Armed=true; gate.Fake.WriteFault=isDpi?3:5;
                    if(isDpi) PrepareDpi(p,w.Reading,1600);
                    var task=isDpi?Commit(p,"CommitDpi"):Commit(p,"CommitRate",1000); WaitFor(gate.Entered);
                    if(!isDpi) gate.Fake.Sleeping=true;
                    gate.Release.Set(); AwaitDirect(task,w);
                    if(isDpi) { Display(p,true,original); Check(Toast(w).Contains("已恢复并读回原值") && gate.Fake.Writes==2,"Confirmed restore not shown"); }
                    else
                    {
                        Display(p,false,0); Check(Toast(w).Contains("未确认") || Toast(w).Contains("未获确认"),"Unconfirmed failure needs explicit notice");
                        Check(Field<WrapPanel>(p,"ratePanel").Children.Cast<Button>().All(b=>!b.IsEnabled),"Unconfirmed write must be disabled");
                        gate.Fake.Sleeping=false; AwaitDirect(w.RefreshDevice(),w); p.UpdateReading();
                        Display(p,false,w.Reading.PollingRate); Check(Field<PerformanceVisualState>(p,"visual")==null,"Fresh reading did not recover UI");
                    }
                }
                finally { gate.Release.Set(); CloseDirectFixture(w); }
            }
        }
        private static void ChangedVisual()
        {
            foreach(string mode in new[]{"key","pid","path","disconnected"}) using(var gate=new PausedDevice())
            {
                var w=DirectFixture(new RazerDeviceClient(new FakeTransport(gate),new HardwareCacheStore(null))); var p=Field<DevicePage>(w,"devicePage");
                try
                {
                    gate.Armed=true; PrepareDpi(p,w.Reading,1600); var task=Commit(p,"CommitDpi"); WaitFor(gate.Entered);
                    if(mode=="key")w.Reading.DeviceKey="new-device";
                    if(mode=="pid")w.Reading.ProductId=0xdf;
                    if(mode=="path")w.Reading.InterfacePath="new-interface";
                    if(mode=="disconnected")w.Reading.IsConnected=false;
                    w.Reading.Dpi=2400; p.UpdateReading(); Check(Field<PerformanceVisualState>(p,"visual")==null,"Stale device retained override");
                    gate.Release.Set(); AwaitDirect(task,w); Display(p,true,2400); Check(Toast(w)=="","Delayed result showed old-device notice");
                }
                finally { gate.Release.Set(); CloseDirectFixture(w); }
            }
        }
        private static void DisposedVisual()
        {
            using(var gate=new PausedDevice())
            {
                var w=DirectFixture(new RazerDeviceClient(new FakeTransport(gate),new HardwareCacheStore(null))); var p=Field<DevicePage>(w,"devicePage");
                try { gate.Armed=true; PrepareDpi(p,w.Reading,1600); var task=Commit(p,"CommitDpi"); WaitFor(gate.Entered); SetField(w,"Demo",true); w.RebuildPages(0); gate.Release.Set(); AwaitDirect(task,w); Check(Toast(w)=="" && Field<bool>(p,"disposed"),"Rebuilt page accepted old result"); }
                finally { gate.Release.Set(); CloseDirectFixture(w); }
            }
        }
        private static void SameAndDemo()
        {
            var f=new ProfileFake(0xa6,91); var w=DirectFixture(Client(f)); var p=Field<DevicePage>(w,"devicePage");
            try { PrepareDpi(p,w.Reading,w.Reading.Dpi); AwaitDirect(Commit(p,"CommitDpi"),w); AwaitDirect(Commit(p,"CommitRate",500),w); AwaitDirect(Commit(p,"CommitRate",123),w); Check(f.Writes==0,"Same or invalid target SET"); SetField(w,"Demo",true); PrepareDpi(p,w.Reading,1600); AwaitDirect(Commit(p,"CommitDpi"),w); Check(f.Writes==0 && Toast(w).Contains("预览模式"),"Demo writes or falsely confirms"); }
            finally { CloseDirectFixture(w); }
        }
        private static void RefreshBarrierAndEpoch()
        {
            var w=Preview(); var p=Field<DevicePage>(w,"devicePage");
            try
            {
                w.Reading=Client(new ProfileFake(0xa6,91)).QueryRazerDeviceInfo(); w.Reading.Dpi=800;
                var state=new PerformanceVisualState(true,1600,w.Reading,1);
                state.Complete(new DpiWriteResult {Success=true,After=1600},4); SetField(p,"visual",state);
                w.DeviceRefreshApplied=3; p.UpdateReading(); Display(p,true,1600);
                w.DeviceRefreshApplied=4; p.UpdateReading(); Display(p,true,1600);
                w.DeviceRefreshApplied=5; w.Reading.Dpi=1600; p.UpdateReading(); Display(p,true,1600);
                Check(Field<PerformanceVisualState>(p,"visual")==null,"Fresh post-readback query must retire display");
                typeof(ShellWindow).GetMethod("QueueDpiReading",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,new object[]{new DpiReading {DeviceKey=w.Reading.DeviceKey,Dpi=800}});
                w.AdvancePerformanceEpoch(); Pump(); Check(w.Reading.Dpi==1600,"Old queued monitor overwrote post-write reading");
                typeof(ShellWindow).GetMethod("QueueDpiReading",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,new object[]{new DpiReading {DeviceKey=w.Reading.DeviceKey,Dpi=2400}});
                Pump(); Display(p,true,2400); Check(w.Reading.Dpi==2400,"New telemetry must resume normally");
            }
            finally {w.ClosePreview();}
        }
        private static void SameDisplayChangedHardware()
        {
            foreach(bool isDpi in new[]{true,false})
            {
                var f=new ProfileFake(0xa6,91); var w=DirectFixture(Client(f)); var p=Field<DevicePage>(w,"devicePage");
                try
                {
                    int target=isDpi?w.Reading.Dpi:w.Reading.PollingRate;
                    if(isDpi) { f.StagePayload[11]=f.StagePayload[13]=5;f.StagePayload[12]=f.StagePayload[14]=120;PrepareDpi(p,w.Reading,target); }
                    else f.Rate=1000;
                    f.Requests.Clear();AwaitDirect(isDpi?Commit(p,"CommitDpi"):Commit(p,"CommitRate",target),w);
                    Check(f.Writes==1 && f.Requests[0][8]==(isDpi?0x86:0xc0),"Same UI value must not skip live GET and required SET"); Display(p,isDpi,target);
                }
                finally {CloseDirectFixture(w);}
            }
        }
        private static MouseBatteryInfo Battery(int percent,bool charging=false)
        { return new MouseBatteryInfo {IsConnected=true,ProtocolStatus=DeviceProtocolStatus.Ready,BatteryKnown=percent>=0,BatteryPercent=percent,ChargingKnown=true,IsCharging=charging,LastUpdated=DateTime.Now}; }
        private static BatteryTrayVisual State(MouseBatteryInfo reading,int threshold=20,int size=20)
        { return BatteryTrayIconRenderer.State(reading,threshold,size,DateTime.Now,TimeSpan.FromSeconds(125)); }
        private static void TrayStates()
        {
            foreach(int value in new[]{0,1,14,15,16,19,20,21,80,99,100}) { var v=State(Battery(value),15); Check(v.Percent==value && v.Low==(value<=15),"Custom threshold or valid boundary"); }
            Check(State(Battery(80,true)).Charging && !State(Battery(15,true)).Low,"Charging priority");
            Check(State(Battery(-1,true)).Percent==-1 && State(Battery(-1,true)).Charging,"Known charge/unknown percent");
            var r=Battery(80,true); r.ChargingKnown=false; Check(!State(r).Charging,"USB/unconfirmed charging badge");
            foreach(string mode in new[]{"sleep","cache","disconnected","stale","future","unknown","negative","overflow","unresponsive"})
            {
                r=Battery(80,true);
                if(mode=="sleep")r.IsSleeping=true; if(mode=="cache")r.ProtocolStatus=DeviceProtocolStatus.Cached;
                if(mode=="disconnected")r.IsConnected=false; if(mode=="stale")r.LastUpdated=DateTime.Now.AddMinutes(-10);
                if(mode=="future")r.LastUpdated=DateTime.Now.AddMinutes(1); if(mode=="unknown")r.BatteryKnown=false;
                if(mode=="negative")r.BatteryPercent=-20; if(mode=="overflow")r.BatteryPercent=200;
                if(mode=="unresponsive")r.ProtocolStatus=DeviceProtocolStatus.PresentUnresponsive;
                Check(State(r).Percent==-1,"Invalid/stale data displayed: "+mode);
                if(mode!="unknown" && mode!="negative" && mode!="overflow") Check(!State(r).Charging,"Stale charging displayed: "+mode);
            }
            r.IsSleeping=false; r=Battery(67); Check(State(r).Percent==67,"Resume did not regain fresh data");
        }
        private static int ColorPixels(Drawing.Bitmap bitmap, Drawing.Color color)
        {
            int count=0; for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
                if(bitmap.GetPixel(x,y).ToArgb()==color.ToArgb())count++;
            return count;
        }
        private static string PixelHash(Drawing.Bitmap bitmap)
        {
            var pixels=new byte[bitmap.Width*bitmap.Height*4];int i=0;
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){
                var c=bitmap.GetPixel(x,y);pixels[i++]=c.A;pixels[i++]=c.R;pixels[i++]=c.G;pixels[i++]=c.B;
            }
            using(var hash=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(pixels));
        }
        private static void TransparentCorners(Drawing.Bitmap b)
        {
            Check(b.Width==b.Height&&b.PixelFormat==Drawing.Imaging.PixelFormat.Format32bppArgb,"Wrong native RGBA format");
            Check(b.GetPixel(0,0).A==0&&b.GetPixel(b.Width-1,0).A==0&&b.GetPixel(0,b.Height-1).A==0&&b.GetPixel(b.Width-1,b.Height-1).A==0,"Opaque card or clipped corner");
        }
        private static double ColorDistance(Drawing.Color a,Drawing.Color b)
        { return Math.Sqrt((a.R-b.R)*(a.R-b.R)+(a.G-b.G)*(a.G-b.G)+(a.B-b.B)*(a.B-b.B)); }
        private static double FillCoverage(Drawing.Color c,Drawing.Color empty,Drawing.Color full)
        {
            double r=full.R-empty.R,g=full.G-empty.G,b=full.B-empty.B,n=r*r+g*g+b*b;
            if(n<4)return 0;
            return Math.Max(0,Math.Min(1,((c.R-empty.R)*r+(c.G-empty.G)*g+(c.B-empty.B)*b)/n));
        }
        private static void TrayPixels()
        {
            foreach(int size in new[]{16,20,24,32})
            {
                using(var solid=BatteryTrayIconRenderer.Render(State(Battery(100),20,size)))
                {
                    double asymmetry=0,total=0;int partial=0,opaque=0,clear=0;
                    for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
                        var c=solid.GetPixel(x,y);var mirror=solid.GetPixel(size-1-x,y);
                        asymmetry+=Math.Abs(c.A-mirror.A);total+=c.A;
                        if(c.A>0&&c.A<255){partial++;if(c.A<200)Check(c.R>100&&c.G>100&&c.B>100,"Dark fringe at transparent outer boundary");}
                        if(c.A==255)opaque++;if(c.A==0)clear++;
                    }
                    Check(partial>=size&&opaque>size&&clear>size,"AA edge, opaque body and transparent background required");
                    Check(asymmetry/total<.04,"Mouse alpha silhouette is asymmetric");
                    Func<int,double> width=y=>Enumerable.Range(0,size).Sum(x=>solid.GetPixel(x,y).A/255.0);
                    Check(width(1)<width(size/2)&&width(size-2)<width(size/2),"Rounded narrowed head/base required");
                }
                for(int percent=-1;percent<=100;percent++)foreach(bool charge in new[]{false,true})
                using(var b=BatteryTrayIconRenderer.Render(State(Battery(percent,charge),37,size)))
                {
                    TransparentCorners(b);Check(b.Width==size,"Wrong native RGBA size");
                    if(charge) {
                        int cx,cy,d;BatteryTrayIconRenderer.PlugLayout(size,out cx,out cy,out d);
                        double white=0,green=0,topWhite=0,bottomWhite=0,coverage=0,body=0;
                        using(var normal=BatteryTrayIconRenderer.Render(State(Battery(percent),37,size)))
                        for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
                            var c=b.GetPixel(x,y);var n=normal.GetPixel(x,y);body+=n.A/255.0;
                            if(x<cx-d/2-2||y>cy+d/2+2)Check(ColorDistance(c,n)<2&&c.A==n.A,"Badge shifted the mouse body");
                            else {
                                if(ColorDistance(c,n)>10)coverage+=n.A/255.0;
                                if(c.A>150&&c.R>150&&c.G>170&&c.B>150) {
                                    double w=(Math.Min(c.R,Math.Min(c.G,c.B))-150)/105.0;white+=w;
                                    if(y<cy)topWhite+=w;else bottomWhite+=w;
                                }
                                if(c.A>150&&c.G>c.R+40&&c.G>c.B+40)green+=c.A/255.0;
                            }
                        }
                        Check(white>1&&green>2&&topWhite>.1&&bottomWhite>.1,"White plug with upper prongs, lower body/cable and green circle contrast required");
                        Check(coverage/body<.18,"Charging badge obscures too much mouse body");
                    }
                    if(new[]{-1,0,1,5,10,11,25,26,45,50,78,99,100}.Contains(percent))
                        b.Save(Path.Combine(trayArtifacts,"screenshots",size+"px-"+percent+(charge?"-charging":"")+".png"),Drawing.Imaging.ImageFormat.Png);
                }
                using(var zero=BatteryTrayIconRenderer.Render(State(Battery(0),20,size)))using(var unknown=BatteryTrayIconRenderer.Render(State(Battery(-1),20,size)))
                    Check(PixelHash(zero)!=PixelHash(unknown),"Zero and unknown must retain different emphasis");
            }
        }
        private static void TrayProgress()
        {
            var green=Drawing.Color.FromArgb(26,212,81);var amber=Drawing.Color.FromArgb(245,179,1);var red=Drawing.Color.FromArgb(229,72,77);
            foreach(int p in new[]{0,1,5,10,11,25,26,45,50,78,99,100})
                Check(TrayMouseBatteryIconRenderer.FillColor(p,green).ToArgb()==(p<=10?red:p<=25?amber:green).ToArgb(),"Exact 25/10 color semantics changed");
            foreach(int size in new[]{16,20,24})using(var empty=BatteryTrayIconRenderer.Render(State(Battery(0),37,size)))
            foreach(bool charge in new[]{false,true}) {
                double previous=-1;
                for(int percent=-1;percent<=100;percent++) {
                    var expected=percent<=10?red:percent<=25?amber:green;
                    using(var full=TrayMouseBatteryIconRenderer.Render(new BatteryTrayVisual(100,false,false,size),expected))
                    using(var b=BatteryTrayIconRenderer.Render(State(Battery(percent,charge),37,size))) {
                        double height=0,mass=0,all=0;int cx,cy,d;BatteryTrayIconRenderer.PlugLayout(size,out cx,out cy,out d);
                        for(int y=0;y<size;y++) {
                            double sum=0,weight=0;
                            for(int x=0;x<size;x++) {
                                if(charge&&x>=cx-d/2-2&&y<=cy+d/2+2)continue;
                                var c=b.GetPixel(x,y);var e=empty.GetPixel(x,y);var f=full.GetPixel(x,y);
                                Check(c.A==e.A,"Fill leaked outside opaque cavity or changed outline alpha");
                                if(percent<0){Check(!(c.G>c.R+35&&c.G>c.B+35)&&!(c.R>c.G+35),"Unknown retained colored fill");continue;}
                                double span=ColorDistance(e,f);
                                if(span<2){Check(ColorDistance(c,e)<3,"Fill escaped cavity/wheel region");continue;}
                                double fraction=FillCoverage(c,e,f),pixelWeight=span*c.A/255.0;
                                sum+=fraction*pixelWeight;weight+=pixelWeight;mass+=fraction*pixelWeight;all+=pixelWeight;
                                // Mixed pixels must lie on the empty-to-fill color segment,
                                // not acquire an unrelated threshold/charging color.
                                double error=Math.Sqrt(Math.Pow(c.R-e.R-(f.R-e.R)*fraction,2)+Math.Pow(c.G-e.G-(f.G-e.G)*fraction,2)+Math.Pow(c.B-e.B-(f.B-e.B)*fraction,2));
                                Check(error<12,"AA fill mixed an incorrect color "+size+"/"+percent+"/"+charge+" @"+x+","+y+" error="+error+" c="+c+" e="+e+" f="+f);
                            }
                            double innerTop=size==16?1.9:size==20?2.2:2.5;
                            double rowOverlap=Math.Max(0,Math.Min(y+1,size-innerTop)-Math.Max(y,innerTop));
                            if(weight>0)height+=sum/weight*rowOverlap;
                        }
                        if(percent>=0) {
                            double normalized=mass/Math.Max(1,all);Check(normalized+0.0003>=previous,"Continuous liquid fill is not monotone");previous=normalized;
                            if(percent==0)Check(mass<1,"Zero invented colored charge");
                            if(percent==100)Check(normalized>.99,"100 did not fill entire cavity");
                            // Geometric height (not area): weighted per-row coverage tracks
                            // the continuous waterline, with <1.6 physical pixels AA tolerance.
                            if(!charge&&percent>0) {
                                double innerHeight=size==16?12.2:size==20?15.6:19.0;
                                Check(Math.Abs(height-innerHeight*percent/100.0)<1.6,"Liquid height is not vertical percentage "+size+"/"+percent+"/"+height);
                            }
                        }
                    }
                }
            }
        }
        private static void TrayTooltipText()
        {
            bool language=Ui.English;
            try {
                foreach(bool english in new[]{false,true}) {
                    Ui.English=english;
                    foreach(int p in new[]{0,1,5,10,25,26,45,78,99,100})foreach(bool charge in new[]{false,true}) {
                        var r=Battery(p,charge);var result=TrayMouseTooltipFormatter.Format(r,State(r));
                        string expected=english?(charge?"Mouse battery: charging ("+p+"%)":"Mouse battery: "+p+"%"):(charge?"鼠标电量：正在充电（"+p+"%）":"鼠标电量："+p+"%");
                        Check(result==expected,"Wrong exact battery tooltip");
                        File.AppendAllText(Path.Combine(trayArtifacts,"tooltip-scenarios.txt"),(english?"EN ":"ZH ")+p+(charge?" charging":"")+" => "+result+Environment.NewLine);
                    }
                    foreach(string mode in new[]{"unknown","sleep","cache","stale","future","invalid","unresponsive","absent"}) {
                        var r=Battery(78,true);
                        if(mode=="unknown")r.BatteryKnown=false;if(mode=="sleep")r.IsSleeping=true;if(mode=="cache")r.ProtocolStatus=DeviceProtocolStatus.Cached;
                        if(mode=="stale")r.LastUpdated=DateTime.Now.AddMinutes(-10);if(mode=="future")r.LastUpdated=DateTime.Now.AddMinutes(2);
                        if(mode=="invalid")r.BatteryPercent=101;if(mode=="unresponsive")r.ProtocolStatus=DeviceProtocolStatus.PresentUnresponsive;if(mode=="absent")r.IsConnected=false;
                        string result=TrayMouseTooltipFormatter.Format(r,State(r));
                        Check(!result.Contains("78%")&&!result.Contains("101%"),"Raw/cached percentage leaked to tooltip");
                        Check(mode=="absent"?result.Contains(english?"no device detected":"未检测到设备"):result.Contains(english?"unknown":"未知"),"Unknown state lacks explanation");
                        File.AppendAllText(Path.Combine(trayArtifacts,"tooltip-scenarios.txt"),(english?"EN ":"ZH ")+mode+" => "+result+Environment.NewLine);
                    }
                    var longName=Battery(78,true);longName.DeviceName=string.Concat(Enumerable.Repeat("😀设备\r\n",30));
                    string text=TrayMouseTooltipFormatter.Format(longName,State(longName));
                    Check(text.Length<=63&&text.Contains("78%")&&text.EndsWith(english?")":"）"),"Model truncation lost battery sentence");
                    Check(!text.Contains("\r")&&!text.Contains("\uD83D…"),"Broken surrogate/model line handling");
                    using(var icon=new System.Windows.Forms.NotifyIcon()){icon.Text=text;Check(icon.Text==text,"Framework rejected length-safe tooltip");}
                }
            } finally {Ui.English=language;}
        }
        private static void TrayTooltipUpdates()
        {
            bool language=Ui.English;Ui.English=false;
            var w=Preview();w.Preferences.ConnectionNotifications=false;w.LegacySettings.LowBatteryAlert=false;w.Reading=Battery(78);w.Reading.DeviceName="Model A";
            var c=new TrayController(w);
            try {
                var icon=Field<System.Windows.Forms.NotifyIcon>(c,"icon");var image=Field<Drawing.Icon>(c,"image");
                Check(icon.Text=="Model A\n鼠标电量：78%","Initial native tooltip");
                w.Reading.DeviceName="Model B";c.Update();
                Check(icon.Text=="Model B\n鼠标电量：78%"&&ReferenceEquals(image,Field<Drawing.Icon>(c,"image")),"Model-only change failed tooltip or regenerated unchanged image");
                w.Reading=Battery(78);w.Reading.DeviceName="Model C";w.Reading.DeviceKey="another-physical-instance";w.Reading.ProductId=0x00DF;c.Update();
                Check(icon.Text=="Model C\n鼠标电量：78%"&&ReferenceEquals(image,Field<Drawing.Icon>(c,"image")),"Actual device/key switch left old tooltip or recreated same visual");
                foreach(var r in new[]{Battery(25),Battery(5),Battery(78,true),Battery(-1,true),Battery(0),Battery(78)}) {
                    w.Reading=r;c.Update();Check(icon.Text==TrayMouseTooltipFormatter.Format(r,State(r, w.Preferences.LowBatteryThreshold,System.Windows.Forms.SystemInformation.SmallIconSize.Width)),"Live native text out of sync");
                    Check(Field<BatteryTrayVisual?>(c,"imageState").Value.Percent==(r.BatteryKnown?r.BatteryPercent:-1),"Native image state out of sync");
                    Check(Field<BatteryTrayVisual?>(c,"imageState").Value.Charging==(r.ChargingKnown&&r.IsCharging),"Native charging state out of sync with text");
                }
                w.Reading=Battery(78);w.Reading.IsSleeping=true;c.Update();Check(icon.Text=="鼠标电量：未知","Sleep tooltip retained raw reading");
                w.Reading.IsConnected=false;c.Update();Check(icon.Text=="鼠标电量：未检测到设备","Disconnect tooltip");
            } finally {c.Dispose();w.ClosePreview();Ui.English=language;}
        }
        private static void TrayFallback()
        {
            for(int size=16;size<=256;size++)foreach(int p in new[]{-1,0,77,100})foreach(bool charge in new[]{false,true})
                using(var b=BatteryTrayIconRenderer.Render(new BatteryTrayVisual(p,charge,false,size))){TransparentCorners(b);Check(b.Width==size,"Fallback changed native size");}
            foreach(var invalid in new[]{new BatteryTrayVisual(77,false,false,15),new BatteryTrayVisual(77,false,false,257),new BatteryTrayVisual(-2,false,false,16),new BatteryTrayVisual(101,false,false,20)})
            {bool rejected=false;try{using(var b=BatteryTrayIconRenderer.Render(invalid)){} }catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"Invalid render input accepted");}
        }
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process,int flags);
        [StructLayout(LayoutKind.Sequential)] private struct TrayIdentifier
        { internal uint Size; internal IntPtr Window; internal uint Id; internal Guid Guid; }
        [StructLayout(LayoutKind.Sequential)] private struct TrayRectangle
        { internal int Left,Top,Right,Bottom; }
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref TrayIdentifier identifier,out TrayRectangle rectangle);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string className,string title);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        private static void CaptureActualTray(TrayController controller,string variant="")
        {
            try
            {
                var icon=Field<System.Windows.Forms.NotifyIcon>(controller,"icon");
                var window=Field<System.Windows.Forms.NativeWindow>(icon,"window");
                var identifier=new TrayIdentifier {Size=(uint)Marshal.SizeOf(typeof(TrayIdentifier)),Window=window.Handle,Id=(uint)Field<int>(icon,"id")};
                TrayRectangle rectangle; Pump(); Thread.Sleep(200); Pump();
                int result=Shell_NotifyIconGetRect(ref identifier,out rectangle);
                Check(result==0 && rectangle.Right>rectangle.Left && rectangle.Bottom>rectangle.Top,"NotifyIcon is hidden or shell has no visible rectangle");
                int width=rectangle.Right-rectangle.Left,height=rectangle.Bottom-rectangle.Top;
                Check(width<=128 && height<=128,"Capture must contain only the isolated notification icon");
                string system,theme;
                using(var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) system=int.Parse((string)key.GetValue("CurrentBuildNumber"))>=22000?"win11":"win10";
                using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize")) theme=key!=null && Convert.ToInt32(key.GetValue("SystemUsesLightTheme",0))!=0?"light":"dark";
                using(var bitmap=new Drawing.Bitmap(width,height))
                {
                    using(var g=Drawing.Graphics.FromImage(bitmap))g.CopyFromScreen(rectangle.Left,rectangle.Top,0,0,new Drawing.Size(width,height));
                    double best=double.MaxValue;
                    using(var expected=BatteryTrayIconRenderer.Render(Field<BatteryTrayVisual?>(controller,"imageState").Value))
                    {
                        for(int top=0;top<=height-expected.Height;top++)for(int left=0;left<=width-expected.Width;left++)
                        {
                            long error=0; int samples=0;
                            for(int y=0;y<expected.Height;y++)for(int x=0;x<expected.Width;x++)
                            {var a=expected.GetPixel(x,y);if(a.A<245)continue;var b=bitmap.GetPixel(left+x,top+y);error+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);samples+=3;}
                            if(samples>0)best=Math.Min(best,error/(double)samples);
                        }
                    }
                    Check(best<30,"Visible shell rectangle does not match the current rendered test icon: "+best);
                    bitmap.Save(Path.Combine(trayArtifacts,"screenshots",system+"-"+theme+"-taskbar"+variant+".png"),Drawing.Imaging.ImageFormat.Png);
                    var screen=System.Windows.Forms.Screen.FromPoint(new Drawing.Point(rectangle.Left,rectangle.Top)).Bounds;
                    int contextLeft=Math.Max(screen.Left,rectangle.Left-48),contextRight=Math.Min(screen.Right,rectangle.Right+48);
                    using(var context=new Drawing.Bitmap(contextRight-contextLeft,height)) {
                        using(var cg=Drawing.Graphics.FromImage(context))cg.CopyFromScreen(contextLeft,rectangle.Top,0,0,new Drawing.Size(context.Width,height));
                        context.Save(Path.Combine(trayArtifacts,"screenshots",system+"-"+theme+"-taskbar"+variant+"-neighbors.png"),Drawing.Imaging.ImageFormat.Png);
                    }
                    File.AppendAllText(Path.Combine(trayArtifacts,"native-captures.tsv"),"PASS\t"+system+"\t"+theme+"\t"+variant+"\tRGB error "+best.ToString("F2")+Environment.NewLine);
                }
                uint dpi=GetDpiForWindow(FindWindow("Shell_TrayWnd",null));
                File.WriteAllText(Path.Combine(trayArtifacts,"actual-taskbar"+variant+".txt"),"PASS: "+system+" "+theme+" current theme, native NotifyIcon rectangle "+width+"x"+height+", native image "+Field<BatteryTrayVisual?>(controller,"imageState").Value.Size+"px, SmallIconSize "+System.Windows.Forms.SystemInformation.SmallIconSize+", taskbar DPI "+dpi+" ("+(dpi/96.0*100)+"%). Other OS/themes/scales NOT TESTED. Screenshot contains only isolated test icon, with Fake battery state.");
            }
            catch(Exception ex) { File.WriteAllText(Path.Combine(trayArtifacts,"actual-taskbar"+variant+".txt"),"NOT TESTED: actual notification area capture unavailable: "+ex.Message+". Dark/light simulated sheets are not hardware/OS acceptance."); }
        }
        private delegate bool HoverEnum(IntPtr window,IntPtr parameter);
        [DllImport("user32.dll",EntryPoint="EnumWindows")] private static extern bool HoverWindows(HoverEnum callback,IntPtr parameter);
        [DllImport("user32.dll",EntryPoint="IsWindowVisible")] private static extern bool HoverVisible(IntPtr window);
        [DllImport("user32.dll",EntryPoint="GetClassNameW",CharSet=CharSet.Unicode)] private static extern int HoverClass(IntPtr window,System.Text.StringBuilder text,int count);
        [DllImport("user32.dll",EntryPoint="GetWindowTextW",CharSet=CharSet.Unicode)] private static extern int HoverText(IntPtr window,System.Text.StringBuilder text,int count);
        [DllImport("user32.dll",EntryPoint="GetWindowRect")] private static extern bool HoverRect(IntPtr window,out TrayRectangle rectangle);
        [DllImport("user32.dll",EntryPoint="FindWindowExW",CharSet=CharSet.Unicode)] private static extern IntPtr HoverChild(IntPtr parent,IntPtr after,string cls,string text);
        [DllImport("user32.dll",EntryPoint="GetDlgItem")] private static extern IntPtr HoverItem(IntPtr window,int id);
        [DllImport("user32.dll",EntryPoint="SendMessageW")] private static extern IntPtr HoverMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
        private static IntPtr OpenHiddenIconPanel(TrayIdentifier id)
        {
            TrayRectangle rectangle;
            if(Shell_NotifyIconGetRect(ref id,out rectangle)==0)return IntPtr.Zero;
            var overflow=FindWindow("NotifyIconOverflowWindow",null);
            if(overflow!=IntPtr.Zero&&HoverVisible(overflow))return IntPtr.Zero;
            var parent=HoverChild(FindWindow("Shell_TrayWnd",null),IntPtr.Zero,"TrayNotifyWnd",null);
            var arrow=HoverItem(parent,1502);
            var name=new System.Text.StringBuilder(80);if(arrow==IntPtr.Zero)return IntPtr.Zero;HoverClass(arrow,name,80);
            if(name.ToString()!="Button"||!HoverRect(arrow,out rectangle)||rectangle.Right-rectangle.Left<=0||rectangle.Right-rectangle.Left>64)return IntPtr.Zero;
            HoverMessage(arrow,0xF5,IntPtr.Zero,IntPtr.Zero);Pump();Thread.Sleep(300);Pump();
            overflow=FindWindow("NotifyIconOverflowWindow",null);
            return arrow; // Retain cleanup ownership even if Shell opens asynchronously.
        }
        private static void CaptureNativeTooltip(TrayController controller)
        {
            var previous=System.Windows.Forms.Cursor.Position;
            try {
                var icon=Field<System.Windows.Forms.NotifyIcon>(controller,"icon");
                var id=new TrayIdentifier {Size=(uint)Marshal.SizeOf(typeof(TrayIdentifier)),Window=Field<System.Windows.Forms.NativeWindow>(icon,"window").Handle,Id=(uint)Field<int>(icon,"id")};
                TrayRectangle slot;Check(Shell_NotifyIconGetRect(ref id,out slot)==0,"Native tooltip slot unavailable");
                System.Windows.Forms.Cursor.Position=new Drawing.Point(slot.Left,slot.Top-40);Pump();Thread.Sleep(200);Pump();
                System.Windows.Forms.Cursor.Position=new Drawing.Point((slot.Left+slot.Right)/2,(slot.Top+slot.Bottom)/2);
                string expected=icon.Text.Replace("\r","");IntPtr tooltip=IntPtr.Zero;
                for(int attempt=0;attempt<20&&tooltip==IntPtr.Zero;attempt++) {
                    Thread.Sleep(150);Pump();
                    HoverWindows((window,parameter)=>{
                        if(!HoverVisible(window))return true;
                        var name=new System.Text.StringBuilder(80);HoverClass(window,name,80);if(name.ToString()!="tooltips_class32")return true;
                        var caption=new System.Text.StringBuilder(256);HoverText(window,caption,256);
                        if(caption.ToString().Replace("\r","")==expected){tooltip=window;return false;}return true;
                    },IntPtr.Zero);
                }
                if(tooltip==IntPtr.Zero){File.WriteAllText(Path.Combine(trayArtifacts,"tooltip-native-hover.txt"),"UNAVAILABLE: Shell tooltip window not exposed within3s; native Text scenarios separately validated");return;}
                TrayRectangle box;Check(HoverRect(tooltip,out box)&&box.Right>box.Left&&box.Bottom>box.Top&&box.Right-box.Left<600&&box.Bottom-box.Top<160,"Native tooltip rectangle invalid");
                using(var b=new Drawing.Bitmap(box.Right-box.Left,box.Bottom-box.Top)) {
                    using(var g=Drawing.Graphics.FromImage(b))g.CopyFromScreen(box.Left,box.Top,0,0,b.Size);
                    b.Save(Path.Combine(trayArtifacts,"screenshots","tooltip-native-78.png"),Drawing.Imaging.ImageFormat.Png);
                }
                File.WriteAllText(Path.Combine(trayArtifacts,"tooltip-native-hover.txt"),"PASS: actual Shell tooltips_class32 hover text matched native Text: "+expected);
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(trayArtifacts,"tooltip-native-hover.txt"),"UNAVAILABLE: "+ex.Message+"; no fake screenshot substituted");}
            finally {System.Windows.Forms.Cursor.Position=previous;Pump();}
        }
        [DllImport("gdi32.dll",EntryPoint="SetPixel")] private static extern uint WriteGdiPixel(IntPtr dc,int x,int y,uint color);
        [DllImport("gdi32.dll",EntryPoint="GetPixel")] private static extern uint ReadGdiPixel(IntPtr dc,int x,int y);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int width,int height,uint step,IntPtr brush,uint flags);
        private static void TrayNativeAlpha()
        {
            foreach(int size in new[]{16,20,24})foreach(int p in new[]{-1,0,5,25,78,100})foreach(bool charge in new[]{false,true}) {
                var state=new BatteryTrayVisual(p,charge,false,size);
                using(var source=BatteryTrayIconRenderer.Render(state))using(var icon=BatteryTrayIconRenderer.Create(state))
                using(var frame=new Drawing.Bitmap(size,size,Drawing.Imaging.PixelFormat.Format24bppRgb))using(var g=Drawing.Graphics.FromImage(frame)) {
                    IntPtr dc=g.GetHdc();double error=0,max=0;int count=0;
                    try {
                        // Set the native DC explicitly: a temporary GDI+ HDC may not
                        // preserve Graphics.Clear's background on every pixel format.
                        for(int y=0;y<size;y++)for(int x=0;x<size;x++)WriteGdiPixel(dc,x,y,0x007D6446);
                        Check(DrawIconEx(dc,0,0,icon.Handle,size,size,0,IntPtr.Zero,3),"Native icon drawing failed");
                        for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
                            var c=source.GetPixel(x,y);double a=c.A/255.0;uint pixel=ReadGdiPixel(dc,x,y);
                            var actual=new[]{(int)(pixel&255),(int)((pixel>>8)&255),(int)((pixel>>16)&255)};
                            var expected=new[]{c.R*a+70*(1-a),c.G*a+100*(1-a),c.B*a+125*(1-a)};
                            for(int k=0;k<3;k++){double delta=Math.Abs(actual[k]-expected[k]);error+=delta;max=Math.Max(max,delta);count++;}
                        }
                    }finally{g.ReleaseHdc(dc);}
                    Check(error/count<1.5&&max<4,"HICON differs from single-alpha composite "+size+"/"+p+"/"+charge+" mean="+error/count+" max="+max);
                    File.AppendAllText(Path.Combine(trayArtifacts,"native-alpha.tsv"),size+"\t"+p+"\t"+charge+"\tMAE="+(error/count).ToString("F3")+"\tmax="+max.ToString("F3")+Environment.NewLine);
                }
            }
        }
        private static void TrayResources()
        {
            var w=Preview(); w.Preferences.ConnectionNotifications=false; w.LegacySettings.LowBatteryAlert=false; w.Reading=Battery(78);
            var controller=new TrayController(w);
            var identifier=new TrayIdentifier(); bool slotWasVisible=false;IntPtr openedPanel=IntPtr.Zero;
            try
            {
                var ni=Field<System.Windows.Forms.NotifyIcon>(controller,"icon");
                identifier=new TrayIdentifier {Size=(uint)Marshal.SizeOf(typeof(TrayIdentifier)),Window=Field<System.Windows.Forms.NativeWindow>(ni,"window").Handle,Id=(uint)Field<int>(ni,"id")};
                TrayRectangle before; Pump(); openedPanel=OpenHiddenIconPanel(identifier); slotWasVisible=Shell_NotifyIconGetRect(ref identifier,out before)==0;
                File.WriteAllText(Path.Combine(trayArtifacts,"native-icon-location.txt"),HoverVisible(FindWindow("NotifyIconOverflowWindow",null))?"Actual Windows notification overflow panel; opened temporarily and restored, no pin or settings change":"Actual main taskbar notification area");
                CaptureActualTray(controller);
                CaptureNativeTooltip(controller);
                w.Reading=Battery(25); controller.Update(); CaptureActualTray(controller,"-low");
                w.Reading=Battery(5); controller.Update(); CaptureActualTray(controller,"-critical");
                w.Reading=Battery(-1); controller.Update(); CaptureActualTray(controller,"-unknown");
                w.Reading=Battery(78,true); controller.Update(); CaptureActualTray(controller,"-charging");
                w.Reading=Battery(88); controller.Update(); CaptureActualTray(controller,"-88");
                w.Reading=Battery(100); controller.Update(); CaptureActualTray(controller,"-100");
                w.Reading=Battery(-1,true); controller.Update(); CaptureActualTray(controller,"-unknown-charging");
                w.Reading=Battery(80); controller.Update();
                var image=Field<Drawing.Icon>(controller,"image"); for(int i=0;i<100;i++)controller.Update();
                Check(ReferenceEquals(image,Field<Drawing.Icon>(controller,"image")),"Unchanged update reallocates icon");
                using(var process=Process.GetCurrentProcess())
                {
                    uint gdi=GetGuiResources(process.Handle,0),user=GetGuiResources(process.Handle,1);
                    for(int i=0;i<500;i++) { w.Reading=Battery(i%101,(i&1)==0); controller.Update(); }
                    GC.Collect(); GC.WaitForPendingFinalizers(); Pump();
                    uint finalGdi=GetGuiResources(process.Handle,0),finalUser=GetGuiResources(process.Handle,1);
                    File.WriteAllText(Path.Combine(trayArtifacts,"gdi-resources.txt"),"500 replacements; GDI "+gdi+" -> "+finalGdi+"; USER "+user+" -> "+finalUser);
                    Check(finalGdi<=gdi+4 && finalUser<=user+4,"GDI/USER resources grow after disposal");
                }
            }
            finally { controller.Dispose(); controller.Dispose(); w.ClosePreview();if(openedPanel!=IntPtr.Zero){if(HoverVisible(FindWindow("NotifyIconOverflowWindow",null))){HoverMessage(openedPanel,0xF5,IntPtr.Zero,IntPtr.Zero);Pump();Thread.Sleep(100);Pump();}bool restored=!HoverVisible(FindWindow("NotifyIconOverflowWindow",null));File.WriteAllText(Path.Combine(trayArtifacts,"native-panel-restoration.txt"),restored?"PASS: temporarily opened notification panel restored hidden; no settings modified":"FAIL: notification panel still visible");Check(restored,"Temporary notification panel was not restored");} }
            Pump(); TrayRectangle after;
            bool removed=Shell_NotifyIconGetRect(ref identifier,out after)!=0;
            if(slotWasVisible) Check(removed,"Disposed NotifyIcon retains a visible native shell slot");
            File.WriteAllText(Path.Combine(trayArtifacts,"tray-icon-disposal.txt"),slotWasVisible&&removed?"PASS: previously visible native test NotifyIcon slot removed after double Dispose":"NOT TESTED: native slot was unavailable before disposal");
        }
    }
}
