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
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    internal static partial class DesktopTests
    {
        private static InputStroke Stroke(int key, bool down, int ms = 0) { return new InputStroke { Trigger = TriggerKind.Keyboard, Key = key, Down = down, Timestamp = (long)(ms * Stopwatch.Frequency / 1000.0) }; }
        private static void RecordingSequence()
        {
            foreach (RecordingDelay mode in Enum.GetValues(typeof(RecordingDelay)))
            {
                var buffer = new RecordingBuffer(mode, 50, 500);
                buffer.Accept(Stroke(162, true, 0)); buffer.Accept(Stroke(65, true, 70));
                buffer.Accept(Stroke(65, true, 75)); // Auto-repeat excluded.
                buffer.Accept(Stroke(65, false, 110)); buffer.Accept(Stroke(162, false, 150));
                foreach (TriggerKind mouse in new[] { TriggerKind.Left, TriggerKind.Right, TriggerKind.Middle, TriggerKind.X1, TriggerKind.X2, TriggerKind.WheelUp, TriggerKind.WheelDown })
                {
                    buffer.Accept(new InputStroke { Trigger = mouse, Down = true });
                    if (!MacroValidation.IsWheel(mouse)) buffer.Accept(new InputStroke { Trigger = mouse, Down = false });
                }
                var injected = Stroke(66, true); injected.Injected = true; buffer.Accept(injected);
                var own = Stroke(67, true); own.BypassBindings = true; buffer.Accept(own);
                buffer.Balance();
                var actions = buffer.Steps.Where(s => s.Kind != ActionKind.Delay).ToList();
                Check(actions.Count == 16 && actions.All(s => s.KeyCode != 66 && s.KeyCode != 67), "Only intended physical input retained");
                Check(actions[0].KeyCode == 162 && actions[3].Press == PressMode.Up, "Chord order retained");
                Check(actions.Last().Mouse == MouseAction.WheelDown, "Wheel mapping correct");
                var delays = buffer.Steps.Where(s => s.Kind == ActionKind.Delay).ToList();
                if (mode == RecordingDelay.Actual) Check(delays[0].Number == 70 && delays[1].Number == 40, "Monotonic inter-event timing");
                if (mode == RecordingDelay.Fixed) Check(delays.Count == 15 && delays.All(s => s.Number == 50), "Fixed timing");
                if (mode == RecordingDelay.None) Check(delays.Count == 0, "No timing");
                var library = new MacroLibrary(); library.Macros.Add(new MacroDefinition { Steps = buffer.Steps }); MacroValidation.Validate(library);
            }
            var releaseOwn = new RecordingBuffer(RecordingDelay.None, 0, 500);
            releaseOwn.Accept(Stroke(65, true)); var up = Stroke(65, false); up.BypassBindings = true; releaseOwn.Accept(up); releaseOwn.Balance();
            Check(releaseOwn.Steps.Count == 2, "Releasing an existing held key in app remains balanced");
        }
        private static void RecordingLimits()
        {
            var buffer = new RecordingBuffer(RecordingDelay.None, 0, 500);
            for (int i = 0; i < 500; i++) Check(buffer.Accept(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true }), "Fits capacity");
            Check(!buffer.Accept(Stroke(65, true)), "Stops before exceeding capacity"); buffer.Balance(); Check(buffer.Steps.Count == 500, "Strict cap");
            buffer = new RecordingBuffer(RecordingDelay.Actual, 0, 2);
            Check(buffer.Accept(Stroke(65, true, 0)), "Reserve release");
            Check(!buffer.Accept(Stroke(66, true, 20)), "Reject input when release reservation would overflow"); buffer.Balance();
            Check(buffer.Steps.Count == 2 && buffer.Steps[1].Press == PressMode.Up, "Balances at limit without sending input");
            buffer = new RecordingBuffer(RecordingDelay.Actual, 0, 500);
            buffer.Accept(Stroke(65, true)); buffer.Accept(Stroke(65, false, 10)); buffer.Accept(Stroke(162, true, 20)); buffer.Accept(Stroke(160, true, 30));
            var stop = Stroke(123, true, 40); stop.Modifiers = KeyModifiers.Control | KeyModifiers.Shift;
            Check(!buffer.Accept(stop), "Emergency finishes session"); buffer.Balance();
            Check(buffer.Steps.Count == 3 && buffer.Steps.Where(s => s.Kind == ActionKind.Keyboard).All(s => s.KeyCode == 65), "Stop gesture and its timing excluded");
            buffer = new RecordingBuffer(RecordingDelay.None, 0, 500);
            buffer.Accept(Stroke(162, true)); buffer.Accept(Stroke(65, true)); buffer.Accept(Stroke(65, false)); buffer.Accept(Stroke(160, true)); buffer.Accept(stop); buffer.Balance();
            Check(buffer.Steps.First().KeyCode == 162 && buffer.Steps.Last().KeyCode == 162 && buffer.Steps.Last().Press == PressMode.Up, "Earlier used modifier is preserved and balanced");
        }
        [StructLayout(LayoutKind.Sequential)] private struct KeyPacket { public uint Key, Scan, Flags, Time; public IntPtr Extra; }
        private static void RecordingPipeline()
        {
            var recorder = new MacroRecorder(RecordingDelay.None, 0, 500);
            using (var hook = new GlobalInputHook(stroke => { recorder.Capture(stroke); return false; }))
            {
                foreach (var pair in new[] { new[] { 0x100, 65, 0 }, new[] { 0x101, 65, 0 }, new[] { 0x100, 66, 0x10 } })
                {
                    var packet = new KeyPacket { Key = (uint)pair[1], Flags = (uint)pair[2] };
                    IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(packet));
                    try { Marshal.StructureToPtr(packet, ptr, false); Invoke(hook, "Keyboard", 0, new IntPtr(pair[0]), ptr); }
                    finally { Marshal.FreeHGlobal(ptr); }
                }
            }
            var steps = recorder.Finish(); Check(recorder.Completed && steps.Count == 2, "Native decoding connects to recorder, injected input excluded");
            var library = new MacroLibrary(); var macro = new MacroDefinition { Steps = steps }; library.Macros.Add(macro); MacroValidation.Validate(library);
            var output = new RecordingOutput();
            using (var engine = new MacroEngine(output)) { Check(engine.Start(library, macro.Id, "recording-test", false, 0), "Recorded draft plays"); Check(SpinWait.SpinUntil(() => !engine.IsRunning, 3000), "Playback completed"); }
            Check(output.Events.SequenceEqual(new[] { "65+", "65-" }), "Recorded down/up replay exactly once");
            recorder.Dispose();
        }
        private static void RecordingPause()
        {
            string file = Path.Combine(artifacts, "recording-" + Guid.NewGuid().ToString("N"), "macros.xml");
            using (var controller = new MacroController(new MacroStore(file), new RecordingOutput(), false))
            {
                var router = Field<BindingRouter>(controller, "router");
                controller.PauseNewBindings(true); controller.PrepareRecording(); controller.StartRecording(RecordingDelay.None, 0, 500);
                controller.Recorder.Capture(Stroke(65, true)); var result = controller.EndRecording();
                Check(result.Count == 2 && router.Suspended, "Balances key and restores pre-existing user pause");
                Check(controller.Snapshot().Macros.Count == 0 && !File.Exists(file), "Session never changes saved macros");
                controller.PauseNewBindings(false); controller.PrepareRecording(); controller.EndRecording(); Check(!router.Suspended, "Cancelled countdown restores unpaused state");
                controller.PrepareRecording(); controller.PauseNewBindings(true); controller.EndRecording(); Check(router.Suspended, "Pause changed during recording persists");
                controller.PauseNewBindings(false); controller.PrepareRecording(); controller.Stop(); Check(controller.RecordingStopRequested, "Tray/global stop also requests recording stop"); controller.EndRecording();
            }
        }
        private sealed class RecordingOutput : IMacroOutput
        {
            internal readonly List<string> Events = new List<string>();
            public void Key(int key, bool down) { Events.Add(key + (down ? "+" : "-")); }
            public void MouseButton(MouseAction button, bool down) { }
            public void Wheel(int notches) { }
            public void Text(string text, CancellationToken token) { }
            public void Launch(string target, string args, bool command) { }
        }
        private static void RevisionThreeUi(ShellWindow window)
        {
            window.Navigate(1); Pump(); var page = Field<MacroPage>(window, "macroPage"); Invoke(page, "SelectTab", false);
            window.Notice("");
            Snapshot(window, "r3-macros-dark.png");
            window.Width = 840; Pump(); Snapshot(window, "r3-macros-narrow.png");
            Check(Field<Button>(page, "newButton").Visibility == Visibility.Collapsed, "Narrow header moves New to More");
            var library = Field<ComboBox>(page, "library"); var record = Field<Button>(page, "recordButton");
            Check(library.TranslatePoint(new Point(library.ActualWidth, 0), page).X < record.TranslatePoint(new Point(), page).X, "Narrow toolbar groups do not overlap");
            window.Width = 1180; Pump(); Invoke(page, "OpenRecording"); Pump(); Snapshot(window, "r3-recording-options.png"); window.CloseDrawer();
            var controller = new MacroController(new MacroStore(Path.Combine(artifacts, "ui-recording-missing.xml")), new RecordingOutput(), false);
            typeof(ShellWindow).GetField("Macros", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, controller);
            int count = window.Draft.Macros.Count;
            try
            {
                Invoke(page, "BeginRecording", false, RecordingDelay.None, 0); Pump();
                Check(page.IsRecording && controller.Recorder == null, "Countdown precedes input listening");
                controller.Stop(); Invoke(page, "TickRecording"); Check(!page.IsRecording && window.Draft.Macros.Count == count, "Emergency cancels countdown without draft changes");
                Invoke(page, "BeginRecording", false, RecordingDelay.None, 0); WaitForRecorder(page, controller);
                Check(controller.Recorder != null, "Countdown starts recorder"); window.Notice(""); Snapshot(window, "r3-recording-active.png");
                controller.Recorder.Capture(Stroke(65, true)); controller.Recorder.Capture(Stroke(65, false)); page.EndRecording(false);
                Check(window.Draft.Macros.Count == count + 1 && window.DraftDirty && controller.Snapshot().Macros.Count == 0, "Recording becomes editable unsaved draft only");
                var recorded = page.Selected; Snapshot(window, "r3-recorded-draft.png");
                Invoke(page, "BeginRecording", true, RecordingDelay.None, 0); page.EndRecording(true);
                Check(recorded.Steps.Count == 2, "Cancel append preserves existing steps");
                window.Draft.Macros.Remove(recorded); page.ReloadLibrary(null);
            }
            finally { page.EndRecording(true); controller.Dispose(); typeof(ShellWindow).GetField("Macros", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, null); }
            window.Navigate(2); Pump();
            var theme = Descendants<ComboBox>(Field<FrameworkElement[]>(window, "views")[2]).First(c => c.Items.Contains("白天"));
            Ui.ReducedMotion = false; theme.IsDropDownOpen = true; PumpFor(30);
            var popup = (Popup)theme.Template.FindName("PART_Popup", theme);
            var surface = Descendants<Border>(popup.Child).First(b => b.Name == "DropSurface");
            var transform = (TranslateTransform)surface.RenderTransform;
            if (SystemParameters.ClientAreaAnimation) Check(transform.HasAnimatedProperties, "Dropdown receives motion clock");
            PumpFor(250); Check(Math.Abs(transform.Y) < .001 && surface.Opacity == 1, "Dropdown settles without text scaling");
            theme.IsDropDownOpen = false; Pump(); Check(!transform.HasAnimatedProperties, "Close clears clocks");
            for (int i = 0; i < 8; i++) { theme.IsDropDownOpen = true; Pump(); theme.IsDropDownOpen = false; Pump(); }
            Ui.ReducedMotion = true; theme.IsDropDownOpen = true; Pump();
            Check(!((TranslateTransform)surface.RenderTransform).HasAnimatedProperties, "Reduced motion opens instantly"); theme.IsDropDownOpen = false;
            var bottomCombo = Ui.Combo(new[] { "One", "Two", "Three", "Four" });
            var host = new Window { Width = 260, Height = 90, Left = 40, Top = SystemParameters.WorkArea.Bottom - 90, Content = bottomCombo, ShowInTaskbar = false };
            host.Show(); Pump(); Ui.ReducedMotion = false; bottomCombo.IsDropDownOpen = true; PumpFor(30);
            popup = (Popup)bottomCombo.Template.FindName("PART_Popup", bottomCombo); surface = Descendants<Border>(popup.Child).First(b => b.Name == "DropSurface");
            Check(surface.PointToScreen(new Point()).Y < bottomCombo.PointToScreen(new Point()).Y, "Bottom-edge popup opens above anchor");
            bottomCombo.IsDropDownOpen = false; host.Close(); Ui.ReducedMotion = true;
        }
    }
}
