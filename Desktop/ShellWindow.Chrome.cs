using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace RazerBatteryTray.Desktop
{
    internal sealed partial class ShellWindow
    {
        private Button maximizeButton;
        private bool captionPressed;
        private const int HitMaxButton = 9;
        private const int GetMinMaxInfoMessage = 0x0024, MonitorDefaultToNearest = 2;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OsVersion { internal int Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Text; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal int Size; internal NativeRect Monitor, Work; internal int Flags; }
        [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo { internal NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);
        [DllImport("user32.dll", EntryPoint="GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [StructLayout(LayoutKind.Sequential)] private struct MouseTracking { internal int Size, Flags; internal IntPtr Window; internal int HoverTime; }
        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref OsVersion version);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref MouseTracking tracking);
        internal static readonly int NativeWindowsBuild = DetectWindowsBuild();
        private static int DetectWindowsBuild() { var v = new OsVersion { Size = Marshal.SizeOf(typeof(OsVersion)) }; return RtlGetVersion(ref v) == 0 && v.Major >= 10 ? v.Build : 0; }
        internal static bool SupportsSnapMenu { get { return NativeWindowsBuild >= 22000; } }
        internal static Point ScreenPoint(IntPtr lparam) { long value = lparam.ToInt64(); return new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff)); }
        internal bool InMaximizeButton(Point screen)
        {
            if (maximizeButton == null || !maximizeButton.IsVisible || !maximizeButton.IsEnabled || PresentationSource.FromVisual(maximizeButton) == null) return false;
            // PointFromScreen converts physical screen coordinates to the button's DIP space.
            Point local = maximizeButton.PointFromScreen(screen);
            return new Rect(new Size(maximizeButton.ActualWidth, maximizeButton.ActualHeight)).Contains(local);
        }
        private void MaximizeOrRestore() { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        private void CaptionHover(bool hover) { if (maximizeButton != null) maximizeButton.Background = Ui.Brush(hover ? "ControlHoverBackground" : "WindowBackground"); }
        internal static bool ApplyMonitorWorkArea(ref MinMaxInfo limits,NativeRect monitor,NativeRect work)
        {
            int width=work.Right-work.Left, height=work.Bottom-work.Top;
            if(width<=0 || height<=0) return false;
            // Win32 physical pixels, relative to the window's current monitor.
            limits.MaxPosition=new NativePoint { X=work.Left-monitor.Left,Y=work.Top-monitor.Top };
            limits.MaxSize=limits.MaxTrackSize=new NativePoint { X=width,Y=height };
            return true;
        }
        private IntPtr ChromeMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if(message==GetMinMaxInfoMessage) {
                var monitor=MonitorFromWindow(hwnd,MonitorDefaultToNearest);
                var info=new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorInfo)) };
                if(lparam!=IntPtr.Zero && monitor!=IntPtr.Zero && GetMonitorInfo(monitor,ref info)) {
                    var limits=(MinMaxInfo)Marshal.PtrToStructure(lparam,typeof(MinMaxInfo));
                    if(ApplyMonitorWorkArea(ref limits,info.Monitor,info.Work)) {
                        Marshal.StructureToPtr(limits,lparam,false); handled=true;
                    }
                }
                return IntPtr.Zero;
            }
            if (!SupportsSnapMenu) return IntPtr.Zero; // Windows 10 keeps the existing WPF chrome path.
            if (message == 0x0084 && InMaximizeButton(ScreenPoint(lparam))) { handled = true; return new IntPtr(HitMaxButton); }
            if (message == 0x00A0) {
                bool over = InMaximizeButton(ScreenPoint(lparam)); CaptionHover(over);
                if (over) { var tracking = new MouseTracking { Size = Marshal.SizeOf(typeof(MouseTracking)), Flags = 0x12, Window = hwnd }; TrackMouseEvent(ref tracking); }
            }
            if (message == 0x02A2) CaptionHover(false);
            if (message == 0x00A1 && wparam.ToInt64() == HitMaxButton) {
                captionPressed = true; SetCapture(hwnd); UiMotion.Press(maximizeButton, true); handled = true;
            }
            if (captionPressed && (message == 0x00A2 || message == 0x0202)) {
                NativePoint point; bool click = GetCursorPos(out point) && InMaximizeButton(new Point(point.X, point.Y));
                captionPressed = false; ReleaseCapture(); UiMotion.Press(maximizeButton, false);
                if (click) MaximizeOrRestore(); handled = true;
            }
            if (message == 0x0215 || message == 0x001F) { captionPressed = false; if (maximizeButton != null) UiMotion.Press(maximizeButton, false); CaptionHover(false); }
            return IntPtr.Zero;
        }
    }
}
