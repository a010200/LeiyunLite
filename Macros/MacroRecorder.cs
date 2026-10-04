using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace RazerBatteryTray.Macros
{
    internal enum RecordingDelay { Actual, Fixed, None }

    // Pure bounded accumulator, separate from hooks and UI. Never emits physical input.
    internal sealed class RecordingBuffer
    {
        internal readonly List<MacroStep> Steps = new List<MacroStep>();
        private readonly Dictionary<PhysicalInputKey, MacroStep> held = new Dictionary<PhysicalInputKey, MacroStep>();
        private readonly RecordingDelay delay;
        private readonly int fixedDelay, capacity;
        private long previous;
        private bool resetTiming;
        internal string Reason = "";
        internal RecordingBuffer(RecordingDelay delay, int fixedDelay, int capacity)
        {
            if (capacity < 2 || capacity > 500 || fixedDelay < 0 || fixedDelay > 600000) throw new ArgumentOutOfRangeException();
            this.delay = delay; this.fixedDelay = fixedDelay; this.capacity = capacity;
        }
        private static bool StopModifier(MacroStep step) { return step.Kind == ActionKind.Keyboard && step.Press == PressMode.Down && (step.KeyCode == 16 || step.KeyCode == 17 || step.KeyCode >= 160 && step.KeyCode <= 163); }
        internal bool Accept(InputStroke input)
        {
            if (input.BreakRecordingTiming) { Balance(); resetTiming = true; return true; }
            if (input.Injected) return true;
            if (input.Trigger == TriggerKind.Keyboard && (input.Key < 8 || input.Key > 254)) return true;
            if (input.Trigger == TriggerKind.Keyboard && input.Down && input.Key == 123 && (input.Modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                // Remove only the unused modifier-down tail belonging to the stop gesture.
                while (Steps.Count > 0)
                {
                    var last = Steps[Steps.Count - 1];
                    if (last.Kind != ActionKind.Delay && !StopModifier(last)) break;
                    if (StopModifier(last)) held.Remove(new PhysicalInputKey(TriggerKind.Keyboard, last.KeyCode));
                    Steps.RemoveAt(Steps.Count - 1);
                }
                Reason = "shortcut"; return false;
            }
            bool wheel = MacroValidation.IsWheel(input.Trigger), wasHeld = held.ContainsKey(input.Physical);
            if ((input.SkipRecording || input.BypassBindings && !input.RecordingAreaAllowed) && (input.Down || !wasHeld)) return true;
            if (!wheel && (input.Down == wasHeld)) return true; // Ignore repeats and pre-session key releases.
            var step = new MacroStep { Kind = input.Trigger == TriggerKind.Keyboard ? ActionKind.Keyboard : ActionKind.Mouse, KeyCode = input.Key, Press = wheel ? PressMode.Tap : input.Down ? PressMode.Down : PressMode.Up, Number = 1 };
            if (step.Kind == ActionKind.Mouse) step.Mouse = (MouseAction)((int)input.Trigger - 1);
            int ms = Steps.Count == 0 || resetTiming || delay == RecordingDelay.None ? 0 : delay == RecordingDelay.Fixed ? fixedDelay : (int)Math.Min(600000, Math.Max(0, (input.Timestamp - previous) * 1000.0 / Stopwatch.Frequency));
            int remainingHeld = held.Count + (wheel ? 0 : input.Down ? 1 : -1);
            if (Steps.Count + (ms > 0 ? 1 : 0) + 1 + remainingHeld > capacity) { Reason = "limit"; return false; }
            if (ms > 0) Steps.Add(new MacroStep { Kind = ActionKind.Delay, Number = ms });
            Steps.Add(step); previous = input.Timestamp; resetTiming = false;
            if (!wheel) { if (input.Down) held[input.Physical] = step; else held.Remove(input.Physical); }
            return true;
        }
        internal void Balance()
        {
            foreach (var step in held.Values.Reverse()) Steps.Add(new MacroStep { Kind = step.Kind, KeyCode = step.KeyCode, Mouse = step.Mouse, Press = PressMode.Up, Number = 1 });
            held.Clear();
        }
    }

    // Hook callback only queues; a dedicated consumer builds the sequence without touching UI or files.
    internal sealed class MacroRecorder : IDisposable
    {
        private readonly BlockingCollection<InputStroke> queue = new BlockingCollection<InputStroke>(1024);
        private readonly RecordingBuffer buffer;
        private readonly Task worker;
        private volatile bool completed;
        private volatile int count;
        private volatile string reason = "";
        private int disposed;
        internal bool Completed { get { return completed; } }
        internal int Count { get { return count; } }
        internal string Reason { get { return reason; } }
        internal MacroRecorder(RecordingDelay delay, int fixedDelay, int capacity)
        {
            buffer = new RecordingBuffer(delay, fixedDelay, capacity);
            worker = Task.Factory.StartNew(() => {
                try
                {
                    foreach (var stroke in queue.GetConsumingEnumerable())
                    {
                        lock (buffer) {
                        if (!buffer.Accept(stroke)) { reason = buffer.Reason; queue.CompleteAdding(); break; }
                        count = buffer.Steps.Count;
                        }
                    }
                }
                catch { reason = "error"; }
                finally { lock (buffer) { buffer.Balance(); count = buffer.Steps.Count; } completed = true; }
            }, TaskCreationOptions.LongRunning);
        }
        internal void Capture(InputStroke stroke)
        {
            if (completed || stroke.Injected) return;
            try { if (!queue.TryAdd(stroke)) { reason = "overflow"; queue.CompleteAdding(); } }
            catch (InvalidOperationException) { } // Concurrent stop; fail open.
        }
        internal MacroStep[] Snapshot() { lock (buffer) return buffer.Steps.ToArray(); }
        internal List<MacroStep> Finish()
        {
            queue.CompleteAdding(); worker.Wait();
            return new List<MacroStep>(buffer.Steps);
        }
        public void Dispose() { if (System.Threading.Interlocked.Exchange(ref disposed, 1) == 0) { Finish(); queue.Dispose(); } }
    }
}
