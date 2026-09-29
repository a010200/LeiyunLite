using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Tests
{
    internal static class MacroTests
    {
        private static string artifacts;
        public static void Run(Action<string, Action> test, string directory)
        {
            artifacts = directory;
            RunCore(test);
            test("Macro editor / step / binding dialogs render, edit, save and reload", Editor);
            test("Native input hooks and SendInput Unicode to an isolated test window", NativeInput);
            test("Native callback decoding: modifiers, key-up, mouse buttons, fractional wheel and injected flags", HookDecoding);
        }
        public static void RunCoreOnly(Action<string, Action> test, string directory)
        {
            artifacts = directory;
            RunCore(test);
        }
        private static void RunCore(Action<string, Action> test)
        {
            test("Macro sequence: keyboard, mouse, wheel, text, launch, command, nested loops and calls", Sequence);
            test("Macro cancellation / exception / disposal releases held input; busy start rejected", Cancellation);
            test("1ms repeating macro has no hidden 25ms loop penalty", Timing);
            test("1ms scheduler does not replay missed delays after a stall", SchedulerAfterStall);
            test("1ms held-middle macro stops with a final left-button release", HeldMiddleRelease);
            test("Canceled macro input drops late downs but preserves ups and a new run", StoppedInputBarrier);
            test("In-memory input timing records only relevant buttons and a release summary", InputTimingSummary);
            test("Macro execution budget bounds nested empty loops", Budget);
            test("Macro validation rejects recursion, missing targets, unbalanced loops and bad bindings", Validation);
            test("Binding router: once, hold, toggle, suppression, injection, disable and emergency stop", Bindings);
            test("Mouse binding ignores unrelated Shift while explicit modifier wins", MouseModifierIndependence);
            test("Physical right plus Shift plus middle chord starts and stops its own macro", RightShiftMiddleChord);
            test("Macro XML round trip, atomic backup, corrupt and DTD rejection", Persistence);
        }
        public static void RunTimingOnly(Action<string, Action> test)
        {
            test("1ms repeating macro has no hidden 25ms loop penalty", Timing);
            test("1ms scheduler does not replay missed delays after a stall", SchedulerAfterStall);
            test("1ms held-middle macro stops with a final left-button release", HeldMiddleRelease);
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Reject(Action action) { bool threw = false; try { action(); } catch { threw = true; } Check(threw, "Expected rejection"); }
        private static void Until(Func<bool> condition)
        { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < 4000) { Application.DoEvents(); Thread.Sleep(5); } Check(condition(), "Timed out"); }
        private static MacroLibrary Library(params MacroStep[] steps)
        { var lib = new MacroLibrary(); var macro = new MacroDefinition { Id = "main", Name = "测试宏" }; macro.Steps.AddRange(steps); lib.Macros.Add(macro); return lib; }
        private static void Sequence()
        {
            var lib = Library(new MacroStep { Kind = ActionKind.Delay, Number = 5 },
                new MacroStep { Kind = ActionKind.Keyboard, KeyCode = 65 },
                new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.X1, Number = 1 },
                new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.WheelDown, Number = 2 },
                new MacroStep { Kind = ActionKind.Text, Value = "中文ABC" },
                new MacroStep { Kind = ActionKind.Launch, Value = "sample.txt" },
                new MacroStep { Kind = ActionKind.Command, Value = "cmd.exe", Arguments = "/c echo test" },
                new MacroStep { Kind = ActionKind.LoopStart, Number = 2 },
                new MacroStep { Kind = ActionKind.LoopStart, Number = 3 },
                new MacroStep { Kind = ActionKind.CallMacro, Value = "child" },
                new MacroStep { Kind = ActionKind.LoopEnd }, new MacroStep { Kind = ActionKind.LoopEnd });
            var child = new MacroDefinition { Id = "child", Name = "子宏" };
            child.Steps.Add(new MacroStep { Kind = ActionKind.Text, Value = "child" }); lib.Macros.Add(child);
            MacroValidation.Validate(lib);
            var output = new Output();
            using (var engine = new MacroEngine(output)) { Check(engine.Start(lib, "main", "test", false, 0), "Start"); Until(() => !engine.IsRunning); }
            Check(string.Join("|", output.Snapshot()) == "K65+|K65-|MX1+|MX1-|W-2|T中文ABC|Lsample.txt:|Ccmd.exe:/c echo test|Tchild|Tchild|Tchild|Tchild|Tchild|Tchild", "Action order");
        }
        private static void Cancellation()
        {
            var lib = Library(new MacroStep { Kind = ActionKind.Keyboard, KeyCode = 162, Press = PressMode.Down },
                new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.Left, Press = PressMode.Down, Number = 1 },
                new MacroStep { Kind = ActionKind.Delay, Number = 600000 });
            var output = new Output();
            using (var engine = new MacroEngine(output))
            {
                engine.Start(lib, "main", "test", false, 0); Until(() => output.Count == 2);
                Check(!engine.Start(lib, "main", "busy", false, 0), "Busy not rejected"); engine.Stop(); Until(() => !engine.IsRunning);
                Check(output.Has("K162-") && output.Has("MLeft-"), "Canceled keys not released");
                output.Clear(); engine.Start(lib, "main", "test", true, 0); Until(() => output.Count == 2);
            }
            Check(output.Has("K162-") && output.Has("MLeft-"), "Dispose release");
            lib.Macros[0].Steps[2] = new MacroStep { Kind = ActionKind.Text, Value = "throw" }; output.Clear(); output.ThrowText = true;
            using (var engine = new MacroEngine(output)) { engine.Start(lib, "main", "test", false, 0); Until(() => !engine.IsRunning); }
            Check(output.Has("K162-") && output.Has("MLeft-"), "Exception release");
        }
        private static void Timing()
        {
            var lib = Library(new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.Left, Press = PressMode.Tap, Number = 1 },
                new MacroStep { Kind = ActionKind.Delay, Number = 1 });
            var output = new TimingOutput();
            using (var engine = new MacroEngine(output))
            {
                Check(engine.Start(lib, "main", "timing", true, 0), "Timing macro did not start");
                Until(() => output.Clicks >= 12); engine.Stop(); Until(() => !engine.IsRunning);
            }
            Check(output.SpanMilliseconds < 200, "Hidden repeat delay remains: " + output.SpanMilliseconds.ToString("0.0") + " ms");
        }
        private static void SchedulerAfterStall()
        {
            using (var scheduler = new PrecisionScheduler(CancellationToken.None))
            {
                scheduler.Wait(1);
                Thread.Sleep(40);
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 3; i++) scheduler.Wait(1);
                watch.Stop();
                Check(watch.Elapsed.TotalMilliseconds >= 2.0,
                    "Missed 1ms waits were replayed as a burst: " + watch.Elapsed.TotalMilliseconds.ToString("0.00") + " ms");
            }
        }
        private static void HeldMiddleRelease()
        {
            var lib = Library(
                new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.Left, Press = PressMode.Down },
                new MacroStep { Kind = ActionKind.Delay, Number = 1 },
                new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.Left, Press = PressMode.Up },
                new MacroStep { Kind = ActionKind.Delay, Number = 1 },
                new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.Left, Press = PressMode.Down },
                new MacroStep { Kind = ActionKind.Delay, Number = 1 },
                new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.Left, Press = PressMode.Up });
            lib.Bindings.Add(new MacroBinding { MacroId = "main", Trigger = TriggerKind.Middle,
                Mode = RunMode.WhileHeld, SuppressOriginal = true });
            var output = new Output();
            using (var engine = new MacroEngine(output))
            {
                var router = new BindingRouter(engine); router.Configure(lib);
                router.Handle(new InputStroke { Trigger = TriggerKind.Right, Down = true });
                Check(router.Handle(new InputStroke { Trigger = TriggerKind.Middle, Down = true }), "Middle did not start");
                Until(() => output.Count >= 100);
                router.Handle(new InputStroke { Trigger = TriggerKind.Right, Down = false });
                Check(engine.IsRunning, "Releasing unrelated right button stopped middle macro");
                var watch = Stopwatch.StartNew();
                Check(router.Handle(new InputStroke { Trigger = TriggerKind.Middle, Down = false }), "Middle release was not paired");
                Until(() => !engine.IsRunning); watch.Stop();
                Check(watch.ElapsedMilliseconds < 250, "Macro stop took " + watch.ElapsedMilliseconds + " ms");
            }
            var events = output.Snapshot();
            int downs = 0, ups = 0;
            foreach (var item in events) { if (item == "MLeft+") downs++; if (item == "MLeft-") ups++; }
            Check(downs > 0 && ups == downs + 1 && events[events.Length - 1] == "MLeft-",
                "Canceled repeat did not end with a redundant left-button release");
            int stoppedCount = output.Count;
            Thread.Sleep(80);
            Check(output.Count == stoppedCount, "Output continued after middle release");
        }
        private static void StoppedInputBarrier()
        {
            var output = new WindowsMacroOutput();
            output.BeginRun(); UIntPtr oldTag = output.CurrentInputTag;
            Check(WindowsMacroOutput.IsOwnInputTag(oldTag) && !output.ShouldSuppressStoppedDown(oldTag), "Active run was blocked");
            output.CancelRun();
            Check(output.ShouldSuppressStoppedDown(oldTag), "Canceled run was not blocked");
            output.BeginRun(); UIntPtr newTag = output.CurrentInputTag;
            Check(newTag != oldTag && !output.ShouldSuppressStoppedDown(newTag) &&
                output.ShouldSuppressStoppedDown(oldTag), "A new run revived stale input or remained blocked");
            int physical = 0;
            using (var hook = new GlobalInputHook(s => { physical++; return false; }, null, output.ShouldSuppressStoppedDown))
            {
                foreach (int message in new[] { 0x201, 0x204, 0x207, 0x20B })
                    Check(Callback(hook, "Mouse", message, new MousePacket { Flags = 1, Extra = oldTag }) == new IntPtr(1),
                        "Late mouse down reached the target: " + message);
                Check(Callback(hook, "Keyboard", 0x100, new KeyboardPacket { Flags = 0x10, Extra = oldTag }) == new IntPtr(1),
                    "Late keyboard down reached the target");
                Check(Callback(hook, "Mouse", 0x202, new MousePacket { Flags = 1, Extra = oldTag }) != new IntPtr(1),
                    "Mouse up was blocked");
                Check(hook.PassedLeftUps == 1 && hook.BlockedStoppedDowns == 1,
                    "Stopped-run mouse up was not observable or the blocked-down count was wrong");
                Check(Callback(hook, "Keyboard", 0x101, new KeyboardPacket { Flags = 0x10, Extra = oldTag }) != new IntPtr(1),
                    "Keyboard up was blocked");
                Check(Callback(hook, "Mouse", 0x201, new MousePacket { Flags = 1, Extra = newTag }) != new IntPtr(1),
                    "New macro mouse down was blocked");
                Callback(hook, "Mouse", 0x201, new MousePacket());
                Check(physical == 1, "Physical left button was blocked or injected input was rerouted");
            }
        }
        private static void InputTimingSummary()
        {
            var store = new MacroStore(Path.Combine(artifacts, "timing-" + Guid.NewGuid().ToString("N"), "macros.xml"));
            using (var controller = new MacroController(store, new Output(), false))
            {
                Check(controller.CopyInputTiming().Contains("尚无相关输入"), "Empty timing state was not reported");
                Call(controller, "RouteInput", new InputStroke { Trigger = TriggerKind.Keyboard, Key = 65, Down = true });
                Call(controller, "RouteInput", new InputStroke { Trigger = TriggerKind.Right, Down = true, RightButtonDown = true });
                Call(controller, "RouteInput", new InputStroke { Trigger = TriggerKind.Middle, Down = true, RightButtonDown = true });
                Call(controller, "RouteInput", new InputStroke { Trigger = TriggerKind.Middle, Down = false, RightButtonDown = true });
                Until(() => controller.CopyInputTiming().Contains("中键松开后1.2秒"));
                string timing = controller.CopyInputTiming();
                Check(timing.Contains("Right按下") && timing.Contains("Middle松开") &&
                    timing.Contains("新增模拟左键按下提交=0") && timing.Contains("新增左键抬起提交=0") &&
                    !timing.Contains("Keyboard") && !timing.Contains("Key=65"),
                    "Timing export missed its release summary or captured unrelated keyboard input");
            }
        }
        private static void Budget()
        {
            var lib = Library(new MacroStep { Kind = ActionKind.LoopStart, Number = 1000 }, new MacroStep { Kind = ActionKind.LoopStart, Number = 1000 },
                new MacroStep { Kind = ActionKind.LoopEnd }, new MacroStep { Kind = ActionKind.LoopEnd });
            MacroValidation.Validate(lib); string status = "";
            using (var engine = new MacroEngine(new Output()))
            { engine.StatusChanged += s => status = s; engine.Start(lib, "main", "test", false, 0); Until(() => status.Contains("失败")); Check(!engine.IsRunning, "Budget failed to terminate"); }
        }
        private static void Validation()
        {
            Reject(() => MacroValidation.Validate(Library(new MacroStep { Kind = ActionKind.LoopEnd })));
            Reject(() => MacroValidation.Validate(Library(new MacroStep { Kind = ActionKind.LoopStart, Number = 1 })));
            Reject(() => MacroValidation.Validate(Library(new MacroStep { Kind = ActionKind.Delay, Number = -1 })));
            Reject(() => MacroValidation.Validate(Library(new MacroStep { Kind = ActionKind.CallMacro, Value = "missing" })));
            Reject(() => MacroValidation.Validate(Library(new MacroStep { Kind = ActionKind.CallMacro, Value = "main" })));
            var lib = Library(); var binding = new MacroBinding { MacroId = "main", Trigger = TriggerKind.WheelUp, Mode = RunMode.WhileHeld }; lib.Bindings.Add(binding);
            Reject(() => MacroValidation.Validate(lib)); binding.Trigger = TriggerKind.Keyboard; binding.KeyCode = 123; binding.Modifiers = KeyModifiers.Control | KeyModifiers.Shift;
            Reject(() => MacroValidation.Validate(lib)); binding.KeyCode = 117; MacroValidation.Validate(lib);
            lib.Bindings.Add(new MacroBinding { MacroId = "main", KeyCode = 117, Modifiers = binding.Modifiers }); Reject(() => MacroValidation.Validate(lib));
            lib.Bindings[1].Enabled = false; MacroValidation.Validate(lib);
            var chain = new MacroLibrary();
            for (int i = 0; i < 18; i++) { var m = new MacroDefinition { Id = i.ToString() }; if (i < 17) m.Steps.Add(new MacroStep { Kind = ActionKind.CallMacro, Value = (i + 1).ToString() }); chain.Macros.Add(m); }
            Reject(() => MacroValidation.Validate(chain));
        }
        private static InputStroke Stroke(TriggerKind kind, bool down = true) { return new InputStroke { Trigger = kind, Key = 117, Down = down }; }
        private static void MouseModifierIndependence()
        {
            var lib = Library(new MacroStep());
            var plain = new MacroBinding { MacroId = "main", Trigger = TriggerKind.Middle,
                Mode = RunMode.WhileHeld, SuppressOriginal = true };
            lib.Bindings.Add(plain);
            var runner = new Runner(); var router = new BindingRouter(runner); router.Configure(lib);
            router.Handle(new InputStroke { Trigger = TriggerKind.Right, Down = true });
            router.Handle(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 160, Down = true, Modifiers = KeyModifiers.Shift });
            Check(router.Handle(new InputStroke { Trigger = TriggerKind.Middle, Down = true, Modifiers = KeyModifiers.Shift,
                RightButtonDown = true }), "Unmodified middle binding failed while Shift was held");
            Check(runner.Starts == 1 && runner.ActiveBinding == plain.Id, "Middle started wrong binding");
            router.Handle(new InputStroke { Trigger = TriggerKind.Keyboard, Key = 160, Down = false });
            Check(runner.IsRunning, "Releasing Shift stopped a held middle binding");
            Check(router.Handle(new InputStroke { Trigger = TriggerKind.Middle, Down = false }), "Middle up was not paired");
            Check(!runner.IsRunning, "Middle up did not stop the macro");
            router.Handle(new InputStroke { Trigger = TriggerKind.Right, Down = false });

            var shifted = new MacroBinding { MacroId = "main", Trigger = TriggerKind.Middle,
                Modifiers = KeyModifiers.Shift, Mode = RunMode.WhileHeld, SuppressOriginal = true };
            lib.Bindings.Add(shifted); MacroValidation.Validate(lib); router.Configure(lib);
            Check(router.Handle(new InputStroke { Trigger = TriggerKind.Middle, Down = true, Modifiers = KeyModifiers.Shift }),
                "Shift+Middle binding did not trigger");
            Check(runner.ActiveBinding == shifted.Id, "Explicit Shift+Middle binding did not win over plain Middle");
            router.Handle(new InputStroke { Trigger = TriggerKind.Middle, Down = false });
            Check(!runner.IsRunning, "Explicit Shift+Middle did not stop on Middle up");

            var wheel = new MacroBinding { MacroId = "main", Trigger = TriggerKind.WheelUp, SuppressOriginal = true };
            lib.Bindings.Add(wheel); router.Configure(lib);
            Check(router.SuppressWheel(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true,
                Modifiers = KeyModifiers.Shift }), "Plain wheel binding lost suppression while Shift was held");
            var shiftedWheel = new MacroBinding { MacroId = "main", Trigger = TriggerKind.WheelUp,
                Modifiers = KeyModifiers.Shift, SuppressOriginal = false };
            lib.Bindings.Add(shiftedWheel); MacroValidation.Validate(lib); router.Configure(lib);
            Check(!router.SuppressWheel(new InputStroke { Trigger = TriggerKind.WheelUp, Down = true,
                Modifiers = KeyModifiers.Shift }), "Explicit wheel binding did not determine suppression");
        }
        private static void RightShiftMiddleChord()
        {
            var lib = Library(new MacroStep());
            var binding = new MacroBinding { MacroId = "main", Trigger = TriggerKind.Middle,
                Mode = RunMode.WhileHeld, SuppressOriginal = true };
            lib.Bindings.Add(binding);
            var runner = new Runner(); var router = new BindingRouter(runner); router.Configure(lib);
            using (var hook = new GlobalInputHook(router.Handle))
            {
                Callback(hook, "Mouse", 0x204, new MousePacket());
                Callback(hook, "Keyboard", 0x100, new KeyboardPacket { Key = 160 });
                Check(Callback(hook, "Mouse", 0x207, new MousePacket()) == new IntPtr(1),
                    "Right+Shift+Middle did not start or intercept its binding");
                Check(runner.IsRunning && runner.ActiveBinding == binding.Id, "Wrong macro started for chord");
                Callback(hook, "Keyboard", 0x101, new KeyboardPacket { Key = 160 });
                Callback(hook, "Mouse", 0x205, new MousePacket());
                Check(runner.IsRunning, "Shift or right release stopped the middle macro");
                Check(Callback(hook, "Mouse", 0x208, new MousePacket()) == new IntPtr(1),
                    "Middle release was not intercepted");
                Check(!runner.IsRunning, "Middle release did not stop the macro");
            }
        }
        private static void Bindings()
        {
            var lib = Library(new MacroStep()); var binding = new MacroBinding { MacroId = "main", KeyCode = 117, SuppressOriginal = true }; lib.Bindings.Add(binding);
            var runner = new Runner(); var router = new BindingRouter(runner); router.Configure(lib);
            Check(router.Handle(Stroke(TriggerKind.Keyboard)), "Down not suppressed"); router.Handle(Stroke(TriggerKind.Keyboard)); Check(runner.Starts == 1, "Auto-repeat retriggers");
            Check(router.Handle(Stroke(TriggerKind.Keyboard, false)), "Up not paired"); runner.Stop();
            var injected = Stroke(TriggerKind.Keyboard); injected.Injected = true; Check(!router.Handle(injected) && runner.Starts == 1, "Injected recursion");
            binding.Mode = RunMode.WhileHeld; router.Handle(Stroke(TriggerKind.Keyboard)); Check(runner.Repeat, "Hold not repeat"); router.Handle(Stroke(TriggerKind.Keyboard, false)); Check(!runner.IsRunning, "Hold release");
            binding.Mode = RunMode.Toggle; router.Handle(Stroke(TriggerKind.Keyboard)); router.Handle(Stroke(TriggerKind.Keyboard, false)); Check(runner.IsRunning, "Toggle ended on release");
            router.Handle(Stroke(TriggerKind.Keyboard)); router.Handle(Stroke(TriggerKind.Keyboard, false)); Check(!runner.IsRunning, "Toggle second press");
            foreach (TriggerKind trigger in Enum.GetValues(typeof(TriggerKind)))
            {
                binding.Trigger = trigger; binding.Mode = RunMode.Once;
                Check(router.Handle(Stroke(trigger)), "Trigger " + trigger); runner.Stop(); router.Handle(Stroke(trigger, false));
            }
            binding.Trigger = TriggerKind.WheelUp; Check(router.SuppressWheel(Stroke(TriggerKind.WheelUp)), "High-resolution wheel suppression");
            var bypass = Stroke(TriggerKind.WheelUp); bypass.BypassBindings = true;
            Check(!router.Handle(bypass) && !router.SuppressWheel(bypass), "Own-window protection");
            router.Suspended = true; int starts = runner.Starts; Check(!router.Handle(Stroke(TriggerKind.WheelUp)), "Suspended trigger"); Check(runner.Starts == starts, "Started while suspended");
            runner.Start(lib, "main", binding.Id, true, 0);
            Check(router.Handle(new InputStroke { Trigger = TriggerKind.Keyboard, Down = true, Key = 123, Modifiers = KeyModifiers.Control | KeyModifiers.Shift }), "Emergency not consumed");
            Check(!runner.IsRunning, "Emergency stop"); router.Suspended = false; lib.BindingsEnabled = false; Check(!router.Handle(Stroke(TriggerKind.WheelUp)), "Disabled");
        }
        private static void Persistence()
        {
            string path = Path.Combine(artifacts, "store-" + Guid.NewGuid().ToString("N"), "macros.xml"); var store = new MacroStore(path);
            Check(store.Load().Macros.Count == 0, "Missing config"); var lib = Library(new MacroStep { Kind = ActionKind.Text, Value = "你好\nABC" });
            lib.Bindings.Add(new MacroBinding { MacroId = "main", Trigger = TriggerKind.WheelDown }); store.Save(lib);
            Check(store.Load().Macros[0].Steps[0].Value == "你好\nABC", "Unicode roundtrip"); lib.Macros[0].Name = "改名"; store.Save(lib);
            Check(new MacroStore(path + ".bak").Load().Macros[0].Name == "测试宏", "Backup"); Check(store.Load().Macros[0].Name == "改名", "Atomic replace");
            File.WriteAllText(path, "<broken>"); Reject(() => store.Load()); Check(File.ReadAllText(path) == "<broken>", "Corrupt overwritten");
            File.WriteAllText(path, "<!DOCTYPE LeiyunLiteMacros [<!ENTITY x SYSTEM 'file:///not-read'>]><LeiyunLiteMacros>&x;</LeiyunLiteMacros>"); Reject(() => store.Load());
        }
        private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance); }
        private static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, args); }
        private static void Capture(Form form, string name)
        {
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(artifacts, name + ".png")); }
        }
        private static void Editor()
        {
            Check(Theme.Green.ToArgb() == ColorTranslator.FromHtml("#44D62C").ToArgb() && Theme.Black.ToArgb() == ColorTranslator.FromHtml("#141414").ToArgb(), "Theme");
            var store = new MacroStore(Path.Combine(artifacts, "editor-" + Guid.NewGuid().ToString("N"), "macros.xml"));
            var lib = Library(new MacroStep { Kind = ActionKind.Text, Value = "雷云lite 测试" }); lib.Bindings.Add(new MacroBinding { MacroId = "main", Trigger = TriggerKind.X1 }); store.Save(lib);
            using (var controller = new MacroController(store, new Output(), false))
            using (var editor = new MacroEditorForm(controller))
            {
                Capture(editor, "macro-editor"); Check(editor.BackColor == Theme.Black, "Editor background");
                Call(editor, "NewMacro"); Field<TextBox>(editor, "nameBox").Text = "界面保存测试";
                Check((bool)Call(editor, "Save"), "Editor save"); Check(store.Load().Macros.Count == 2 && store.Load().Macros[1].Name == "界面保存测试", "New macro save");
                Call(editor, "CopyMacro"); Check((bool)Call(editor, "Save"), "Copy save");
                using (var step = new MacroStepDialog(lib, "main", lib.Macros[0].Steps[0])) Capture(step, "macro-step");
                using (var binding = new MacroBindingDialog(lib, lib.Bindings[0])) Capture(binding, "macro-binding");
            }
        }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        private static void NativeInput()
        {
            Check(Marshal.SizeOf(typeof(WindowsMacroOutput.Input)) == (IntPtr.Size == 8 ? 40 : 28), "Native INPUT layout");
            int injectedRouted = 0;
            using (var hook = new GlobalInputHook(s => { if (s.Key == 135 || s.Key == 231) Interlocked.Increment(ref injectedRouted); return false; }))
            using (var target = new Form { Text = "LeiyunLite isolated input test", Size = new Size(500, 170), TopMost = true })
            using (var textbox = new TextBox { Dock = DockStyle.Fill, Multiline = true })
            {
                hook.Start(); target.Controls.Add(textbox); target.Show(); target.Activate(); SetForegroundWindow(target.Handle); textbox.Focus(); Application.DoEvents();
                Check(GetForegroundWindow() == target.Handle && textbox.Focused, "Test target must be foreground before injection");
                var output = new WindowsMacroOutput(); output.Text("LeiyunLite 测试", CancellationToken.None); output.Key(135, true); output.Key(135, false);
                Until(() => textbox.Text == "LeiyunLite 测试"); Check(injectedRouted == 0, "Injected input reached macro bindings");
                output.Launch(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c exit 0", true);
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardPacket { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct MousePacket { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        private static IntPtr Callback(GlobalInputHook hook, string method, int message, object packet)
        {
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(packet));
            try { Marshal.StructureToPtr(packet, memory, false); return (IntPtr)Call(hook, method, 0, new IntPtr(message), memory); }
            finally { Marshal.FreeHGlobal(memory); }
        }
        private static void HookDecoding()
        {
            var strokes = new List<InputStroke>();
            using (var hook = new GlobalInputHook(s => { strokes.Add(s); return true; }, s => true))
            {
                Callback(hook, "Keyboard", 0x100, new KeyboardPacket { Key = 162 });
                Check(strokes[0].Down && strokes[0].Modifiers == KeyModifiers.Control, "Ctrl down decoding");
                Callback(hook, "Keyboard", 0x100, new KeyboardPacket { Key = 117 });
                Check(strokes[1].Key == 117 && strokes[1].Modifiers == KeyModifiers.Control, "Combo decoding");
                Callback(hook, "Keyboard", 0x101, new KeyboardPacket { Key = 117 }); Check(!strokes[2].Down, "Key up decoding");
                Callback(hook, "Keyboard", 0x101, new KeyboardPacket { Key = 162 }); Check(strokes[3].Modifiers == KeyModifiers.None, "Modifier release");
                int count = strokes.Count; Callback(hook, "Keyboard", 0x100, new KeyboardPacket { Key = 117, Flags = 0x10 });
                Check(count == strokes.Count, "Injected keyboard not ignored");
                int[] messages = { 0x201, 0x202, 0x204, 0x205, 0x207, 0x208, 0x20B, 0x20C, 0x20B, 0x20C };
                TriggerKind[] triggers = { TriggerKind.Left, TriggerKind.Right, TriggerKind.Middle, TriggerKind.X1, TriggerKind.X2 };
                for (int i = 0; i < messages.Length; i++)
                {
                    uint data = i >= 8 ? 2u << 16 : i >= 6 ? 1u << 16 : 0;
                    Callback(hook, "Mouse", messages[i], new MousePacket { Data = data }); var stroke = strokes[strokes.Count - 1];
                    Check(stroke.Trigger == triggers[i / 2] && stroke.Down == (i % 2 == 0), "Mouse message " + messages[i]);
                }
                Callback(hook, "Mouse", 0x204, new MousePacket());
                Callback(hook, "Mouse", 0x207, new MousePacket());
                Check(strokes[strokes.Count - 1].Trigger == TriggerKind.Middle && strokes[strokes.Count - 1].RightButtonDown, "Right + middle chord state lost");
                Callback(hook, "Mouse", 0x208, new MousePacket()); Callback(hook, "Mouse", 0x205, new MousePacket());
                count = strokes.Count;
                Check(Callback(hook, "Mouse", 0x20A, new MousePacket { Data = 60u << 16 }) == new IntPtr(1), "Fractional wheel leaked");
                Check(strokes.Count == count, "Fractional wheel triggered early");
                Callback(hook, "Mouse", 0x20A, new MousePacket { Data = 60u << 16 }); Check(strokes.Count == count + 1 && strokes[count].Trigger == TriggerKind.WheelUp, "Wheel accumulation");
                Callback(hook, "Mouse", 0x20A, new MousePacket { Data = unchecked((uint)(-240 << 16)) });
                Check(strokes.Count == count + 3 && strokes[count + 2].Trigger == TriggerKind.WheelDown, "Multiple reverse notches");
                count = strokes.Count; Callback(hook, "Mouse", 0x201, new MousePacket { Flags = 1 }); Check(strokes.Count == count, "Injected mouse");
            }
        }
        private sealed class Output : IMacroOutput
        {
            private readonly List<string> events = new List<string>(); public bool ThrowText;
            private void Add(string value) { lock (events) events.Add(value); }
            public int Count { get { lock (events) return events.Count; } }
            public string[] Snapshot() { lock (events) return events.ToArray(); }
            public bool Has(string value) { lock (events) return events.Contains(value); }
            public void Clear() { lock (events) events.Clear(); }
            public void Key(int key, bool down) { Add("K" + key + (down ? "+" : "-")); }
            public void MouseButton(MouseAction button, bool down) { Add("M" + button + (down ? "+" : "-")); }
            public void Wheel(int notches) { Add("W" + notches); }
            public void Text(string text, CancellationToken token) { if (ThrowText) throw new Exception("test failure"); Add("T" + text); }
            public void Launch(string target, string args, bool command) { Add((command ? "C" : "L") + target + ":" + args); }
        }
        private sealed class TimingOutput : IMacroOutput
        {
            private readonly List<long> clicks = new List<long>();
            public int Clicks { get { lock (clicks) return clicks.Count; } }
            public double SpanMilliseconds { get { lock (clicks) return clicks.Count < 2 ? double.MaxValue : (clicks[clicks.Count - 1] - clicks[0]) * 1000.0 / Stopwatch.Frequency; } }
            public void Key(int key, bool down) { }
            public void MouseButton(MouseAction button, bool down) { if (button == MouseAction.Left && down) lock (clicks) clicks.Add(Stopwatch.GetTimestamp()); }
            public void Wheel(int notches) { }
            public void Text(string text, CancellationToken token) { }
            public void Launch(string target, string arguments, bool command) { }
        }
        private sealed class Runner : IMacroRunner
        {
            public bool IsRunning { get; private set; } public string ActiveBinding { get; private set; } public bool Repeat; public int Starts;
            public bool Start(MacroLibrary lib, string macro, string binding, bool repeat, int delay) { if (IsRunning) return false; Starts++; Repeat = repeat; IsRunning = true; ActiveBinding = binding; return true; }
            public void Stop() { IsRunning = false; ActiveBinding = null; }
        }
    }
}
