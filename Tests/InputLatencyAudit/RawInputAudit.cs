using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace RazerBatteryTray.Macros
{
    // Separate process; passive observation, no hook, no SendInput, no HID.
    internal sealed class RawInputAudit : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page,Usage;public uint Flags;public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] private struct DeviceEntry { public IntPtr Handle; public uint Type; }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo { public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended; }
        [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterRawInputDevices(Device[] devices,uint count,uint size);
        [DllImport("user32.dll",SetLastError=true)] private static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint header);
        [DllImport("user32.dll",SetLastError=true)] private static extern uint GetRawInputDeviceList([In,Out] DeviceEntry[] list,ref uint count,uint size);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryInfo info);
        private const int Capacity=524288;
        private readonly long[] time=new long[Capacity];private readonly int[] dx=new int[Capacity],dy=new int[Capacity];
        private readonly IntPtr buffer=Marshal.AllocHGlobal(128);
        private int count,errors,absolute,otherDevices;private IntPtr selected;
        internal bool Capturing = true;
        internal int SampleCount { get { return count; } }
        internal void BeginCapture() { Capturing=false;count=errors=absolute=otherDevices=0;Capturing=true; }
        internal RawInputAudit(int deviceIndex)
        {
            if(IntPtr.Size!=8)throw new InvalidOperationException("x64 only; RAWINPUTHEADER is 24 bytes");
            if(deviceIndex>=0) {uint n=0,size=(uint)Marshal.SizeOf(typeof(DeviceEntry));if(GetRawInputDeviceList(null,ref n,size)==uint.MaxValue)throw new System.ComponentModel.Win32Exception();var entries=new DeviceEntry[n];if(GetRawInputDeviceList(entries,ref n,size)==uint.MaxValue)throw new System.ComponentModel.Win32Exception();int index=0;foreach(var d in entries)if(d.Type==0){if(index++==deviceIndex)selected=d.Handle;}if(selected==IntPtr.Zero)throw new ArgumentException("Mouse index unavailable");}
            CreateHandle(new CreateParams{Caption="LeiyunLite passive Raw Input audit",Parent=new IntPtr(-3)});
            var devices=new[]{new Device{Page=1,Usage=2,Flags=0x100,Target=Handle}};
            if(!RegisterRawInputDevices(devices,1,(uint)Marshal.SizeOf(typeof(Device))))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0xFF)
            {
                long now=Stopwatch.GetTimestamp();uint size=128;
                uint read=GetRawInputData(message.LParam,0x10000003,buffer,ref size,24);
                if(read==uint.MaxValue || read<48)errors++;
                else if(Marshal.ReadInt32(buffer,0)==0)
                {
                    ushort flags=unchecked((ushort)Marshal.ReadInt16(buffer,24));
                    if((flags&1)!=0)absolute++; // Never persist absolute desktop coordinates.
                    else
                    {
                        int x=Marshal.ReadInt32(buffer,36),y=Marshal.ReadInt32(buffer,40);
                        IntPtr device=Marshal.ReadIntPtr(buffer,8);
                        if(x!=0 || y!=0)
                        {
                            if(selected==IntPtr.Zero)selected=device; // One device only; do not mix intervals from multiple mice.
                            if(device!=selected)otherDevices++;
                            else if(Capturing) {int slot=Interlocked.Increment(ref count)-1;if(slot<Capacity){time[slot]=now;dx[slot]=x;dy[slot]=y;}}
                        }
                    }
                }
            }
            base.WndProc(ref message); // Required default processing/cleanup; input is not suppressed.
        }
        internal void Save(string path,string scenario,int hz,double elapsed,double cpuMs,int gc0,int gc1,int gc2,int threads)
        {
            Directory.CreateDirectory(path);int n=Math.Min(Capacity,count);long origin=n==0?0:time[0];
            using(var w=new StreamWriter(Path.Combine(path,"raw-motion.csv"))) {w.WriteLine("relative_ticks,dx,dy");for(int i=0;i<n;i++)w.WriteLine((time[i]-origin)+","+dx[i]+","+dy[i]);}
            long[] intervals=new long[Math.Max(0,n-1)];for(int i=1;i<n;i++)intervals[i-1]=time[i]-time[i-1];
            using(var w=new StreamWriter(Path.Combine(path,"raw-interval.csv"))){w.WriteLine(AuditMetrics.Header);AuditMetrics.Row(w,scenario,"RawInput.interval",intervals,Math.Max(0,count-1),hz>0?1000.0/hz:0);}
            // Row sorted a private intervals array, after capture stopped. No logging in WM_INPUT.
            double sum=0,square=0;int gap4=0,gap10=0,gap20=0;double[] longest=new double[Math.Min(10,intervals.Length)];
            foreach(long ticks in intervals) {double ms=AuditMetrics.Ms(ticks);sum+=ms;square+=ms*ms;if(ms>4)gap4++;if(ms>10)gap10++;if(ms>20)gap20++;}
            for(int i=0;i<longest.Length;i++)longest[i]=AuditMetrics.Ms(intervals[intervals.Length-1-i]);
            double avg=intervals.Length==0?0:sum/intervals.Length;
            File.WriteAllText(Path.Combine(path,"raw-stage15-summary.json"),new JavaScriptSerializer().Serialize(new {count=count,interval_count=intervals.Length,duration_ms=elapsed,motion_span_ms=n<2?0:AuditMetrics.Ms(time[n-1]-time[0]),gap_gt4ms=gap4,gap_gt10ms=gap10,gap_gt20ms=gap20,gap_gt4ms_ratio=intervals.Length==0?(double?)null:(double)gap4/intervals.Length,gap_gt10ms_ratio=intervals.Length==0?(double?)null:(double)gap10/intervals.Length,gap_gt20ms_ratio=intervals.Length==0?(double?)null:(double)gap20/intervals.Length,longest_10_gaps_ms=longest,relative_dx_dy_samples=n,dropped=Math.Max(0,count-Capacity),read_errors=errors,nonempty=count>1}));
            var mem=new MemoryInfo{Length=(uint)Marshal.SizeOf(typeof(MemoryInfo))};bool memoryAvailable=GlobalMemoryStatusEx(ref mem);
            File.WriteAllText(Path.Combine(path,"raw-metadata.json"),new JavaScriptSerializer().Serialize(new {scenario=scenario,count=count,stored=n,dropped=Math.Max(0,count-Capacity),read_errors=errors,absolute_events_discarded=absolute,other_device_motion_discarded=otherDevices,expected_hz_user_reported=hz,stopwatch_frequency=Stopwatch.Frequency,elapsed_ms=elapsed,cpu_ms=cpuMs,cpu_one_core_pct=100*cpuMs/elapsed,gen0=gc0,gen1=gc1,gen2=gc2,threads=threads,total_memory_bytes=memoryAvailable?mem.TotalPhysical:0,limits="WM_INPUT dispatch arrival, not device timestamp or display latency; standard single read; busy loop/backlog may batch, high-rate testing requires validation; no keyboard text, device path, desktop location or window title saved"}));
        }
        public void Dispose(){RegisterRawInputDevices(new[]{new Device{Page=1,Usage=2,Flags=1,Target=IntPtr.Zero}},1,(uint)Marshal.SizeOf(typeof(Device)));DestroyHandle();Marshal.FreeHGlobal(buffer);}
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                if(args.Length>0 && args[0]=="--guided")return Stage15Round.Run(args);
                if(args.Length==1 && args[0]=="--self-test")
                {
                    IntPtr b=Marshal.AllocHGlobal(64);try{for(int i=0;i<64;i++)Marshal.WriteByte(b,i,0);Marshal.WriteInt32(b,36,-23);Marshal.WriteInt32(b,40,17);if(Marshal.ReadInt32(b,36)!=-23 || Marshal.ReadInt32(b,40)!=17)throw new Exception("RAWMOUSE offsets");}finally{Marshal.FreeHGlobal(b);}
                    if(AuditMetrics.Percentile(new long[]{1,2,3,4},.5)!=AuditMetrics.Ms(2))throw new Exception("nearest rank");
                    if(!double.IsNaN(AuditMetrics.Percentile(new long[0],.99)))throw new Exception("empty must not PASS as zero");Console.WriteLine("RAW HARNESS SELF TEST PASS (3)");return 0;
                }
                if(args.Length<4)throw new ArgumentException("Usage: RawInputAudit.exe NEW-directory seconds hz scenario [mouse-index]");
                string path=Path.GetFullPath(args[0]);int seconds=int.Parse(args[1]),hz=int.Parse(args[2]);string scenario=args[3];
                if(seconds<1||seconds>300||hz<0||hz>8000||Directory.Exists(path))throw new ArgumentException("Use new directory, seconds 1..300, hz 0..8000");
                using(var window=new RawInputAudit(args.Length>4?int.Parse(args[4]):-1))
                using(var timer=new System.Windows.Forms.Timer{Interval=seconds*1000})
                {
                    timer.Tick+=(s,e)=>{timer.Stop();Application.ExitThread();};
                    var p=Process.GetCurrentProcess();TimeSpan cpu=p.TotalProcessorTime;int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);var elapsed=Stopwatch.StartNew();
                    Console.WriteLine("RAW READY: passive capture, scenario="+scenario+", seconds="+seconds);timer.Start();Application.Run();elapsed.Stop();p.Refresh();
                    window.Save(path,scenario,hz,elapsed.Elapsed.TotalMilliseconds,(p.TotalProcessorTime-cpu).TotalMilliseconds,GC.CollectionCount(0)-gc0,GC.CollectionCount(1)-gc1,GC.CollectionCount(2)-gc2,p.Threads.Count);
                    Console.WriteLine("RAW CAPTURE COMPLETE: samples="+window.count+" (zero/idle samples do not establish motion PASS)");
                }
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
