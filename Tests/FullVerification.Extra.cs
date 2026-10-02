// Extra adversarial test coverage. All stores, outputs and device writes are fake.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using RazerBatteryTray;
using RazerBatteryTray.Desktop;
using RazerBatteryTray.Macros;
using DpiScale=RazerBatteryTray.Desktop.DpiScale;

internal static partial class FullVerification
{
    sealed class MemoryStore:IMacroStore
    {
        internal MacroLibrary Library;internal bool FailSave;internal int Saves;
        public string FilePath {get{return "test-memory-only";}}
        public MacroLibrary Load(){return Library.Clone();}
        public void Save(MacroLibrary l){Saves++;if(FailSave)throw new IOException("simulated save failure");Library=l.Clone();}
    }
    [StructLayout(LayoutKind.Sequential)] struct KeyData {public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] struct MouseData {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    static IntPtr Callback(GlobalInputHook hook,string name,int msg,object data)
    {
        var p=Marshal.AllocHGlobal(Marshal.SizeOf(data));try{Marshal.StructureToPtr(data,p,false);return (IntPtr)Call(hook,name,0,new IntPtr(msg),p);}finally{Marshal.FreeHGlobal(p);}
    }
    static IEnumerable<ComboBox> Combos(DependencyObject parent)
    {
        var combo=parent as ComboBox;if(combo!=null){yield return combo;yield break;}
        foreach(object child in LogicalTreeHelper.GetChildren(parent)){var d=child as DependencyObject;if(d!=null)foreach(var c in Combos(d))yield return c;}
    }
    static void ChangeLanguage(ShellWindow w,int index)
    {
        var views=(FrameworkElement[])Field(w,"views");Combos(views[2]).First().SelectedIndex=index;Pump(5);
    }
    static int ExtraTests()
    {
        foreach(string operation in new[]{"RemoveBinding","DisableBinding","DisableAll","Suspend","Emergency","Dispose","FailedSafetySave"}){string op=operation;
            Test("Controller-stop-"+op,"MacroController",()=>{
                var lib=Lib(new MacroStep{Kind=ActionKind.Keyboard,KeyCode=65,Press=PressMode.Down},new MacroStep{Kind=ActionKind.Mouse,Mouse=MouseAction.Left,Press=PressMode.Down,Number=1},new MacroStep{Kind=ActionKind.Delay,Number=600000});
                lib.Bindings.Add(new MacroBinding{Id="held",MacroId="main",Trigger=TriggerKind.Middle,Mode=RunMode.WhileHeld,SuppressOriginal=true});
                var store=new MemoryStore{Library=lib};var output=new Output();using(var controller=new MacroController(store,output,false)){
                    var router=(BindingRouter)Field(controller,"router");Check(router.Handle(new InputStroke{Trigger=TriggerKind.Middle,Down=true}),"Not started");Until(()=>output.Events.Contains("MLeft+"));
                    switch(op){case "RemoveBinding":controller.RemoveBinding("held");break;case "DisableBinding":controller.SetBindingEnabled("held",false);break;case "DisableAll":controller.SetBindingsEnabled(false);break;case "Suspend":controller.SuspendBindings(true);break;case "Emergency":router.Handle(new InputStroke{Trigger=TriggerKind.Keyboard,Key=123,Modifiers=KeyModifiers.Control|KeyModifiers.Shift,Down=true});break;case "Dispose":controller.Dispose();break;case "FailedSafetySave":store.FailSave=true;Reject(()=>controller.RemoveBinding("held"));Check(controller.SafetySavePending,"Failed safety save not flagged");break;}
                    Until(()=>!controller.IsRunning);Check(output.Events.Contains("K65-")&&output.Events.Contains("MLeft-"),"Cancel left input held");if(op!="Dispose")Check(router.Handle(new InputStroke{Trigger=TriggerKind.Middle,Down=false}),"Paired physical Up lost");
                }
            });
        }
        Test("Hook-native-structs-injected-filter-all-paths","GlobalInputHook",()=>{
            foreach(int msg in new[]{0x100,0x101,0x104,0x105}){int routed=0;using(var hook=new GlobalInputHook(s=>{routed++;return true;})){Callback(hook,"Keyboard",msg,new KeyData{Key=65,Flags=0x10});Check(routed==0,"Injected keyboard routed");}}
            foreach(int msg in new[]{0x201,0x202,0x204,0x205,0x207,0x208,0x20B,0x20C,0x20A,0x200}){int routed=0;using(var hook=new GlobalInputHook(s=>{routed++;return true;})){Callback(hook,"Mouse",msg,new MouseData{Data=120u<<16,Flags=1});Check(routed==0,"Injected mouse routed");}}
        });
        Test("Hook-stopped-generation-down-block-up-pass-new-run","GlobalInputHook",()=>{
            var output=new WindowsMacroOutput();output.BeginRun();var old=output.CurrentInputTag;output.CancelRun();int routed=0;
            using(var hook=new GlobalInputHook(s=>{routed++;return true;},null,output.ShouldSuppressStoppedDown)){
                foreach(int msg in new[]{0x201,0x204,0x207,0x20B})Check(Callback(hook,"Mouse",msg,new MouseData{Flags=1,Extra=old})==new IntPtr(1),"Stale down passed");
                Check(Callback(hook,"Keyboard",0x100,new KeyData{Key=65,Flags=0x10,Extra=old})==new IntPtr(1),"Stale keyboard passed");
                Callback(hook,"Mouse",0x202,new MouseData{Flags=1,Extra=old});Check(hook.PassedLeftUps==1,"Up not passed");
                output.BeginRun();Callback(hook,"Mouse",0x201,new MouseData{Flags=1,Extra=output.CurrentInputTag});Check(hook.PassedLeftDowns==1&&hook.BlockedStoppedDowns==1&&routed==0,"New run blocked or recursion");
            }
        });
        Test("Macro-XML-structured-roundtrip-fuzz-10000","Fuzz",()=>{
            string file=Path.Combine(Dir(),"structured.xml");var store=new MacroStore(file);for(int i=0;i<10000;i++){
                var lib=Lib();lib.Macros[0].Name="中文😀 & < "+i;int count=Random.Next(1,9);for(int j=0;j<count;j++)lib.Macros[0].Steps.Add(new MacroStep{Kind=ActionKind.Delay,Number=Random.Next(0,600001)});
                if(i%2==0){store.Save(lib);var read=store.Load();Check(read.Macros.Count==1&&read.Macros[0].Name==lib.Macros[0].Name&&read.Macros[0].Steps.Select(s=>s.Number).SequenceEqual(lib.Macros[0].Steps.Select(s=>s.Number)),"Roundtrip data loss case="+i);}
                else{lib.Macros.Add(lib.Macros[0]);Reject(()=>store.Save(lib));}
            }
        });
        if (App == null) SetupUi();
        Test("UI-language-100-draft-binding-reading-preserved","UI",()=>{
            var w=Demo();try{w.Draft=Lib(new MacroStep{Kind=ActionKind.Delay,Number=1});w.Draft.Macros[0].Name="unsaved 中文😀";w.Draft.Bindings.Add(new MacroBinding{Id="binding",MacroId="main",Trigger=TriggerKind.Middle});w.DraftDirty=true;w.RebuildPages(1);
                for(int i=0;i<100;i++){ChangeLanguage(w,i%2==0?2:1);Check(w.DraftDirty&&w.Draft.Macros[0].Name=="unsaved 中文😀"&&w.Draft.Bindings.Count==1&&w.Reading.Dpi==800&&w.Reading.RotationAngle==-8,"Language lost draft/reading");}
            }finally{w.ClosePreview();}
        });
        Test("UI-language-macro-selection-preserved","UI",()=>{
            var w=Demo();try{w.Draft=Lib(new MacroStep{Kind=ActionKind.Delay,Number=1});w.Draft.Macros.Add(new MacroDefinition{Id="second",Name="second"});w.Draft.Macros[1].Steps.Add(new MacroStep{Kind=ActionKind.Delay,Number=2});w.RebuildPages(1);var page=(MacroPage)Field(w,"macroPage");page.ReloadLibrary("second");Check(page.Selected.Id=="second","Selection fixture failed");ChangeLanguage(w,2);var after=(MacroPage)Field(w,"macroPage");Check(after.Selected!=null&&after.Selected.Id=="second","Selected macro changed after language switch: "+(after.Selected==null?"null":after.Selected.Id));}finally{w.ClosePreview();}
        });
        Test("UI-theme-real-control-toggle-100","UI",()=>{var w=Demo();try{for(int i=0;i<100;i++){var views=(FrameworkElement[])Field(w,"views");Combos(views[2]).Skip(1).First().SelectedIndex=1-i%2;Check(w.Preferences.Theme==(i%2==0?"light":"dark")&&Ui.Light==(i%2==0),"Theme control not applied");Pump(1);Check(w.Macros==null,"Demo macro initialized");}}finally{w.ClosePreview();}});
        Test("UI-drawer-navigation-and-dirty-close-guard","UI",()=>{var w=Demo();try{w.Navigate(1);w.OpenDrawer("test",new TextBlock{Text="test only"});w.Navigate(0);Check((int)Field(w,"currentPage")==1&&w.DrawerOpen,"Drawer discarded on navigate");w.CloseDrawer();w.DraftDirty=true;w.Close();Check(!(bool)Field(w,"disposed")&&w.DrawerOpen,"Dirty close destroyed window");w.CloseDrawer();}finally{w.ClosePreview();}});
        Test("Demo-device-preview-commit-does-not-write","Demo",()=>{var w=Demo();try{var page=Field(w,"devicePage");var slider=(Slider)Field(page,"dpi");slider.Value=DpiScale.ToPosition(1200);((System.Threading.Tasks.Task)Call(page,"CommitDpi")).GetAwaiter().GetResult();Check(w.Reading.Dpi==800,"Demo DPI changed");((Slider)Field(page,"angle")).Value=10;((System.Threading.Tasks.Task)Call(page,"CommitRotation")).GetAwaiter().GetResult();Check(w.Reading.RotationAngle==-8,"Demo rotation changed");Check(w.Macros==null&&Field(w,"monitor")==null&&Field(w,"tray")==null,"Demo opened real services");}finally{w.ClosePreview();}});
        Test("UI-device-switch-invalidates-pending-preview","DeviceUI",()=>{var w=Demo();try{var page=Field(w,"devicePage");((Slider)Field(page,"dpi")).Value=DpiScale.ToPosition(1200);((Slider)Field(page,"angle")).Value=10;w.Reading.DeviceKey="different-test-instance";((System.Threading.Tasks.Task)Call(page,"CommitDpi")).GetAwaiter().GetResult();((System.Threading.Tasks.Task)Call(page,"CommitRotation")).GetAwaiter().GetResult();Check(!(bool)Field(page,"pendingDpi")&&!(bool)Field(page,"pendingRotation")&&w.Reading.Dpi==800&&w.Reading.RotationAngle==-8,"Stale preview applied");}finally{w.ClosePreview();}});
        Test("UI-render-all-pages-dark-light","UI",()=>{var w=Demo();try{foreach(string theme in new[]{"dark","light"}){w.Preferences.Theme=theme;Ui.ApplyTheme(theme);w.RebuildPages(0);for(int i=0;i<4;i++){w.Navigate(i);Screenshot(w,"render-"+theme+"-"+i+".png");}}}finally{w.ClosePreview();}});
        Test("Tray-left-single-double-triple-card-only","Tray",()=>{var w=Demo();TrayController tray=null;try{tray=new TrayController(w);var icon=(System.Windows.Forms.NotifyIcon)Field(tray,"icon");var invoke=typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic);Check(invoke!=null,"Test adapter missing OnMouseDown");foreach(int clicks in new[]{1,2,3}){invoke.Invoke(icon,new object[]{new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,clicks,0,0,0)});var fly=(TrayFlyout)Field(tray,"flyout");Check(fly!=null&&!fly.IsMenu&&!w.IsVisible,"Left tray event opened main window/menu");}}finally{if(tray!=null)tray.Dispose();w.ClosePreview();}});
        Test("Tray-right-menu-actual-page-pause-exit-actions","Tray",()=>{for(int i=0;i<6;i++){int action=i;var w=Demo();TrayFlyout fly=null;try{bool paused=false;fly=new TrayFlyout(w,true,false,b=>paused=b);var buttons=(List<Button>)Field(fly,"options");Check(buttons.Count==6,"Menu missing action");buttons[action].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(action<3)Check((int)Field(w,"currentPage")==action&&w.IsVisible,"Menu opened wrong page");if(action==3)Check(paused,"Pause action not called");if(action==5)Check((bool)Field(w,"disposed"),"Exit action did not close");}finally{if(fly!=null)fly.Close();w.ClosePreview();}}});
        App.Shutdown();File.WriteAllLines(Path.Combine(Root,"results.tsv"),Rows);Console.WriteLine("RESULT: "+Pass+" passed, "+Fail+" failed");return Fail==0?0:1;
    }
}
