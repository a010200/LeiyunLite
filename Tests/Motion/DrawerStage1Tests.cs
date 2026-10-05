using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RazerBatteryTray.Macros;
using Path = System.IO.Path;

namespace RazerBatteryTray.Desktop
{
    // Demo-only WPF integration. No InitializeRuntime, hook installation,
    // real input injection, physical HID access or user configuration writes.
    internal static class DrawerStage1Tests
    {
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        private static ShellWindow window;
        private static MacroPage macro;
        private static string artifacts;
        private static int failed;
        private static readonly List<string> results = new List<string>();
        private static T Get<T>(object value, string field) { return (T)value.GetType().GetField(field, Flags).GetValue(value); }
        private static void Call(object value, string method, params object[] args) { value.GetType().GetMethod(method, Flags).Invoke(value, args); }
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        private static void Pump(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s,e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject { if (root is T) yield return (T)root; for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i))) yield return child; }
        private static void Capture(string name, FrameworkElement visual) { visual=window; if(visual.ActualWidth<=0||visual.ActualHeight<=0)return;var bmp=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var stream=new FileStream(Path.Combine(artifacts,name+".png"),FileMode.CreateNew))encoder.Save(stream); }
        private static void Test(string name, Action action) {
            window.CloseDrawer(); UiMotion.SettleAll(); window.Activate(); Pump(40);
            try {action();results.Add("PASS\t"+name);Console.WriteLine("PASS "+name);} catch(Exception ex){failed++;results.Add("FAIL\t"+name+"\t"+ex.Message);Console.WriteLine("FAIL "+name+": "+ex);}
            finally {window.CloseDrawer();UiMotion.SettleAll();}
        }
        private static void Manage() { window.Navigate(1); var more=Find<Button>(macro).First(b=>b.Content is string && ((string)b.Content).Contains("更多"));more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static void Probe(string name, Action open, bool reduced=false) {
            Ui.ReducedMotion=reduced; UiMotion.SettleAll();open();
            var drawer=Get<Border>(window,"activeDrawer");var handle=Get<Button>(window,"drawerCollapseButton");var host=Get<Grid>(window,"activeDrawerHost");
            Check(drawer!=null && window.DrawerOpen,"Drawer not open");
            bool initiallyLoaded=drawer.IsLoaded, loaded=false, loadedAllowed=false, loadedActive=false;double loadedPosition=double.NaN;
            var rows=new List<string>{"event,elapsed_ms,is_loaded,is_visible,window_active,allowed,spring_active,x,handle_x,opacity,handle_opacity"};var clock=Stopwatch.StartNew();
            Action<string> record=kind=>{var movement=UiMotion.Transform(drawer).Entry;var binding=SpringMotion.Get(drawer,movement,TranslateTransform.XProperty,SpringPreset.Smooth,0);var transform=handle.RenderTransform.Value;
                rows.Add(string.Join(",",kind,clock.ElapsedMilliseconds,drawer.IsLoaded,drawer.IsVisible,window.IsActive,binding.Life.Allowed,binding.Active,movement.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture),transform.OffsetX.ToString("R",System.Globalization.CultureInfo.InvariantCulture),drawer.Opacity.ToString("R",System.Globalization.CultureInfo.InvariantCulture),handle.Opacity.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));};
            RoutedEventHandler onLoaded=(s,e)=>{loaded=true;var binding=SpringMotion.Get(drawer,UiMotion.Transform(drawer).Entry,TranslateTransform.XProperty,SpringPreset.Smooth,0);loadedAllowed=binding.Life.Allowed;loadedActive=binding.Active;loadedPosition=binding.State.Position;record("Loaded");};
            drawer.Loaded+=onLoaded;int frame=0;double max=0,min=0;bool synchronized=true;
            EventHandler rendering=(s,e)=>{var movement=UiMotion.Transform(drawer).Entry;max=Math.Max(max,movement.X);min=Math.Min(min,movement.X);synchronized &= ReferenceEquals(handle.RenderTransform,drawer.RenderTransform)&&Math.Abs(handle.RenderTransform.Value.OffsetX-movement.X)<.00001&&Math.Abs(handle.Opacity-drawer.Opacity)<.00001;
                record("frame");if(frame==1||frame==3||frame==7||frame==13||frame==22)Capture(name+"-frame-"+frame.ToString("D2"),host);frame++;};
            CompositionTarget.Rendering+=rendering;
            try {
                record("returned");Pump(1400);record("settled");
                File.WriteAllLines(Path.Combine(artifacts,name+".csv"),rows);
                File.WriteAllText(Path.Combine(artifacts,name+"-summary.txt"),"returned_IsLoaded="+initiallyLoaded+"; loaded="+loaded+"; allowed="+loadedAllowed+"; active_at_Loaded="+loadedActive+"; state_position_at_Loaded="+loadedPosition+"; max="+max+"; min="+min+"; frames="+frame+"; synchronized="+synchronized);
                Check(loaded||initiallyLoaded,"Loaded not observed");Check(loadedAllowed==!reduced,"Unexpected Lifetime.Allowed");
                if(reduced){Check(!loadedActive&&max==0&&min==0,"Reduced motion animated");}
                else {Check(loadedActive&&loadedPosition==UiMotion.MotionTokens.DrawerEnterOffset,"Spring never started at Loaded: returned_IsLoaded="+initiallyLoaded+", active="+loadedActive+", position="+loadedPosition);Check(max>0&&max<=16&&min<0&&min>=-2,"No real enter/overshoot frames: "+max+" / "+min);}
                Check(synchronized,"Handle out of sync");Check(UiMotion.Transform(drawer).Entry.X==0&&SpringMotion.ActiveCount==0&&!SpringMotion.RenderingSubscribed,"Did not settle/detach");
            } finally {drawer.Loaded-=onLoaded;CompositionTarget.Rendering-=rendering;}
        }
        [STAThread] private static int Main(string[] args) {
            artifacts=Path.GetFullPath(args[0]);Directory.CreateDirectory(artifacts);bool baseline=args.Length>1&&args[1]=="baseline", preview=args.Length>1&&args[1]=="preview";
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};DesktopApp.LoadTheme(app);Ui.English=false;Ui.ReducedMotion=true;
            window=new ShellWindow(true){ShowInTaskbar=false};window.Show();window.Activate();Pump(100);
            window.Draft.Macros.Add(new MacroDefinition{Id="stage1-safe",Name="Stage 1 示例",Steps=new List<MacroStep>{new MacroStep{Kind=ActionKind.Delay,Number=100}}});
            Check(window.SaveMacros(),"Demo seed save");macro=Get<MacroPage>(window,"macroPage");macro.ReloadLibrary("stage1-safe");window.Navigate(1);Pump(80);Ui.ReducedMotion=false;
            try {
                Check(SystemParameters.ClientAreaAnimation,"Windows ClientAreaAnimation disabled; real-motion test cannot run");
                Test("Manage Macro Drawer",()=>Probe("manage",Manage));
                if(!baseline&&!preview){
                    Test("Advanced Binding Drawer",()=>Probe("advanced",()=>Call(macro,"OpenAdvancedBindings")));
                    Test("Mouse Binding Drawer",()=>Probe("mouse",()=>Call(macro,"OpenMouseBinding",TriggerKind.X1,null)));
                    Test("Rotation Calibration Drawer (demo only)",()=>{window.Navigate(0);Pump(60);UiMotion.SettleAll();Probe("rotation",()=>Call(Get<DevicePage>(window,"devicePage"),"OpenCalibration"));});
                    Test("Reduced Motion",()=>Probe("reduced",()=>window.OpenDrawer("Reduced",Ui.Text("Instant")),true));
                    Test("Close before Loaded; stale replacement handler cannot animate old Drawer",()=>{
                        Ui.ReducedMotion=false;window.OpenDrawer("Close pending",Ui.Text("Pending"));var old=Get<Border>(window,"activeDrawer");var originalTransform=old.RenderTransform;window.CloseDrawer();old.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));Pump(60);
                        Check(!window.DrawerOpen&&ReferenceEquals(originalTransform,old.RenderTransform)&&!old.HasAnimatedProperties&&SpringMotion.ActiveCount==0,"Closed Drawer animated late");
                        window.OpenDrawer("First pending",Ui.Text("First"));old=Get<Border>(window,"activeDrawer");originalTransform=old.RenderTransform;
                        window.OpenDrawer("Replacement",Ui.Text("Replacement"));var replacement=Get<Border>(window,"activeDrawer");old.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                        Check(ReferenceEquals(originalTransform,old.RenderTransform)&&ReferenceEquals(replacement,Get<Border>(window,"activeDrawer")),"Old handler affected replacement");Pump(70);
                        Check(SpringMotion.Get(replacement,UiMotion.Transform(replacement).Entry,TranslateTransform.XProperty,SpringPreset.Smooth,0).Active,"Replacement not animated");window.CloseDrawer();Pump(60);Check(SpringMotion.ActiveCount==0&&!SpringMotion.RenderingSubscribed,"Close leaked rendering");
                    });
                    Test("Handle / Esc / outside mask close remain unchanged",()=>{
                        Ui.ReducedMotion=false;window.OpenDrawer("Handle",Ui.Text("Content"));Pump(70);Get<Button>(window,"drawerCollapseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!window.DrawerOpen,"Handle close");
                        window.OpenDrawer("Escape",Ui.Text("Content"));Pump(70);window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=UIElement.KeyDownEvent});Check(!window.DrawerOpen,"Escape close");
                        window.OpenDrawer("Mask",Ui.Text("Content"));Pump(70);var mask=Get<Grid>(window,"overlay");mask.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});Check(!window.DrawerOpen,"Mask close");Check(SpringMotion.ActiveCount==0&&!SpringMotion.RenderingSubscribed,"Close detach");
                    });
                }
            } finally {window.ClosePreview();UiMotion.SettleAll();app.Shutdown();File.WriteAllLines(Path.Combine(artifacts,"results.tsv"),results);}
            Console.WriteLine("DRAWER STAGE 1: "+(results.Count-failed)+" PASS / "+failed+" FAIL");return failed==0?0:1;
        }
    }
}
