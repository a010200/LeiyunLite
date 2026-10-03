using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    internal static partial class DesktopTests
    {
        private sealed class SafetyStore : IMacroStore
        {
            internal MacroLibrary Saved = new MacroLibrary(); internal bool Fail;
            public string FilePath { get { return "isolated-in-memory"; } }
            public MacroLibrary Load() { return Saved.Clone(); }
            public void Save(MacroLibrary value) { if (Fail) throw new IOException("Simulated disk failure"); Saved = value.Clone(); }
        }
        private sealed class SafetyOutput : IMacroOutput
        {
            internal readonly ConcurrentQueue<string> Events = new ConcurrentQueue<string>();
            public void Key(int key, bool down) { Events.Enqueue(key + (down ? "+" : "-")); }
            public void MouseButton(MouseAction button, bool down) { Events.Enqueue(button + (down ? "+" : "-")); }
            public void Wheel(int notches) { Events.Enqueue("wheel"); }
            public void Text(string text, CancellationToken token) { Events.Enqueue(text); }
            public void Launch(string target, string args, bool command) { throw new Exception("No process execution in safety tests"); }
        }
        private static MacroLibrary WheelLibrary()
        {
            var l = new MacroLibrary(); var m = new MacroDefinition { Name = "测试连点", Steps = new List<MacroStep> { new MacroStep { Kind = ActionKind.Keyboard, KeyCode = 65, Press = PressMode.Down }, new MacroStep { Kind = ActionKind.Delay, Number = 60000 } } };
            l.Macros.Add(m); l.Bindings.Add(new MacroBinding { MacroId = m.Id, Trigger = TriggerKind.WheelUp, Mode = RunMode.Toggle, SuppressOriginal = true }); return l;
        }
        private static void BindingSafety()
        {
            var store = new SafetyStore { Saved = WheelLibrary() }; var output = new SafetyOutput();
            using (var controller = new MacroController(store, output, false)) {
                var stale = controller.Snapshot(); var router = Field<BindingRouter>(controller, "router"); var binding = stale.Bindings[0];
                Check(router.Handle(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true }), "Bound wheel suppressed");
                Check(SpinWait.SpinUntil(() => output.Events.Contains("65+"), 2000), "Macro held a key");
                controller.RemoveBinding(binding.Id); // No SaveDefinitions/Save call follows.
                Check(SpinWait.SpinUntil(() => !controller.IsRunning, 2000), "Unbind cancels running macro");
                Check(output.Events.Contains("65-"), "Unbind releases the simulated held key");
                int count = output.Events.Count;
                for (int i = 0; i < 1000; i++) Check(!router.Handle(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true }), "Removed wheel stays original");
                Check(output.Events.Count == count && !router.SuppressWheel(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true }), "No retrigger / residual wheel suppression");
                Check(store.Saved.Bindings.Count == 0, "Unbind persisted without second save");
                stale.Macros[0].Name = "edited after unbind"; controller.SaveDefinitions(stale);
                Check(controller.Snapshot().Bindings.Count == 0 && store.Saved.Bindings.Count == 0, "Stale draft cannot resurrect revoked binding");
                controller.ApplyBinding(binding, false); controller.SetBindingEnabled(binding.Id, false);
                Check(!controller.Snapshot().Bindings[0].Enabled && !router.Handle(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true }), "Individual disable immediate");
                controller.SetBindingEnabled(binding.Id, true); controller.SetBindingsEnabled(false);
                Check(!controller.Snapshot().BindingsEnabled && !store.Saved.BindingsEnabled, "Master disable immediately persisted");
            }
            using (var reloaded = new MacroController(store, new SafetyOutput(), false)) Check(!reloaded.Snapshot().BindingsEnabled, "Restart retains disabled state");
            string path = Path.Combine(artifacts, "r4-" + Guid.NewGuid().ToString("N"), "macros.xml"); var disk = new MacroStore(path); disk.Save(WheelLibrary());
            using (var c = new MacroController(disk, new SafetyOutput(), false)) c.RemoveBinding(c.Snapshot().Bindings[0].Id);
            using (var c = new MacroController(disk, new SafetyOutput(), false)) Check(c.Snapshot().Bindings.Count == 0, "Real isolated XML reload stays unbound");
        }
        private static void BindingSaveFailures()
        {
            var store = new SafetyStore { Saved = WheelLibrary() };
            using (var c = new MacroController(store, new SafetyOutput(), false)) {
                var draft = c.Snapshot(); draft.Macros[0].Name = "UNSAVED"; store.Fail = true;
                bool failedSave = false; try { c.RemoveBinding(draft.Bindings[0].Id); } catch (IOException) { failedSave = true; }
                Check(failedSave && c.SafetySavePending && c.Snapshot().Bindings.Count == 0, "Failure keeps runtime revoked and persistence visibly pending");
                Check(c.Snapshot().Macros[0].Name != "UNSAVED" && store.Saved.Bindings.Count == 1, "Unbind does not save unrelated draft; disk failure not hidden");
                Check(!Field<BindingRouter>(c, "router").Handle(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true }), "Failure is fail-closed for this session");
                try { c.ApplyBinding(draft.Bindings[0], false); } catch (IOException) { }
                Check(c.Snapshot().Bindings.Count == 0 && c.SafetySavePending, "Failed new assignment does not reactivate input");
                store.Fail = false; c.RetrySafetySave(); Check(!c.SafetySavePending && store.Saved.Bindings.Count == 0, "Retry persists runtime safety snapshot only");
                c.SaveDefinitions(draft); Check(c.Snapshot().Bindings.Count == 0, "Later macro save preserves revocation");
                c.ApplyBinding(draft.Bindings[0], false); c.RemoveMacro(draft.Macros[0].Id);
                Check(c.Snapshot().Macros.Count == 0 && c.Snapshot().Bindings.Count == 0 && store.Saved.Bindings.Count == 0, "Macro removal also revokes its bindings");
            }
        }
        private sealed class RaceRunner : IMacroRunner
        {
            internal int Starts; public bool IsRunning { get { return false; } } public string ActiveBinding { get { return null; } }
            public bool Start(MacroLibrary l, string m, string b, bool repeat, int delay) { Interlocked.Increment(ref Starts); return true; }
            public void Stop() { }
        }
        private static void BindingReplacementRace()
        {
            var runner = new RaceRunner(); var router = new BindingRouter(runner); router.Configure(WheelLibrary());
            var stop = new CancellationTokenSource();
            var input = Task.Run(() => { while (!stop.IsCancellationRequested) router.Handle(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true }); });
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref runner.Starts) > 100, 2000), "Concurrent input started");
            router.ReplaceAndStop(new MacroLibrary()); int atReturn = Volatile.Read(ref runner.Starts);
            for (int i = 0; i < 10000; i++) router.Handle(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true });
            stop.Cancel(); Check(input.Wait(2000) && runner.Starts == atReturn, "No old binding starts after replacement returns"); stop.Dispose();
        }
        private static string OfferJson(string tag, string digest, bool preview = true)
        {
            string file = "LeiyunLite-" + tag + "-win-x64.zip";
            return "{\"tag_name\":\"" + tag + "\",\"prerelease\":" + (preview ? "true" : "false") + ",\"assets\":[{\"name\":\"" + file + "\",\"state\":\"uploaded\",\"size\":100,\"digest\":\"sha256:" + digest + "\",\"browser_download_url\":\"https://github.com/a010200/LeiyunLite/releases/download/" + tag + "/" + file + "\"}]}";
        }
        private static void UpdateSelection()
        {
            string hash = new string('a', 64); string json = "[" + OfferJson("v1.0.0-r3", hash) + "," + OfferJson("v1.0.0-r10", hash) + "," + OfferJson("v1.0.0-r5", hash) + "]";
            Check(ReleaseUpdateService.Select(json, true, "1.0.0-r4").Tag == "v1.0.0-r10", "Numeric revision sorting");
            Check(ReleaseUpdateService.Select(json, false, "1.0.0-r4") == null, "Stable channel excludes previews");
            Check(ReleaseUpdateService.Select("[" + OfferJson("v1.0.0", hash, false) + "]", false, "1.0.0-r4") != null, "Stable version supersedes prerelease");
            Check(ReleaseUpdateService.Select("[" + OfferJson("v1.0.0-r3", hash) + "]", true, "1.0.0-r4") == null, "No downgrade to published R3");
            Check(ReleaseUpdateService.Select(json.Replace("a010200", "attacker"), true, "1.0.0-r4") == null, "Foreign assets ignored");
            Check(ReleaseUpdateService.Select(json.Replace("win-x64", "win-arm64"), true, "1.0.0-r4") == null, "Wrong architecture ignored");
            Check(ReleaseUpdateService.Select(json.Replace("\"prerelease\":true", "\"draft\":true,\"prerelease\":true"), true, "1.0.0-r4") == null, "Drafts excluded");
            Check(ReleaseVersion.Parse("v1.0.0-r99999999999999999999") == null, "Version overflow rejected");
            Check(!ReleaseUpdateService.AllowedDownloadUri(new Uri("https://github.com.attacker.test/file")) && !ReleaseUpdateService.AllowedDownloadUri(new Uri("http://github.com/a010200/LeiyunLite/releases/download/a/b")), "Redirect allowlist and HTTPS enforced");
            Check(ReleaseUpdateService.ParseChecksum(hash + "  app.zip\n", "app.zip") == hash, "Exact checksum filename");
            bool duplicate = false; try { ReleaseUpdateService.ParseChecksum(hash + "  app.zip\n" + hash + "  app.zip", "app.zip"); } catch (InvalidDataException) { duplicate = true; } Check(duplicate, "Ambiguous checksums rejected");
        }
        private static void UpdateIntegrity()
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("isolated release payload"); string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            string path = Path.Combine(artifacts, "verified-" + Guid.NewGuid().ToString("N") + ".part");
            using (var stream = new MemoryStream(data)) ReleaseUpdateService.CopyVerified(stream, path, data.Length, hash, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(File.ReadAllBytes(path).SequenceEqual(data), "Verified download bytes preserved");
            foreach (bool corrupt in new[] { true, false }) {
                bool rejected = false; string invalid = Path.Combine(artifacts, "invalid-" + Guid.NewGuid().ToString("N") + ".part");
                try { using (var stream = new MemoryStream(data)) ReleaseUpdateService.CopyVerified(stream, invalid, corrupt ? data.Length : 1, corrupt ? new string('0', 64) : hash, null, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Corrupt/oversized input rejected before usable ZIP publication");
            }
            var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
            try { using (var stream = new MemoryStream(data)) ReleaseUpdateService.CopyVerified(stream, Path.Combine(artifacts, Guid.NewGuid().ToString("N") + ".part"), data.Length, hash, null, cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Download cancellation honored"); cancelled.Dispose();
        }
        private static void RevisionFourUi(ShellWindow window)
        {
            window.Width = 1180; window.Height = 840; window.Preferences.Theme = "dark"; Ui.ApplyTheme("dark"); window.RebuildPages(1); Pump();
            var page = Field<MacroPage>(window, "macroPage"); page.Width = double.NaN;
            Invoke(page, "NewMacro"); page.Selected.Name = "滚轮连点测试"; page.Selected.Steps.Add(new MacroStep { Kind = ActionKind.Keyboard, KeyCode = 65 }); window.SaveMacros();
            string id = page.Selected.Id; Invoke(page, "SelectTab", true); Pump();
            Invoke(page, "OpenMouseBinding", TriggerKind.WheelUp, id); Pump();
            Check(window.ActiveMacros.Bindings.Count == 0 || !window.ActiveMacros.Bindings.Any(b => b.MacroId == id), "Opening assignment has no side effect");
            window.CloseDrawer(); Check(!window.ActiveMacros.Bindings.Any(b => b.MacroId == id), "Cancel leaves runtime unchanged");
            var map = Field<MouseBindingMap>(page, "mouseMap"); var labels = Field<Dictionary<TriggerKind, Button>>(map, "labels");
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                new object[] { new DataObject(MouseBindingMap.MacroFormat, id), DragDropKeyStates.LeftMouseButton, DragDropEffects.Link, labels[TriggerKind.WheelUp], new Point(5, 5) }, null);
            args.RoutedEvent = DragDrop.DropEvent;
            labels[TriggerKind.WheelUp].RaiseEvent(args); Pump();
            Check(window.DrawerOpen, "Dropping saved macro opens assignment confirmation");
            var overlay = Field<Grid>(window, "overlay");
            Descendants<Button>(overlay).First(b => (b.Content as string ?? "").StartsWith("确认")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(window.ActiveMacros.Bindings.Any(b => b.MacroId == id && b.Trigger == TriggerKind.WheelUp), "Confirm persists active assignment without global Save");
            Check(Field<Dictionary<TriggerKind, TextBlock>>(map, "names")[TriggerKind.WheelUp].Text == "滚轮连点测试", "Hotspot shows macro name");
            Snapshot(window, "r4-bindings-dark.png");
            Invoke(page, "OpenMouseBinding", TriggerKind.WheelUp, null); Pump();
            Descendants<Button>(overlay).First(b => (b.Content as string ?? "").StartsWith("解除绑定")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!window.ActiveMacros.Bindings.Any(b => b.MacroId == id) && Field<Dictionary<TriggerKind, TextBlock>>(map, "names")[TriggerKind.WheelUp].Text == "向上滚动", "Unbind instantly restores original label and active state");
            window.SaveMacros(); Check(!window.ActiveMacros.Bindings.Any(b => b.MacroId == id), "Global save does not resurrect binding");
            Invoke(page, "OpenMouseBinding", TriggerKind.Left, id); Pump();
            Descendants<ComboBox>(overlay).Last().SelectedIndex = 0;
            Descendants<Button>(overlay).First(b => (b.Content as string ?? "").StartsWith("确认")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.DrawerOpen && !window.ActiveMacros.Bindings.Any(b => b.Trigger == TriggerKind.Left), "Left replacement requires explicit risk acknowledgment"); window.CloseDrawer();
            window.Width = 760; Pump(); // Compact is based on window DIP (<820), not page width.
            var shelf = Field<Expander>(page, "compactShelf");
            Check(!shelf.IsExpanded, "Compact shelf starts folded so the mouse model remains primary");
            shelf.IsExpanded = true; Pump();
            Check(Field<ListBox>(page, "mappingLibrary").ActualHeight >= 80, "Narrow library remains usable and scrollable");
            Snapshot(window, "r4-bindings-narrow.png");
            var mappingScroll = (ScrollViewer)Field<FrameworkElement>(page, "bindingWorkspace");
            Check(mappingScroll.ScrollableHeight > 0 && mappingScroll.ActualWidth > 400, "Expanded compact library and model can be scrolled");
            mappingScroll.ScrollToEnd(); Pump(); Check(Math.Abs(mappingScroll.VerticalOffset - mappingScroll.ScrollableHeight) < 1, "Mouse model bottom remains reachable"); Snapshot(window, "r4-bindings-narrow-bottom.png");
            window.Width = 1180; window.Preferences.Theme = "light"; Ui.ApplyTheme("light"); window.RebuildPages(1); page = Field<MacroPage>(window, "macroPage"); Invoke(page, "SelectTab", true); Pump();
            Check(Ui.Light && ((System.Windows.Media.SolidColorBrush)Ui.Foreground).Color == ThemeTokens.ColorFor("TextPrimary", "fluent"), "Legacy light binding view maps to the Fluent palette");
            Snapshot(window, "r4-bindings-light.png");
            window.Preferences.Theme = "dark"; Ui.ApplyTheme("dark"); window.RebuildPages(0); Pump();
            var flyout = new TrayFlyout(window, false, false, b => { }); flyout.ShowActivated = false; flyout.Show(); Pump();
            try {
                Check(Field<TextBlock>(flyout, "performance").Text.Contains("1000 Hz"), "Tray polling readback shown");
                Invoke(window, "OnDpi", new DpiReading { DeviceKey = window.Reading.DeviceKey, Dpi = 1600, Stage = 1, Count = 1 }); Pump();
                Check(Field<TextBlock>(flyout, "performance").Text.Contains("1600"), "Open tray follows DPI monitor event without HID query"); Snapshot(flyout, "r4-tray-card.png");
            } finally { flyout.Close(); }
            window.Navigate(3); Pump(); var updater = Field<UpdatePage>(window, "updatePage"); window.Updates.Check(true).GetAwaiter().GetResult(); Pump();
            Check(Field<TextBlock>(updater, "status").Text.Contains("演示"), "Demo update check never contacts network"); Snapshot(window, "r4-updates.png");
        }
    }
}
