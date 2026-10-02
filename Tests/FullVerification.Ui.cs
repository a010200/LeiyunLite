// Isolated demo-only UI tests. Never initialize the non-demo runtime.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RazerBatteryTray;
using RazerBatteryTray.Desktop;

internal static partial class FullVerification
{
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flag);
    static Application App;
    static void SetupUi() { App=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};DesktopApp.LoadTheme(App); }
    static void Pump(int milliseconds)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(Math.Max(1,milliseconds))};
        timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    static ShellWindow Demo()
    {
        var w=new ShellWindow(true){ShowActivated=false,ShowInTaskbar=false};new WindowInteropHelper(w).EnsureHandle();
        w.InitializeRuntime().GetAwaiter().GetResult();w.Measure(new Size(1180,840));w.Arrange(new Rect(0,0,1180,840));w.UpdateLayout();return w;
    }
    static void Screenshot(ShellWindow w,string name)
    {
        // Render content explicitly: a never-shown Window is not a screenshot surface.
        var content=(FrameworkElement)w.Content;content.Measure(new Size(1180,840));content.Arrange(new Rect(0,0,1180,840));content.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1180,840,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var pixels=new byte[1180*840*4];bitmap.CopyPixels(pixels,1180*4,0);Check(pixels.Any(b=>b!=0),"Offscreen screenshot was empty");
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(Root,name)))png.Save(f);
    }
    static int UiStress()
    {
        SetupUi();var w=Demo();int dispatcherErrors=0;
        App.DispatcherUnhandledException+=(s,e)=>{dispatcherErrors++;Console.WriteLine("DISPATCHER ERROR "+e.Exception);e.Handled=true;};
        Test("Demo-runtime-safe-initialization","Demo",()=>{Check(w.Macros==null,"Demo macro controller active");Check(Field(w,"tray")==null&&Field(w,"monitor")==null,"Demo live service active");Check(w.Device!=null,"Demo device facade missing");});
        Test("UI-page-switch-1000","UI",()=>{for(int i=0;i<1000;i++){int page=Random.Next(4);w.Navigate(page);Pump(1);Check((int)Field(w,"currentPage")==page,"Wrong page");var views=(FrameworkElement[])Field(w,"views");Check(views.Count(v=>v.Visibility==Visibility.Visible)==1,"Overlapping pages");}Check(dispatcherErrors==0,"Dispatcher error");});
        Test("UI-theme-language-rebuild-100","UI",()=>{for(int i=0;i<100;i++){w.Preferences.Theme=i%2==0?"light":"dark";w.Preferences.Language=i%2==0?"en":"zh";w.Preferences.ReducedMotion=i%2==0;Call(w,"ApplyLanguage");Ui.ApplyTheme(w.Preferences.Theme);w.RebuildPages(i%4);Pump(2);Check(w.Reading.Dpi==800&&w.Reading.ProductId==0x00DF,"Demo reading lost");Check(w.Macros==null&&Field(w,"monitor")==null,"Demo isolation lost");}Check(dispatcherErrors==0,"Dispatcher error");});
        Screenshot(w,"demo-render.png");
        var csv=Path.Combine(Root,"resources.csv");File.WriteAllText(csv,"minute,working_set,private_bytes,handles,threads,cpu_seconds,gdi,user\r\n");
        var watch=Stopwatch.StartNew();int minute=-1,operations=0;
        while(watch.Elapsed.TotalMinutes<30)
        {
            w.Navigate(operations%4);if(operations%20==0){w.Preferences.Theme=operations%40==0?"dark":"light";Ui.ApplyTheme(w.Preferences.Theme);w.RebuildPages(operations%4);}operations++;Pump(200);
            int now=(int)watch.Elapsed.TotalMinutes;if(now!=minute){minute=now;using(var p=Process.GetCurrentProcess()){p.Refresh();string row=minute+","+p.WorkingSet64+","+p.PrivateMemorySize64+","+p.HandleCount+","+p.Threads.Count+","+p.TotalProcessorTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+GetGuiResources(p.Handle,0)+","+GetGuiResources(p.Handle,1);File.AppendAllText(csv,row+"\r\n");Console.WriteLine("STABILITY "+row);Console.Out.Flush();}}
        }
        using(var p=Process.GetCurrentProcess()){p.Refresh();File.AppendAllText(csv,"30,"+p.WorkingSet64+","+p.PrivateMemorySize64+","+p.HandleCount+","+p.Threads.Count+","+p.TotalProcessorTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+GetGuiResources(p.Handle,0)+","+GetGuiResources(p.Handle,1)+"\r\n");}
        Test("UI-stability-30min-no-dispatcher-error","Stress",()=>{Check(watch.Elapsed.TotalMinutes>=30&&dispatcherErrors==0,"Stability did not complete cleanly");});
        Console.WriteLine("STABILITY OPERATIONS "+operations);w.ClosePreview();App.Shutdown();File.WriteAllLines(Path.Combine(Root,"results.tsv"),Rows);Console.WriteLine("RESULT: "+Pass+" passed, "+Fail+" failed");return Fail==0?0:1;
    }
    static int ColdStart()
    {
        SetupUi();Test("Demo-cold-construction-close-100","Lifecycle",()=>{for(int i=0;i<100;i++){var w=Demo();Check(w.Macros==null&&Field(w,"tray")==null&&Field(w,"monitor")==null,"Demo service active");w.ClosePreview();Pump(1);Check((bool)Field(w,"disposed"),"Window did not dispose");} });
        App.Shutdown();File.WriteAllLines(Path.Combine(Root,"results.tsv"),Rows);Console.WriteLine("RESULT: "+Pass+" passed, "+Fail+" failed");return Fail==0?0:1;
    }
    static int Hardware()
    {
        int count=0;new HidTransport().Visit(d=>{var identity=d as IHidDescriptor;if(identity!=null){var x=identity.Descriptor;string key;using(var sha=System.Security.Cryptography.SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(x.InstanceKey))).Replace("-","").Substring(0,16);Console.WriteLine("HW-0 VID="+x.VendorId.ToString("X4")+" PID="+x.ProductId.ToString("X4")+" version="+x.Version.ToString("X4")+" feature="+x.ReportLength+" usage="+x.UsagePage.ToString("X4")+":"+x.Usage+" instance-sha256-prefix="+key+" product="+x.ProductString);count++;}return false;});
        Console.WriteLine("HW-0 interfaces="+count+"; protocol exchanges=0; hardware writes=0; HW-1 blocked while daily client is running");return 0;
    }
}
