using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

namespace RazerBatteryTray.Macros
{
    internal static class AuditHarness
    {
        private delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
        private static string root;
        private static readonly IMacroOutput output = new NullOutput();
        private static Func<long> allocated;
        private static readonly List<object> resources = new List<object>();
        private static int checks;
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        private sealed class NullOutput : IMacroOutput
        {
            internal long calls;
            public void Key(int key,bool down) { Interlocked.Increment(ref calls); }
            public void MouseButton(MouseAction button,bool down) { Interlocked.Increment(ref calls); }
            public void Wheel(int count) { Interlocked.Increment(ref calls); }
            public void Text(string text,CancellationToken token) { token.ThrowIfCancellationRequested(); }
            public void Launch(string target,string arguments,bool command) { throw new InvalidOperationException("External execution is forbidden in this harness"); }
        }
        private sealed class MemoryStore : IMacroStore
        {
            internal MacroLibrary library;
            public string FilePath { get { return "DIAGNOSTIC-MEMORY"; } }
            public MacroLibrary Load() { return library; }
            public void Save(MacroLibrary next) { library=next; }
        }
        private sealed class Runner : IMacroRunner
        {
            public bool IsRunning { get; private set; }
            public string ActiveBinding { get; private set; }
            internal int starts,stops;
            public bool Start(MacroLibrary l,string macro,string binding,bool repeat,int delay) { starts++;IsRunning=true;ActiveBinding=binding;return true; }
            public void Stop() { stops++;IsRunning=false;ActiveBinding=null; }
        }
        private static void Check(bool condition,string message) { if(!condition) throw new Exception("CHECK FAILED: "+message); checks++; }
        private static T Field<T>(object target,string name) { return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
        private static Func<InputStroke,bool> Route(MacroController c) { return (Func<InputStroke,bool>)Delegate.CreateDelegate(typeof(Func<InputStroke,bool>),c,typeof(MacroController).GetMethod("RouteInput",BindingFlags.Instance|BindingFlags.NonPublic)); }
        private static Func<InputStroke,bool> WheelRoute(MacroController c) { return (Func<InputStroke,bool>)Delegate.CreateDelegate(typeof(Func<InputStroke,bool>),c,typeof(MacroController).GetMethod("SuppressWheel",BindingFlags.Instance|BindingFlags.NonPublic)); }
        private static Callback Hook(GlobalInputHook h,string name) { return (Callback)Delegate.CreateDelegate(typeof(Callback),h,typeof(GlobalInputHook).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)); }
        private static InputStroke Stroke(TriggerKind trigger,bool down,int key) { return new InputStroke{Trigger=trigger,Down=down,Key=key}; }
        private static MacroLibrary Library(int bindings)
        {
            var l=new MacroLibrary(); var m=new MacroDefinition{Id="audit-macro",Name="Diagnostic delay only"};
            m.Steps.Add(new MacroStep{Kind=ActionKind.Delay,Number=1}); l.Macros.Add(m);
            for(int i=0;i<bindings;i++) l.Bindings.Add(new MacroBinding{Id="b"+i,MacroId=m.Id,Trigger=TriggerKind.Keyboard,KeyCode=32+i%58,Modifiers=(KeyModifiers)(i/58+4),Mode=RunMode.Once});
            MacroValidation.Validate(l);return l;
        }
        private static void Warm(Action action,int count) { AuditMetrics.Enabled=false;for(int i=0;i<count;i++) action(); }
        private static void Measure(string name,int count,Action action)
        {
            Warm(action,Math.Min(count,2000)); AuditMetrics.Reset();
            var samples=new long[count]; var p=Process.GetCurrentProcess(); p.Refresh();
            TimeSpan cpu=p.TotalProcessorTime; int threads=p.Threads.Count;
            int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2); long memory=GC.GetTotalMemory(false),bytes=allocated==null?0:allocated();
            long begin=Stopwatch.GetTimestamp();
            for(int i=0;i<count;i++) { long start=Stopwatch.GetTimestamp(); action();samples[i]=Stopwatch.GetTimestamp()-start; }
            long end=Stopwatch.GetTimestamp(); bytes=allocated==null?-1:allocated()-bytes;
            long memoryDelta=GC.GetTotalMemory(false)-memory;
            int gc0=GC.CollectionCount(0)-g0,gc1=GC.CollectionCount(1)-g1,gc2=GC.CollectionCount(2)-g2;
            p.Refresh(); double cpuMs=(p.TotalProcessorTime-cpu).TotalMilliseconds;
            AuditMetrics.Save(root,name,0,name=="MouseMove-direct");
            using(var w=new StreamWriter(Path.Combine(root,name+"-external.csv"))) { w.WriteLine(AuditMetrics.Header);AuditMetrics.Row(w,name,"External.total",samples,count,0); }
            resources.Add(new {scenario=name,count=count,elapsed_ms=AuditMetrics.Ms(end-begin),cpu_ms=cpuMs,cpu_one_core_pct=100*cpuMs/AuditMetrics.Ms(end-begin),threads_before=threads,threads_after=p.Threads.Count,gen0=gc0,gen1=gc1,gen2=gc2,allocated_bytes=bytes,retained_memory_delta=memoryDelta,allocation_scope="calling thread only; warmup and samples array excluded",measurement="direct managed callback; no OS delivery, no physical events"});
            Console.WriteLine("MEASURED "+name+" count="+count+" GC="+gc0+"/"+gc1+"/"+gc2+" allocated="+bytes);
        }
        private static void Callbacks(bool middleOnly)
        {
            using(var controller=new MacroController(new MemoryStore{library=Library(0)},output,false))
            using(var hook=new GlobalInputHook(Route(controller),WheelRoute(controller)))
            {
                var mouse=Hook(hook,"Mouse"); var keyboard=Hook(hook,"Keyboard");
                IntPtr pointer=Marshal.AllocHGlobal(40);
                try
                {
                    for(int i=0;i<40;i++) Marshal.WriteByte(pointer,i,0);
                    if(middleOnly) {Measure("Mouse-Middle",128,()=>{mouse(0,new IntPtr(0x207),pointer);mouse(0,new IntPtr(0x208),pointer);});return;}
                    Measure("MouseMove-direct",100000,()=>mouse(0,new IntPtr(0x200),IntPtr.Zero));
                    if(AuditMetrics.Count(0)>0) { Check(AuditMetrics.Count(6)==0,"MouseMove must not route");Check(AuditMetrics.Count(8)==0,"MouseMove must not enter router");Check(AuditMetrics.Count(9)==0,"MouseMove must not acquire gate");Check(AuditMetrics.Count(11)==0 && AuditMetrics.Count(12)==0,"MouseMove must not start/stop macros"); }
                    int[] keys={87,65,83,68,160,162};string[] keyNames={"W","A","S","D","Shift","Ctrl"};
                    for(int k=0;k<keys.Length;k++) { Marshal.WriteInt32(pointer,0,keys[k]);Measure("Keyboard-"+keyNames[k],20000,()=>{keyboard(0,new IntPtr(0x100),pointer);keyboard(0,new IntPtr(0x101),pointer);}); }
                    int[] down={0x201,0x204,0x207,0x20B,0x20B};int[] up={0x202,0x205,0x208,0x20C,0x20C};string[] names={"Left","Right","Middle","X1","X2"};
                    for(int b=0;b<names.Length;b++) { if(b==2)continue;Marshal.WriteInt32(pointer,8,b==3?1<<16:b==4?2<<16:0);int d=down[b],u=up[b];Measure("Mouse-"+names[b],10000,()=>{mouse(0,new IntPtr(d),pointer);mouse(0,new IntPtr(u),pointer);}); }
                    Marshal.WriteInt32(pointer,8,120<<16);Measure("Wheel-Up",20000,()=>mouse(0,new IntPtr(0x20A),pointer));
                    Marshal.WriteInt32(pointer,8,unchecked(-120<<16));Measure("Wheel-Down",20000,()=>mouse(0,new IntPtr(0x20A),pointer));
                }
                finally { Marshal.FreeHGlobal(pointer); }
            }
        }
        private static void RouterAndAllocation()
        {
            foreach(int n in new[]{0,16,200})
            {
                var library=Library(n);var router=new BindingRouter(new Runner());router.Configure(library);
                var down=Stroke(TriggerKind.Keyboard,true,87);var up=Stroke(TriggerKind.Keyboard,false,87);
                Measure("Router-no-match-"+n,50000,()=>{router.Handle(down);router.Handle(up);});
                library.BindingsEnabled=false;Measure("Router-disabled-"+n,50000,()=>{router.Handle(down);router.Handle(up);});
            }
            var stroke=Stroke(TriggerKind.Keyboard,true,87);var sink=stroke.Physical;
            Measure("Physical-only",100000,()=>sink=stroke.Physical);
            Check(sink.Equals(stroke.Physical),"Physical value preserved");
            Measure("Probe-overhead",100000,()=>{long t=Stopwatch.GetTimestamp();AuditMetrics.Record(21,Stopwatch.GetTimestamp()-t);});
        }
        private static void EngineCosts()
        {
            using(var engine=new MacroEngine(output))
            {
                var library=Library(0);
                Measure("Engine-idle-Stop",20000,()=>engine.Stop());
                AuditMetrics.Enabled=false;
                long coldStart=Stopwatch.GetTimestamp();Check(engine.Start(library,"audit-macro","audit",true,0),"cold engine starts");coldStart=Stopwatch.GetTimestamp()-coldStart;
                long coldStop=Stopwatch.GetTimestamp();engine.Stop();coldStop=Stopwatch.GetTimestamp()-coldStop;
                var coldTimeout=Stopwatch.StartNew();while(engine.IsRunning && coldTimeout.ElapsedMilliseconds<2000)Thread.Yield();Check(!engine.IsRunning,"cold engine stops");
                using(var w=new StreamWriter(Path.Combine(root,"Engine-cold-first-external.csv"))) {w.WriteLine(AuditMetrics.Header);AuditMetrics.Row(w,"Engine-cold-first","Start",new[]{coldStart},1,0);AuditMetrics.Row(w,"Engine-cold-first","Stop",new[]{coldStop},1,0);}
                for(int warm=0;warm<16;warm++) {Check(engine.Start(library,"audit-macro","audit",true,0),"warm engine starts");engine.Stop();var timeout=Stopwatch.StartNew();while(engine.IsRunning && timeout.ElapsedMilliseconds<2000)Thread.Yield();Check(!engine.IsRunning,"warm engine stops");}
                var startTicks=new long[256];var stopTicks=new long[256];AuditMetrics.Reset();
                for(int i=0;i<256;i++)
                {
                    long start=Stopwatch.GetTimestamp();Check(engine.Start(library,"audit-macro","audit",true,0),"real engine starts");startTicks[i]=Stopwatch.GetTimestamp()-start;
                    start=Stopwatch.GetTimestamp();engine.Stop();stopTicks[i]=Stopwatch.GetTimestamp()-start;
                    var timeout=Stopwatch.StartNew();while(engine.IsRunning && timeout.ElapsedMilliseconds<2000) Thread.Yield();Check(!engine.IsRunning,"engine cancellation ends");
                }
                AuditMetrics.Save(root,"Engine-running-StartStop",0,false);
                using(var w=new StreamWriter(Path.Combine(root,"Engine-running-StartStop-external.csv"))) { w.WriteLine(AuditMetrics.Header);AuditMetrics.Row(w,"Engine-running","Start",startTicks,256,0);AuditMetrics.Row(w,"Engine-running","Stop",stopTicks,256,0); }
                using(var w=new StreamWriter(Path.Combine(root,"Engine-running-samples.csv"))) {w.WriteLine("start_ticks,stop_ticks");for(int i=0;i<256;i++)w.WriteLine(startTicks[i]+","+stopTicks[i]);}
            }
        }
        private static void ConfigurationContention(string operation,bool running)
        {
            string dir=Path.Combine(root,"isolated-config",operation+(running?"-running":""));Directory.CreateDirectory(dir);
            var store=new MacroStore(Path.Combine(dir,"audit-library.xml"));store.Save(Library(16));
            using(var controller=new MacroController(store,output,false))
            {
                var router=Field<BindingRouter>(controller,"router");var engine=Field<MacroEngine>(controller,"engine");
                var down=Stroke(TriggerKind.Keyboard,true,87);var up=Stroke(TriggerKind.Keyboard,false,87);
                var barrier=new ManualResetEvent(false);var ready=new ManualResetEvent(false);var done=new ManualResetEvent(false);Exception failure=null;
                int events=0;long ticks=0;double inputCpuMs=0;
                var thread=new Thread(()=>{
                    try {
                        uint tid=GetCurrentThreadId();var process=Process.GetCurrentProcess();ProcessThread native=null;
                        foreach(ProcessThread pt in process.Threads) if(pt.Id==tid) {native=pt;break;}
                        TimeSpan before=native==null?TimeSpan.Zero:native.TotalProcessorTime;
                        ready.Set();barrier.WaitOne();long t=Stopwatch.GetTimestamp();
                        // Samples stop at fixed capacity. Flag a truncated observation window explicitly.
                        while(!done.WaitOne(0) && events<AuditMetrics.Capacity-2) {router.Handle(down);router.Handle(up);events+=2;}
                        ticks=Stopwatch.GetTimestamp()-t;inputCpuMs=native==null?-1:(native.TotalProcessorTime-before).TotalMilliseconds;
                    } catch(Exception ex) {failure=ex;ready.Set();}
                }){Name="Audit.SimulatedInput",IsBackground=true};thread.SetApartmentState(ApartmentState.STA);
                AuditMetrics.Reset();thread.Start();Check(ready.WaitOne(5000),"input producer ready");if(failure!=null)throw failure;barrier.Set();
                int iterations=operation=="idle"?200:80;long uiBegin=Stopwatch.GetTimestamp();
                for(int i=0;i<iterations;i++)
                {
                    if(running && !engine.IsRunning) engine.Start(controller.Snapshot(),"audit-macro","audit",true,0);
                    switch(operation)
                    {
                        case "save-definitions":controller.SaveDefinitions(controller.Snapshot());break;
                        case "add-binding":controller.ApplyBinding(new MacroBinding{Id="added",MacroId="audit-macro",Trigger=TriggerKind.X1},true);break;
                        case "delete-binding":controller.ApplyBinding(new MacroBinding{Id="added",MacroId="audit-macro",Trigger=TriggerKind.X1},true);controller.RemoveBinding("added");break;
                        case "enable-disable":controller.SetBindingEnabled("b0",(i&1)==0);break;
                        case "replace-stop":router.ReplaceAndStop(controller.Snapshot());break;
                        default:using(var pause=new ManualResetEvent(false))pause.WaitOne(1);break;
                    }
                }
                long uiTicks=Stopwatch.GetTimestamp()-uiBegin;done.Set();Check(thread.Join(5000),"input producer exits");if(failure!=null) throw failure;
                engine.Stop();var timeout=Stopwatch.StartNew();while(engine.IsRunning && timeout.ElapsedMilliseconds<2000) Thread.Yield();Check(!engine.IsRunning,"contention worker stops");
                string scenario="Contention-"+operation+(running?"-running":"");AuditMetrics.Save(root,scenario,0,false);
                resources.Add(new {scenario=scenario,events=events,configuration_operations=iterations,observation_window_truncated=events>=AuditMetrics.Capacity-2,ui_elapsed_ms=AuditMetrics.Ms(uiTicks),input_elapsed_ms=AuditMetrics.Ms(ticks),simulated_input_thread_cpu_ms=inputCpuMs,scope="unpaced stress; controller methods + real isolated MacroStore; not WPF UI or real hook thread"});
                barrier.Dispose();ready.Dispose();done.Dispose();Console.WriteLine("CONTENTION "+scenario+" events="+events);
            }
        }
        private static void Semantics()
        {
            foreach(var mode in new[]{RunMode.Once,RunMode.WhileHeld,RunMode.Toggle}) foreach(bool suppress in new[]{false,true})
            {
                var library=Library(0);library.Bindings.Add(new MacroBinding{Id="m",MacroId="audit-macro",Trigger=TriggerKind.Middle,Mode=mode,SuppressOriginal=suppress});var runner=new Runner();var r=new BindingRouter(runner);r.Configure(library);
                var down=Stroke(TriggerKind.Middle,true,0);down.Modifiers=KeyModifiers.Shift;down.RightButtonDown=true;
                Check(r.Handle(down)==suppress,"down suppression");Check(runner.starts==1,"unrelated Shift/right cannot block");Check(r.Handle(down)==suppress && runner.starts==1,"repeat bookkeeping");
                Check(r.Handle(Stroke(TriggerKind.Right,false,0))==false && runner.IsRunning,"unrelated right up cannot stop");
                Check(r.Handle(Stroke(TriggerKind.Middle,false,0))==suppress,"paired up");Check(runner.IsRunning==(mode!=RunMode.WhileHeld),"WhileHeld stops on own up only");
                if(mode==RunMode.Toggle) {r.Handle(down);Check(!runner.IsRunning,"toggle second press stops");r.Handle(Stroke(TriggerKind.Middle,false,0));}
                r.Handle(down);r.ReplaceAndStop(new MacroLibrary());Check(!runner.IsRunning,"replacement stops");Check(r.Handle(Stroke(TriggerKind.Middle,false,0))==suppress,"replacement preserves suppressed up");
            }
            foreach(var trigger in new[]{TriggerKind.Keyboard,TriggerKind.Left,TriggerKind.Right,TriggerKind.Middle,TriggerKind.X1,TriggerKind.X2,TriggerKind.WheelUp,TriggerKind.WheelDown})
            {
                var library=Library(0);library.Bindings.Add(new MacroBinding{Id="b",MacroId="audit-macro",Trigger=trigger,KeyCode=87,SuppressOriginal=true});var runner=new Runner();var r=new BindingRouter(runner);r.Configure(library);
                Check(r.Handle(Stroke(trigger,true,87)),"all triggers route");Check(runner.starts==1,"all triggers start");if(!MacroValidation.IsWheel(trigger))Check(r.Handle(Stroke(trigger,false,87)),"all trigger ups pair");
            }
            var emergencyRunner=new Runner();var emergency=new BindingRouter(emergencyRunner);Check(emergency.Handle(new InputStroke{Trigger=TriggerKind.Keyboard,Key=123,Down=true,Modifiers=KeyModifiers.Control|KeyModifiers.Shift}),"emergency synchronous suppression");Check(emergencyRunner.stops==1,"emergency stops");
        }
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                if(args.Length<1) throw new ArgumentException("Usage: Audit.exe NEW-result-directory [--middle-only | --hook-capture seconds hz]");root=Path.GetFullPath(args[0]);if(Directory.Exists(root))throw new ArgumentException("Result directory must be new");Directory.CreateDirectory(root);
                var allocationMethod=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);if(allocationMethod!=null)allocated=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),allocationMethod);
                if(args.Length>1 && args[1]=="--hook-capture") Capture(int.Parse(args[2]),int.Parse(args[3]));
                else if(args.Length>1 && args[1]=="--engine-only")EngineCosts();
                else if(args.Length>1 && args[1]=="--middle-only") Callbacks(true);
                else {
                    Semantics();Callbacks(false);RouterAndAllocation();EngineCosts();
                    foreach(string op in new[]{"idle","save-definitions","add-binding","delete-binding","enable-disable","replace-stop"}) ConfigurationContention(op,false);
                    ConfigurationContention("save-definitions",true);ConfigurationContention("enable-disable",true);
                }
                AuditMetrics.Enabled=false;
                File.WriteAllText(Path.Combine(root,"resources.json"),new JavaScriptSerializer().Serialize(new {runtime=Environment.Version.ToString(),x64=IntPtr.Size==8,stopwatch_frequency=Stopwatch.Frequency,logical_cpus=Environment.ProcessorCount,allocated_bytes_api=allocated!=null,checks=checks,resources=resources,limits="No SendInput/hardware/user-config access. Real diagnostic hooks only in --hook-capture; direct mode has no installed hooks. Short CPU ratios may exceed 100% due to OS CPU-time quantization/background activity: do not interpret them as sustained load. GC pause/context switches not measured; no CPU average equals motion PASS."}));
                Console.WriteLine("AUDIT PASS: checks="+checks);return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        private static void Capture(int seconds,int hz)
        {
            if(seconds<1||seconds>300||hz<1||hz>8000)throw new ArgumentException("seconds 1..300, hz 1..8000");
            using(var controller=new MacroController(new MemoryStore{library=Library(0)},output,false))
            using(var hook=new GlobalInputHook(Route(controller),WheelRoute(controller)))
            using(var wait=new ManualResetEvent(false))
            {
                // Empty diagnostic bindings, NullOutput. Not a replacement for the full product A/B.
                AuditMetrics.Reset();hook.Start();Console.WriteLine("HOOK READY: empty diagnostic bindings; no output injection; Ctrl+Shift+F12 remains emergency stop");
                var p=Process.GetCurrentProcess();uint id=Field<uint>(hook,"threadId");ProcessThread input=null;foreach(ProcessThread pt in p.Threads)if(pt.Id==id)input=pt;
                TimeSpan cpu=p.TotalProcessorTime,hookCpu=input==null?TimeSpan.Zero:input.TotalProcessorTime;int gen0=GC.CollectionCount(0),gen1=GC.CollectionCount(1),gen2=GC.CollectionCount(2);long start=Stopwatch.GetTimestamp();
                wait.WaitOne(seconds*1000);p.Refresh();double inputCpu=input==null?-1:(input.TotalProcessorTime-hookCpu).TotalMilliseconds;
                resources.Add(new {scenario="LiveDiagnosticHook",elapsed_ms=AuditMetrics.Ms(Stopwatch.GetTimestamp()-start),process_cpu_ms=(p.TotalProcessorTime-cpu).TotalMilliseconds,input_hooks_thread_cpu_ms=inputCpu,threads=p.Threads.Count,gen0=GC.CollectionCount(0)-gen0,gen1=GC.CollectionCount(1)-gen1,gen2=GC.CollectionCount(2)-gen2});
                hook.Dispose();AuditMetrics.Save(root,"LiveDiagnosticHook",1000.0/hz,true);
            }
        }
    }
}
