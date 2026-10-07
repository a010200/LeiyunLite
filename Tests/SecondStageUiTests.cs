using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;
using RazerBatteryTray.Macros;
using RazerBatteryTray.Updates;

namespace RazerBatteryTray.Desktop
{
    // Demo + memory/fake update adapters only. No HID, native hooks, SendInput or user configuration.
    internal static class SecondStageUiTests
    {
        private static readonly List<string> results = new List<string>();
        private static string artifacts;
        private static int passed, failed;
        private static void Check(bool v, string message) { if (!v) throw new Exception(message); }
        private static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
        private static void Call(object o, string name, params object[] args) { o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, args); Pump(); }
        private static void CallWithoutPump(object o, string name) { o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(o, null); }
        private static void Test(string name, Action a) { try { a(); passed++; results.Add("PASS\t" + name); } catch (Exception e) { failed++; results.Add("FAIL\t" + name + "\t" + e); } Console.WriteLine(results.Last()); }
        private static void Pump(int ms = 40) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        private static void Await(Task task) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (!task.IsCompleted && clock.ElapsedMilliseconds < 3000) Pump(10); Check(task.IsCompleted, "Update operation hung"); task.GetAwaiter().GetResult(); }
        private static IEnumerable<T> Find<T>(DependencyObject o) where T : DependencyObject { if (o is T) yield return (T)o; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++) foreach (var c in Find<T>(VisualTreeHelper.GetChild(o, i))) yield return c; }
        private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        private static void Capture(Window window, string name) { window.UpdateLayout(); var visual = (FrameworkElement)window.Content; var image = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using (var f = File.Create(Path.Combine(artifacts, name + ".png"))) png.Save(f); }
        private static void Within(FrameworkElement page) { foreach (var b in Find<Button>(page).Where(b => b.IsVisible && b.ActualWidth > 0)) { var p = b.TranslatePoint(new Point(), page); Check(p.X >= -1 && p.X + b.ActualWidth <= page.ActualWidth + 1, "Clipped button " + b.Content + ": " + p.X + "/" + b.ActualWidth + "/" + page.ActualWidth); } }
        private static DragEventArgs Drag(Button target, string id, RoutedEvent kind) { return (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { new DataObject(MouseBindingMap.MacroFormat, id), DragDropKeyStates.LeftMouseButton, DragDropEffects.Link, target, new Point(5, 5) }, null); }
        private sealed class FakeSource : IReleaseUpdateSource
        {
            internal readonly List<string> Calls = new List<string>();
            internal ReleaseOffer Offer = new ReleaseOffer { Tag = "v1.2.7", Version = ReleaseVersion.Parse("1.2.7"), SignedManifestUrl = "https://example.invalid/signed" };
            internal bool Fail, Wait;
            public async Task<ReleaseOffer> Check(bool p, CancellationToken t) { Calls.Add("check"); await Task.Delay(5, t); return Offer; }
            public async Task<string> Download(ReleaseOffer o, IProgress<int> p, CancellationToken t) { Calls.Add("portable"); await Task.Delay(5, t); return "isolated.zip"; }
            public async Task<string> DownloadSigned(ReleaseOffer o, InstallLayout l, IProgress<int> p, CancellationToken t) { Calls.Add("signed"); p.Report(50); await Task.Delay(Wait ? 5000 : 5, t); if (Fail) throw new InvalidDataException("verification rejected"); return Path.Combine(l.Root, "fake-verified-job"); }
        }
        [STAThread] private static int Main(string[] args)
        {
            artifacts = Path.GetFullPath(args[0]); Directory.CreateDirectory(artifacts);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; ShellWindow window = null;
            // Match Application.Run: button async continuations must return to the UI dispatcher.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.DispatcherUnhandledException += (s, e) => { failed++; string detail = e.Exception.GetType().FullName + ": " + e.Exception.Message + "\n" + e.Exception.StackTrace; results.Add("FAIL\tDispatcher\t" + detail); Console.WriteLine("DISPATCHER: " + detail); e.Handled = true; };
            try {
                DesktopApp.LoadTheme(app); Ui.ReducedMotion = true;
                window = new ShellWindow(true) { ShowActivated = false, ShowInTaskbar = false }; window.Preferences.Language = "zh"; window.Preferences.ReducedMotion = true; window.RebuildPages(1); window.Show(); Await(window.InitializeRuntime());
                var macro = new MacroDefinition { Id = "ui-primary", Name = "连点示例（安全预览）", Steps = new List<MacroStep> { new MacroStep { Kind = ActionKind.Delay, Number = 10 }, new MacroStep { Kind = ActionKind.Keyboard, KeyCode = 65 }, new MacroStep { Kind = ActionKind.Delay, Number = 20 } } };
                var second = new MacroDefinition { Id = "ui-second", Name = "第二个宏", Steps = new List<MacroStep> { new MacroStep { Kind = ActionKind.Delay, Number = 1 } } };
                window.Draft.Macros.Add(macro); window.Draft.Macros.Add(second);
                var page = Field<MacroPage>(window, "macroPage"); page.ReloadLibrary(macro.Id); Check(page.SaveDraft(), "Seed save"); Pump();
                Test("Navigation has no LITE spacer / first button starts at rail top", () => {
                    var first = Field<Button[]>(window, "navigation")[0]; var top = (StackPanel)first.Parent;
                    Check(top.Children[0] == first && !Find<TextBlock>(window).Any(x => x.Text == "LITE"), "Navigation brand/spacer remains");
                    Check(Math.Abs(first.TranslatePoint(new Point(0, 0), top).Y - first.Margin.Top) < 1, "Navigation kept old brand height");
                });
                Test("Supported and unknown devices hide prose while independent write gates remain", () => {
                    var previous = window.Reading;
                    try {
                        foreach (string language in new[] { "zh", "en" }) {
                            window.Preferences.Language = language; window.RebuildPages(0); Pump(); var device = Field<DevicePage>(window, "devicePage");
                            window.Reading = new MouseBatteryInfo { ProductId = 0x00DF, IsConnected = true, IsWriteSupported = true, IsDpiWriteSupported = true, IsPollingWriteSupported = true, DpiKnown = true, PollingKnown = true, ProtocolStatus = DeviceProtocolStatus.Ready, DpiTrust = CapabilityTrust.UpstreamVerified, PollingTrust = CapabilityTrust.UpstreamVerified, Dpi = 1600, PollingRate = 1000, DeviceKey = "fake-supported" };
                            device.UpdateReading(); Pump(); var hint = Field<TextBlock>(device, "performanceInfo");
                            Check(hint.Visibility == Visibility.Collapsed && hint.Text == "" && Field<Slider>(device, "dpi").IsEnabled && Field<WrapPanel>(device, "ratePanel").Children.Count == 3, "Supported capability UI changed");
                            window.Reading = new MouseBatteryInfo { ProductId = 0xFFFF, IsConnected = true, DeviceKey = "fake-unknown" };
                            device.UpdateReading(); Pump();
                            Check(hint.Visibility == Visibility.Collapsed && hint.Text == "" && !Field<Slider>(device, "dpi").IsEnabled && Field<WrapPanel>(device, "ratePanel").Children.Count == 0, "Unknown remains write-disabled without prose");
                            Capture(window, "unknown-device-" + language);
                            window.Reading.ProductId = 0x00DF; device.UpdateReading(); Pump(); Check(hint.Visibility == Visibility.Collapsed && hint.Text == "", "Stale unknown hint");
                        }
                    } finally { window.Reading = previous; window.Preferences.Language = "zh"; window.RebuildPages(1); Pump(); page = Field<MacroPage>(window, "macroPage"); }
                });
                Test("Trimmed Settings/About and Update cards retain policies in Chinese and English", () => {
                    foreach (string language in new[] { "zh", "en" }) {
                        window.Preferences.Language = language; window.RebuildPages(2); Pump(); var settings = Field<FrameworkElement[]>(window, "views")[2];
                        string text = string.Join("\n", Find<TextBlock>(settings).Select(x => x.Text));
                        Check(text.Contains(language == "zh" ? "独立开源修改版，非雷蛇官方产品。" : "Independent open-source modification, not an official Razer product.") && !text.Contains("DPI 浮窗") && !text.Contains("DPI overlay"), "About prose changed incorrectly");
                        window.Navigate(3); Pump(); var update = Field<UpdatePage>(window, "updatePage"); text = string.Join("\n", Find<TextBlock>(update).Select(x => x.Text));
                        foreach (string removed in new[] { "便携版：支持下载", "安装版：签名验证", "自动安装仅在至少2分钟", "更新会验证项目签名", "更新包必须通过项目签名", "项目更新签名不等于", "Portable: download only", "Installed: signed", "Automatic installation waits", "Updates verify the project", "Installation requires the project", "Project signatures are not" }) Check(!text.Contains(removed), "Removed update prose remains: " + removed);
                        foreach (string policy in new[] { "automaticCheck", "automaticDownload", "automaticInstall", "previews" }) Check(Field<CheckBox>(update, policy).IsVisible, "Policy missing");
                        Check(!Field<CheckBox>(update, "automaticInstall").IsEnabled && Field<Button>(update, "check").IsEnabled, "Portable behavior changed");
                    }
                    window.Preferences.Language = "zh"; window.RebuildPages(1); Pump(); page = Field<MacroPage>(window, "macroPage");
                });
                Test("SaveBar fixed at bottom / saved / recording disables save", () => { var bar = Field<Border>(page, "saveBar"); Check(Grid.GetRow(bar) == 3 && bar.IsVisible, "Bottom save row"); Check(Field<TextBlock>(page, "dirty").Text == "已保存", "Saved state"); page.GetType().GetField("recording", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(page, true); try { CallWithoutPump(page, "UpdateDirty"); Check(!Field<Button>(page, "saveButton").IsEnabled && !page.SaveDraft() && Field<TextBlock>(page, "dirty").Text == "正在录制", "Recording can save"); } finally { page.GetType().GetField("recording", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(page, false); CallWithoutPump(page, "UpdateDirty"); } });
                Test("Dirty binding navigation prompts / continue preserves / save enters", () => { window.DraftDirty = true; Call(page, "SelectTab", true); Check(window.DrawerOpen && !page.IsBindingsTab, "Skipped dirty gate"); var overlay = Field<Grid>(window, "overlay"); Capture(window, "dirty-binding-prompt"); Click(Find<Button>(overlay).First(b => (b.Content as string) == "继续编辑")); Check(window.DraftDirty && !page.IsBindingsTab, "Continue changed draft"); Call(page, "SelectTab", true); Click(Find<Button>(overlay).First(b => (b.Content as string) == "保存并进入按键绑定")); Check(page.IsBindingsTab && !window.DraftDirty && !window.DrawerOpen, "Successful save did not enter"); Call(page, "SelectTab", false); });
                Test("Save failure stays dirty / binding gate never enters", () => { macro.Steps.Add(new MacroStep { Kind = ActionKind.CallMacro, Value = "missing" }); window.DraftDirty = true; Call(page, "SelectTab", true); Click(Find<Button>(Field<Grid>(window, "overlay")).First(b => (b.Content as string) == "保存并进入按键绑定")); Check(window.DrawerOpen && window.DraftDirty && !page.IsBindingsTab && Field<TextBlock>(page, "dirty").Text.StartsWith("保存失败"), "Failed save applied or lost state"); macro.Steps.RemoveAt(macro.Steps.Count - 1); window.CloseDrawer(); Check(page.SaveDraft(), "Valid retry failed"); });
                Test("Ctrl+S command commits pending valid edit / invalid remains pending", () => { window.Width = 1180; Field<ListBox>(page, "steps").SelectedIndex = 0; Call(page, "EditStep", true); Pump(); Find<TextBox>(Field<StackPanel>(page, "editor")).First().Text = "25";
                    Check(ApplicationCommands.Save.InputGestures.OfType<KeyGesture>().Any(g => g.Key == Key.S && g.Modifiers == ModifierKeys.Control), "Ctrl+S gesture missing"); Check(ApplicationCommands.Save.CanExecute(null, window), "Save command unavailable"); ApplicationCommands.Save.Execute(null, window);
                    Check(macro.Steps[0].Number == 25 && !page.HasPendingEdit && !window.DraftDirty, "Pending edit not saved"); Call(page, "EditStep", true); Find<TextBox>(Field<StackPanel>(page, "editor")).First().Text = "bad"; Check(!page.SaveDraft() && page.HasPendingEdit && macro.Steps[0].Number == 25, "Invalid edit saved"); Call(page, "CloseStepEditor"); });
                Test("Action groups common 3 / advanced 5 default expanded", () => { var palette = (StackPanel)page.GetType().GetMethod("CreateActionPalette", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(page, null); Check(palette.Children.OfType<Button>().Count() == 3, "Common actions"); var group = palette.Children.OfType<Expander>().Single(); Check(group.IsExpanded && ((Panel)group.Content).Children.Count == 5, "Advanced actions"); });
                Test("Timeline reorder preserves actions / delete / create", () => { var before = macro.Steps.ToArray(); Call(page, "Reorder", 0, 3); Check(macro.Steps.SequenceEqual(new[] { before[1], before[2], before[0] }), "Reorder changed semantics"); Field<ListBox>(page, "steps").SelectedIndex = 2; Call(page, "DeleteBlock"); Check(macro.Steps.Count == 2, "Delete"); Call(page, "NewMacro"); Check(window.Draft.Macros.Count == 3 && window.DraftDirty, "New macro"); window.Draft.Macros.Remove(page.Selected); page.ReloadLibrary(macro.Id); Check(page.SaveDraft(), "Save after edit"); });
                Test("Normal diagnostics hidden / diagnostics flag visible", () => { Check(Find<TextBlock>(page).Single(t => t.Name == "InputDiagnostic").Visibility == Visibility.Collapsed && Find<Button>(page).Single(b => b.Name == "CopyInputTiming").Visibility == Visibility.Collapsed, "Diagnostics exposed"); DesktopApp.Diagnostics = true; window.RebuildPages(1); page = Field<MacroPage>(window, "macroPage"); Pump(); Check(Find<TextBlock>(page).Single(t => t.Name == "InputDiagnostic").IsVisible && Find<Button>(page).Single(b => b.Name == "CopyInputTiming").IsVisible && window.Macros == null, "Flag not visible / demo not isolated"); Capture(window, "diagnostics-demo"); DesktopApp.Diagnostics = false; window.RebuildPages(1); page = Field<MacroPage>(window, "macroPage"); });
                Test("Binding click / confirmed / disable / enable / unbind", () => { Call(page, "SelectTab", true); var map = Field<MouseBindingMap>(page, "mouseMap"); Click(Field<Dictionary<TriggerKind, Button>>(map, "labels")[TriggerKind.Middle]); Click(Find<Button>(Field<Grid>(window, "overlay")).First(b => (b.Content as string) == "确认绑定")); Check(window.ActiveMacros.Bindings.Single().Trigger == TriggerKind.Middle, "Assignment missing"); string id = window.ActiveMacros.Bindings.Single().Id; Check(window.SetSavedBinding(id, false) && !window.ActiveMacros.Bindings.Single().Enabled, "Disable"); Check(window.SetSavedBinding(id, true) && window.ActiveMacros.Bindings.Single().Enabled, "Enable"); window.RemoveSavedBinding(id); Check(window.ActiveMacros.Bindings.Count == 0, "Unbind"); });
                Test("Drag target accepts saved macro / rejected IDs clear highlight", () => { var map = Field<MouseBindingMap>(page, "mouseMap"); var target = Field<Dictionary<TriggerKind, Button>>(map, "pins")[TriggerKind.Middle]; var lines = Field<Dictionary<TriggerKind, Polyline>>(map, "lines"); var labels = Field<Dictionary<TriggerKind, Button>>(map, "labels"); var over = Drag(target, macro.Id, DragDrop.DragOverEvent); over.RoutedEvent = DragDrop.DragOverEvent; target.RaiseEvent(over); Check(over.Effects == DragDropEffects.Link && lines[TriggerKind.Middle].Stroke == Ui.Accent && labels[TriggerKind.Middle].BorderThickness.Left == 2, "Valid drag highlight"); var bad = Drag(target, "missing", DragDrop.DragOverEvent); bad.RoutedEvent = DragDrop.DragOverEvent; target.RaiseEvent(bad); Check(bad.Effects == DragDropEffects.None && labels[TriggerKind.Middle].BorderThickness.Left == 1, "Invalid drag accepted"); var drop = Drag(target, macro.Id, DragDrop.DropEvent); drop.RoutedEvent = DragDrop.DropEvent; target.RaiseEvent(drop); Check(window.DrawerOpen, "Drop must open confirmation, not silently assign"); window.CloseDrawer(); });
                Test("Keyboard-focus linked pin/card highlighting", () => { var map = Field<MouseBindingMap>(page, "mouseMap"); var labels = Field<Dictionary<TriggerKind, Button>>(map, "labels"); var pins = Field<Dictionary<TriggerKind, Button>>(map, "pins"); var lines = Field<Dictionary<TriggerKind, Polyline>>(map, "lines"); foreach (var b in new[] { labels[TriggerKind.Middle], pins[TriggerKind.Middle] }) { Keyboard.Focus(b); Pump(); Check(lines[TriggerKind.Middle].Stroke == Ui.Accent && pins[TriggerKind.Middle].BorderBrush == Ui.Accent && labels[TriggerKind.Middle].BorderBrush == Ui.Accent, "Linked focus"); } });
                Test("Language rebuild preserves selected macro / step / tab / draft / bindings", () => { Call(page, "SelectTab", false); page.ReloadLibrary(second.Id); page.RestoreStep(0); var draft = window.Draft; window.DraftDirty = true; int count = draft.Macros.Count; foreach (string language in new[] { "en", "zh" }) { window.Preferences.Language = language; window.RebuildPages(1); page = Field<MacroPage>(window, "macroPage"); Check(page.Selected.Id == second.Id && page.SelectedStepIndex == 0 && !page.IsBindingsTab && ReferenceEquals(window.Draft, draft) && window.DraftDirty && count == draft.Macros.Count, "Language state changed"); } Check(page.SaveDraft(), "Restore save"); });
                string root = Path.Combine(artifacts, "fake-install"); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "install.id"), InstallLayout.Marker); var layout = new InstallLayout(root);
                Test("Update one action: check then signed preparation / no install", () => { var fake = new FakeSource(); using (var s = new UpdateSession(window, fake, layout)) { Await(s.CheckAndPrepareUpdate()); Check(fake.Calls.SequenceEqual(new[] { "check", "signed" }) && s.Job != null && !s.Busy && s.Downloaded == s.Job, "Flow did not prepare"); Check(!File.Exists(Path.Combine(root, "current.json")), "Fake flow installed"); } });
                Test("No update does not download", () => { var fake = new FakeSource { Offer = null }; using (var s = new UpdateSession(window, fake, layout)) { Await(s.CheckAndPrepareUpdate()); Check(fake.Calls.SequenceEqual(new[] { "check" }) && s.Job == null, "Downloaded absent update"); } });
                Test("Verification failure has no install job", () => { var fake = new FakeSource { Fail = true }; using (var s = new UpdateSession(window, fake, layout)) { Await(s.CheckAndPrepareUpdate()); Check(s.Job == null && s.Downloaded == null && !s.Busy && s.Status.Contains("失败"), "Failure shown as ready"); } });
                Test("Cancellation / duplicate UI clicks cannot prepare stale job", () => { var fake = new FakeSource { Wait = true }; using (var s = new UpdateSession(window, fake, layout)) { var task = s.CheckAndPrepareUpdate(); while (!fake.Calls.Contains("signed")) Pump(10); Await(s.CheckAndPrepareUpdate()); s.Cancel(); Await(task); Check(s.Job == null && !s.Busy && fake.Calls.Count == 2, "Cancel or concurrency failed"); } });
                Test("Portable checks only / manual download no execution", () => { var fake = new FakeSource(); using (var s = new UpdateSession(window, fake)) { Await(s.CheckAndPrepareUpdate()); Check(fake.Calls.SequenceEqual(new[] { "check" }) && s.Job == null, "Portable auto downloaded"); Await(s.Download(true)); Check(fake.Calls.Last() == "portable" && s.Job == null && s.Downloaded != null, "Portable installed"); } });
                Test("Safety block reason remains dirty/recording/running guarded", () => { window.DraftDirty = true; Check(window.UpdateBlockReason() != null, "Dirty not blocked"); window.DraftDirty = false; foreach (bool state in new[] { true, false }) { page.GetType().GetField("recording", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(page, state); Check((window.UpdateBlockReason() != null) == state, "Recording guard"); } });
                Test("Release notes max4 / skips technical content / plain text / original unchanged", () => { string text = "# Release\n- Desktop27/27\n- TID 0x1F\n- 优化宏编辑。\n- **改善绑定。**\n- [改进主题](https://example.invalid)\n- 简化更新。\n- 第五项。"; string formatted = ReleaseNotesFormatter.Format(text); Check(formatted.Split('\n').Length == 4 && !formatted.Contains("TID") && !formatted.Contains("27/27") && !formatted.Contains("https") && !formatted.Contains("第五"), "Notes filtering"); Check(ReleaseNotesFormatter.Format(null).Contains("修复了一些已知问题"), "Notes fallback"); });
                Test("Installed UI check-and-prepare / direct policies / final confirmation entry", () => {
                    var previous = window.Updates; var fake = new FakeSource(); fake.Offer.Notes = "- 优化宏编辑。\n- 改进按键绑定。";
                    using (var session = new UpdateSession(window, fake, layout)) {
                        try {
                            window.Updates = session; window.RebuildPages(3); Pump(); var ui = Field<UpdatePage>(window, "updatePage");
                            Check((string)Field<Button>(ui, "check").Content == "检查并更新" && !Find<Expander>(ui).Any(), "Ordinary update UI policies hidden");
                            foreach (string policy in new[] { "automaticCheck", "automaticDownload", "automaticInstall", "previews" }) Check(Field<CheckBox>(ui, policy).IsVisible, "Policy is not directly visible: " + policy);
                            Click(Field<Button>(ui, "check")); while (session.Busy) Pump(10);
                            Check(session.Job != null && Field<Button>(ui, "install").IsVisible && (string)Field<Button>(ui, "install").Content == "更新并重启" && Field<Button>(ui, "check").Visibility == Visibility.Collapsed && Field<Button>(ui, "download").Visibility == Visibility.Collapsed, "Verified job UI: " + session.Job + "; status=" + session.Status + "; installVisible=" + Field<Button>(ui, "install").IsVisible + "; pageVisible=" + ui.IsVisible + "; calls=" + string.Join(",", fake.Calls));
                            foreach (int width in new[] { 1180, 700 }) { window.Width = width; window.Notice(""); Pump(); Within(ui); Capture(window, "installed-ready-" + width); }
                            Check(!File.Exists(Path.Combine(root, "current.json")), "UI unexpectedly installed");
                        } finally { window.Updates = previous; window.RebuildPages(1); page = Field<MacroPage>(window, "macroPage"); }
                    }
                });
                Test("Cancel during check never starts download", () => { var fake = new FakeSource(); using (var s = new UpdateSession(window, fake, layout)) { var task = s.CheckAndPrepareUpdate(); s.Cancel(); Await(task); Check(fake.Calls.SequenceEqual(new[] { "check" }) && s.Job == null && !s.Busy, "Cancelled check prepared update"); } });
                Test("Preview / unsigned fallback cannot produce installation job", () => { var fake = new FakeSource(); fake.Offer.SignedManifestUrl = null; using (var s = new UpdateSession(window, fake, layout)) { Await(s.CheckAndPrepareUpdate()); Check(fake.Calls.SequenceEqual(new[] { "check", "portable" }) && s.Job == null && s.Downloaded != null, "Unsigned offer became installable"); } });
                Test("Restored daytime semantic palette / green accent", () => {
                    var expected = new Dictionary<string, string> { { "WindowBackground", "#FAFAFA" }, { "NavigationBackground", "#FAFAFA" }, { "PageBackground", "#F2F2F2" }, { "CardBackground", "#FFFFFF" }, { "CardBorder", "#DADADA" }, { "TextPrimary", "#1B1B1B" }, { "TextSecondary", "#5F5F5F" }, { "SelectionBackground", "#E6E6E6" }, { "ControlBorder", "#B8B8B8" }, { "Accent", "#44D62C" }, { "SelectionIndicator", "#44D62C" } };
                    foreach (var token in expected) Check(ThemeTokens.Fluent[token.Key] == token.Value, "Daytime color changed: " + token.Key);
                });
                Test("Advanced actions neutral inline info / command risk retained", () => {
                    var palette = (StackPanel)page.GetType().GetMethod("CreateActionPalette", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(page, null);
                    var advanced = (StackPanel)palette.Children.OfType<Expander>().Single().Content;
                    foreach (StackPanel row in advanced.Children) {
                        Check(row.Orientation == Orientation.Horizontal && row.Children.Count == 2 && row.Children[0] is Button, "Info layout is not inline");
                        var tip = (TextBlock)row.Children[1]; Check(tip.Text == "ⓘ" && tip.Foreground == Ui.Muted && tip.Margin.Left >= 4 && tip.Margin.Left <= 6, "Inconsistent info icon");
                    }
                    var command = (StackPanel)advanced.Children[2]; var tooltip = (ToolTip)((FrameworkElement)command.Children[1]).ToolTip;
                    Check(((StackPanel)tooltip.Content).Children.OfType<TextBlock>().Any(t => t.Text.Contains("可信")), "Command safety explanation removed");
                });
                Test("Binding shelf shows unsaved macros but still refuses assignment", () => {
                    window.Width = 1180; Call(page, "SelectTab", true);
                    var unsaved = new MacroDefinition { Id = "ui-unsaved", Name = "未保存宏" }; window.Draft.Macros.Add(unsaved);
                    try {
                        Call(page, "RefreshBindingMap"); var workspace = Field<FrameworkElement>(page, "bindingWorkspace");
                        Check(Field<ListBox>(page, "mappingLibrary").Items.OfType<ListBoxItem>().Any(r => (string)r.Tag == unsaved.Id), "Unsaved macro not shown");
                        Check(!Find<TextBox>(workspace).Any() && !Find<Button>(workspace).Any(b => (b.Content as string) == "+ 新建宏"), "Search or create entry remains");
                        Check(!window.IsMacroSaved(unsaved.Id), "Unsaved qualification widened");
                        var map = Field<MouseBindingMap>(page, "mouseMap"); var target = Field<Dictionary<TriggerKind, Button>>(map, "pins")[TriggerKind.Middle];
                        var over = Drag(target, unsaved.Id, DragDrop.DragOverEvent); over.RoutedEvent = DragDrop.DragOverEvent; target.RaiseEvent(over); Check(over.Effects == DragDropEffects.None, "Unsaved drag accepted");
                        var advanced = Find<Button>(Field<Border>(page, "modelCard")).Single(b => b.Name == "AdvancedBindings");
                        Check(advanced.Style == Application.Current.FindResource(typeof(Button)), "Advanced entry lost the themed secondary style");
                        var top = (Panel)Field<TextBlock>(page, "bindingStatus").Parent; Check(top.Children.Count == 3 && top.Children[0] is CheckBox && ((FrameworkElement)top.Children[2]).Name == "RetryBindingSave", "Global binding header contains unrelated controls");
                    } finally { window.Draft.Macros.Remove(unsaved); Call(page, "RefreshBindingMap"); Call(page, "SelectTab", false); }
                });
                Test("Advanced keyboard binding add / edit / save / unbind uses existing callbacks", () => {
                    window.Width = 1180; window.Height = 840; window.Navigate(1); Call(page, "OpenAdvancedBindings"); var overlay = Field<Grid>(window, "overlay");
                    Click(Find<Button>(overlay).Single(b => (b.Content as string) == "+ 新增绑定"));
                    var key = Find<ComboBox>(overlay).Single(c => c.Items.OfType<MacroLabels.KeyChoice>().Any()); key.SelectedItem = key.Items.OfType<MacroLabels.KeyChoice>().Single(k => k.Code == 65);
                    Find<CheckBox>(overlay).Single(c => (c.Content as string) == "Ctrl").IsChecked = true;
                    Click(Find<Button>(overlay).Single(b => (b.Content as string) == "确认绑定"));
                    Check(!window.DrawerOpen, "Keyboard confirmation did not close");
                    var binding = window.ActiveMacros.Bindings.Single(b => b.Trigger == TriggerKind.Keyboard && b.KeyCode == 65);
                    Check(binding.Modifiers == KeyModifiers.Control && binding.MacroId == page.Selected.Id, "Keyboard binding was not saved");
                    foreach (string t in new[] { "classic", "fluent" }) foreach (int w in new[] { 1180, 700 }) {
                        Ui.ApplyTheme(t); window.Width = w; Pump(); Call(page, "OpenAdvancedBindings"); Capture(window, t + "-" + w + "-advanced-populated"); window.CloseDrawer();
                    }
                    window.Width = 1180; Pump(); Call(page, "OpenAdvancedBindings"); var list = Field<ListBox>(page, "bindings"); list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(r => ((MacroBinding)r.Tag).Id == binding.Id);
                    Click(Find<Button>(overlay).Single(b => (b.Content as string) == "编辑"));
                    Find<CheckBox>(overlay).Single(c => (c.Content as string) == "Ctrl").IsChecked = false; Find<CheckBox>(overlay).Single(c => (c.Content as string) == "Shift").IsChecked = true;
                    Click(Find<Button>(overlay).Single(b => (b.Content as string) == "确认绑定")); Check(window.ActiveMacros.Bindings.Single(b => b.Id == binding.Id).Modifiers == KeyModifiers.Shift, "Edit not saved");
                    Call(page, "OpenAdvancedBindings"); list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(r => ((MacroBinding)r.Tag).Id == binding.Id);
                    Click(Find<Button>(overlay).Single(b => (b.Content as string) == "解除绑定")); Check(!window.ActiveMacros.Bindings.Any(b => b.Id == binding.Id), "Advanced unbind did not take effect"); window.CloseDrawer();
                });
                Test("Open advanced drawer follows live layout / ordinary width remains 390", () => {
                    window.Width = 1180; window.Height = 840; Call(page, "OpenAdvancedBindings"); var overlay = Field<Grid>(window, "overlay"); var drawer = Field<Border>(window, "activeDrawer");
                    foreach (int w in new[] { 1180, 960, 760, 700, 1180 }) { window.Width = w; Pump(); double expected = w >= 1100 ? 580 : w >= 820 ? 520 : overlay.ActualWidth - 24; Check(Math.Abs(drawer.ActualWidth - expected) < 2 && drawer.ActualWidth <= overlay.ActualWidth, "Advanced width on resize: " + w + " / " + drawer.ActualWidth); }
                    window.CloseDrawer(); window.OpenDrawer("Normal", Ui.Text("No changes")); Pump(); Check(Math.Abs(Field<Border>(window, "activeDrawer").ActualWidth - 390) < 1, "Ordinary drawer width changed"); window.CloseDrawer();
                });
                foreach (string theme in new[] { "classic", "fluent" }) foreach (int width in new[] { 1180, 960, 760, 700 }) {
                    int w = width; string t = theme;
                    Test(t + " " + w + " macro/binding/update layouts", () => {
                        window.Navigate(2); Pump(); // Materialize the settings visual tree before using its real selector.
                        Find<ComboBox>(Field<FrameworkElement[]>(window, "views")[2]).Single(c => c.Name == "ThemeSelector").SelectedIndex = t == "fluent" ? 1 : 0;
                        Ui.ApplyTheme(t); window.Width = w; window.Height = w == 1180 ? 840 : w == 960 ? 760 : w == 760 ? 650 : 560;
                        Call(page, "SelectTab", false); window.Navigate(1); window.Notice(""); Pump(); Within(page);
                        foreach (var nav in Field<Button[]>(window, "navigation")) {
                            Check(nav.Content is StackPanel && Find<Viewbox>(nav).Count() == 1 && Find<System.Windows.Shapes.Path>(nav).Count() == 1, "Navigation is not one vector");
                            Check(Find<TextBlock>((DependencyObject)nav.Content).Count() == (w < 820 ? 0 : 1), "Navigation label mode");
                            Check(ReferenceEquals(Find<System.Windows.Shapes.Path>(nav).Single().Fill, nav.Foreground), "Icon foreground detached from button");
                            Check(Find<Viewbox>(nav).Single().ActualWidth == 20, "Vector size differs");
                        }
                        Check(Field<Border>(page, "saveBar").TranslatePoint(new Point(0, Field<Border>(page, "saveBar").ActualHeight), page).Y <= page.ActualHeight + 1, "SaveBar clipped vertically"); Capture(window, t + "-" + w + "-macro");
                        if (w < 820) { Click(Field<Button>(page, "compactPaletteButton")); Pump(); Capture(window, t + "-" + w + "-advanced-actions"); window.CloseDrawer(); }
                        Call(page, "SelectTab", true); Pump(); Within(page);
                        if (w < 820) { Check(!Field<Expander>(page, "compactShelf").IsExpanded, "Compact shelf not collapsed"); Field<Expander>(page, "compactShelf").IsExpanded = true; Pump(); Check(Field<Border>(page, "macroShelf").Height == 200 && Field<ListBox>(page, "mappingLibrary").ActualHeight >= 80, "Compact shelf usability"); Capture(window, t + "-" + w + "-bindings-shelf"); Field<Expander>(page, "compactShelf").IsExpanded = false; Pump(); }
                        else { Check(Field<Grid>(page, "mappingLayout").ColumnDefinitions[0].Width.Value == (w >= 1100 ? 280 : 230), "Shelf width"); Check(Field<Expander>(page, "compactShelf").Visibility == Visibility.Collapsed && Field<Border>(page, "macroShelf").Parent is Grid, "Wide shelf is still a chooser"); }
                        Capture(window, t + "-" + w + "-bindings");
                        var scroll = (ScrollViewer)Field<FrameworkElement>(page, "bindingWorkspace"); scroll.ScrollToEnd(); Pump(); Capture(window, t + "-" + w + "-bindings-model"); scroll.ScrollToTop();
                        Call(page, "OpenAdvancedBindings"); var overlay = Field<Grid>(window, "overlay"); Within((FrameworkElement)overlay.Children[0]); Capture(window, t + "-" + w + "-advanced-bindings"); ((ScrollViewer)Field<Border>(window, "activeDrawer").Child).ScrollToEnd(); Pump(); Capture(window, t + "-" + w + "-advanced-bindings-bottom"); window.CloseDrawer();
                        window.Navigate(2); Pump(); var settings = Field<FrameworkElement[]>(window, "views")[2]; Check(!Find<TextBlock>(settings).Any(x => x.Text.Contains("蓝灰") || x.Text.Contains("charcoal / green")), "Theme color legend remains"); Within(settings); Capture(window, t + "-" + w + "-settings");
                        window.Navigate(3); Pump(); Within(Field<FrameworkElement[]>(window, "views")[3]); Capture(window, t + "-" + w + "-update");
                        window.Navigate(0); Pump(); var deviceView = Field<FrameworkElement[]>(window, "views")[0]; Within(deviceView);
                        var performance = Find<Border>(deviceView).Single(b => b.Child is StackPanel && ((StackPanel)b.Child).Children.Contains(Field<WrapPanel>(deviceView, "ratePanel")));
                        Check(performance.VerticalAlignment == VerticalAlignment.Top && double.IsNaN(performance.Height) && Math.Abs(performance.ActualHeight - performance.DesiredSize.Height + performance.Margin.Top + performance.Margin.Bottom) < 2, "Performance card retained stretched blank height");
                        Capture(window, t + "-" + w + "-device");
                    });
                }
                Test("Bounded rapid navigate / resize / theme / drawer smoke", () => { Ui.ReducedMotion = false; var clock = System.Diagnostics.Stopwatch.StartNew(); for (int i = 0; i < 24; i++) { window.CloseDrawer(); window.Width = i % 2 == 0 ? 760 : 1180; Ui.ApplyTheme(i % 2 == 0 ? "fluent" : "classic"); window.Navigate(i % 4); window.OpenDrawer("Preview", Ui.Text("No side effects")); Pump(10); window.CloseDrawer(); } Ui.ReducedMotion = true; Check(clock.ElapsedMilliseconds < 10000 && !window.DrawerOpen, "UI smoke hung"); });
            } catch (Exception e) { failed++; results.Add("FAIL\tHarness\t" + e); Console.WriteLine(e); }
            finally { DesktopApp.Diagnostics = false; if (window != null) window.ClosePreview(); File.WriteAllLines(Path.Combine(artifacts, "results.tsv"), results); Console.WriteLine("SECOND STAGE UI: " + passed + " PASS / " + failed + " FAIL"); app.Shutdown(); }
            return failed == 0 ? 0 : 1;
        }
    }
}
