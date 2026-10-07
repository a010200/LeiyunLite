using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RazerBatteryTray.Desktop;

namespace RazerBatteryTray.Tests
{
    internal static partial class OpenRazerCompatibilityTests
    {
        private static void RunDirectUiTests()
        {
            Test("Chinese/English performance UI has only discrete DPI constraints",UiPerformanceText);
            Test("Ready DPI commits directly through initial read, SET and readback",UiDirectDpi);
            Test("Ready polling button writes directly with readback and no Drawer",UiDirectPolling);
            Test("Direct UI stale key/path, revoked permission and cached state issue no SET",UiDirectGuards);
            Test("Direct UI initial read failure issues no SET",UiDirectInitialFailure);
            Test("Direct UI mismatched readback restores and confirms original values",UiDirectRollback);
        }
        private static void SetField(object value,string name,object next)
        {value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(value,next);}
        private static void UiPerformanceText()
        {
            var window=Preview();
            try {
                foreach(string language in new[]{"zh","en"}) {
                    window.Preferences.Language=language;window.RebuildPages(0);var page=Field<DevicePage>(window,"devicePage");
                    foreach(int pid in new[]{0xa6,0x15,0xffff}) {
                        window.Reading=Client(new ProfileFake(pid,91)).QueryRazerDeviceInfo();page.UpdateReading();Pump();
                        string prose=string.Join("\n",Descendants<TextBlock>(page).Select(t=>t.Text));
                        foreach(string banned in new[]{"社区协议兼容","公开协议证据","本型号未在雷云 Lite 实机验证","Community protocol compatibility","public evidence","not hardware tested"})
                            Check(!prose.Contains(banned),"No relocated trust text: "+banned);
                        var hint=Field<TextBlock>(page,"performanceInfo");
                        if(pid==0x15) Check(hint.Visibility==Visibility.Visible && hint.Text.StartsWith(language=="zh"?"可精确写入的 DPI: ":"Exact writable DPI: ") && hint.Text.Contains("344"),"Discrete constraint retained");
                        else Check(hint.Visibility==Visibility.Collapsed && hint.Text=="" && hint.ActualHeight==0,"No prose or layout placeholder");
                    }
                }
            } finally {window.ClosePreview();}
        }
        // Initialize exclusively in Demo, then inject a memory-only Fake
        // client before exercising production commit handlers with Demo=false.
        // Never initialize real runtime, hooks, Run, user settings or native HID.
        private static ShellWindow DirectFixture(RazerDeviceClient client)
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            var window=Preview();SetField(window,"Device",client);window.Reading=client.QueryRazerDeviceInfo();
            Field<DevicePage>(window,"devicePage").UpdateReading();SetField(window,"Demo",false);return window;
        }
        private static void CloseDirectFixture(ShellWindow window)
        {SetField(window,"Demo",true);window.ClosePreview();}
        private static void PrepareDpi(DevicePage page,MouseBatteryInfo r,int value)
        {SetField(page,"dpiEditKey",r.DeviceKey);SetField(page,"dpiEditPath",r.InterfacePath);Field<TextBox>(page,"dpiValue").Text=value.ToString();SetField(page,"pendingDpi",true);}
        private static Task Commit(DevicePage page,string method,params object[] args)
        {return (Task)typeof(DevicePage).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(page,args);}
        private static void AwaitDirect(Task task,ShellWindow window)
        {
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(!task.IsCompleted) {Check(!window.DrawerOpen,"No Drawer during operation");if(clock.ElapsedMilliseconds>10000) throw new Exception("Direct UI timeout");Pump();System.Threading.Thread.Sleep(1);}
            task.GetAwaiter().GetResult();Check(!window.DrawerOpen,"No Drawer after operation");
        }
        private static string Toast(ShellWindow window)
        {return Descendants<TextBlock>(Field<UiSnackbar>(window,"toast")).Single().Text;}
        private static void UiDirectDpi()
        {
            var f=new ProfileFake(0xa6,91);var window=DirectFixture(Client(f));var page=Field<DevicePage>(window,"devicePage");
            try {
                f.Requests.Clear();PrepareDpi(page,window.Reading,1600);AwaitDirect(Commit(page,"CommitDpi"),window);
                Check(window.Reading.Dpi==1600 && f.Writes==1,"Exact final DPI, one SET");
                Check(f.Requests[0][7]==4 && f.Requests[0][8]==0x86,"Initial live stages GET");
                int at=f.Requests.FindIndex(q=>q[8]<0x80);Check(at>0 && f.Requests[at][8]==6 && f.Requests[at+1][8]==0x86,"SET then readback");
                Check(Toast(window).Contains("已写入并读回确认"),"Success feedback retained");
            } finally {CloseDirectFixture(window);}
        }
        private static void UiDirectPolling()
        {
            var f=new ProfileFake(0xa6,91);var window=DirectFixture(Client(f));var page=Field<DevicePage>(window,"devicePage");
            try {
                f.Requests.Clear();var button=Descendants<Button>(Field<WrapPanel>(page,"ratePanel")).Single(b=>(string)b.Content=="1000 Hz");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var clock=System.Diagnostics.Stopwatch.StartNew();while(Field<bool>(page,"writing")) {Check(!window.DrawerOpen,"Button never opens Drawer");if(clock.ElapsedMilliseconds>10000)throw new Exception("Polling click timeout");Pump();System.Threading.Thread.Sleep(1);}
                Pump();Check(f.Rate==1000 && f.Writes==1,"Polling writes once directly");
                Check(f.Requests.Take(3).Select(q=>q[8]).SequenceEqual(new byte[]{0xC0,0x40,0xC0}),"Initial GET / SET / readback");
                Check(!window.DrawerOpen && Toast(window).Contains("已写入并读回确认"),"No Drawer, success feedback");
            } finally {CloseDirectFixture(window);}
        }
        private static void UiDirectGuards()
        {
            foreach(string mode in new[]{"edit-key","edit-path","backend-key","backend-path","descriptor","revoked","cached","unresponsive","identity","unknown","invalid","ambiguous","wrong-pid","unsupported-transport"}) {
                var f=new ProfileFake(0xa6,91);var window=DirectFixture(Client(f));var page=Field<DevicePage>(window,"devicePage");
                try {
                    PrepareDpi(page,window.Reading,1600);
                    if(mode=="edit-key")SetField(page,"dpiEditKey","stale");
                    if(mode=="edit-path")SetField(page,"dpiEditPath","stale");
                    if(mode=="backend-key"){window.Reading.DeviceKey="stale";PrepareDpi(page,window.Reading,1600);}
                    if(mode=="backend-path"){window.Reading.InterfacePath="stale";PrepareDpi(page,window.Reading,1600);}
                    if(mode=="descriptor")f.Descriptor.Version++;
                    ApplyRevokedUiState(window.Reading,mode);
                    if(mode=="invalid")PrepareDpi(page,window.Reading,30001);
                    page.UpdateReading();AwaitDirect(Commit(page,"CommitDpi"),window);Check(f.Writes==0,"DPI zero SET: "+mode);
                    if(mode!="edit-key" && mode!="edit-path") {
                        if(mode=="backend-key")window.Reading.DeviceKey="stale";
                        if(mode=="backend-path")window.Reading.InterfacePath="stale";
                        if(mode=="descriptor")f.Descriptor.Version++;
                        ApplyRevokedUiState(window.Reading,mode);
                        AwaitDirect(Commit(page,"CommitRate",mode=="invalid"?2000:1000),window);Check(f.Writes==0,"Polling zero SET: "+mode);
                    }
                } finally {CloseDirectFixture(window);}
            }
        }
        private static void ApplyRevokedUiState(MouseBatteryInfo r,string mode)
        {
            if(mode=="revoked" || mode=="ambiguous")r.IsDpiWriteSupported=r.IsPollingWriteSupported=false;
            if(mode=="ambiguous")r.ProtocolReason="ambiguous-control-path";
            if(mode=="cached")r.ProtocolStatus=DeviceProtocolStatus.Cached;
            if(mode=="unresponsive")r.ProtocolStatus=DeviceProtocolStatus.PresentUnresponsive;
            if(mode=="identity")r.ProtocolStatus=DeviceProtocolStatus.IdentityOnly;
            if(mode=="unknown")r.DpiKnown=r.PollingKnown=false;
            if(mode=="wrong-pid")r.ProductId=0xffff;
            if(mode=="unsupported-transport")r.ProductId=0x13;
        }
        private static void UiDirectInitialFailure()
        {
            foreach(bool dpi in new[]{true,false}) {
                var f=new ProfileFake(0xa6,91);var window=DirectFixture(Client(f));var page=Field<DevicePage>(window,"devicePage");
                try {
                    f.Requests.Clear();if(dpi){f.FailDpi=true;PrepareDpi(page,window.Reading,1600);}else f.FailPolling=true;
                    AwaitDirect(dpi?Commit(page,"CommitDpi"):Commit(page,"CommitRate",1000),window);
                    Check(f.Writes==0 && f.Requests.Count>0 && Toast(window).Contains("未写入"),"Initial failure stops SET, retains feedback");
                } finally {CloseDirectFixture(window);}
            }
        }
        private static void UiDirectRollback()
        {
            foreach(bool dpi in new[]{true,false}) {
                var f=new ProfileFake(0xa6,91);var window=DirectFixture(Client(f));var page=Field<DevicePage>(window,"devicePage");
                try {
                    var original=(byte[])f.StagePayload.Clone();f.Requests.Clear();f.WriteFault=3;if(dpi)PrepareDpi(page,window.Reading,1600);
                    AwaitDirect(dpi?Commit(page,"CommitDpi"):Commit(page,"CommitRate",1000),window);
                    Check(f.Writes==2 && (dpi?f.StagePayload.SequenceEqual(original):f.Rate==500),"Mismatch restores original");
                    int last=f.Requests.FindLastIndex(q=>q[8]<0x80);Check(last>=0 && f.Requests[last+1][8]==(dpi?0x86:0xC0),"Independent restore readback");
                    Check(Toast(window).Contains("已恢复并读回原值"),"Rollback feedback retained");
                } finally {CloseDirectFixture(window);}
            }
        }
    }
}
