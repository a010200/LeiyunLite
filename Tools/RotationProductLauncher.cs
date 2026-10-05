using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

// Device-page regression host; NOT a normal-entry-point acceptance test.
// Construct without startup side effects, then enable the candidate's real
// device runtime. Registry sync, input Hooks and automatic updates are omitted.
// No fake Reading, input injection, automatic SET or preference/macro saves by
// the host. The normal device Probe may still refresh its telemetry cache.
internal static class RotationProductLauncher
{
    private static StreamWriter log;
    private static string last;
    private static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private static void Write(string text){log.WriteLine("timestamp="+DateTime.UtcNow.ToString("o")+" "+text);}
    private static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
    private static void Observe(object shell)
    {
        object reading=shell.GetType().GetField("Reading",Fields).GetValue(shell);
        Func<string,object> value=name=>reading.GetType().GetProperty(name).GetValue(reading,null);
        string key=value("DeviceKey") as string;
        string row="SelectedPID="+((int)value("ProductId")).ToString("X4")+" DeviceKeyHashPrefix="+(key==null?"none":Hash(Encoding.UTF8.GetBytes(key)).Substring(0,16))+
            " ProtocolStatus="+value("ProtocolStatus")+" RotationKnown="+value("RotationKnown")+" RotationAngle="+value("RotationAngle")+
            " IsRotationWriteSupported="+value("IsRotationWriteSupported")+" IsRotationHardwareVerified="+value("IsRotationHardwareVerified")+
            " DPI="+value("Dpi")+" PollingRate="+value("PollingRate");
        object page=shell.GetType().GetField("devicePage",Fields).GetValue(shell);
        row+=" UIAngle="+((TextBox)page.GetType().GetField("angleValue",Fields).GetValue(page)).Text+
            " ApplyEnabled="+((Button)page.GetType().GetField("applyRotation",Fields).GetValue(page)).IsEnabled;
        if(row!=last){Write(row);last=row;}
    }
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length!=2)return 2;
        using(log=new StreamWriter(args[1],false,new UTF8Encoding(false))){
            log.AutoFlush=true;
            try {
                Assembly candidate=Assembly.LoadFrom(args[0]);
                Write("CandidateVersion="+candidate.GetName().Version+" CandidateSHA256="+Hash(File.ReadAllBytes(args[0])));
                Write("NormalEntryPoint=false RealProductDeviceRuntime=true StartupSyncOmitted=true HooksOmitted=true DeviceWritesByLauncher=0");
                bool created;
                using(var mutex=new Mutex(true,"Local\\LeiyunLite.v1",out created)){
                    if(!created){Write("STOP=OtherProductInstanceRunning");return 3;}
                    try {
                        var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
                        var desktop=candidate.GetType("RazerBatteryTray.Desktop.DesktopApp",true);
                        desktop.GetMethod("LoadTheme",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{app});
                        var type=candidate.GetType("RazerBatteryTray.Desktop.ShellWindow",true);
                        object shell=Activator.CreateInstance(type,Fields,null,new object[]{true,false},null);
                        type.GetField("Demo",Fields).SetValue(shell,false);
                        object settings=type.GetField("store",Fields).GetValue(shell);
                        object prefs=settings.GetType().GetMethod("Load").Invoke(settings,null);
                        type.GetField("Preferences",Fields).SetValue(shell,prefs);
                        object legacy=type.GetField("legacyStore",Fields).GetValue(shell);
                        type.GetField("LegacySettings",Fields).SetValue(shell,legacy.GetType().GetMethod("Load").Invoke(legacy,null));
                        // A normal in-memory macro model is needed by page construction;
                        // suspend it and omit input Hooks for this Rotation-only experiment.
                        var macroStore=Activator.CreateInstance(candidate.GetType("RazerBatteryTray.Macros.MacroStore",true),new object[]{null});
                        var output=Activator.CreateInstance(candidate.GetType("RazerBatteryTray.Macros.WindowsMacroOutput",true),true);
                        var macros=Activator.CreateInstance(candidate.GetType("RazerBatteryTray.Macros.MacroController",true),new object[]{macroStore,output,false,true,false});
                        type.GetField("Macros",Fields).SetValue(shell,macros);
                        type.GetField("Draft",Fields).SetValue(shell,macros.GetType().GetMethod("Snapshot").Invoke(macros,null));
                        type.GetMethod("ApplyLanguage",Fields).Invoke(shell,null);
                        candidate.GetType("RazerBatteryTray.Desktop.Ui",true).GetMethod("ApplyTheme",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,new object[]{prefs.GetType().GetField("Theme").GetValue(prefs)});
                        type.GetMethod("RebuildPages",Fields).Invoke(shell,new object[]{0});
                        var window=(Window)shell;
                        // Prevent the original demo constructor's caption from implying
                        // that actual device writes are disabled after this runtime switch.
                        Relabel(window);
                        window.Title="雷云 Lite · Rotation 专项设备页验证（真实硬件）";
                        app.MainWindow=window;
                        type.GetEvent("Refreshed",Fields).GetAddMethod(true).Invoke(shell,new object[]{new Action(()=>Observe(shell))});
                        var watch=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
                        watch.Tick+=(s,e)=>Observe(shell);
                        app.Startup+=async(s,e)=>{
                            try {
                                window.Show();
                                var initialization=(Task)type.GetMethod("InitializeRuntime",Fields).Invoke(shell,null);
                                object updates=type.GetField("Updates",Fields).GetValue(shell);
                                ((DispatcherTimer)updates.GetType().GetField("timer",Fields).GetValue(updates)).Stop();
                                Write("AutomaticUpdateTimerPausedForRegression=true PreferencesLoadedWithoutSaving=true");
                                watch.Start();await initialization;
                            }catch(Exception ex){Write("RuntimeFailure="+ex.GetType().Name);type.GetMethod("AbortStartup",Fields).Invoke(shell,null);}
                        };
                        try{app.Run();}finally{watch.Stop();Write("HostNormalExit=true");}
                    }finally{mutex.ReleaseMutex();}
                }
                return 0;
            }catch(Exception ex){Write("LauncherFailure="+ex.GetType().Name);return 1;}
        }
    }
    private static void Relabel(DependencyObject root)
    {
        var text=root as TextBlock;
        if(text!=null && (text.Text.Contains("安全预览") || text.Text.Contains("Safe preview")))
            text.Text="雷云 Lite · Rotation 专项验证 · 真实硬件";
        foreach(object child in LogicalTreeHelper.GetChildren(root)){
            var element=child as DependencyObject;if(element!=null)Relabel(element);
        }
    }
}
