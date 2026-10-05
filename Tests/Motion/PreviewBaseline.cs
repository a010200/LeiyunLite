using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

// The assembly is supplied explicitly. Baseline is the protected pre-Motion
// local candidate, never a user's running install. Only public WPF + reflection;
// no app entry point, hooks, HID initialization, real input or config writes.
internal static class PreviewBaseline
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type ui;
    private static string output;
    private static void Pump(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s,e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    private static object UiCall(string name, params object[] args) { return ui.GetMethod(name,Flags).Invoke(null,args); }
    private static void Capture(string name, FrameworkElement visual) {
        visual.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using(var file=new FileStream(Path.Combine(output,name+".png"),FileMode.CreateNew))png.Save(file);
    }
    private static void Press(Button button,bool down) { button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){ RoutedEvent=down?UIElement.PreviewMouseLeftButtonDownEvent:UIElement.PreviewMouseLeftButtonUpEvent }); }
    private static void Series(string name,Action begin,Action middle,FrameworkElement visual) {
        var frames=new List<string>{"frame,elapsed_ms"}; var clock=Stopwatch.StartNew(); begin();
        for(int i=0;i<18;i++){ if(i==5 && middle!=null)middle(); Pump(35); Capture(name+"-"+i.ToString("D2"),visual); frames.Add(i+","+clock.ElapsedMilliseconds); }
        File.WriteAllLines(Path.Combine(output,name+".csv"),frames);
    }
    [STAThread] private static int Main(string[] args) {
        output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));ui=assembly.GetType("RazerBatteryTray.Desktop.Ui",true);
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};assembly.GetType("RazerBatteryTray.Desktop.DesktopApp").GetMethod("LoadTheme",Flags).Invoke(null,new object[]{app});
        var panel=new StackPanel{Margin=new Thickness(25)};
        var primary=(Button)UiCall("PrimaryButton","保存并应用 · Primary",new Action(()=>{}));
        var ordinary=(Button)UiCall("Button","普通按钮 · Space / Enter",new Action(()=>{}),false);
        var toggle=(CheckBox)UiCall("Toggle","Toggle / 连续切换",false,new Action<bool>(b=>{}));
        var combo=(ComboBox)UiCall("Combo",new string[]{"选项一","选项二","选项三"},0);
        var entry=(Border)UiCall("Card",UiCall("Text","Page / Drawer entrance · 阻尼弹簧",19.0,null));entry.Height=90;
        foreach(var control in new UIElement[]{primary,ordinary,toggle,combo,entry})panel.Children.Add(control);
        var window=new Window{Title="雷云 Lite 动效基线 · 隔离预览",Width=530,Height=450,Content=panel,Background=(Brush)ui.GetField("Background",Flags).GetValue(null),ShowInTaskbar=false};window.Show();window.Activate();Pump(100);
        try {
            var cpu=Process.GetCurrentProcess().TotalProcessorTime;int[] gc={GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};var clock=Stopwatch.StartNew();Pump(10000);
            File.WriteAllText(Path.Combine(output,"performance.txt"),"duration_ms="+clock.ElapsedMilliseconds+"; cpu_ms="+(Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalMilliseconds+"; gc="+(GC.CollectionCount(0)-gc[0])+","+(GC.CollectionCount(1)-gc[1])+","+(GC.CollectionCount(2)-gc[2]));
            foreach(var theme in new[]{"classic","fluent"}) {
                UiCall("ApplyTheme",theme);ui.GetProperty("ReducedMotion",Flags).SetValue(null,false,null);toggle.IsChecked=false;Pump(400);
                Series(theme+"-button",()=>Press(primary,true),()=>Press(primary,false),panel);
                Series(theme+"-toggle",()=>toggle.IsChecked=true,()=>toggle.IsChecked=false,panel);
                Series(theme+"-drawer",()=>UiCall("Enter",entry,true),null,panel);
                Series(theme+"-page",()=>UiCall("Enter",entry,false),null,panel);
                combo.IsDropDownOpen=true;Pump(45);var popup=(Popup)combo.Template.FindName("PART_Popup",combo);combo.IsDropDownOpen=false;Pump(40);
                Series(theme+"-dropdown",()=>combo.IsDropDownOpen=true,null,(FrameworkElement)popup.Child);combo.IsDropDownOpen=false;Pump(100);
            }
            ui.GetProperty("ReducedMotion",Flags).SetValue(null,true,null);UiCall("Enter",entry,true);Press(primary,true);toggle.IsChecked=true;Capture("reduced-motion",panel);
            // Actual Drawer handle before/after uses the same shared entrance.
            var shellType=assembly.GetType("RazerBatteryTray.Desktop.ShellWindow");var shell=(Window)Activator.CreateInstance(shellType,Flags,null,new object[]{true,false},null);shell.ShowInTaskbar=false;shell.Show();shell.Activate();Pump(60);
            try {
                ui.GetProperty("ReducedMotion",Flags).SetValue(null,false,null);
                shellType.GetMethod("OpenDrawer",Flags).Invoke(shell,new object[]{"Drawer / Handle",new TextBlock{Text="隔离动效预览"},null});Pump(50);
                var handle=(Button)shellType.GetField("drawerCollapseButton",Flags).GetValue(shell);var host=(FrameworkElement)shellType.GetField("activeDrawerHost",Flags).GetValue(shell);
                var magneticType=assembly.GetType("RazerBatteryTray.Desktop.MagneticMotion");object magnet=magneticType==null?null:magneticType.GetMethod("For",Flags).Invoke(null,new object[]{handle});
                Series("drawer-handle",()=>{ if(magnet!=null)magneticType.GetMethod("UpdateTarget",Flags).Invoke(magnet,new object[]{new Point(handle.ActualWidth,handle.ActualHeight/2)});Press(handle,true); },()=>{Press(handle,false);if(magnet!=null)magneticType.GetMethod("Leave",Flags).Invoke(magnet,null);},host);
            } finally {shellType.GetMethod("ClosePreview",Flags).Invoke(shell,null);}
            Console.WriteLine("Preview baseline: "+assembly.Location+"; continuous WPF frames saved");return 0;
        } finally {window.Close();app.Shutdown();}
    }
}
