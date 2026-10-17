using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RazerBatteryTray.Macros;
using MouseAction=RazerBatteryTray.Macros.MouseAction;

namespace RazerBatteryTray.Desktop
{
    // UI-only: Demo shell, no normal Main, HID, recorder, tray registration or global input.
    internal static class FinesseStageBTests
    {
        private static string output;
        private static int passed, failed;
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static T Field<T>(object o, string name) { var f=o.GetType().GetField(name,Flags); return f==null ? default(T) : (T)f.GetValue(o); }
        private static void Set(object o,string name,object v) { o.GetType().GetField(name,Flags).SetValue(o,v); }
        private static object Call(object o,string name,params object[] args) { return o.GetType().GetMethod(name,Flags).Invoke(o,args); }
        private static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        private static void Pump(int ms=30) { var clock=Stopwatch.StartNew(); do { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); Thread.Sleep(2); } while(clock.ElapsedMilliseconds<ms); }
        private static void Test(string name,Action action) { try { action(); passed++; File.AppendAllText(Path.Combine(output,"results.tsv"),"PASS\t"+name+"\n"); } catch(Exception ex) { failed++; File.AppendAllText(Path.Combine(output,"results.tsv"),"FAIL\t"+name+"\t"+ex.GetBaseException().Message+"\n"); } finally { Ui.ReducedMotion=true; } }
        private static void Capture(FrameworkElement visual,string name) { visual.UpdateLayout(); var bmp=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32); bmp.Render(visual); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using(var s=File.Create(Path.Combine(output,name+".png"))) png.Save(s); }
        private static void Frames(ShellWindow shell,string name,Action trigger) { var clock=Stopwatch.StartNew(); trigger(); var times=new List<string>{"Frame\tElapsedMs"}; for(int i=0;i<12;i++){ Capture((FrameworkElement)shell.Content,name+"-"+i.ToString("D2")); times.Add(i+"\t"+clock.ElapsedMilliseconds); Pump(18); } File.WriteAllLines(Path.Combine(output,name+"-timestamps.tsv"),times); }
        private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static void CloseNow(ShellWindow w) { Ui.ReducedMotion=true; w.CloseDrawer(); Pump(); }
        private static void AssertClosed(ShellWindow w) { Check(!w.DrawerOpen && Field<Grid>(w,"overlay").Children.Count==0 && Field<Border>(w,"activeDrawer")==null,"Logical drawer close delayed"); }
        private sealed class MemoryStore : IMacroStore { public string FilePath { get { return "memory-only"; } } public MacroLibrary Load(){return new MacroLibrary();} public void Save(MacroLibrary value){} }
        private sealed class NoOutput : IMacroOutput
        {
            internal int Calls;
            private void Reject(){Calls++;throw new InvalidOperationException("Recording UI fixture must not emit input");}
            public void Key(int k,bool d){Reject();}public void MouseButton(MouseAction b,bool d){Reject();}public void Wheel(int n){Reject();}public void Text(string t,CancellationToken c){Reject();}public void Launch(string t,string a,bool c){Reject();}
        }
        [STAThread] private static int Main(string[] args)
        {
            output=args[0]; Directory.CreateDirectory(output); var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown}; DesktopApp.LoadTheme(app);
            var shell=new ShellWindow(true); shell.Show(); shell.InitializeRuntime().GetAwaiter().GetResult(); Pump(160);
            try {
                Check(shell.Macros==null,"Demo must not create macros/input services");
                Test("B01 close immediately unloads live controls, returns focus and uses inert snapshot",()=>{
                    shell.Navigate(0); var focus=Field<Button[]>(shell,"navigation")[0]; focus.Focus();
                    var input=Ui.Input("draft unchanged"); int unloads=0; input.Unloaded+=(s,e)=>unloads++;
                    shell.OpenDrawer("Close safety",input); Pump(60); Ui.ReducedMotion=false; shell.CloseDrawer(); AssertClosed(shell);
                    var initialLayer=Field<Grid>(shell,"drawerExitLayer"); double initialOpacity=initialLayer==null?0:initialLayer.Opacity; Pump(10);
                    Check(unloads==1 && input.Text=="draft unchanged" && ReferenceEquals(Keyboard.FocusedElement,focus),"Live tree/focus not closed synchronously");
                    var layer=Field<Grid>(shell,"drawerExitLayer"); Check(layer!=null && layer.Visibility==Visibility.Visible && !layer.IsHitTestVisible && !layer.Focusable,"Missing inert exit snapshot");
                    Check(initialOpacity>.95,"Exit flashes invisible before its first render");
                    Check(layer.Children.Count==1 && layer.Children[0] is Image,"Exit retains live controls");
                    var bitmap=(BitmapSource)((Image)layer.Children[0]).Source; var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                    Check(bitmap.IsFrozen && Enumerable.Range(0,pixels.Length/4).Count(i=>pixels[i*4+3]>200)>pixels.Length/32,"Snapshot content clipped/empty");
                    Pump(220); Check(layer.Children.Count==0 && layer.Visibility==Visibility.Collapsed,"Exit exceeds bounded interval");
                });
                Test("B02 close/reopen rejects stale completion; double close keeps new focus",()=>{
                    shell.OpenDrawer("Old",Ui.Input()); Pump(); Ui.ReducedMotion=false; shell.CloseDrawer(); var newer=Ui.Input("new"); shell.OpenDrawer("New",newer); Pump(230);
                    Check(shell.DrawerOpen && Field<Border>(shell,"activeDrawer")!=null && newer.IsLoaded,"Old exit removed new drawer");
                    shell.CloseDrawer(); shell.CloseDrawer(); AssertClosed(shell); Pump(220); Check(Field<Grid>(shell,"drawerExitLayer").Children.Count==0,"Double close left snapshot");
                });
                Test("B03 open Combo popup unloads immediately; Escape and outside retain existing routing",()=>{
                    var combo=Ui.Combo(new[]{"One","Two"}); shell.OpenDrawer("Popup",combo); Pump(); combo.IsDropDownOpen=true; Pump(); Ui.ReducedMotion=false; shell.CloseDrawer(); AssertClosed(shell); Pump();
                    Check(!combo.IsLoaded && !combo.IsDropDownOpen,"Unloaded popup remains open"); Pump(220);
                    shell.OpenDrawer("Escape",Ui.Input()); Pump(); var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(shell),Environment.TickCount,Key.Escape){RoutedEvent=UIElement.KeyDownEvent}; shell.RaiseEvent(e); AssertClosed(shell);
                });
                Test("B04 reduced motion, hide and minimize cancel visual exit immediately",()=>{
                    foreach(int mode in new[]{0,1,2}) { shell.OpenDrawer("Lifecycle",Ui.Input()); Pump(); Ui.ReducedMotion=false; shell.CloseDrawer(); if(mode==0)Ui.ReducedMotion=true; else if(mode==1)shell.Hide(); else shell.WindowState=WindowState.Minimized; Pump(); var layer=Field<Grid>(shell,"drawerExitLayer"); Check(layer!=null && layer.Children.Count==0,"Lifecycle leaves snapshot"); shell.WindowState=WindowState.Normal; shell.Show(); shell.Activate(); }
                });
                Test("B05 stale Fade Completed cannot act after replacement or Stop",()=>{
                    var element=new Border{Width=20,Height=20}; int old=0,current=0; Ui.ReducedMotion=false;
                    UiMotion.Fade(element,0,170,1,()=>old++); var fades=(IList)typeof(UiMotion).GetField("fades",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null); var first=fades[fades.Count-1];
                    UiMotion.Fade(element,0,170,1,()=>current++); typeof(UiMotion).GetMethod("Complete",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{first});
                    Check(old==0,"Superseded completion executed"); Ui.ReducedMotion=true; Check(current==1,"Current completion missing/repeated");
                    Ui.ReducedMotion=false; UiMotion.Fade(element,0,170,1,()=>old++); var stopped=fades[fades.Count-1]; UiMotion.Stop(element); typeof(UiMotion).GetMethod("Complete",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{stopped}); Check(old==0,"Stopped completion executed");
                });
                Test("B06 Snackbar keeps full reading interval, fades and cannot clear replacement",()=>{
                    var toast=Field<UiSnackbar>(shell,"toast"); Ui.ReducedMotion=false; shell.Notice("First error, retained for full interval"); Pump(2900); Check(toast.Visibility==Visibility.Visible,"Message hidden before three seconds");
                    Call(toast,"Dismiss"); Check(toast.Visibility==Visibility.Visible && Field<TextBlock>(toast,"text").Text.StartsWith("First"),"Dismiss clears before fade");
                    shell.Notice("Replacement error"); Pump(230); Check(toast.Visibility==Visibility.Visible && Field<TextBlock>(toast,"text").Text=="Replacement error","Old completion cleared replacement");
                    Call(toast,"Dismiss"); Pump(220); Check(toast.Visibility==Visibility.Collapsed && Field<TextBlock>(toast,"text").Text=="","Snackbar failed to settle");
                    shell.Notice("Clear immediately"); toast.Clear(); Check(toast.Visibility==Visibility.Collapsed,"Explicit lifecycle clear delayed");
                });
                Test("B07 InfoTip delay, reentry, disabled and unload never leave popup",()=>{
                    var tip=Ui.InfoTip("Information","Explanation remains readable"); shell.OpenDrawer("Tip",tip); Pump(); var session=(UiFeedback.TipSession)tip.Tag; var popup=(ToolTip)tip.ToolTip;
                    Check(ToolTipService.GetInitialShowDelay(tip)==350,"Hover delay changed"); Ui.ReducedMotion=false; session.Open(); Pump(); session.Close(); session.Open(); Pump(220); Check(popup.IsOpen,"Stale close ended reentered tip");
                    tip.IsEnabled=false; Check(!popup.IsOpen,"Disabled anchor leaves popup open"); tip.IsEnabled=true; session.Open(); CloseNow(shell); Check(!popup.IsOpen,"Unload leaves popup open");
                });
                Test("B08 update progress reserves layout and truthful busy/error state",()=>{
                    shell.Navigate(3); Pump(); var page=Field<UpdatePage>(shell,"updatePage"); var session=shell.Updates; var progress=Field<ProgressBar>(page,"progress");
                    session.Progress=0; session.Status="Ready"; session.Notify(); Pump(); Check(progress.Visibility==Visibility.Hidden,"Inactive progress does not reserve its slot"); var cardY=Field<TextBlock>(page,"status").TranslatePoint(new Point(),page).Y;
                    Set(session,"operation",new CancellationTokenSource()); session.Status="Checking (isolated state)"; session.Notify(); Pump(); Check(progress.IsIndeterminate && progress.Visibility==Visibility.Visible && !Field<Button>(page,"check").IsEnabled,"Busy UI untruthful");
                    Check(Math.Abs(cardY-Field<TextBlock>(page,"status").TranslatePoint(new Point(),page).Y)<.1,"Progress shifts status/card layout");
                    session.Progress=47; session.Notify(); Check(!progress.IsIndeterminate && progress.Value==47,"Progress not session-driven");
                    var token=Field<CancellationTokenSource>(session,"operation"); Set(session,"operation",null); token.Dispose(); session.Progress=0; session.Status="Verification failed (isolated state)"; session.Notify(); Check(Field<TextBlock>(page,"status").Text==session.Status && !Field<Button>(page,"install").IsEnabled,"Failure appears installable");
                });
                Test("B09 100 page navigations keep logical visibility and settle clocks",()=>{
                    Ui.ReducedMotion=false; for(int i=0;i<100;i++){shell.Navigate(i%4);Pump(2);} Pump(1400); Check(SpringMotion.ActiveCount==0 && !SpringMotion.RenderingSubscribed,"Hidden navigation retains render subscription");
                    var views=Field<FrameworkElement[]>(shell,"views"); Check(views.Count(v=>v.Visibility==Visibility.Visible)==1,"Navigation visibility changed");
                });
                Test("B10 100 drawer interruptions leave no old live controls or orphan visual",()=>{
                    int unloads=0; var times=new List<double>(); Ui.ReducedMotion=false; for(int i=0;i<100;i++){var input=Ui.Input();input.Unloaded+=(s,e)=>unloads++;shell.OpenDrawer("Repeat",input);Pump(2);var clock=Stopwatch.StartNew();shell.CloseDrawer();times.Add(clock.Elapsed.TotalMilliseconds);AssertClosed(shell);} Pump(300);
                    Check(unloads==100 && Field<Grid>(shell,"drawerExitLayer").Children.Count==0 && SpringMotion.ActiveCount==0,"Repeated drawer lifecycle leak");
                    times.Sort();File.WriteAllText(Path.Combine(output,"drawer-close-latency.txt"),"Close incl snapshot n=100; p50="+times[49]+"ms; p95="+times[94]+"ms; max="+times[99]+"ms\n");
                });
                Test("B11 four-page galleries and real Drawer/Snackbar/recording preparation frames",()=>{
                    shell.Updates.Progress=0;shell.Updates.Status=Ui.T("准备就绪","Ready");shell.Updates.Notify();
                    foreach(string theme in new[]{"classic","fluent"}) { Ui.ApplyTheme(theme); foreach(int width in new[]{1180,960,760}) { shell.Width=width; CloseNow(shell); Ui.ReducedMotion=true; for(int page=0;page<4;page++){shell.Navigate(page);Pump(60);Capture((FrameworkElement)shell.Content,theme+"-"+width+"-page"+page);} }
                        shell.Width=960; shell.Navigate(1); Call(Field<MacroPage>(shell,"macroPage"),"OpenRecording"); Pump(70); Capture((FrameworkElement)shell.Content,theme+"-recording-preparation"); Ui.ReducedMotion=false; Frames(shell,theme+"-drawer-exit",()=>shell.CloseDrawer());
                        shell.OpenDrawer("Bounded exit / reopening",Ui.Stack(Ui.Text("Old controls unload immediately"),Ui.Input("Read-only demonstration")));Pump(100);Frames(shell,theme+"-drawer-reopen",()=>{shell.CloseDrawer();shell.OpenDrawer("New drawer",Ui.Input("New draft remains editable"));});CloseNow(shell);
                        Ui.ReducedMotion=false;shell.Notice("Error stays readable before exit / 失败信息");Pump(200);Frames(shell,theme+"-snackbar-exit",()=>{var t=Field<UiSnackbar>(shell,"toast");if(t.GetType().GetMethod("Dismiss",Flags)==null)t.Clear();else Call(t,"Dismiss");});
                    }
                });
                Test("B12 deadline generation cannot clear new drawer and timer stops at terminal state",()=>{
                    CloseNow(shell);shell.OpenDrawer("Deadline",Ui.Input());Pump();Ui.ReducedMotion=false;shell.CloseDrawer();
                    var old=Field<EventHandler>(shell,"drawerExitTimeout");Check(old!=null,"Missing bounded exit deadline");
                    shell.OpenDrawer("New owner",Ui.Input());old(null,EventArgs.Empty);Check(shell.DrawerOpen,"Stale deadline closed new drawer");
                    shell.CloseDrawer();Ui.ReducedMotion=true;Check(!Field<DispatcherTimer>(shell,"drawerExitDeadline").IsEnabled && Field<Grid>(shell,"drawerExitLayer").Children.Count==0,"Reduced motion leaves deadline active");
                });
                Test("B13 InfoTip owner deactivation/hide closes popup without foreign input",()=>{
                    shell.Show();shell.Activate();var tip=Ui.InfoTip("Owner lifecycle","No orphan popup");shell.OpenDrawer("Owner",tip);Pump();var session=(UiFeedback.TipSession)tip.Tag;var popup=(ToolTip)tip.ToolTip;
                    Ui.ReducedMotion=false;session.Open();Pump();var other=new Window{Width=220,Height=100,Owner=shell,Content=Ui.Text("Isolated focus target")};
                    try{other.Show();other.Activate();Pump(160);Check(!popup.IsOpen,"Deactivated owner leaves InfoTip open");}finally{other.Close();}
                    shell.Activate();session.Open();shell.Hide();Pump();Check(!popup.IsOpen,"Hidden owner leaves InfoTip open");shell.Show();CloseNow(shell);
                });
                Test("B14 real UI countdown uses existing Stopwatch with hookless recorder and zero output",()=>{
                    foreach(string theme in new[]{"classic","fluent"}) {
                        CloseNow(shell);Ui.ApplyTheme(theme);shell.Navigate(1);shell.Width=960;Pump();var page=Field<MacroPage>(shell,"macroPage");var noOutput=new NoOutput();var controller=new MacroController(new MemoryStore(),noOutput,false);
                        Set(shell,"Macros",controller);
                        try {
                            Set(page,"areaRecording",true);Ui.ReducedMotion=false;var clock=Stopwatch.StartNew();Call(page,"BeginRecording",false,RecordingDelay.Actual,0);
                            Check(controller.Recorder==null && Field<bool>(page,"countingDown"),"Recorder started before countdown");Capture((FrameworkElement)shell.Content,theme+"-hookless-countdown-3");
                            Pump(1050);Check(controller.Recorder==null,"Recorder started before three seconds");Capture((FrameworkElement)shell.Content,theme+"-hookless-countdown-2");Pump(1000);Check(controller.Recorder==null,"Recorder started before three seconds");Capture((FrameworkElement)shell.Content,theme+"-hookless-countdown-1");
                            Pump(1100);Check(clock.ElapsedMilliseconds>=3000 && controller.Recorder!=null && !Field<bool>(page,"countingDown"),"Countdown did not start existing recorder");
                            Check(Field<Button>(page,"stopButton").IsEnabled && Field<Border>(page,"recordingStatus").IsVisible && !controller.KeyboardHookInstalled && !controller.MouseHookInstalled,"Recording controls/hooks incorrect");Capture((FrameworkElement)shell.Content,theme+"-hookless-recording");
                            page.EndRecording(true);Check(!page.IsRecording && !controller.RecordingSession && noOutput.Calls==0,"Cancellation changed input/output state");Capture((FrameworkElement)shell.Content,theme+"-hookless-recording-cancelled");
                        } finally {page.EndRecording(true);Set(shell,"Macros",null);controller.Dispose();}
                    }
                });
                Test("B15 two-theme update Ready/Checking/47-percent/Error render only injected session facts",()=>{
                    foreach(string theme in new[]{"classic","fluent"}) {Ui.ApplyTheme(theme);shell.Navigate(3);var session=shell.Updates;
                        session.Progress=0;session.Status="Ready / 隔离状态样本";session.Notify();Pump();Capture((FrameworkElement)shell.Content,theme+"-update-ready");var token=new CancellationTokenSource();Set(session,"operation",token);
                        try{session.Status="Checking / 隔离状态样本";session.Notify();Pump();Capture((FrameworkElement)shell.Content,theme+"-update-checking");session.Progress=47;session.Status="Downloading 47% / 隔离状态样本";session.Notify();Pump();Capture((FrameworkElement)shell.Content,theme+"-update-progress47");}
                        finally{Set(session,"operation",null);token.Dispose();}
                        session.Progress=0;session.Status="Verification failed / 隔离错误样本";session.Notify();Pump();Capture((FrameworkElement)shell.Content,theme+"-update-error");
                    }
                });
            } finally { Ui.ReducedMotion=true; shell.ClosePreview(); app.Shutdown(); }
            File.WriteAllText(Path.Combine(output,"metadata.txt"),"Product templates loaded. Demo only; 96 DPI WPF client renders. Update busy/progress/error explicitly isolated state injection, no network. B14 uses real page Stopwatch/countdown + memory store + MacroController(installHooks:false) + rejecting output; zero native hooks/output/physical input. Other recording samples are preparation only. Product version="+AppVersion.Number+"; Stage C local verification.\n");
            Console.WriteLine(passed+" passed, "+failed+" failed"); return failed==0?0:1;
        }
    }
}
