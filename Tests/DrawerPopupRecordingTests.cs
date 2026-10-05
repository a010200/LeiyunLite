using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RazerBatteryTray.Macros;
using Path = System.IO.Path;

namespace RazerBatteryTray.Desktop
{
    // WPF routed-event integration only: no native input injection, installed hooks,
    // personal configuration, HID runtime, updater network or disk-backed macro store.
    internal static class DrawerPopupRecordingTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static ShellWindow window;
        private static MacroPage page;
        private static MacroController controller;
        private static MemoryStore store;
        private static string artifacts;
        private static int failures;
        private static string only;
        private static readonly List<string> results = new List<string>();
        private static T Get<T>(object obj, string name) { return (T)obj.GetType().GetField(name, Flags).GetValue(obj); }
        private static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name, Flags).Invoke(obj, args); }
        private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
        private static void Pump(int ms = 35)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static void Test(string name, Action test)
        {
            if (!string.IsNullOrEmpty(only) && !name.Contains(only)) return;
            try { test(); results.Add("PASS\t" + name); }
            catch (Exception ex) { failures++; results.Add("FAIL\t" + name + "\t" + ex); }
            finally { if (page.IsRecording) page.EndRecording(true); window.CloseDrawer(); Pump(); }
            Console.WriteLine(results.Last());
        }
        private static void Open(string method, params object[] args) { Call(page, method, args); Pump(); Check(window.DrawerOpen, "Drawer did not open: " + method); }
        private static Popup Drop(ComboBox combo)
        {
            combo.IsDropDownOpen = true; Pump();
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            Check(popup != null && popup.IsOpen, "Actual template Popup unavailable"); return popup;
        }
        private static MouseButtonEventArgs OverlayClick(UIElement source)
        {
            var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = source };
            Get<Grid>(window, "overlay").RaiseEvent(e);
            Check(ReferenceEquals(e.OriginalSource, source), "Harness lost OriginalSource"); return e;
        }
        private static void SendKey(UIElement source, Key key)
        {
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
                { RoutedEvent = UIElement.PreviewKeyDownEvent };
            source.RaiseEvent(e);
            if (!e.Handled) { e.RoutedEvent = UIElement.KeyDownEvent; source.RaiseEvent(e); }
        }
        private static void Capture(string name, FrameworkElement visual = null)
        {
            window.UpdateLayout(); visual = visual ?? window;
            var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new FileStream(Path.Combine(artifacts, name + ".png"), FileMode.CreateNew)) png.Save(stream);
        }
        private static void SelectionRound(string method, object[] args, int count, bool capture = false)
        {
            Open(method, args); var combos = Find<ComboBox>(Get<Border>(window, "activeDrawer")).ToArray();
            Check(combos.Length == count, "Unexpected ComboBox count in " + method);
            for (int c = 0; c < combos.Length; c++) {
                var combo = combos[c]; int original = combo.SelectedIndex;
                for (int target = 0; target < Math.Min(combo.Items.Count, 3); target++) {
                    var popup = Drop(combo); var row = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(target);
                    Check(row != null && (bool)Call(window, "InsideDrawer", row), "Popup item is not owned by current Drawer");
                    var e = OverlayClick(row); Check(window.DrawerOpen && !e.Handled, "Preview cancelled selection");
                    if (capture && c == 2 && target == 1) { Capture("drawer-combo-open"); Capture("drawer-combo-popup", (FrameworkElement)popup.Child); }
                    // Run WPF's actual ComboBoxItem mouse-up selection handler. No OS SendInput.
                    row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                    Pump(); Check(combo.SelectedIndex == target && !combo.IsDropDownOpen && window.DrawerOpen, "WPF selection / close failed");
                }
                combo.SelectedIndex = original;
            }
        }
        private static void Begin(bool area = true)
        {
            page.GetType().GetField("areaRecording", Flags).SetValue(page, area);
            Call(page, "BeginRecording", false, RecordingDelay.None, 0);
            Get<DispatcherTimer>(page, "recordingTimer").Stop();
        }
        private static void At(int ms)
        {
            var clock = Get<Stopwatch>(page, "recordingClock"); clock.Reset();
            var elapsed = typeof(Stopwatch).GetField("elapsed", Flags) ?? typeof(Stopwatch).GetField("_elapsed", Flags);
            Check(elapsed != null, "Framework Stopwatch elapsed field unavailable");
            elapsed.SetValue(clock, (long)ms * Stopwatch.Frequency / 1000);
            Call(page, "TickRecording");
        }
        private static Grid Countdown() { return Get<Grid>(page, "countdownOverlay"); }
        private static string Number() { return Get<TextBlock>(page, "countdownNumber").Text; }
        private static void Route(InputStroke stroke) { Call(controller, "RouteInput", stroke); }
        private static void RecordingTests()
        {
            Test("R1/R3 exact countdown boundaries and 3000ms recorder start", () => {
                Begin();
                int[] times = { 0, 999, 1000, 1999, 2000, 2999 };
                string[] numbers = { "3", "3", "2", "2", "1", "1" };
                for (int i = 0; i < times.Length; i++) { At(times[i]); Check(Number() == numbers[i] && Countdown().Visibility == Visibility.Visible && controller.Recorder == null, "Boundary " + times[i]); }
                At(3000); Check(controller.Recorder != null && !Get<bool>(page, "countingDown") && Countdown().Visibility == Visibility.Collapsed && Number() != "0", "Start boundary");
            });
            Test("R2 keyboard/button/wheel excluded throughout countdown; area inactive", () => {
                Begin(); foreach (int time in new[] { 0, 1000, 2000, 2999 }) {
                    At(time); Call(page, "UpdateRecordingRegion");
                    Check(!Get<bool>(Get<object>(controller, "recordingArea"), "Focused"), "Area enabled before recorder start");
                    Route(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 65, Down = true });
                    Route(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 65, Down = false });
                    Route(new InputStroke { Trigger = TriggerKind.Left, Down = true });
                    Route(new InputStroke { Trigger = TriggerKind.Left, Down = false });
                    Route(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true });
                    Check(controller.Recorder == null && Get<ListBox>(page, "recordedActions").Items.Count == 0, "Countdown captured input");
                }
                At(3000); Check(controller.Recorder.Count == 0, "Countdown input leaked into new recorder");
            });
            Test("R4 cancel at 3/2/1 preserves draft and clears all countdown state", () => {
                var before = window.Draft.Clone(); foreach (int ms in new[] { 0, 1000, 2000 }) {
                    Begin(); At(ms); page.EndRecording(true);
                    Check(!page.IsRecording && !Get<bool>(page, "countingDown") && Countdown().Visibility == Visibility.Collapsed && Get<int>(page, "lastCountdownNumber") == -1, "Cancel visual/state");
                    Check(window.Draft.Macros.Count == before.Macros.Count && window.Draft.Macros[0].Steps.Count == before.Macros[0].Steps.Count && controller.Recorder == null, "Cancel changed original macro");
                }
            });
            Test("R4 Stop and Ctrl+Shift+F12 cancel countdown without new steps", () => {
                int count = window.Draft.Macros.Count;
                Begin(); controller.Stop(); Call(page, "TickRecording"); Check(!page.IsRecording && Countdown().Visibility == Visibility.Collapsed, "Stop cleanup");
                Begin(); Route(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 123, Down = true, Modifiers = KeyModifiers.Control | KeyModifiers.Shift });
                Call(page, "TickRecording"); Check(!page.IsRecording && Countdown().Visibility == Visibility.Collapsed && window.Draft.Macros.Count == count, "Emergency cleanup");
            });
            Test("R5 external mode preserves top countdown; no central digits", () => {
                Begin(false); At(1000); Check(Countdown().Visibility == Visibility.Collapsed && Get<TextBlock>(page, "recordingText").Text.Contains("切换到目标窗口"), "External countdown UI");
                At(3000); Check(controller.Recorder != null && Countdown().Visibility == Visibility.Collapsed, "External start");
            });
            Test("R6 Chinese/English and both themes; Accent, hit-testing and reduced motion", () => {
                foreach (string theme in new[] { "classic", "fluent" }) foreach (bool english in new[] { false, true }) {
                    Ui.ApplyTheme(theme); Ui.English = english; Ui.ReducedMotion = true; Begin(); At(2000);
                    Check(Number() == "1" && !Countdown().IsHitTestVisible && ReferenceEquals(Get<TextBlock>(page, "countdownNumber").Foreground, Ui.Accent), "Countdown style/interaction");
                    Check(Get<TextBlock>(page, "countdownCaption").Text == (english ? "Get ready" : "准备录制"), "Caption language");
                    page.EndRecording(true);
                }
                Ui.English = false; Ui.ApplyTheme("classic");
            });
            Test("R1 real clock 3/2/1 and recording flow; memory-only result, no output", () => {
                Ui.ReducedMotion = true; Call(page, "BeginRecording", false, RecordingDelay.None, 0);
                Check(controller.Recorder == null && Number() == "3", "Initial countdown"); Capture("recording-countdown-3");
                Pump(1080); Check(controller.Recorder == null && Number() == "2", "Second number"); Capture("recording-countdown-2");
                Pump(1000); Check(controller.Recorder == null && Number() == "1", "Last number"); Capture("recording-countdown-1");
                Pump(1100); Check(controller.Recorder != null && Get<Stopwatch>(page, "recordingClock").ElapsedMilliseconds < 1000 && Countdown().Visibility == Visibility.Collapsed, "Real-clock start");
                var pad = Get<Border>(page, "recordingPad"); Check(pad.Focusable && pad.IsEnabled && pad.IsVisible, "Recording pad changed");
                controller.Recorder.Capture(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 65, Down = true, BypassBindings = true, RecordingAreaAllowed = true });
                controller.Recorder.Capture(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 65, Down = false, BypassBindings = true, RecordingAreaAllowed = true });
                Pump(150); Call(page, "TickRecording"); Check(Get<ListBox>(page, "recordedActions").Items.Count == 2, "Live results missing"); Capture("recording-active");
                int count = window.Draft.Macros.Count; page.EndRecording(false);
                Check(window.Draft.Macros.Count == count + 1 && window.Draft.Macros.Last().Steps.Count == 2 && store.Saves == 0, "Recording draft / save policy");
            });
        }
        [STAThread] private static int Main(string[] args)
        {
            artifacts = Path.GetFullPath(args[0]); Directory.CreateDirectory(artifacts); bool baseline = args.Length > 1 && args[1] == "baseline";
            only = args.Length > 2 ? args[2] : null;
            typeof(DesktopTests).GetField("artifacts", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, artifacts);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; DesktopApp.LoadTheme(app); Ui.ReducedMotion = true;
            window = new ShellWindow(true) { ShowInTaskbar = false };
            window.Preferences.Language = "zh"; window.Preferences.Theme = "classic"; window.Preferences.ReducedMotion = true;
            window.RebuildPages(1); window.Show(); Pump(); page = Get<MacroPage>(window, "macroPage");
            foreach (string id in new[] { "safe-one", "safe-two" }) window.Draft.Macros.Add(new MacroDefinition { Id = id, Name = "示例 " + id, Steps = new List<MacroStep> { new MacroStep { Kind = ActionKind.Delay, Number = 100 } } });
            page.ReloadLibrary("safe-one"); Check(window.SaveMacros(), "Memory seed save");
            store = new MemoryStore(window.Draft); controller = new MacroController(store, new NoOutput(), false);
            window.GetType().GetField("Macros", Flags).SetValue(window, controller);
            try {
                Test("D0 current-template Popup ownership / retargeted overlay preview", () => {
                    Open("OpenRecording"); var combo = Find<ComboBox>(Get<Border>(window, "activeDrawer")).First(); var popup = Drop(combo);
                    var row = Find<ComboBoxItem>(popup.Child).First();
                    bool owned = (bool)Call(window, "InsideDrawer", row);
                    var e = OverlayClick(row);
                    Console.WriteLine("POPUP BASELINE: owned=" + owned + "; drawer_open=" + window.DrawerOpen + "; handled=" + e.Handled);
                    Check(owned && window.DrawerOpen && !e.Handled, "Current Popup row misclassified / captured preview closes Drawer"); combo.IsDropDownOpen = false;
                });
                Test("R0 central countdown visual exists", () => Check(page.GetType().GetField("countdownOverlay", Flags) != null, "Central 3/2/1 layer absent"));
                if (!baseline) {
                    Test("D1 recording scope/destination/timing selections", () => SelectionRound("OpenRecording", new object[0], 3, true));
                    Test("D2 mouse macro/mode/original policy selections", () => SelectionRound("OpenMouseBinding", new object[] { TriggerKind.X1, null }, 3));
                    Test("D2 advanced binding selections", () => SelectionRound("EditBinding", new object[] { false }, 4));
                    Test("D3/D4 outside mask closes only after dropdown capture finishes", () => {
                        Open("OpenRecording"); var combo = Find<ComboBox>(Get<Border>(window, "activeDrawer")).First(); Drop(combo);
                        window.Activate(); combo.Focus();
                        Console.WriteLine("CAPTURE: before=" + (Mouse.Captured == null ? "null" : Mouse.Captured.GetType().Name));
                        Check(Mouse.Capture(combo, CaptureMode.SubTree), "Own test ComboBox could not capture");
                        var mask = Get<Grid>(window, "overlay"); var first = OverlayClick(mask);
                        Check(window.DrawerOpen && !first.Handled, "First outside click stole open Popup");
                        // WPF retargets an outside MouseDown to the captured ComboBox.
                        // Raising its actual class event closes the dropdown (releasing
                        // capture alone is not equivalent; WPF can reacquire it).
                        Check(Mouse.Captured == combo, "ComboBox capture was not established");
                        combo.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                        Pump(); Check(!combo.IsDropDownOpen && window.DrawerOpen, "Outside class event did not close only dropdown");
                        var second = OverlayClick(mask); Check(!window.DrawerOpen && second.Handled, "Second mask click did not dismiss");
                    });
                    Test("D5 Chevron closes; D6 Esc first dropdown then Drawer", () => {
                        Open("OpenRecording"); var combo = Find<ComboBox>(Get<Border>(window, "activeDrawer")).First(); Drop(combo);
                        SendKey(combo, Key.Escape); Check(window.DrawerOpen && !combo.IsDropDownOpen, "First Esc");
                        SendKey(combo, Key.Escape); Check(!window.DrawerOpen, "Second Esc");
                        Open("OpenRecording"); Get<Button>(window, "drawerCollapseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!window.DrawerOpen, "Chevron");
                    });
                    Test("D7 owned Popup/ContextMenu; unrelated popup cannot keep Drawer", () => {
                        var anchor = new Button { Content = "Popup anchor" }; window.OpenDrawer("Popup owner", anchor); Pump();
                        var popup = new Popup { PlacementTarget = anchor, Child = new Border { Child = new TextBlock { Text = "Owned popup" } }, IsOpen = true }; Pump();
                        try { Check((bool)Call(window, "InsideDrawer", ((Border)popup.Child).Child), "PlacementTarget not followed"); } finally { popup.IsOpen = false; }
                        var menu = new ContextMenu { PlacementTarget = anchor }; var item = new MenuItem { Header = "Owned action" }; menu.Items.Add(item); menu.IsOpen = true; Pump();
                        try { Check((bool)Call(window, "InsideDrawer", item), "ContextMenu owner lost"); } finally { menu.IsOpen = false; }
                        var foreign = new Popup { PlacementTarget = Get<Button[]>(window, "navigation")[0], Child = new TextBlock { Text = "Other page popup" }, IsOpen = true }; Pump();
                        try { Check(!(bool)Call(window, "InsideDrawer", foreign.Child), "Unrelated popup whitelisted"); Check(OverlayClick((UIElement)foreign.Child).Handled && !window.DrawerOpen, "Unrelated popup prevented dismissal"); } finally { foreign.IsOpen = false; }
                    });
                    Test("D8 ordinary internal content, inline text, scrollbar and action", () => {
                        var text = new TextBlock(); var run = new System.Windows.Documents.Run("Internal inline"); text.Inlines.Add(run);
                        var input = new TextBox { Text = "Draft" }; int clicks = 0; var action = Ui.Button("Local", () => clicks++);
                        window.OpenDrawer("Internal", Ui.Stack(text, input, action, new Border { Height = 1200 })); Pump();
                        foreach (UIElement control in new UIElement[] { text, input, action, Find<ScrollBar>(Get<Border>(window, "activeDrawer")).First() })
                            Check(!OverlayClick(control).Handled && window.DrawerOpen, "Internal target dismissed");
                        Check((bool)Call(window, "InsideDrawer", run), "Inline logical parent lost"); action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(clicks == 1, "Internal action blocked");
                    });
                    RecordingTests();
                    foreach (string name in new[] { "RecordingSequence", "RecordingLimits", "RecordingPipeline", "RecordingPause", "AreaRecording" }) {
                        string existing = name; Test("Existing recording regression: " + existing, () => typeof(DesktopTests).GetMethod(existing, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
                    }
                }
            } finally {
                if (page.IsRecording) page.EndRecording(true); window.ClosePreview(); app.Shutdown();
                File.WriteAllLines(Path.Combine(artifacts, "results.tsv"), results);
                Console.WriteLine("DRAWER / RECORDING: " + (results.Count - failures) + " PASS / " + failures + " FAIL");
            }
            return failures == 0 ? 0 : 1;
        }
        private sealed class MemoryStore : IMacroStore
        {
            private readonly MacroLibrary library; internal int Saves;
            internal MemoryStore(MacroLibrary library) { this.library = library.Clone(); }
            public string FilePath { get { return "memory-only"; } }
            public MacroLibrary Load() { return library.Clone(); }
            public void Save(MacroLibrary value) { Saves++; throw new InvalidOperationException("Test must not save"); }
        }
        private sealed class NoOutput : IMacroOutput
        {
            private static void Reject() { throw new InvalidOperationException("Unexpected input output"); }
            public void Key(int key, bool down) { Reject(); }
            public void MouseButton(RazerBatteryTray.Macros.MouseAction button, bool down) { Reject(); }
            public void Wheel(int value) { Reject(); }
            public void Text(string value, CancellationToken token) { Reject(); }
            public void Launch(string target, string arguments, bool command) { Reject(); }
        }
    }
}
