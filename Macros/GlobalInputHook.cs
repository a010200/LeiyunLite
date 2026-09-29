using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray.Macros
{
    internal sealed class GlobalInputHook : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string name);
        private readonly Func<InputStroke, bool> handle;
        private readonly Func<InputStroke, bool> suppressWheel;
        private readonly Func<UIntPtr, bool> suppressStoppedDown;
        private readonly HashSet<int> keys = new HashSet<int>();
        private readonly HashSet<TriggerKind> mouseButtons = new HashSet<TriggerKind>();
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private Thread thread;
        private uint threadId;
        private IntPtr keyboard, mouse;
        private HookProc keyboardProc, mouseProc;
        private Exception error;
        private int wheelRemainder;
        private KeyModifiers wheelModifiers;
        private long passedLeftDowns, passedLeftUps, blockedStoppedDowns, lastPassedLeftDown, lastPassedLeftUp;
        private bool disposed;
        internal long PassedLeftDowns { get { return Interlocked.Read(ref passedLeftDowns); } }
        internal long PassedLeftUps { get { return Interlocked.Read(ref passedLeftUps); } }
        internal long BlockedStoppedDowns { get { return Interlocked.Read(ref blockedStoppedDowns); } }
        internal long LastPassedLeftDown { get { return Interlocked.Read(ref lastPassedLeftDown); } }
        internal long LastPassedLeftUp { get { return Interlocked.Read(ref lastPassedLeftUp); } }
        public GlobalInputHook(Func<InputStroke, bool> handle, Func<InputStroke, bool> suppressWheel = null,
            Func<UIntPtr, bool> suppressStoppedDown = null)
        { this.handle = handle; this.suppressWheel = suppressWheel; this.suppressStoppedDown = suppressStoppedDown; }
        public void Start()
        {
            if (thread != null) return;
            thread = new Thread(Run) { IsBackground = true, Name = "LeiyunLite.InputHooks" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            if (!ready.WaitOne(5000)) throw new InvalidOperationException("输入监听初始化超时。");
            if (error != null) throw new InvalidOperationException("无法启用按键绑定：" + error.Message, error);
        }
        private void Run()
        {
            try
            {
                threadId = GetCurrentThreadId();
                keyboardProc = Keyboard; mouseProc = Mouse;
                keyboard = SetWindowsHookEx(13, keyboardProc, GetModuleHandle(null), 0);
                mouse = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0);
                if (keyboard == IntPtr.Zero || mouse == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                // Creating a native control ensures this thread owns a message queue.
                using (var queue = new Control())
                {
                    IntPtr hwnd = queue.Handle;
                    ready.Set();
                    Application.Run();
                }
            }
            catch (Exception ex) { error = ex; ready.Set(); }
            finally
            {
                if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard);
                if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
            }
        }
        private KeyModifiers Modifiers
        {
            get
            {
                KeyModifiers result = KeyModifiers.None;
                if (keys.Contains(17) || keys.Contains(162) || keys.Contains(163)) result |= KeyModifiers.Control;
                if (keys.Contains(16) || keys.Contains(160) || keys.Contains(161)) result |= KeyModifiers.Shift;
                if (keys.Contains(18) || keys.Contains(164) || keys.Contains(165)) result |= KeyModifiers.Alt;
                if (keys.Contains(91) || keys.Contains(92)) result |= KeyModifiers.Windows;
                return result;
            }
        }
        private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code >= 0)
                {
                    var info = (KeyboardData)Marshal.PtrToStructure(data, typeof(KeyboardData));
                    int msg = message.ToInt32();
                    // Drop only stale presses. Releases must still reach the target.
                    if ((info.Flags & 0x10) != 0 && (msg == 0x100 || msg == 0x104) &&
                        suppressStoppedDown != null && suppressStoppedDown(info.Extra)) return new IntPtr(1);
                    if ((info.Flags & 0x10) == 0 && !WindowsMacroOutput.IsOwnInputTag(info.Extra))
                    {
                        bool down = msg == 0x100 || msg == 0x104;
                        if (down) keys.Add((int)info.Key); else keys.Remove((int)info.Key);
                        if (handle(new InputStroke { Trigger = TriggerKind.Keyboard, Key = (int)info.Key, Down = down, Modifiers = Modifiers })) return new IntPtr(1);
                    }
                }
            }
            catch { /* Fail open: never trap user input if a callback fails. */ }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        private IntPtr Mouse(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code >= 0)
                {
                    int msg = message.ToInt32();
                    if (msg == 0x200) return CallNextHookEx(IntPtr.Zero, code, message, data);
                    var info = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
                    // This runs before a queued injected event reaches the target.
                    bool ownInjected = (info.Flags & 1) != 0 && WindowsMacroOutput.IsOwnInputTag(info.Extra);
                    if (ownInjected && (msg == 0x201 || msg == 0x204 || msg == 0x207 || msg == 0x20B) &&
                        suppressStoppedDown != null && suppressStoppedDown(info.Extra))
                    {
                        if (msg == 0x201) Interlocked.Increment(ref blockedStoppedDowns);
                        return new IntPtr(1);
                    }
                    if (ownInjected && msg == 0x201)
                    {
                        Interlocked.Increment(ref passedLeftDowns);
                        Interlocked.Exchange(ref lastPassedLeftDown, Stopwatch.GetTimestamp());
                    }
                    if (ownInjected && msg == 0x202)
                    {
                        Interlocked.Increment(ref passedLeftUps);
                        Interlocked.Exchange(ref lastPassedLeftUp, Stopwatch.GetTimestamp());
                    }
                    if ((info.Flags & 1) == 0 && !WindowsMacroOutput.IsOwnInputTag(info.Extra))
                    {
                        TriggerKind kind; bool down;
                        switch (msg)
                        {
                            case 0x201: case 0x202: kind = TriggerKind.Left; down = msg == 0x201; break;
                            case 0x204: case 0x205: kind = TriggerKind.Right; down = msg == 0x204; break;
                            case 0x207: case 0x208: kind = TriggerKind.Middle; down = msg == 0x207; break;
                            case 0x20B: case 0x20C: kind = (info.Data >> 16) == 1 ? TriggerKind.X1 : TriggerKind.X2; down = msg == 0x20B; break;
                            case 0x20A:
                                int delta = unchecked((short)(info.Data >> 16));
                                if (wheelModifiers != Modifiers) { wheelRemainder = 0; wheelModifiers = Modifiers; }
                                wheelRemainder += delta;
                                bool suppress = suppressWheel != null && suppressWheel(new InputStroke {
                                    Trigger = delta > 0 ? TriggerKind.WheelUp : TriggerKind.WheelDown, Down = true, Modifiers = Modifiers });
                                while (Math.Abs(wheelRemainder) >= 120)
                                {
                                    bool up = wheelRemainder > 0; wheelRemainder += up ? -120 : 120;
                                    suppress |= handle(new InputStroke { Trigger = up ? TriggerKind.WheelUp : TriggerKind.WheelDown, Down = true, Modifiers = Modifiers });
                                }
                                if (suppress) return new IntPtr(1);
                                return CallNextHookEx(IntPtr.Zero, code, message, data);
                            default: return CallNextHookEx(IntPtr.Zero, code, message, data);
                        }
                        if (down) mouseButtons.Add(kind); else mouseButtons.Remove(kind);
                        if (handle(new InputStroke { Trigger = kind, Down = down, Modifiers = Modifiers,
                            RightButtonDown = mouseButtons.Contains(TriggerKind.Right) })) return new IntPtr(1);
                    }
                }
            }
            catch { }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (thread != null && thread.IsAlive) { PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero); thread.Join(1500); }
            if (thread == null || !thread.IsAlive) ready.Dispose();
        }
    }
}
