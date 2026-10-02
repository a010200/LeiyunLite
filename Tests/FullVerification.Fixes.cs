// Limited regression entry point for the seven confirmed defects. Never enables native writes/hooks.
using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using RazerBatteryTray.Macros;
using System.Windows;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using RazerBatteryTray;
using RazerBatteryTray.Desktop;

internal static partial class FullVerification
{
    static Func<string,bool> FixFilter;
    static int FixTests(string group)
    {
        if(group == "Hardware")return FixHardware();
        FixFilter = name =>
            (group == "Rotation" && name.StartsWith("Rotation-")) ||
            ((group == "Rotation" || group == "Final") && (name == "Protocol-decode-rotation-invalid-offset" || name == "Protocol-payload-decoder-length-matrix")) ||
            ((group == "HID" || group == "Final") && (name.StartsWith("HID-report-length-") || name == "HID-stale-previous-command" || name.StartsWith("DPI-stage-preserve-") || name.StartsWith("DPI-write-fault-") || name.StartsWith("Polling-") || name == "Hotplug-sleep-wake-A-B" || name.StartsWith("Rotation-"))) ||
            ((group == "Cache" || group == "Final") && (name.StartsWith("Cache-") || name == "Hotplug-sleep-wake-A-B")) ||
            ((group == "Settings" || group == "Final") && name.StartsWith("Desktop-settings-")) ||
            ((group == "Version" || group == "Final") && name == "Updater-version-boundary") ||
            ((group == "UI" || group == "Final") && name == "UI-language-macro-selection-preserved") ||
            name.StartsWith("FIX-"+group+"-") || (group == "Final" && name.StartsWith("FIX-") && !name.StartsWith("FIX-Hardware-"));
        ProtocolTests(); DpiTests(); DeviceTests(); RotationTests(); PersistenceTests(); UpdateTests();
        if(group == "Rotation" || group == "Final") NetworkTests();
        FixCases();
        if(group == "Final") {
            typeof(DesktopTests).GetField("artifacts",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,Root);
            foreach(string method in new[]{"VersionMetadata","Capabilities","Dpi","Rejections","Rates","RotationCommands","RotationFailures","Settings","ReplySafety","InstanceSafety","UpdateSelection"}) {
                string name=method;Test("FIX-Core-"+name,"ExistingDesktop",()=>{try{typeof(DesktopTests).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);}catch(TargetInvocationException ex){throw ex.InnerException;}});
            }
            RazerBatteryTray.Tests.MacroTests.RunCoreOnly((name,action)=>Test("FIX-Macro-"+name,"ExistingMacro",action),Root);
        }
        if(group == "UI" || group == "Final") { if(App == null) SetupUi(); ExtraTests(); }
        File.WriteAllLines(Path.Combine(Root,"results.tsv"),Rows);
        Console.WriteLine("RESULT: "+Pass+" passed, "+Fail+" failed"); return Fail == 0 ? 0 : 1;
    }
    static int FixHardware()
    {
        try {
            var transport=new ReadOnlyTransport();var client=new RazerDeviceClient(transport,new HardwareCacheStore(null));string instance=null;
            for(int i=0;i<5;i++) {
                Check(Process.GetProcessesByName("LeiyunLite.Desktop").Length==0,"Daily instance running; stop readonly probe");
                var r=client.QueryRazerDeviceInfo();
                Console.WriteLine("READ sample="+i+" model="+r.DeviceName+" PID="+r.ProductId.ToString("X4")+" status="+r.ProtocolStatus+" battery-known="+r.BatteryKnown+" battery="+r.BatteryPercent+" DPI="+r.Dpi+" Hz="+r.PollingRate+" rotation-known="+r.RotationKnown+" rotation="+r.RotationAngle);
                if(instance==null)instance=r.DeviceKey;
                Check(r.IsConnected&&r.ProtocolStatus==DeviceProtocolStatus.Ready&&r.BatteryKnown&&r.Dpi>0&&r.PollingRate>0&&r.DeviceKey==instance,"Readonly telemetry failed; stop without hardware writes");
                Check(!r.IsRotationWriteSupported||r.RotationKnown,"Supported rotation read failed");Thread.Sleep(300);
            }
            Check(transport.BlockedWrites==0&&transport.RotationWrites==0,"Hardware SET attempted");
            Console.WriteLine("PASS FIX-Hardware-readonly-5; GETs="+transport.Reads+" SETs="+transport.RotationWrites);return 0;
        } catch(Exception ex){Console.WriteLine("FAIL FIX-Hardware-readonly-5 "+ex);return 1;}
    }
    static void FixCases()
    {
        Test("FIX-HID-polling-90-91-legacy-modern","Protocol",()=>{
            foreach(int length in new[]{90,91}) foreach(int pid in new[]{0x00DF,0x00C1}) foreach(int hz in new[]{125,500,1000}) {
                var h=new Hid(length,pid);h.Rate=hz;int requests=0;
                h.Filter=(q,r)=>{int o=length==91?1:0;Check(q[o+1]==0x1F,"TID changed");if(q[o+6]==0&&(q[o+7]==0x85||q[o+7]==0xC0))requests++;return r;};
                Check(Client(h).QueryRazerDeviceInfo().PollingRate==hz&&requests==1,"Polling compatibility/single request");
            }
        });
        Test("FIX-HID-failed-polling-single-request-next-refresh","Protocol",()=>{
            var h=new Hid();int requests=0;bool fail=true;
            h.Filter=(q,r)=>{if(q[7]==0&&(q[8]==0x85||q[8]==0xC0)){requests++;if(fail)return null;}return r;};
            var client=Client(h);Check(client.QueryRazerDeviceInfo().PollingRate==0&&requests==1,"Retry inside probe");fail=false;
            Check(client.QueryRazerDeviceInfo().PollingRate==1000&&requests==2,"Next refresh failed");
        });
        Test("FIX-Cache-normal-invalid-stages-isolation","Cache",()=>{
            string root=@"Software\LeiyunLite.FixVerification\"+Guid.NewGuid().ToString("N");
            try {
                var cache=new HardwareCacheStore(root);cache.SelectDevice("a");string path=(string)Field(cache,"CacheRegistryKey");
                using(var key=Registry.CurrentUser.CreateSubKey(path)) {
                    key.SetValue("CatalogRevision",RazerIdentityCatalog.Revision);key.SetValue("BatteryKnown",1);key.SetValue("BatteryPercent",80);
                    key.SetValue("Dpi",1600);key.SetValue("PollingRate",1000);key.SetValue("DpiStageCount",3);key.SetValue("DpiStage",2);key.SetValue("DpiStages","400,800,1600");
                }
                cache.LoadHardwareCache();Check(cache.CachedBatteryKnown&&cache.CachedBatteryPercent==80&&cache.CachedDpi==1600&&cache.CachedPollingRate==1000&&cache.CachedDpiStages.SequenceEqual(new[]{400,800,1600})&&cache.CachedDpiStage==2,"Normal cache lost");
                foreach(int hz in new[]{0,125,500,1000,2000,4000,8000}) {
                    using(var key=Registry.CurrentUser.CreateSubKey(path)){key.SetValue("PollingRate",hz);key.SetValue("Dpi",35000);key.SetValue("BatteryPercent",0);}
                    cache.LoadHardwareCache();Check(cache.CachedBatteryKnown&&cache.CachedBatteryPercent==0&&cache.CachedDpi==35000&&cache.CachedPollingRate==hz,"Legal cached values lost");
                }
                foreach(int bad in new[]{-1,1,99,35001,int.MaxValue}) {
                    using(var key=Registry.CurrentUser.CreateSubKey(path)){key.SetValue("BatteryPercent",999);key.SetValue("Dpi",bad);key.SetValue("PollingRate",-1);}
                    cache.LoadHardwareCache();Check(!cache.CachedBatteryKnown&&cache.CachedBatteryPercent==0&&cache.CachedDpi==0&&cache.CachedPollingRate==0,"Invalid numeric cache accepted");
                }
                foreach(string stages in new[]{"400,,1600","400,99,1600","400,800,35001","400,bad,1600","400,800"}) {
                    using(var key=Registry.CurrentUser.CreateSubKey(path))key.SetValue("DpiStages",stages);
                    cache.LoadHardwareCache();Check(cache.CachedDpiStages==null&&cache.CachedDpiStageCount==0&&cache.CachedDpiStage==0,"Partial/corrupt stages retained");
                }
                foreach(int count in new[]{-1,0,6}) {
                    using(var key=Registry.CurrentUser.CreateSubKey(path)){key.SetValue("DpiStageCount",count);key.SetValue("DpiStages","400,800,1600");}
                    cache.LoadHardwareCache();Check(cache.CachedDpiStages==null&&cache.CachedDpiStageCount==0&&cache.CachedDpiStage==0,"Invalid count retained");
                }
                using(var key=Registry.CurrentUser.CreateSubKey(path)){key.SetValue("DpiStageCount",3);key.SetValue("DpiStage",4);}
                cache.LoadHardwareCache();Check(cache.CachedDpiStages==null&&cache.CachedDpiStage==0,"Invalid active stage retained");
                cache.SelectDevice("b");Check(!cache.CachedBatteryKnown&&cache.CachedDpi==0&&cache.CachedPollingRate==0&&cache.CachedDpiStages==null,"Cache crossed devices");
            } finally {Registry.CurrentUser.DeleteSubKeyTree(root,false);}
        });
        Test("FIX-Settings-normal-default-corrupt-DTD-preserve-file","Settings",()=>{
            string dir=Dir(),path=Path.Combine(dir,"desktop.xml");var store=new DesktopSettings(dir);
            Action<DesktopPreferences> defaults=p=>Check(p!=null&&p.Theme=="dark"&&p.Language=="system"&&p.CloseToTray&&p.AutoCheckUpdates&&!p.AutoDownloadUpdates&&!p.AutoInstallUpdates&&p.LowBatteryThreshold==20,"Default preferences changed");
            defaults(store.Load());store.Save(new DesktopPreferences{Theme="light",Language="en",ReducedMotion=true});var normal=store.Load();Check(normal.Theme=="light"&&normal.Language=="en"&&normal.ReducedMotion,"Normal XML lost");
            foreach(string text in new[]{"<DesktopPreferences><Theme>","<!DOCTYPE DesktopPreferences [<!ENTITY theme 'light'>]><DesktopPreferences><Theme>&theme;</Theme></DesktopPreferences>"}) {
                File.WriteAllText(path,text);defaults(store.Load());Check(File.ReadAllText(path)==text,"Invalid settings overwritten");
            }
        });
        Test("FIX-Rotation-offset-normal-invalid","Protocol",()=>{
            foreach(int offset in new[]{0,1}) {
                var report=new byte[90+offset];report[offset+5]=3;report[offset+8]=report[offset+9]=1;report[offset+10]=unchecked((byte)(sbyte)-9);
                int angle;Check(RazerProtocol.TryDecodeRotationPayload(report,offset,out angle)&&angle==-9,"Valid rotation offset rejected");
            }
            foreach(int offset in new[]{-1,int.MinValue,91,int.MaxValue,int.MaxValue-10}) {int angle;Check(!RazerProtocol.TryDecodeRotationPayload(new byte[91],offset,out angle),"Invalid offset accepted");}
            int value;Check(!RazerProtocol.TryDecodeRotationPayload(null,0,out value)&&!RazerProtocol.TryDecodeRotationPayload(new byte[10],0,out value),"Short/null decoded");
        });
        Test("FIX-Version-strict-boundaries-sort","Updates",()=>{
            foreach(string s in new[]{"1.2.3","v1.2.3","1.2.3-r1"})Check(ReleaseVersion.Parse(s)!=null,"Valid version rejected");
            foreach(string s in new[]{"1.2.3\n","1.2.3\r\n"," 1.2.3","1.2.3 "})Check(ReleaseVersion.Parse(s)==null,"Whitespace version accepted");
            Check(ReleaseVersion.Parse("1.2.3-r1").CompareTo(ReleaseVersion.Parse("1.2.3"))<0&&ReleaseVersion.Parse("1.2.3").CompareTo(ReleaseVersion.Parse("1.2.4"))<0,"Version sorting changed");
        });
        Test("FIX-UI-language-selected-tab-draft-bindings","UI",()=>{
            if(App == null)SetupUi();var w=Demo();
            try {
                w.Draft=Lib(new MacroStep{Kind=ActionKind.Delay,Number=1});w.Draft.Macros.Add(new MacroDefinition{Id="second",Name="second"});w.Draft.Macros[1].Steps.Add(new MacroStep{Kind=ActionKind.Delay,Number=2});
                w.Draft.Bindings.Add(new MacroBinding{Id="binding",MacroId="second",Trigger=TriggerKind.Middle});w.DraftDirty=true;w.Preferences.Language="zh";w.RebuildPages(1);
                var draft=w.Draft;var binding=draft.Bindings[0];var page=(MacroPage)Field(w,"macroPage");page.ReloadLibrary("second");Call(page,"SelectTab",true);
                foreach(int language in new[]{2,1}) {
                    ChangeLanguage(w,language);page=(MacroPage)Field(w,"macroPage");
                    Check(page.Selected!=null&&page.Selected.Id=="second","Selected macro changed");
                    Check(((FrameworkElement)Field(page,"bindingWorkspace")).Visibility==Visibility.Visible,"Macro subtab changed");
                    Check(object.ReferenceEquals(w.Draft,draft)&&draft.Macros.Count==2&&draft.Macros[1].Steps[0].Number==2&&w.DraftDirty&&object.ReferenceEquals(draft.Bindings[0],binding)&&binding.MacroId=="second"&&w.Macros==null,"Draft/bindings/runtime changed");
                }
            } finally {w.ClosePreview();}
        });
    }
}
