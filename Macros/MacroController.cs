using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RazerBatteryTray.Macros
{
    internal sealed class MacroController : IDisposable
    {
        private readonly IMacroStore store;
        private readonly object configurationGate = new object();
        internal bool SafetySavePending { get; private set; }
        private readonly MacroEngine engine;
        private readonly BindingRouter router;
        private GlobalInputHook hooks;
        private MacroLibrary library;
        private volatile MacroRecorder recorder;
        private bool manuallyPaused;
        private volatile bool recordingSession;
        private volatile bool recordingStopRequested;
        private volatile bool recordingInArea;
        private sealed class Area { internal System.Drawing.Rectangle Bounds; internal bool Focused; }
        private volatile Area recordingArea = new Area();
        internal void SetRecordingArea(System.Drawing.Rectangle bounds, bool focused)
        {
            bool wasFocused = recordingArea.Focused;
            recordingArea = new Area { Bounds = bounds, Focused = focused };
            var active = recorder;
            if (recordingInArea && wasFocused && !focused && active != null) active.Capture(new InputStroke { BreakRecordingTiming = true });
        }
        internal bool RecordingStopRequested { get { return recordingStopRequested; } }
        internal bool RecordingSession { get { return recordingSession; } }
        internal MacroRecorder Recorder { get { return recorder; } }
        private readonly uint processId = (uint)Process.GetCurrentProcess().Id;
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out System.Drawing.Point point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
        public string Status { get; private set; }
        public string ConfigPath { get { return store.FilePath; } }
        public bool IsRunning { get { return engine.IsRunning; } }
        public event Action<string> StatusChanged;
        public MacroController(IMacroStore store, IMacroOutput output, bool installHooks, bool initiallySuspended = false)
        {
            this.store = store;
            library = store.Load();
            engine = new MacroEngine(output);
            router = new BindingRouter(engine); router.Configure(library);
            manuallyPaused = initiallySuspended; router.Suspended = initiallySuspended;
            Status = "就绪 · Ctrl+Shift+F12 停止所有宏";
            engine.StatusChanged += message => {
                Status = message;
                var handler = StatusChanged; if (handler != null) handler(message);
            };
            if (installHooks)
            {
                hooks = new GlobalInputHook(RouteInput, SuppressWheel);
                try { hooks.Start(); } catch { hooks.Dispose(); engine.Dispose(); throw; }
            }
        }
        public MacroLibrary Snapshot() { lock (configurationGate) return library.Clone(); }
        private void ProtectOwnWindow(InputStroke stroke)
        {
            uint foregroundProcess;
            GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcess);
            stroke.BypassBindings = foregroundProcess == processId;
            // Mouse hooks run before activation. Exclude our Stop/Cancel click by its target HWND too.
            if (stroke.Trigger != TriggerKind.Keyboard)
            {
                System.Drawing.Point point;
                if (GetCursorPos(out point)) { uint targetProcess; GetWindowThreadProcessId(WindowFromPoint(point), out targetProcess); stroke.BypassBindings |= targetProcess == processId; }
            }
        }
        private bool RouteInput(InputStroke stroke)
        {
            ProtectOwnWindow(stroke);
            var active = recorder;
            if (active != null) {
                if (recordingInArea) {
                    var area = recordingArea; System.Drawing.Point point;
                      bool accepted = AllowsAreaStroke(stroke, area.Bounds, area.Focused, GetCursorPos(out point) ? (System.Drawing.Point?)point : null);
                    stroke.RecordingAreaAllowed = accepted; stroke.SkipRecording = !accepted;
                }
                active.Capture(stroke);
            }
            if (recordingSession && stroke.Down && stroke.Trigger == TriggerKind.Keyboard && stroke.Key == 123 && (stroke.Modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift)) recordingStopRequested = true;
            return router.Handle(stroke);
        }
        private bool SuppressWheel(InputStroke stroke) { ProtectOwnWindow(stroke); return router.SuppressWheel(stroke); }
        internal static bool AllowsAreaStroke(InputStroke stroke, System.Drawing.Rectangle bounds, bool focused, System.Drawing.Point? point)
        {
            return focused && stroke.BypassBindings && !stroke.Injected
                && (stroke.Trigger == TriggerKind.Keyboard || point.HasValue && bounds.Contains(point.Value));
        }
        public void Save(MacroLibrary edited)
        {
            lock (configurationGate) Commit(edited.Clone(), false);
        }
        // Definition drafts must never restore a previously revoked binding.
        internal void SaveDefinitions(MacroLibrary edited)
        {
            lock (configurationGate)
            {
                var next = edited.Clone();
                next.Bindings = library.Clone().Bindings; next.BindingsEnabled = library.BindingsEnabled;
                Commit(next, false);
            }
        }
        internal void ApplyBinding(MacroBinding binding, bool replaceSignature)
        {
            lock (configurationGate)
            {
                var next = library.Clone();
                if (next.Find(binding.MacroId) == null || next.Find(binding.MacroId).Steps.Count == 0)
                    throw new InvalidOperationException("请先保存一个包含动作的宏。");
                next.Bindings.RemoveAll(b => b.Id == binding.Id || (replaceSignature && b.Signature == binding.Signature));
                next.Bindings.Add(binding); Commit(next.Clone(), false);
            }
        }
        internal void RemoveBinding(string id)
        {
            lock (configurationGate) { var next = library.Clone(); next.Bindings.RemoveAll(b => b.Id == id); Commit(next, true); }
        }
        internal void SetBindingEnabled(string id, bool enabled)
        {
            lock (configurationGate)
            {
                var next = library.Clone(); var b = next.Bindings.Find(x => x.Id == id);
                if (b == null) throw new InvalidOperationException("绑定已不存在。");
                b.Enabled = enabled; Commit(next, !enabled);
            }
        }
        internal void SetBindingsEnabled(bool enabled)
        {
            lock (configurationGate) { var next = library.Clone(); next.BindingsEnabled = enabled; Commit(next, !enabled); }
        }
        internal void RemoveMacro(string id)
        {
            lock (configurationGate)
            {
                var next = library.Clone(); next.Bindings.RemoveAll(b => b.MacroId == id); next.Macros.RemoveAll(m => m.Id == id);
                Commit(next, true); // Validate references before deleting anything.
            }
        }
        internal void RetrySafetySave() { lock (configurationGate) { store.Save(library.Clone()); SafetySavePending = false; } }
        private void Commit(MacroLibrary next, bool safety)
        {
            MacroValidation.Validate(next);
            if (safety)
            {
                router.ReplaceAndStop(next); library = next; SafetySavePending = true;
                // A storage failure must not reactivate a revoked input in this session.
                store.Save(next); SafetySavePending = false;
            }
            else
            {
                store.Save(next); router.ReplaceAndStop(next); library = next; SafetySavePending = false;
            }
        }
        public void SuspendBindings(bool suspend)
        {
            manuallyPaused = suspend; router.Suspended = suspend || recordingSession;
            if (suspend) engine.Stop();
        }
        internal bool BeginUpdateHandoff()
        {
            lock (configurationGate) { return !recordingSession && !SafetySavePending && router.TrySuspendIdle(); }
        }
        internal void CancelUpdateHandoff() { router.Suspended = manuallyPaused || recordingSession; }
        // Tray pause affects future triggers only; Stop is a separate, explicit action.
        public void PauseNewBindings(bool pause) { manuallyPaused = pause; router.Suspended = pause || recordingSession; }
        internal void PrepareRecording()
        {
            if (recordingSession) throw new InvalidOperationException("Recording already active.");
            recordingStopRequested = false; recordingSession = true; router.Suspended = true; engine.Stop();
        }
        internal void StartRecording(RecordingDelay delay, int fixedDelay, int capacity, bool inArea = false)
        {
            if (!recordingSession || recorder != null) throw new InvalidOperationException();
            recordingInArea = inArea; recorder = new MacroRecorder(delay, fixedDelay, capacity);
        }
        internal System.Collections.Generic.List<MacroStep> EndRecording()
        {
            var active = recorder; recorder = null;
            try { return active == null ? new System.Collections.Generic.List<MacroStep>() : active.Finish(); }
            finally { if (active != null) active.Dispose(); recordingSession = false; recordingStopRequested = false; router.Suspended = manuallyPaused; }
        }
        public bool Preview(MacroLibrary edited, string macroId)
        {
            if (recordingSession) return false;
            MacroValidation.Validate(edited);
            return engine.Start(edited.Clone(), macroId, "preview", false, 3000);
        }
        public void Stop() { if (recordingSession) recordingStopRequested = true; engine.Stop(); }
        public void Dispose()
        {
            manuallyPaused = true;
            EndRecording();
            router.Suspended = true;
            // Leave hooks alive while SendInput releases keys; they run on their
            // own message thread, so UI-thread disposal cannot deadlock them.
            engine.Dispose();
            if (hooks != null) { hooks.Dispose(); hooks = null; }
        }
    }
}
