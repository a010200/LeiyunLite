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
        internal long TotalInputsSent { get { return Interlocked.Read(ref totalInputsSent); } }
        private void Send(params Input[] inputs)
        {
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) != inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未接受模拟输入；请检查目标窗口权限。");
            Interlocked.Add(ref totalInputsSent, inputs.Length);
        }
        public void Key(int key, bool down)
        {
            bool extended = key == 163 || key == 165 || (key >= 33 && key <= 46) || key == 91 || key == 92 || key == 93 || key == 111 || key == 144;
            Send(new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput {
                Key = (ushort)key, Flags = (down ? 0u : 2u) | (extended ? 1u : 0u), Extra = InputTag } } });
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
            Send(new Input { Data = new InputData { Mouse = new MouseInput { Flags = flags, Data = data, Extra = InputTag } } });
        }
        public void Wheel(int notches)
        {
            Send(new Input { Data = new InputData { Mouse = new MouseInput { Flags = 0x0800, Data = unchecked((uint)(notches * 120)), Extra = InputTag } } });
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
                    var up = new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Scan = ch, Flags = 6, Extra = InputTag } } };
                    try { Send(new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Scan = ch, Flags = 4, Extra = InputTag } } }); }
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
