using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace RazerBatteryTray.Macros
{
    // Human-operated guide built around the existing RawInputAudit. No keyboard/hook installation.
    internal sealed class Stage15Round : Form
    {
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc,int index);
        private static readonly string[] ids={"M1","M2","M3","M4","M5","M6","M7","M8","M9"};
        private static readonly string[] instructions={"慢速微调：小范围、低速持续移动，模拟瞄准微调。","中速连续左右：中等幅度，尽量稳定连续左右移动。","快速甩枪：左右大幅快速移动，快速启动与停止。","高频左右拉枪：连续快速左右反转。","移动＋左键：保持移动，同时正常左键单击。","移动＋右键：保持移动，同时正常右键单击。","移动＋侧键：保持移动，同时按当前正常使用的侧键。","移动＋WASD：保持移动，真实按 W/A/S/D（工具不记录键盘）。","移动＋Shift/Ctrl：保持移动，真实按 Shift 或 Ctrl。"};
        private readonly string root,round;
        private readonly int seconds,hz;
        private readonly RawInputAudit raw;
        private readonly Label title=new Label(),instruction=new Label(),status=new Label();
        private readonly Button start=new Button(),skip=new Button();
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=100};
        private readonly List<object> segments=new List<object>(),cpuSamples=new List<object>();
        private int index,attempt,phase; // 0 ready, 1 countdown, 2 capture, 3 done
        private long phaseStarted,captureStarted,lastCpu,idleBefore,kernelBefore,userBefore;
        private TimeSpan observerCpuBefore;
        private int gc0,gc1,gc2,processViolations;
        private ManualResetEvent cpuStop;
        private Thread cpuThread;
        private int cpuReadErrors;
        private bool completed;
        private Stage15Round(string directory,string label,int duration,int polling,int deviceIndex)
        {
            root=directory;round=label;seconds=duration;hz=polling;
            if(Directory.Exists(root))throw new ArgumentException("Use a new round output directory");Directory.CreateDirectory(root);
            raw=new RawInputAudit(deviceIndex);raw.Capturing=false;
            // Initialize statistic storage before a human motion measurement begins.
            AuditMetrics.Percentile(new long[]{1},.5);
            Text="雷云 Lite Stage 1.5 · "+round+" · 只测量";ClientSize=new Size(770,440);StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Microsoft YaHei UI",11);BackColor=Color.White;
            title.SetBounds(24,18,722,35);title.Font=new Font(Font.FontFamily,17,FontStyle.Bold);
            instruction.SetBounds(24,64,722,86);instruction.Font=new Font(Font.FontFamily,13);
            var area=new Panel{Location=new Point(24,154),Size=new Size(722,154),BackColor=Color.FromArgb(244,246,248)};
            var safe=new Label{Text="可在这块空白区域移动／点击。\r\nWASD、侧键测试时请让诊断窗口获得焦点，避免其他应用执行动作。\r\n不读取键盘内容；不记录窗口标题或桌面坐标。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};area.Controls.Add(safe);
            status.SetBounds(24,322,722,40);status.ForeColor=Color.FromArgb(0,95,160);
            start.SetBounds(24,374,480,42);skip.SetBounds(520,374,226,42);skip.Text="侧键不可用，跳过 M7";
            start.Click+=(s,e)=>{if(completed)Close();else BeginCountdown();};skip.Click+=(s,e)=>SkipSide();
            Controls.Add(title);Controls.Add(instruction);Controls.Add(area);Controls.Add(status);Controls.Add(start);Controls.Add(skip);
            timer.Tick+=(s,e)=>TickGuide();FormClosing+=(s,e)=>FinishManifest();
            Ready();timer.Start();
            Console.WriteLine("STAGE15 GUIDE READY: "+round+"; passive Raw Input only; operator presses Start for each action.");
        }
        private static Process[] ProductProcesses()
        {
            var list=new List<Process>();foreach(var p in Process.GetProcesses())
            {if(p.ProcessName.StartsWith("LeiyunLite",StringComparison.OrdinalIgnoreCase)||p.ProcessName.StartsWith("RazerBatteryTray",StringComparison.OrdinalIgnoreCase))list.Add(p);else p.Dispose();}
            return list.ToArray();
        }
        private bool CheckState()
        {
            var products=ProductProcesses();bool ok=round.StartsWith("A",StringComparison.Ordinal)?products.Length==0:products.Length==1;
            foreach(var p in products)p.Dispose();return ok;
        }
        private void Ready()
        {
            phase=0;raw.Capturing=false;attempt=0;
            title.Text=round+" / "+ids[index]+" / "+(index+1)+" of "+ids.Length;
            instruction.Text=instructions[index];status.Text="准备好后点开始；3 秒倒计时后采样 "+seconds+" 秒。";
            start.Text="准备好，开始 "+ids[index]+"（真实手动操作）";start.Enabled=true;
            skip.Visible=index==6;skip.Enabled=true;
        }
        private void BeginCountdown()
        {
            if(!CheckState()){MessageBox.Show(this,round.StartsWith("A")?"A 组需要完全退出雷云 Lite。请手动退出后再开始。":"B 组需要恰好一个雷云 Lite 进程。请正常启动已确认的 v1.2.6，并手动确认宏绑定总开关关闭。","状态不符合，未采样");return;}
            phase=1;phaseStarted=Stopwatch.GetTimestamp();start.Enabled=false;skip.Enabled=false;
            status.Text="3 秒后开始："+ids[index]+"。请准备真实动作。";
        }
        private void StartCapture()
        {
            cpuSamples.Clear();processViolations=0;cpuReadErrors=0;GetSystemTimes(out idleBefore,out kernelBefore,out userBefore);
            using(var p=Process.GetCurrentProcess())observerCpuBefore=p.TotalProcessorTime;
            gc0=GC.CollectionCount(0);gc1=GC.CollectionCount(1);gc2=GC.CollectionCount(2);
            captureStarted=Stopwatch.GetTimestamp();lastCpu=captureStarted;raw.BeginCapture();phase=2;
            cpuStop=new ManualResetEvent(false);cpuThread=new Thread(()=>{do{try{ObserveCpu();}catch{Interlocked.Increment(ref cpuReadErrors);}}while(!cpuStop.WaitOne(1000));}){IsBackground=true,Name="Stage15.CpuObserver"};cpuThread.Start();
            status.Text="现在做 "+ids[index]+"：剩余 "+seconds+" 秒。";
        }
        private void ObserveCpu()
        {
            long idle,kernel,user;bool system=GetSystemTimes(out idle,out kernel,out user);
            var products=ProductProcesses();var productCpu=new List<object>();foreach(var p in products)
            {
                try{productCpu.Add(new {pid=p.Id,cpu_ms=p.TotalProcessorTime.TotalMilliseconds,threads=p.Threads.Count});}
                catch{productCpu.Add(new {pid=p.Id,cpu_unavailable=true});}
                finally{p.Dispose();}
            }
            if(round.StartsWith("A")?products.Length!=0:products.Length!=1)processViolations++;
            cpuSamples.Add(new {relative_ms=AuditMetrics.Ms(Stopwatch.GetTimestamp()-captureStarted),system_times_available=system,idle_100ns=idle,kernel_100ns=kernel,user_100ns=user,product_cpu=productCpu});
        }
        private void EndCapture()
        {
            raw.Capturing=false;phase=0;long end=Stopwatch.GetTimestamp();cpuStop.Set();if(!cpuThread.Join(5000))throw new InvalidOperationException("CPU observer did not stop; do not reuse partial statistics");cpuStop.Dispose();cpuStop=null;cpuThread=null;
            double elapsed=AuditMetrics.Ms(end-captureStarted),cpu;
            int threads;using(var p=Process.GetCurrentProcess()){cpu=(p.TotalProcessorTime-observerCpuBefore).TotalMilliseconds;threads=p.Threads.Count;}
            int dg0=GC.CollectionCount(0)-gc0,dg1=GC.CollectionCount(1)-gc1,dg2=GC.CollectionCount(2)-gc2;
            long idle,kernel,user;bool system=GetSystemTimes(out idle,out kernel,out user);long total=(kernel-kernelBefore)+(user-userBefore);
            string segment=ids[index]+"-attempt"+(++attempt).ToString("00");string path=Path.Combine(root,segment);
            raw.Save(path,round+"-"+ids[index],hz,elapsed,cpu,dg0,dg1,dg2,threads);
            File.WriteAllText(Path.Combine(path,"environment.json"),new JavaScriptSerializer().Serialize(new {round=round,action=ids[index],duration_ms=elapsed,expected_polling_hz_user_reported=hz,product_state_violations=processViolations,cpu_read_errors=cpuReadErrors,system_cpu_pct=system&&total>0?(double?)Math.Max(0,Math.Min(100,100.0*(total-(idle-idleBefore))/total)):null,cpu_observations=cpuSamples,observer_gc=new[]{dg0,dg1,dg2},product_gc="NOT AVAILABLE: no product instrumentation or external CLR counter",scope="operator-driven desktop; no extra diagnostic hook; CPU observation on separate background thread, not WM_INPUT/UI queue"}));
            bool valid=raw.SampleCount>1 && processViolations==0;
            segments.Add(new {action=ids[index],directory=segment,count=raw.SampleCount,operator_started=true,process_state_valid=processViolations==0,valid=valid});
            Console.WriteLine("STAGE15 SEGMENT: "+round+" "+ids[index]+" count="+raw.SampleCount+" valid="+valid);
            if(!valid){status.Text="样本为零/状态变化，未接受该项。请重新采样，或关闭窗口保留未完成报告。";start.Text="重新采样 "+ids[index];start.Enabled=true;skip.Enabled=true;return;}
            if(++index==ids.Length){completed=true;phase=3;timer.Stop();title.Text=round+" 已完成";instruction.Text="本轮真实采样已保存。请关闭这个窗口，然后在聊天里告诉我本轮完成。";status.Text="不要自行切换到下一状态，等待我核对数据并给下一轮提示。";start.Text="本轮完成，关闭窗口";start.Enabled=true;skip.Visible=false;FinishManifest();}
            else Ready();
        }
        private void TickGuide()
        {
            if(phase==1){double elapsed=AuditMetrics.Ms(Stopwatch.GetTimestamp()-phaseStarted);if(elapsed>=3000)StartCapture();else status.Text="开始倒计时："+Math.Ceiling((3000-elapsed)/1000)+" 秒";}
            else if(phase==2){long now=Stopwatch.GetTimestamp();double elapsed=AuditMetrics.Ms(now-captureStarted);if(elapsed>=seconds*1000)EndCapture();else if(AuditMetrics.Ms(now-lastCpu)>=1000){lastCpu=now;status.Text="正在采样 "+ids[index]+"：剩余 "+Math.Ceiling(seconds-elapsed/1000)+" 秒；鼠标样本 "+raw.SampleCount;}}
        }
        private void SkipSide()
        {
            if(index!=6||phase!=0)return;segments.Add(new {action="M7",skipped=true,reason="operator reports side button unavailable"});index++;Ready();
        }
        private void FinishManifest()
        {
            raw.Capturing=false;
            if(cpuStop!=null){cpuStop.Set();if(cpuThread!=null)cpuThread.Join(5000);cpuStop.Dispose();cpuStop=null;cpuThread=null;}
            IntPtr dc=GetDC(IntPtr.Zero);int refresh=dc==IntPtr.Zero?0:GetDeviceCaps(dc,116);if(dc!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,dc);
            File.WriteAllText(Path.Combine(root,"round-manifest.json"),new JavaScriptSerializer().Serialize(new {round=round,completed=completed,expected_polling_hz_user_reported=hz,product_version=round.StartsWith("A")?"not running":"1.2.6 operator must confirm",primary_display_refresh_hz=refresh>1?(int?)refresh:null,segments=segments,privacy="relative mouse ticks/dx/dy, statistics and CPU only; no keyboard text, device paths, windows or desktop positions"}));
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();raw.Dispose();}base.Dispose(disposing);}
        internal static int Run(string[] args)
        {
            if(args.Length<4)throw new ArgumentException("Usage: RawInputAudit.exe --guided NEW-directory A1|B1|A2|B2|A3|B3 500 [seconds=15] [mouse-index=-1]");
            string round=args[2];if(round.Length!=2||(round[0]!='A'&&round[0]!='B')||round[1]<'1'||round[1]>'3')throw new ArgumentException("A1/B1/A2/B2/A3/B3 only");
            int hz=int.Parse(args[3]),duration=args.Length>4?int.Parse(args[4]):15;if(hz!=500||duration<10||duration>20)throw new ArgumentException("Stage1.5 current hardware: 500Hz, 10..20 seconds only");
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var guide=new Stage15Round(Path.GetFullPath(args[1]),round,duration,hz,args.Length>5?int.Parse(args[5]):-1))Application.Run(guide);
            return 0;
        }
    }
}
