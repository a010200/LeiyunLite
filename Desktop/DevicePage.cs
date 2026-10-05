using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RazerBatteryTray.Desktop
{
    internal sealed class DevicePage : UserControl
    {
        private readonly ShellWindow shell;
        private readonly TextBlock name = Ui.Text("—", 23), battery = Ui.Text("—", 52, Ui.BatteryForeground), status = Ui.Text("—", 13, Ui.Muted), performanceInfo = Ui.Text("", 12, Ui.Muted);
        private readonly WrapPanel ratePanel = new WrapPanel();
        private readonly Slider dpi = new Slider { Minimum = 0, Maximum = 1, IsSnapToTickEnabled = false, IsMoveToPointEnabled = true };
        private readonly TextBox dpiValue = Ui.Input("800");
        private readonly Canvas bubble = new Canvas { Height = 44 };
        private readonly Slider angle = new Slider { Minimum = -44, Maximum = 44, SmallChange = 1, LargeChange = 5, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true };
        private readonly TextBox angleValue = Ui.Input("0");
        private readonly MouseOrientation mouse = new MouseOrientation();
        private readonly Button applyRotation;
        private readonly TextBlock rotationInfo = Ui.Text("", 12, Ui.Muted);
        private bool updating, updatingRotation, pendingDpi, pendingRotation, writing;
        private string dpiEditKey, rotationEditKey;
        internal DevicePage(ShellWindow shell)
        {
            this.shell = shell;
            dpiValue.Width = 88; dpiValue.TextAlignment = TextAlignment.Center; dpiValue.Foreground = Ui.Foreground; dpiValue.Margin = new Thickness(0);
            bubble.Children.Add(dpiValue);
            dpi.SizeChanged += (s, e) => PositionBubble();
            dpi.ValueChanged += (s, e) => { if (!updating) { dpiEditKey = shell.Reading.DeviceKey; dpiValue.Text = DpiScale.FromPosition(dpi.Value).ToString(); pendingDpi = true; } PositionBubble(); };
            dpiValue.GotKeyboardFocus += (s, e) => dpiEditKey = shell.Reading.DeviceKey;
            dpi.PreviewKeyDown += (s, e) => {
                int delta = e.Key == Key.Left || e.Key == Key.Down ? -50 : e.Key == Key.Right || e.Key == Key.Up ? 50 : e.Key == Key.PageUp ? 100 : e.Key == Key.PageDown ? -100 : 0;
                if (delta == 0 && e.Key != Key.Home && e.Key != Key.End) return;
                int value = e.Key == Key.Home ? 100 : e.Key == Key.End ? 8000 : Math.Max(100, Math.Min(8000, DpiScale.FromPosition(dpi.Value) + delta));
                dpi.Value = DpiScale.ToPosition(value); e.Handled = true;
            };
            dpi.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(async (s, e) => { if (!e.Canceled) await CommitDpi(); }));
            dpi.PreviewMouseLeftButtonUp += async (s, e) => { if (pendingDpi) await CommitDpi(); };
            dpi.PreviewKeyUp += async (s, e) => { if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown) { e.Handled = true; await CommitDpi(); } };
            dpiValue.PreviewKeyDown += async (s, e) => { if (e.Key == Key.Enter) { pendingDpi = true; await CommitDpi(); e.Handled = true; } if (e.Key == Key.Escape) { pendingDpi = false; Keyboard.ClearFocus(); UpdateDpi(); e.Handled = true; } };
            angleValue.Width = 76; angleValue.TextAlignment = TextAlignment.Center; angleValue.HorizontalAlignment = HorizontalAlignment.Center;
            angleValue.GotKeyboardFocus += (s, e) => rotationEditKey = shell.Reading.DeviceKey;
            angleValue.TextChanged += (s, e) => {
                if (!updatingRotation && angleValue.IsKeyboardFocusWithin) { pendingRotation = true; UpdateRotationControls(); }
            };
            angle.ValueChanged += (s, e) => {
                mouse.Angle = angle.Value; angleValue.Text = ((int)angle.Value).ToString();
                if (!updatingRotation) { pendingRotation = true; rotationEditKey = shell.Reading.DeviceKey; UpdateRotationControls(); }
            };
            angleValue.PreviewKeyDown += (s, e) => {
                if (e.Key != Key.Enter) return; int value;
                if (int.TryParse(angleValue.Text, out value) && value >= -44 && value <= 44) {
                    pendingRotation = true;
                    updatingRotation = true; angle.Value = value; updatingRotation = false; UpdateRotationControls();
                } else shell.Notice(Ui.T("角度范围为 −44°～44°。", "Angle must be between −44° and 44°."));
                e.Handled = true;
            };
            var range = new DockPanel(); var right = Ui.Text("44°", 12, Ui.Muted); DockPanel.SetDock(right, Dock.Right); range.Children.Add(right); range.Children.Add(Ui.Text("−44°", 12, Ui.Muted));
            applyRotation = Ui.Button(Ui.T("应用到鼠标", "Apply to mouse"), async () => await CommitRotation()); applyRotation.IsEnabled = false;
            applyRotation.ToolTip = Ui.T("支持已验证的 SE 随附接收器。写入后读回；失败时尝试恢复原角度并确认。", "Supports the tested SE bundled receiver. Writes are read back; failures attempt to restore and verify the original angle.");
            var hero = new Grid(); hero.ColumnDefinitions.Add(new ColumnDefinition()); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.Children.Add(Ui.Stack(name, status, Ui.Row(Ui.Button(Ui.T("刷新设备", "Refresh device"), async () => await shell.RefreshDevice()))));
            Grid.SetColumn(battery, 1); battery.Margin = new Thickness(24, 0, 0, 0); battery.VerticalAlignment = VerticalAlignment.Center; hero.Children.Add(battery);
            var performance = Ui.Card(Ui.Stack(Ui.Text(Ui.T("灵敏度与回报率", "Sensitivity and polling"), 20), Ui.Text("DPI", 13, Ui.Muted), bubble, dpi,
                    Ui.Text(Ui.T("拖动后松手应用 · 输入数值后按 Enter · Esc 撤销输入", "Release to apply · Enter to apply a typed value · Esc to revert"), 12, Ui.Muted),
                    Ui.Text(Ui.T("回报率", "Polling rate"), 14), ratePanel, performanceInfo));
            var rotation = Ui.Card(Ui.Stack(Ui.Row(Ui.Text(Ui.T("旋转校正", "Rotation correction"), 20), Ui.Text(Ui.T("  设备读回与预览", "  Device readback and preview"), 12, Ui.Muted)),
                    Ui.Text(Ui.T("拖动仅预览，点击应用才写入鼠标；设置结果通过设备读回确认。", "Drag to preview; click Apply to write to the mouse. The result is confirmed by device readback."), 13, Ui.Muted), mouse, angleValue, angle, range,
                    Ui.Row(Ui.Button(Ui.T("校准向导", "Calibrate"), OpenCalibration), Ui.Button(Ui.T("撤销预览", "Undo preview"), RestoreRotationPreview), applyRotation), rotationInfo));
            var tuning = new Grid(); tuning.ColumnDefinitions.Add(new ColumnDefinition()); tuning.ColumnDefinitions.Add(new ColumnDefinition());
            tuning.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); tuning.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            performance.VerticalAlignment = VerticalAlignment.Top;
            performance.Margin = new Thickness(0, 0, 12, 16); rotation.Margin = new Thickness(0, 0, 0, 16); Grid.SetColumn(rotation, 1); tuning.Children.Add(performance); tuning.Children.Add(rotation);
            Content = Ui.Scroll(Ui.Stack(Ui.Text(Ui.T("你的设备", "Your device"), 32), Ui.Card(hero), tuning));
            SizeChanged += (s, e) => {
                bool wide = ResponsiveLayout.ForWidth(shell.ActualWidth > 0 ? shell.ActualWidth : shell.Width) == LayoutMode.Wide;
                tuning.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Grid.SetColumn(rotation, wide ? 1 : 0); Grid.SetRow(rotation, wide ? 0 : 1);
                performance.Margin = new Thickness(0, 0, wide ? 12 : 0, 16);
                battery.FontSize = wide ? 52 : 40; name.FontSize = wide ? 23 : 20;
            };
        }
        private void PositionBubble()
        {
            double width = Math.Max(0, dpi.ActualWidth), fraction = (dpi.Value - dpi.Minimum) / Math.Max(1, dpi.Maximum - dpi.Minimum);
            Canvas.SetLeft(dpiValue, Math.Max(0, Math.Min(width - 88, 11 + Math.Max(0, width - 22) * fraction - 44)));
        }
        internal void UpdateDpi()
        {
            if (pendingDpi || writing || dpi.IsMouseCaptureWithin || dpiValue.IsKeyboardFocusWithin) return;
            updating = true;
            if (shell.Reading.Dpi > 0) { dpi.Value = DpiScale.ToPosition(shell.Reading.Dpi); dpiValue.Text = shell.Reading.Dpi.ToString(); }
            else { dpi.Value = 0; dpiValue.Text = "—"; }
            updating = false; PositionBubble();
        }
        internal void UpdateReading()
        {
            var r = shell.Reading; var cap = DeviceCapabilities.For(r.ProductId);
            name.Text = r.IsConnected ? r.DeviceName : Ui.T("未检测到鼠标", "No mouse detected");
            battery.Text = r.BatteryKnown ? r.BatteryPercent + "%" : "—";
            status.Text = !r.IsConnected ? Ui.T("连接雷蛇鼠标后刷新。", "Connect a Razer mouse and refresh.") : r.IsSleeping ? Ui.T("接收器已连接 · 未收到新遥测（休眠或被其他程序占用）", "Receiver detected · no fresh telemetry (asleep or busy)") :
                (r.IsCharging ? Ui.T("正在充电", "Charging") : Ui.T("电池供电", "On battery")) + "  ·  " + r.LastUpdated.ToString("HH:mm:ss") + "  ·  1532:" + r.ProductId.ToString("X4");
            if (r.ProtocolStatus == DeviceProtocolStatus.IdentityOnly) status.Text = Ui.T("已识别设备 · 暂无兼容控制协议", "Device identified · no compatible control protocol");
            if (r.ProtocolStatus == DeviceProtocolStatus.PresentUnresponsive) status.Text = Ui.T("设备接口已连接 · 未收到有效遥测", "Device interface present · no valid telemetry");
            if (r.ProtocolStatus == DeviceProtocolStatus.Cached) status.Text = Ui.T("近期缓存 · 未收到新遥测", "Recent cache · no fresh telemetry");
            if (r.ProtocolStatus == DeviceProtocolStatus.Ready && !r.BatteryKnown) status.Text = Ui.T("遥测已连接 · 电量未知", "Telemetry ready · battery unknown");
            dpi.IsEnabled = dpiValue.IsEnabled = cap.Known && r.IsWriteSupported && !writing;
            // Keep the useful drag range independent of the sensor's hardware limit.
            // Typed input and readback still use the actual device capability.
            UpdateDpi();
            ratePanel.Children.Clear();
            foreach (int rate in cap.Rates)
            {
                int chosen = rate; var button = Ui.Button(rate + " Hz", async () => await CommitRate(chosen), rate == r.PollingRate && !r.IsSleeping);
                button.Padding = new Thickness(12, 8, 12, 8); button.IsEnabled = r.IsWriteSupported && !writing; ratePanel.Children.Add(button);
            }
            performanceInfo.Text = cap.Known ? "" : Ui.T("尚无此设备 / 接收器组合的可靠能力表，暂不开放写入。", "No verified capability profile for this device / receiver; writes are disabled.");
            performanceInfo.Visibility = cap.Known ? Visibility.Collapsed : Visibility.Visible;
            if (!pendingRotation && !writing && !angle.IsMouseCaptureWithin && !angleValue.IsKeyboardFocusWithin && r.RotationKnown) {
                updatingRotation = true; angle.Value = r.RotationAngle; angleValue.Text = r.RotationAngle.ToString(CultureInfo.InvariantCulture); updatingRotation = false;
                rotationEditKey = r.DeviceKey;
            }
            UpdateRotationControls();
        }
        private void UpdateRotationControls()
        {
            var r = shell.Reading;
            bool supported = r.ProtocolStatus == DeviceProtocolStatus.Ready && r.RotationKnown && r.IsRotationWriteSupported && r.IsRotationHardwareVerified;
            int proposed;
            bool valid = int.TryParse(angleValue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out proposed) && DeviceCapabilities.For(r).AcceptsRotation(proposed);
            angle.IsEnabled = angleValue.IsEnabled = !writing;
            if (applyRotation != null) applyRotation.IsEnabled = supported && valid && pendingRotation && !writing && rotationEditKey == r.DeviceKey && proposed != r.RotationAngle;
            rotationInfo.Text = r.RotationKnown
                ? Ui.T("设备读回：", "Device readback: ") + r.RotationAngle + "° · " + (supported
                    ? Ui.T("当前连接已验证旋转与读回。", "Rotation and readback tested for this connection.")
                    : Ui.T("此连接暂不开放写入。", "Writes are disabled for this connection."))
                : Ui.T("未获得有效旋转读回，保持只预览。", "No valid rotation readback; preview only.");
        }
        private void RestoreRotationPreview()
        {
            if (!shell.Reading.RotationKnown) return;
            updatingRotation = true; angle.Value = shell.Reading.RotationAngle; angleValue.Text = shell.Reading.RotationAngle.ToString(CultureInfo.InvariantCulture); updatingRotation = false;
            pendingRotation = false; rotationEditKey = shell.Reading.DeviceKey; UpdateRotationControls();
        }
        private async Task CommitDpi()
        {
            if (writing || !pendingDpi) return;
            if (dpiEditKey != shell.Reading.DeviceKey) {
                pendingDpi = false; Keyboard.ClearFocus(); UpdateDpi();
                shell.Notice(Ui.T("设备已切换，请重新选择 DPI。", "Device changed. Choose the DPI again.")); return;
            }
            int value; var cap = DeviceCapabilities.For(shell.Reading.ProductId);
            if (!int.TryParse(dpiValue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || !cap.AcceptsDpi(value)) { shell.Notice(Ui.T("请输入有效 DPI：", "Enter a valid DPI: ") + cap.MinimumDpi + "–" + cap.MaximumDpi); return; }
            pendingDpi = false; writing = true; UpdateReading();
            int pid = shell.Reading.ProductId; string targetKey = shell.Reading.DeviceKey;
            try
            {
                bool ok = !shell.Demo && await Task.Run(() => shell.Device.SetDpiVerified(pid, value, targetKey));
                shell.Notice(shell.Demo ? Ui.T("预览模式，不写入硬件。", "Preview mode: hardware was not changed.") : ok ? Ui.T("DPI 已写入并读回确认。", "DPI written and verified by readback.") : Ui.T("DPI 未获读回确认，请刷新并检查官方软件是否占用设备。", "DPI readback was not confirmed. Refresh and check for competing device software."));
            }
            catch (Exception ex) { shell.Notice(ex.Message); }
            finally { writing = false; }
            await shell.RefreshDevice(); UpdateReading();
        }
        private async Task CommitRate(int rate)
        {
            if (writing) return; writing = true; int pid = shell.Reading.ProductId; string targetKey = shell.Reading.DeviceKey; UpdateReading();
            try
            {
                bool ok = !shell.Demo && await Task.Run(() => shell.Device.SetRateVerified(pid, rate, targetKey));
                shell.Notice(shell.Demo ? Ui.T("预览模式，不写入硬件。", "Preview mode: hardware was not changed.") : ok ? Ui.T("回报率已写入并读回确认。", "Polling rate written and verified by readback.") : Ui.T("回报率未获读回确认；不会显示为设置成功。", "Polling readback not confirmed; the change is not reported as successful."));
            }
            catch (Exception ex) { shell.Notice(ex.Message); }
            finally { writing = false; }
            await shell.RefreshDevice(); UpdateReading();
        }
        private async Task CommitRotation()
        {
            if (writing || !pendingRotation) return;
            if (rotationEditKey != shell.Reading.DeviceKey) {
                RestoreRotationPreview(); shell.Notice(Ui.T("设备已切换，请重新选择角度。", "Device changed. Choose the angle again.")); return;
            }
            int value, pid = shell.Reading.ProductId; string targetKey = shell.Reading.DeviceKey;
            if (!int.TryParse(angleValue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || !DeviceCapabilities.For(shell.Reading).AcceptsRotation(value)) { shell.Notice(Ui.T("请输入 −44°～44° 的整数角度。", "Enter a whole-number angle between -44 and 44 degrees.")); return; }
            if (shell.Reading.ProtocolStatus != DeviceProtocolStatus.Ready || !shell.Reading.RotationKnown || !shell.Reading.IsRotationWriteSupported) return;
            writing = true; UpdateReading();
            try
            {
                RotationWriteResult result = shell.Demo ? new RotationWriteResult() : await Task.Run(() => shell.Device.SetRotationVerified(pid, value, targetKey));
                if (shell.Demo) shell.Notice(Ui.T("预览模式，不写入硬件。", "Preview mode: hardware was not changed."));
                else if (result.Success) shell.Notice(Ui.T("旋转角度已写入并读回确认。", "Rotation angle written and verified by readback."));
                else if (result.RollbackAttempted && result.RollbackSucceeded) shell.Notice(Ui.T("目标值未获确认，已恢复并读回原角度。", "The target was not confirmed; the original angle was restored and read back."));
                else if (result.RollbackAttempted) shell.Notice(Ui.T("写入失败且未能确认恢复，请立即用官方软件检查旋转角度。", "Write failed and restore could not be confirmed. Check the rotation angle in the vendor app immediately."));
                else shell.Notice(Ui.T("未写入：设备状态或初始读回未通过检查，请刷新后重试。", "Not written: device state or initial readback failed validation. Refresh and try again."));
            }
            catch (Exception ex) { shell.Notice(ex.Message); }
            finally { pendingRotation = false; writing = false; }
            await shell.RefreshDevice(); UpdateReading();
        }
        private void OpenCalibration()
        {
            var measured = new List<double>(); var points = new List<Point>();
            var instructions = Ui.Text(Ui.T("以自然握姿，在区域内按住左键水平来回划动，每次松手结束。至少 10 条长直线。这里只测量角度，不修改设备。", "With your usual grip, hold left click and swipe horizontally. Release after each stroke. Collect at least 10 long straight strokes. This measures only; it does not modify the device."), 13, Ui.Muted);
            var current = Ui.Input(shell.Reading.RotationKnown ? shell.Reading.RotationAngle.ToString(CultureInfo.InvariantCulture) : "0");
            var canvas = new Canvas { Height = 210, Background = Ui.Frame, ClipToBounds = true, Margin = new Thickness(0, 8, 0, 12) };
            var label = Ui.Text("0 / 10", 18, Ui.Accent); Polyline stroke = null;
            canvas.MouseLeftButtonDown += (s, e) => { points.Clear(); stroke = new Polyline { Stroke = Ui.Accent, StrokeThickness = 2 }; canvas.Children.Clear(); canvas.Children.Add(stroke); var p = e.GetPosition(canvas); points.Add(p); stroke.Points.Add(p); canvas.CaptureMouse(); e.Handled = true; };
            canvas.MouseMove += (s, e) => { if (!canvas.IsMouseCaptured || stroke == null) return; var p = e.GetPosition(canvas); if (p.X < 0 || p.X > canvas.ActualWidth || p.Y < 0 || p.Y > canvas.ActualHeight) { canvas.ReleaseMouseCapture(); stroke = null; return; } points.Add(p); stroke.Points.Add(p); };
            canvas.MouseLeftButtonUp += (s, e) => { if (!canvas.IsMouseCaptured) return; canvas.ReleaseMouseCapture(); double a; if (RotationMath.TryStroke(points, out a)) { measured.Add(a); label.Text = measured.Count + " / 10"; } else label.Text = measured.Count + " / 10 · " + Ui.T("线太短或弯曲，请重试", "Too short or curved; retry"); };
            var evaluate = Ui.Button(Ui.T("计算建议角度", "Estimate angle"), () => {
                double baseline, result;
                if (!double.TryParse(current.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out baseline) || !RotationMath.TryCorrection(measured, baseline, out result)) { label.Text = Ui.T("需要至少 10 条一致的线，且结果在 ±44° 内。", "Need 10 consistent strokes and a result within ±44°."); return; }
                angle.Value = result; label.Text = Ui.T("建议预览角度：", "Suggested preview angle: ") + result + "°";
            }, true);
            shell.OpenDrawer(Ui.T("旋转校准", "Rotation calibration"), Ui.Stack(instructions, Ui.Text(Ui.T("当前已应用角度（有设备读回时自动填入）", "Currently applied angle (prefilled when device readback is available)"), 12), current, canvas, label, evaluate,
                Ui.Text(Ui.T("画布采样的是系统指针轨迹，不是原始传感器数据。先计算当前角度减去残余倾斜；不要把已校正轨迹再次作为零角度输入。建议角度还需在官方软件中对照验证。", "Samples the system pointer, not raw sensor data. Computes current correction minus residual tilt; do not treat corrected input as uncorrected. Verify the estimate in the vendor app."), 12, Ui.Muted)));
        }
    }
}
