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
        public bool SuppressWheel(InputStroke stroke)
        {
            lock (gate)
            {
                return !stroke.BypassBindings && !Suspended && library.BindingsEnabled && library.Bindings.Exists(b =>
                    b.Enabled && b.SuppressOriginal && b.Trigger == stroke.Trigger && b.Modifiers == stroke.Modifiers);
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
                    string binding;
                    if (held.TryGetValue(physical, out binding))
                    {
                        held.Remove(physical);
                        if (engine.ActiveBinding == binding) engine.Stop();
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
                foreach (var binding in library.Bindings)
                {
                    if (!binding.Enabled || binding.Trigger != stroke.Trigger || binding.Modifiers != stroke.Modifiers ||
                        (binding.Trigger == TriggerKind.Keyboard && binding.KeyCode != stroke.Key)) continue;
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
