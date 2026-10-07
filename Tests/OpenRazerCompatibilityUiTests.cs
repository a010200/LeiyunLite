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
            Test("Actual WPF per-capability controls retain independent gates", UiCapabilities);
            RunDirectUiTests();
            Test("Supplemental WPF ranges, direct writes, stale paths and revoked permission", SupplementalUi);
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
                Check(Field<TextBlock>(page,"performanceInfo").Text=="" && Field<TextBlock>(page,"performanceInfo").Visibility==Visibility.Collapsed,"No ordinary performance prose");
                window.Reading.ProtocolStatus=DeviceProtocolStatus.Cached; page.UpdateReading();
                Check(!Field<Slider>(page,"dpi").IsEnabled && Descendants<Button>(Field<WrapPanel>(page,"ratePanel")).All(b=>!b.IsEnabled),"Cached disables every write");
                Check(window.Reading.PollingTrust==CapabilityTrust.UpstreamVerified,"Backend evidence is not relabeled HardwareVerified");
            } finally { window.ClosePreview(); }
        }
    }
}
