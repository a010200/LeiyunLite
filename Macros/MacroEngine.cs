using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RazerBatteryTray.Macros
{
    internal interface IMacroOutput
    {
        void Key(int key, bool down);
        void MouseButton(MouseAction button, bool down);
        void Wheel(int notches);
        void Text(string text, CancellationToken token);
        void Launch(string target, string arguments, bool command);
    }
    internal interface IMacroRunner
    {
        bool IsRunning { get; }
        string ActiveBinding { get; }
        bool Start(MacroLibrary library, string macroId, string bindingId, bool repeat, int initialDelay);
        void Stop();
    }
    internal sealed class MacroEngine : IMacroRunner, IDisposable
    {
        private readonly object gate = new object();
        private readonly IMacroOutput output;
        private CancellationTokenSource cancellation;
        private Task worker;
        private string activeBinding;
        private bool disposed;
        public event Action<string> StatusChanged;
        public bool IsRunning { get { lock (gate) return cancellation != null; } }
        public string ActiveBinding { get { lock (gate) return activeBinding; } }
        internal MacroEngine(IMacroOutput output) { this.output = output; }
        private void Report(string message)
        {
            var handler = StatusChanged;
            if (handler != null) { try { handler(message); } catch { } }
        }
        public bool Start(MacroLibrary library, string macroId, string bindingId, bool repeat, int initialDelay)
        {
            lock (gate)
            {
                if (disposed || cancellation != null) return false;
                var macro = library.Find(macroId);
                if (macro == null || macro.Steps.Count == 0) { Report("宏没有动作，无法执行。"); return false; }
                // library is an immutable snapshot supplied by MacroController.
                var source = new CancellationTokenSource();
                cancellation = source; activeBinding = bindingId;
                worker = Task.Factory.StartNew(() => Run(library, macro, repeat, initialDelay, source),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                return true;
            }
        }
        private void Run(MacroLibrary library, MacroDefinition macro, bool repeat, int initialDelay, CancellationTokenSource source)
        {
            var heldKeys = new HashSet<int>(); var heldButtons = new HashSet<MouseAction>();
            string status = "已完成：" + macro.Name;
            try
            {
                Report("执行中：" + macro.Name + " · Ctrl+Shift+F12 停止");
                Wait(initialDelay, source.Token);
                do
                {
                    int budget = 100000;
                    Execute(library, macro.Steps, 0, macro.Steps.Count, source.Token, heldKeys, heldButtons, ref budget);
                    string releaseError = Release(heldKeys, heldButtons);
                    if (releaseError != null) throw new InvalidOperationException(releaseError);
                    if (repeat) Wait(25, source.Token);
                } while (repeat);
            }
            catch (OperationCanceledException) { status = "已停止：" + macro.Name; }
            catch (Exception ex) { status = "宏执行失败：" + ex.Message; }
            finally
            {
                string releaseError = Release(heldKeys, heldButtons);
                if (releaseError != null) status = "释放按键失败：" + releaseError;
                lock (gate)
                {
                    if (ReferenceEquals(cancellation, source)) { cancellation = null; activeBinding = null; }
                    source.Dispose();
                }
                Report(status);
            }
        }
        private string Release(HashSet<int> keys, HashSet<MouseAction> buttons)
        {
            string error = null;
            foreach (int key in new List<int>(keys))
            {
                try { output.Key(key, false); keys.Remove(key); } catch (Exception ex) { error = ex.Message; }
            }
            foreach (var button in new List<MouseAction>(buttons))
            {
                try { output.MouseButton(button, false); buttons.Remove(button); } catch (Exception ex) { error = ex.Message; }
            }
            return error;
        }
        private static void Wait(int milliseconds, CancellationToken token)
        {
            if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested();
        }
        private void Execute(MacroLibrary library, List<MacroStep> steps, int start, int end, CancellationToken token,
            HashSet<int> keys, HashSet<MouseAction> buttons, ref int budget)
        {
            for (int i = start; i < end; i++)
            {
                token.ThrowIfCancellationRequested();
                if (--budget < 0) throw new InvalidOperationException("单次宏超过 100000 个动作，请缩小循环。");
                var step = steps[i];
                switch (step.Kind)
                {
                    case ActionKind.Delay: Wait(step.Number, token); break;
                    case ActionKind.Keyboard:
                        if (step.Press != PressMode.Up) { output.Key(step.KeyCode, true); keys.Add(step.KeyCode); }
                        if (step.Press != PressMode.Down) { output.Key(step.KeyCode, false); keys.Remove(step.KeyCode); }
                        break;
                    case ActionKind.Mouse:
                        if (step.Mouse == MouseAction.WheelUp || step.Mouse == MouseAction.WheelDown)
                            output.Wheel(step.Mouse == MouseAction.WheelUp ? step.Number : -step.Number);
                        else
                        {
                            if (step.Press != PressMode.Up) { output.MouseButton(step.Mouse, true); buttons.Add(step.Mouse); }
                            if (step.Press != PressMode.Down) { output.MouseButton(step.Mouse, false); buttons.Remove(step.Mouse); }
                        }
                        break;
                    case ActionKind.Text: output.Text(step.Value ?? "", token); break;
                    case ActionKind.Launch: output.Launch(step.Value, step.Arguments, false); break;
                    case ActionKind.Command: output.Launch(step.Value, step.Arguments, true); break;
                    case ActionKind.CallMacro:
                        var child = library.Find(step.Value);
                        Execute(library, child.Steps, 0, child.Steps.Count, token, keys, buttons, ref budget);
                        break;
                    case ActionKind.LoopStart:
                        int depth = 1, close = i + 1;
                        for (; close < end; close++)
                        {
                            if (steps[close].Kind == ActionKind.LoopStart) depth++;
                            if (steps[close].Kind == ActionKind.LoopEnd && --depth == 0) break;
                        }
                        if (close == end) throw new InvalidOperationException("循环没有结束标记。");
                        for (int n = 0; n < step.Number; n++)
                        {
                            token.ThrowIfCancellationRequested();
                            if (--budget < 0) throw new InvalidOperationException("循环超过单次执行上限。");
                            Execute(library, steps, i + 1, close, token, keys, buttons, ref budget);
                        }
                        i = close;
                        break;
                }
            }
        }
        public void Stop() { lock (gate) { if (cancellation != null) cancellation.Cancel(); } }
        public void Dispose()
        {
            Task pending;
            lock (gate) { if (disposed) return; disposed = true; if (cancellation != null) cancellation.Cancel(); pending = worker; }
            if (pending != null && Task.CurrentId != pending.Id) pending.Wait(2000);
        }
    }
}
