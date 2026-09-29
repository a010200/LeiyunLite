using System;
using System.Collections.Generic;

namespace RazerBatteryTray.Macros
{
    internal sealed class InputStroke
    {
        public TriggerKind Trigger;
        public int Key;
        public KeyModifiers Modifiers;
        public bool Down;
        public long Timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        public bool Injected { get; set; }
        public bool BypassBindings { get; set; }
        public bool RightButtonDown { get; set; }
        internal bool RecordingAreaAllowed, SkipRecording, BreakRecordingTiming;
        public string Physical { get { return Trigger + ":" + (Trigger == TriggerKind.Keyboard ? Key : 0); } }
    }
    internal sealed class BindingRouter
    {
        private readonly object gate = new object();
        private readonly IMacroRunner engine;
        private MacroLibrary library = new MacroLibrary();
        private readonly HashSet<string> pressed = new HashSet<string>();
        private readonly HashSet<string> suppressed = new HashSet<string>();
        private readonly Dictionary<string, string> held = new Dictionary<string, string>();
        public volatile bool Suspended;
        internal BindingRouter(IMacroRunner engine) { this.engine = engine; }
        public void Configure(MacroLibrary snapshot) { lock (gate) { library = snapshot; held.Clear(); } }
        // Input dispatch and configuration replacement share this gate. No old binding
        // can start between cancellation and installation of the replacement snapshot.
        internal void ReplaceAndStop(MacroLibrary snapshot)
        {
            lock (gate) { library = snapshot; held.Clear(); engine.Stop(); }
            // Keep pressed/suppressed until physical key-up, so an intercepted down
            // is paired correctly even when its binding was removed while held.
        }
        public void Stop() { engine.Stop(); }
        internal bool TrySuspendIdle()
        {
            lock (gate) { if (engine.IsRunning) return false; Suspended = true; return true; }
        }
        private static bool Matches(MacroBinding binding, InputStroke stroke)
        {
            if (!binding.Enabled || binding.Trigger != stroke.Trigger) return false;
            if (binding.Trigger == TriggerKind.Keyboard)
                return binding.KeyCode == stroke.Key && binding.Modifiers == stroke.Modifiers;
            // A mouse button remains its own trigger while unrelated keyboard
            // modifiers are held. Explicit combinations require their modifiers.
            return (stroke.Modifiers & binding.Modifiers) == binding.Modifiers;
        }
        private MacroBinding FindMatch(InputStroke stroke)
        {
            MacroBinding best = null; int bestSpecificity = -1;
            foreach (var binding in library.Bindings)
            {
                if (!Matches(binding, stroke)) continue;
                int value = (int)binding.Modifiers, specificity = 0;
                while (value != 0) { specificity += value & 1; value >>= 1; }
                if (specificity > bestSpecificity) { best = binding; bestSpecificity = specificity; }
            }
            return best;
        }
        public bool SuppressWheel(InputStroke stroke)
        {
            lock (gate)
            {
                if (stroke.BypassBindings || Suspended || !library.BindingsEnabled) return false;
                var match = FindMatch(stroke);
                return match != null && match.SuppressOriginal;
            }
        }
        public bool Handle(InputStroke stroke)
        {
            if (stroke.Injected) return false;
            lock (gate)
            {
                string physical = stroke.Physical;
                if (!stroke.Down)
                {
                    pressed.Remove(physical);
                    string heldBinding;
                    if (held.TryGetValue(physical, out heldBinding))
                    {
                        held.Remove(physical);
                        if (engine.ActiveBinding == heldBinding) engine.Stop();
                    }
                    return suppressed.Remove(physical);
                }
                bool wheel = MacroValidation.IsWheel(stroke.Trigger);
                bool repeated = !wheel && !pressed.Add(physical);
                if (stroke.Trigger == TriggerKind.Keyboard && stroke.Key == 123 &&
                    (stroke.Modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    engine.Stop(); suppressed.Add(physical); return true;
                }
                if (repeated) return suppressed.Contains(physical);
                if (stroke.BypassBindings || Suspended || !library.BindingsEnabled) return false;
                var binding = FindMatch(stroke);
                if (binding != null)
                {
                    if (binding.Mode == RunMode.Toggle && engine.ActiveBinding == binding.Id) engine.Stop();
                    else
                    {
                        bool started = engine.Start(library, binding.MacroId, binding.Id, binding.Mode != RunMode.Once, 0);
                        if (started && binding.Mode == RunMode.WhileHeld) held[physical] = binding.Id;
                    }
                    if (binding.SuppressOriginal && !wheel) suppressed.Add(physical);
                    return binding.SuppressOriginal;
                }
                return false;
            }
        }
    }
}
