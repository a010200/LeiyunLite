using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace RazerBatteryTray.Macros
{
    internal sealed class WindowsMacroOutput : IMacroOutput
    {
        internal static readonly UIntPtr InputTag = new UIntPtr(0x4C594C31);
        private const ulong TagMarker = 0x4C594C31UL;
        private const uint NativePrefix = 0x4C590000u;
        private const int NativeRunCount = 65534; // Reserve zero and the legacy LYL1 tag.
        // Windows on this machine preserves only the low 32 bits of mouse Extra.
        // Keep LY identification plus a run slot there; high bits retain the full run.
        private static uint NativeTag(long run)
        {
            uint slot = (uint)((run - 1) % NativeRunCount + 1);
            if (slot >= 0x4C31) slot++;
            return NativePrefix | slot;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputData Data; }
        [StructLayout(LayoutKind.Explicit)] internal struct InputData
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
        { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput
        { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        private long totalInputsSent;
        private long mouseSubmitted, mouseObserved, inputsSubmitted, inputsObserved;
        internal bool InputPending { get { return Interlocked.Read(ref inputsObserved) < Interlocked.Read(ref inputsSubmitted); } }
        internal bool MouseInputPending { get { return Interlocked.Read(ref mouseObserved) < Interlocked.Read(ref mouseSubmitted); } }
        private bool IsCurrentInput(UIntPtr tag)
        {
            ulong value = tag.ToUInt64(), current = CurrentInputTag.ToUInt64();
            return value == current || (value >> 32) == 0 && (uint)value == (uint)current;
        }
        internal void ObserveMouseInput(UIntPtr tag)
        { if (IsCurrentInput(tag)) { Interlocked.Increment(ref mouseObserved); Interlocked.Increment(ref inputsObserved); } }
        internal void ObserveKeyboardInput(UIntPtr tag)
        { if (IsCurrentInput(tag)) Interlocked.Increment(ref inputsObserved); }
        private long leftDownsSent, leftUpsSent;
        // Both native low bits and full-width high bits identify the run.
        // A canceled run stays recognizable even if another macro starts.
        private long generation, cancelledThrough, activeTag = (long)TagMarker;
        internal long TotalInputsSent { get { return Interlocked.Read(ref totalInputsSent); } }
        internal long LeftDownsSent { get { return Interlocked.Read(ref leftDownsSent); } }
        internal long LeftUpsSent { get { return Interlocked.Read(ref leftUpsSent); } }
        internal UIntPtr CurrentInputTag
        {
            get
            {
                ulong value = unchecked((ulong)Interlocked.Read(ref activeTag));
                return IntPtr.Size == 8 ? new UIntPtr(value) : new UIntPtr((uint)value);
            }
        }
        internal static bool IsOwnInputTag(UIntPtr tag)
        {
            uint low = (uint)tag.ToUInt64();
            return low == (uint)TagMarker || (low & 0xFFFF0000u) == NativePrefix && (low & 0xFFFFu) != 0;
        }
        internal void BeginRun()
        {
            if (InputPending) throw new InvalidOperationException("上一轮模拟输入尚未排空，请稍后重试。");
            long next = Interlocked.Increment(ref generation);
            Interlocked.Exchange(ref activeTag, unchecked((long)(((ulong)(uint)next << 32) | NativeTag(next))));
        }
        internal void CancelRun()
        {
            Interlocked.Exchange(ref cancelledThrough, Interlocked.Read(ref generation));
        }
        internal bool ShouldSuppressStoppedDown(UIntPtr tag)
        {
            if (!IsOwnInputTag(tag)) return false;
            ulong raw = tag.ToUInt64();
            long current = Interlocked.Read(ref generation), run;
            if (IntPtr.Size == 8 && (raw >> 32) != 0)
            {
                // Extend the full-width generation relative to the current epoch.
                run = (current & ~0xFFFFFFFFL) | (long)(raw >> 32);
                if (run > current) run -= 0x100000000L;
            }
            else
            {
                uint low = (uint)raw;
                if (low == (uint)TagMarker) return false; // Legacy unversioned input.
                uint slot = low & 0xFFFFu;
                if (slot >= 0x4C31) slot--;
                if (current == 0) return false;
                run = ((current - 1) / NativeRunCount) * NativeRunCount + slot;
                if (run > current) run -= NativeRunCount;
            }
            // BeginRun cannot reuse a slot until all prior native input is observed.
            // Thus a wrapped native slot cannot revive an accepted queued old DOWN.
            return run > 0 && run <= Interlocked.Read(ref cancelledThrough);
        }
        private void Send(params Input[] inputs)
        {
            int mice = 0; foreach (var input in inputs) if (input.Type == 0) mice++;
            Interlocked.Add(ref mouseSubmitted, mice); Interlocked.Add(ref inputsSubmitted, inputs.Length);
            uint accepted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
            if (accepted != inputs.Length)
            {
                // Our current mouse sends contain one packet; count only accepted
                // packets so a rejected SendInput cannot pin the hook forever.
                int rejected = 0;
                for (int i = (int)accepted; i < inputs.Length; i++) if (inputs[i].Type == 0) rejected++;
                Interlocked.Add(ref mouseSubmitted, -rejected); Interlocked.Add(ref inputsSubmitted, -(inputs.Length - accepted));
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未接受模拟输入；请检查目标窗口权限。");
            }
            Interlocked.Add(ref totalInputsSent, inputs.Length);
        }
        public void Key(int key, bool down)
        {
            bool extended = key == 163 || key == 165 || (key >= 33 && key <= 46) || key == 91 || key == 92 || key == 93 || key == 111 || key == 144;
            Send(new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput {
                Key = (ushort)key, Flags = (down ? 0u : 2u) | (extended ? 1u : 0u), Extra = CurrentInputTag } } });
        }
        public void MouseButton(MouseAction button, bool down)
        {
            uint flags, data = 0;
            switch (button)
            {
                case MouseAction.Left: flags = down ? 0x02u : 0x04u; break;
                case MouseAction.Right: flags = down ? 0x08u : 0x10u; break;
                case MouseAction.Middle: flags = down ? 0x20u : 0x40u; break;
                case MouseAction.X1: flags = down ? 0x80u : 0x100u; data = 1; break;
                case MouseAction.X2: flags = down ? 0x80u : 0x100u; data = 2; break;
                default: throw new ArgumentException("不是鼠标按钮。");
            }
            Send(new Input { Data = new InputData { Mouse = new MouseInput { Flags = flags, Data = data, Extra = CurrentInputTag } } });
            if (button == MouseAction.Left)
            {
                if (down) Interlocked.Increment(ref leftDownsSent);
                else Interlocked.Increment(ref leftUpsSent);
            }
        }
        public void Wheel(int notches)
        {
            Send(new Input { Data = new InputData { Mouse = new MouseInput { Flags = 0x0800, Data = unchecked((uint)(notches * 120)), Extra = CurrentInputTag } } });
        }
        public void Text(string text, CancellationToken token)
        {
            foreach (char ch in text)
            {
                token.ThrowIfCancellationRequested();
                if (ch == '\r') continue;
                if (ch == '\n' || ch == '\t')
                {
                    int key = ch == '\n' ? 13 : 9;
                    try { Key(key, true); } finally { Key(key, false); }
                }
                else
                {
                    var up = new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Scan = ch, Flags = 6, Extra = CurrentInputTag } } };
                    try { Send(new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Scan = ch, Flags = 4, Extra = CurrentInputTag } } }); }
                    finally { Send(up); }
                }
            }
        }
        public void Launch(string target, string arguments, bool command)
        {
            // Commands are an explicitly selected executable + arguments, without
            // silently wrapping arbitrary text in a shell.
            var info = new ProcessStartInfo(target, arguments ?? "") { UseShellExecute = !command, CreateNoWindow = command };
            using (var process = Process.Start(info)) { }
        }
    }
}
