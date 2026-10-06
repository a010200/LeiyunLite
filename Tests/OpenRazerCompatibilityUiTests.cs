using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RazerBatteryTray.Desktop;

namespace RazerBatteryTray.Tests
{
    internal static partial class OpenRazerCompatibilityTests
    {
        private static void RunUiTests()
        {
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown }; DesktopApp.LoadTheme(app);
            Test("Actual WPF per-capability controls and trust labels", UiCapabilities);
            Test("Actual confirmation cancel, close, replace, accept and changed target", UiConsent);
            Test("Supplemental WPF ranges, rates, evidence, path and revoked consent", SupplementalUi);
            app.Shutdown();
        }
        private static T Field<T>(object value,string name)
        { return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(value); }
        private static void Pump()
        { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
        { if(root is T) yield return (T)root; for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var item in Descendants<T>(VisualTreeHelper.GetChild(root,i))) yield return item; }
        private static ShellWindow Preview()
        {
            var window=new ShellWindow(true); window.Preferences.Language="zh"; window.Preferences.ReducedMotion=true; window.RebuildPages(0);
            window.ShowActivated=false; window.ShowInTaskbar=false; window.Show(); window.InitializeRuntime().GetAwaiter().GetResult(); Pump(); return window;
        }
        private static void UiCapabilities()
        {
            var window=Preview(); var page=Field<DevicePage>(window,"devicePage");
            try {
                foreach(int pid in new[] {0x46,0x42,0xa6,0xb3,0x15,0x13,0xffff}) {
                    var f=new ProfileFake(pid,91); window.Reading=Client(f).QueryRazerDeviceInfo(); page.UpdateReading(); Pump();
                    Check(Field<Slider>(page,"dpi").IsEnabled==window.Reading.IsDpiWriteSupported,"DPI UI " + pid);
                    Check(Descendants<Button>(Field<WrapPanel>(page,"ratePanel")).All(b=>b.IsEnabled==window.Reading.IsPollingWriteSupported),"Polling UI " + pid);
                }
                var failed=new ProfileFake(0xa6,91) { FailDpi=true }; window.Reading=Client(failed).QueryRazerDeviceInfo(); page.UpdateReading();
                Check(!Field<Slider>(page,"dpi").IsEnabled && Descendants<Button>(Field<WrapPanel>(page,"ratePanel")).All(b=>b.IsEnabled),"DPI failure does not disable valid polling");
                Check(Field<TextBlock>(page,"performanceInfo").Text.Contains("公开协议证据"),"Upstream wording rendered");
                window.Reading.ProtocolStatus=DeviceProtocolStatus.Cached; page.UpdateReading();
                Check(!Field<Slider>(page,"dpi").IsEnabled && Descendants<Button>(Field<WrapPanel>(page,"ratePanel")).All(b=>!b.IsEnabled),"Cached disables every write");
                Check(DevicePage.TrustText(CapabilityTrust.HardwareVerified).Contains("实机验证") && DevicePage.TrustText(CapabilityTrust.IdentityOnly).Contains("暂无安全控制协议"),"Hardware and identity wording");
            } finally { window.ClosePreview(); }
        }
        private static Task<bool> Confirm(DevicePage page,PerformanceCapability capability,MouseBatteryInfo r)
        { return (Task<bool>)typeof(DevicePage).GetMethod("ConfirmUpstream",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(page,new object[] { capability,r.DeviceKey,r.InterfacePath }); }
        private static void Click(ShellWindow window,string label)
        { Pump(); var button=Descendants<Button>(Field<Grid>(window,"overlay")).Single(b=>b.Content as string==label); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        private static void UiConsent()
        {
            var window=Preview(); var page=Field<DevicePage>(window,"devicePage");
            var demo=typeof(ShellWindow).GetField("Demo",BindingFlags.Instance|BindingFlags.NonPublic);
            var f=new ProfileFake(0xa6,91); var r=Client(f).QueryRazerDeviceInfo(); window.Reading=r; page.UpdateReading();
            // Construct and initialize exclusively in Demo. Only exercise the
            // confirmation UI with Demo=false; never initialize real runtime,
            // call a commit handler or run normal Main. Restore before cleanup.
            demo.SetValue(window,false);
            try {
                var task=Confirm(page,PerformanceCapability.Dpi,r); Check(!task.IsCompleted && window.DrawerOpen,"First write awaits choice");
                Click(window,"取消"); Check(task.IsCompleted && !task.Result && window.PerformanceConsent.NeedsConfirmation(r.DeviceKey,PerformanceCapability.Dpi,CapabilityTrust.UpstreamVerified),"Cancel never persists consent");
                task=Confirm(page,PerformanceCapability.Dpi,r); window.CloseDrawer(); Pump(); Check(task.IsCompleted && !task.Result,"Close/Esc/collapse cancel");
                task=Confirm(page,PerformanceCapability.Dpi,r); window.OpenDrawer("replacement",new TextBlock()); Pump(); Check(task.IsCompleted && !task.Result,"Replaced drawer cancels"); window.CloseDrawer();
                task=Confirm(page,PerformanceCapability.Dpi,r); Click(window,"继续"); Check(task.IsCompleted && task.Result,"Continue grants this session choice");
                Check(Confirm(page,PerformanceCapability.Dpi,r).Result && !window.DrawerOpen,"Same capability/session does not ask again");
                Check(window.PerformanceConsent.NeedsConfirmation(r.DeviceKey,PerformanceCapability.Polling,CapabilityTrust.UpstreamVerified),"Polling confirmation is independent");
                task=Confirm(page,PerformanceCapability.Polling,r); window.Reading=Client(new ProfileFake(0xa6,91)).QueryRazerDeviceInfo(); Click(window,"继续"); Check(task.IsCompleted && !task.Result,"Device switch cancels stale approval");
                Check(new UpstreamWriteConsent().NeedsConfirmation(r.DeviceKey,PerformanceCapability.Dpi,CapabilityTrust.UpstreamVerified),"New session asks again");
                Check(!window.PerformanceConsent.NeedsConfirmation(r.DeviceKey,PerformanceCapability.Dpi,CapabilityTrust.HardwareVerified),"HardwareVerified needs no upstream prompt");
                Check(f.Writes==0,"UI test never writes hardware, including Fake");
            } finally { demo.SetValue(window,true); window.ClosePreview(); }
        }
    }
}
