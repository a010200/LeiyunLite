using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace RazerBatteryTray.Desktop
{
    internal sealed class TrayController : IDisposable
    {
        private readonly ShellWindow shell;
        private readonly Forms.NotifyIcon icon;
        private Drawing.Icon image;
        private TrayFlyout flyout;
        private Window notification;
        private readonly DispatcherTimer notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private bool paused, lowNotified, initialized, previousConnected, disposed;
        private Drawing.Point anchor;
        internal TrayController(ShellWindow shell)
        {
            this.shell = shell;
            notificationTimer.Tick += (s, e) => CloseNotification();
            icon = new Forms.NotifyIcon { Text = "雷云lite", Visible = false };
            // MouseDown also fires for the second press of a double click; no click classifier delay.
            icon.MouseDown += (s, e) => { anchor = Forms.Cursor.Position; if (e.Button == Forms.MouseButtons.Left) Show(false); else if (e.Button == Forms.MouseButtons.Right) Show(true); };
            Update(); icon.Visible = true;
        }
        internal void PreferencesChanged() { CloseFlyout(); CloseNotification(); Update(); }
        private void CloseNotification() { notificationTimer.Stop(); if (notification != null) notification.Close(); notification = null; }
        private void Notify(string message)
        {
            CloseNotification();
            notification = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false, Topmost = true, ResizeMode = ResizeMode.NoResize, Width = 300, SizeToContent = SizeToContent.Height };
            var surface = Ui.Card(Ui.Stack(Ui.Text("雷云lite", 13, Ui.Muted), Ui.Text(message, 16))); surface.Margin = new Thickness(8); surface.Padding = new Thickness(18); notification.Content = surface;
            notification.Opacity = 0; notification.Show(); notification.UpdateLayout();
            var screen = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea; var transform = PresentationSource.FromVisual(notification).CompositionTarget.TransformFromDevice;
            var bounds = new Rect(transform.Transform(new Point(screen.Left, screen.Top)), transform.Transform(new Point(screen.Right, screen.Bottom)));
            var position = TrayFlyout.Place(new Point(bounds.Right - 12, bounds.Bottom - 8), new Size(notification.ActualWidth, notification.ActualHeight), bounds);
            notification.Left = position.X; notification.Top = position.Y; notification.Opacity = 1; notificationTimer.Start();
            notification.MouseLeftButtonUp += (s, e) => { CloseNotification(); shell.OpenPage(0); };
            if (shell.Preferences.TrayAnimation) Ui.Enter(surface);
        }
        private void Show(bool menu)
        {
            if (disposed) return;
            if (flyout != null && flyout.IsMenu == menu) { flyout.Activate(); return; }
            CloseFlyout();
            flyout = new TrayFlyout(shell, menu, paused, b => { paused = b; if (shell.Macros != null) shell.Macros.PauseNewBindings(b); });
            var current = flyout; current.Closed += (s, e) => { if (flyout == current) flyout = null; };
            current.ShowAt(anchor);
        }
        private void CloseFlyout() { if (flyout != null) flyout.Close(); flyout = null; }
        internal void Update()
        {
            if (disposed) return;
            var r = shell.Reading;
            var next = BrandIcon(r.IsConnected, Math.Max(16, Forms.SystemInformation.SmallIconSize.Width)); icon.Icon = next; if (image != null) image.Dispose(); image = next;
            string label = string.IsNullOrWhiteSpace(r.DeviceName) ? "雷云lite" : r.DeviceName;
            label += r.IsConnected && r.BatteryKnown && !r.IsSleeping ? " · " + r.BatteryPercent + "%" : r.IsConnected ? Ui.T(" · 未收到新电量", " · No fresh battery data") : Ui.T(" · 未连接", " · Disconnected");
            icon.Text = label.Length > 63 ? label.Substring(0, 63) : label;
            if (initialized && previousConnected != r.IsConnected && shell.Preferences.ConnectionNotifications)
                Notify(r.IsConnected ? Ui.T("鼠标已连接", "Mouse connected") : Ui.T("鼠标已断开", "Mouse disconnected"));
            initialized = true; previousConnected = r.IsConnected;
            bool low = r.IsConnected && r.BatteryKnown && !r.IsSleeping && r.LastUpdated != DateTime.MinValue && r.BatteryPercent <= shell.Preferences.LowBatteryThreshold && !r.IsCharging && shell.LegacySettings.LowBatteryAlert;
            if (low && !lowNotified) Notify(Ui.T("鼠标电量不足：", "Low mouse battery: ") + r.BatteryPercent + "%");
            lowNotified = low;
        }
        internal static Drawing.Icon BrandIcon(bool connected, int size = 32)
        {
            using (var bitmap = new Drawing.Bitmap(size, size))
            using (var g = Drawing.Graphics.FromImage(bitmap))
            using (var green = new Drawing.SolidBrush(connected ? Drawing.Color.FromArgb(0x44, 0xD6, 0x2C) : Drawing.Color.FromArgb(0x88, 0x88, 0x88)))
            using (var black = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x14, 0x14, 0x14)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(size / 128f, size / 128f);
                g.FillEllipse(green, 8, 8, 112, 112);
                g.FillPolygon(black, new[] { new Drawing.PointF(40, 30), new Drawing.PointF(58, 30), new Drawing.PointF(58, 76), new Drawing.PointF(90, 76), new Drawing.PointF(79, 96), new Drawing.PointF(40, 96) });
                // Keep a single strong L at 16/20px; secondary lightning detail only at larger sizes.
                if (size >= 24) g.FillPolygon(black, new[] { new Drawing.PointF(78, 29), new Drawing.PointF(97, 29), new Drawing.PointF(79, 61), new Drawing.PointF(67, 61) });
                var handle = bitmap.GetHicon(); try { using (var borrowed = Drawing.Icon.FromHandle(handle)) return (Drawing.Icon)borrowed.Clone(); } finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        public void Dispose() { if (disposed) return; disposed = true; CloseFlyout(); CloseNotification(); icon.Visible = false; icon.Dispose(); if (image != null) image.Dispose(); }
    }

    internal sealed class TrayFlyout : Window
    {
        internal readonly bool IsMenu;
        internal bool DismissedOnDeactivate;
        private readonly ShellWindow shell;
        private readonly Border surface;
        private readonly List<Button> options = new List<Button>();
        private readonly TextBlock model = Ui.Text("", 16), battery = Ui.Text("", 38), state = Ui.Text("", 12, Ui.Muted), timestamp = Ui.Text("", 12, Ui.Muted);
        private readonly TextBlock performance = Ui.Text("", 14);
        private readonly Border fill = new Border { Height = 5, Background = Ui.Accent, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        private bool ready;
        internal TrayFlyout(ShellWindow shell, bool menu, bool paused, Action<bool> pause)
        {
            this.shell = shell; IsMenu = menu;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; SizeToContent = SizeToContent.Height; Width = menu ? 256 : 316;
            surface = new Border { Margin = new Thickness(8), Padding = new Thickness(menu ? 7 : 18), CornerRadius = new CornerRadius(8), Background = Ui.Light ? Ui.Brush("#191919") : Ui.Background, BorderBrush = Ui.Brush("#353535"), BorderThickness = new Thickness(1) };
            surface.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.2 };
            Content = surface;
            if (menu)
            {
                var stack = new StackPanel(); surface.Child = stack;
                Add(stack, "⌂", Ui.T("打开主界面", "Open main window"), () => shell.OpenPage(0));
                Add(stack, "⌘", Ui.T("宏与绑定", "Macros and bindings"), () => shell.OpenPage(1));
                Add(stack, "⚙", Ui.T("设置", "Settings"), () => shell.OpenPage(2));
                stack.Children.Add(Divider());
                Add(stack, paused ? "▷" : "Ⅱ", paused ? Ui.T("恢复宏绑定", "Resume macro bindings") : Ui.T("暂停宏绑定", "Pause macro bindings"), () => pause(!paused));
                Add(stack, "□", Ui.T("停止所有宏", "Stop all macros"), () => { if (shell.Macros != null) shell.Macros.Stop(); });
                options[options.Count - 1].ToolTip = "Ctrl + Shift + F12";
                stack.Children.Add(Divider()); Add(stack, "×", Ui.T("退出软件", "Exit"), shell.RequestExit);
            }
            else
            {
                var bar = new Border { Height = 5, Background = Ui.Brush("#343434"), CornerRadius = new CornerRadius(3), Child = fill, Margin = new Thickness(0, 4, 0, 14) };
                var refresh = Ui.Button(Ui.T("刷新", "Refresh"), async () => { await shell.RefreshDevice(); UpdateCard(); });
                var open = Ui.Button(Ui.T("打开主界面", "Open app"), () => { Close(); shell.OpenPage(0); }, true); options.Add(open); options.Add(refresh);
                performance.Margin = new Thickness(0, 0, 0, 12);
                surface.Child = Ui.Stack(model, state, battery, bar, performance, timestamp, Ui.Row(open, refresh));
                shell.Refreshed += UpdateCard; UpdateCard();
            }
            Deactivated += (s, e) => {
                // Give the tray's MouseDown handler a chance to reactivate this same HWND.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                    if (ready && !IsActive) { DismissedOnDeactivate = true; ready = false; Close(); }
                }));
            };
            Closing += (s, e) => ready = false;
            Closed += (s, e) => { ready = false; shell.Refreshed -= UpdateCard; Ui.Stop(surface); };
            PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Down || e.Key == Key.Up) { int index = options.FindIndex(b => b.IsKeyboardFocusWithin); index = (index + (e.Key == Key.Down ? 1 : options.Count - 1) + options.Count) % options.Count; options[index].Focus(); e.Handled = true; }
                else if (e.Key == Key.Enter) { var button = Keyboard.FocusedElement as Button; if (button != null) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; } }
            };
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        }
        private static UIElement Divider() { return new Border { Height = 1, Background = Ui.Brush("#353535"), Margin = new Thickness(8, 5, 8, 5) }; }
        private void Add(Panel panel, string glyph, string label, Action action)
        {
            var button = Ui.Button("", () => { Close(); action(); }); button.Height = 38; button.Margin = new Thickness(0, 1, 0, 1); button.Padding = new Thickness(10, 0, 8, 0); button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var row = new DockPanel(); var symbol = Ui.Text(glyph, 16, Ui.Muted); symbol.Width = 29; symbol.Margin = new Thickness(0); symbol.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(symbol);
            var text = Ui.Text(label); text.Margin = new Thickness(0); text.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(text); button.Content = row;
            button.MouseEnter += (s, e) => button.Background = Ui.Brush("#292929"); button.MouseLeave += (s, e) => button.Background = Brushes.Transparent;
            button.GotKeyboardFocus += (s, e) => button.Background = Ui.Brush("#292929"); button.LostKeyboardFocus += (s, e) => button.Background = Brushes.Transparent;
            panel.Children.Add(button); options.Add(button);
        }
        private void UpdateCard()
        {
            if (IsMenu) return; var r = shell.Reading; bool known = r.BatteryKnown;
            model.Text = string.IsNullOrWhiteSpace(r.DeviceName) ? Ui.T("未检测到鼠标", "No mouse detected") : r.DeviceName;
            state.Text = !r.IsConnected ? Ui.T("未连接", "Disconnected") : r.IsSleeping ? Ui.T("未收到新遥测 · 可能休眠或被占用", "No fresh telemetry · asleep or busy") : r.IsCharging ? Ui.T("已连接 · 正在充电", "Connected · charging") : Ui.T("已连接 · 电池供电", "Connected · on battery");
            battery.Text = known ? r.BatteryPercent + "%" : "—";
            if (r.ProtocolStatus == DeviceProtocolStatus.IdentityOnly) state.Text = Ui.T("已识别 · 暂无控制协议", "Identified · no control protocol");
            if (r.ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive) state.Text = Ui.T("接口已连接 · 遥测未知", "Interface present · telemetry unknown");
            if (r.ProtocolStatus == DeviceProtocolStatus.Ready && !r.BatteryKnown) state.Text = Ui.T("已连接 · 电量未知", "Connected · battery unknown");
            performance.Text = "DPI  " + (r.Dpi > 0 ? r.Dpi.ToString() : "—") + "   ·   " + Ui.T("回报率  ", "Polling  ") + (r.PollingRate > 0 ? r.PollingRate + " Hz" : "—");
            performance.ToolTip = Ui.T("设备最近读回的数值；休眠或断开时为缓存。", "Last device readback; cached while sleeping or disconnected.");
            fill.Width = known ? Math.Max(0, Math.Min(100, r.BatteryPercent)) * 2.62 : 0;
            timestamp.Text = known ? (r.IsSleeping || !r.IsConnected ? Ui.T("缓存读数 · ", "Cached reading · ") : Ui.T("最近读数 · ", "Last reading · ")) + r.LastUpdated.ToString("HH:mm:ss") : Ui.T("尚无有效电量读数", "No valid battery reading yet");
        }
        internal static Point Place(Point anchor, Size size, Rect work)
        {
            return new Point(Math.Max(work.Left, Math.Min(anchor.X - size.Width + 12, work.Right - size.Width)), Math.Max(work.Top, Math.Min(anchor.Y - size.Height - 4, work.Bottom - size.Height)));
        }
        internal void ShowAt(Drawing.Point anchor)
        {
            // Screen/Cursor are Win32 pixel coordinates; convert using this HWND's actual WPF transform.
            // Measure the real HWND before revealing it; Window.DesiredSize can be zero before Show.
            Opacity = 0; Show(); UpdateLayout();
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); var transform = source.CompositionTarget.TransformFromDevice;
            var area = Forms.Screen.FromPoint(anchor).WorkingArea;
            var topLeft = transform.Transform(new Point(area.Left, area.Top)); var bottomRight = transform.Transform(new Point(area.Right, area.Bottom));
            var position = Place(transform.Transform(new Point(anchor.X, anchor.Y)), new Size(ActualWidth, ActualHeight), new Rect(topLeft, bottomRight));
            Left = position.X; Top = position.Y; Opacity = 1; Activate(); ready = true;
            if (options.Count > 0) options[0].Focus();
            if (Ui.Motion && shell.Preferences.TrayAnimation)
            {
                var move = new TranslateTransform(); surface.RenderTransform = move;
                UiMotion.To(move, TranslateTransform.YProperty, 0, UiMotion.Normal, 7);
                UiMotion.Fade(surface, 0, UiMotion.Normal);
            }
        }
    }
}
