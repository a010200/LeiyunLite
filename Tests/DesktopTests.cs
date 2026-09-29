using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    internal static partial class DesktopTests
    {
        private static int passed, failed;
        private static readonly List<string> results = new List<string>();
        private static string artifacts;
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [STAThread]
        private static int Main()
        {
            artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts"); Directory.CreateDirectory(artifacts);
            Test("v1.2.3 assembly, display and update version consistency", VersionMetadata);
            Test("Update handoff blocks draft, recording, running macros and failed safety saves", UpdateHandoffSafety);
            Test("Explicit capabilities: SE includes 500, no speculative 8k or unknown writes", Capabilities);
            Test("Verified DPI preserves all other stages (90 and 91 byte reports)", Dpi);
            Test("Failed / mismatched readback is never success; wrong PID is never written", Rejections);
            Test("Polling-rate readback / bounds", Rates);
            Test("Rotation protocol read, signed encoding, write/readback and rollback", RotationCommands);
            Test("Rotation exceptions, lost replies, rollback failure and unverified routes", RotationFailures);
            Test("Rotation matrix is length preserving, correct sign, not angle snapping", Rotation);
            Test("Calibration reversals, curves, outliers and prior correction", Calibration);
            Test("Preferences roundtrip in isolated path; malformed XML is not overwritten", Settings);
            Test("Recording timing, key pairs, mouse buttons and injected/repeated input filtering", RecordingSequence);
            Test("Recording capacity reserves releases and removes emergency gesture", RecordingLimits);
            Test("Recording worker, native callback decoding and playback round trip", RecordingPipeline);
            Test("Recording suspension preserves manual pause and never saves automatically", RecordingPause);
            Test("R4 unbind persists immediately, releases held input and cannot be resurrected", BindingSafety);
            Test("R4 safety persistence failure stays revoked; retry excludes unrelated drafts", BindingSaveFailures);
            Test("R4 routing replacement excludes concurrent old wheel triggers", BindingReplacementRace);
            Test("R4 update selection: numeric versions, channels, architecture, source restrictions", UpdateSelection);
            Test("R4 download integrity: SHA256, length bounds and cancellation", UpdateIntegrity);
            Test("R5 nonlinear DPI curve: bounds, inverse, monotonic and low-range precision", DpiCurve);
            Test("R5 identities do not imply controls; attributes and interface selection", IdentitySafety);
            Test("R5 replies validate TID, CRC, length and payload size", ReplySafety);
            Test("R5 instance cache isolation and stale write target rejection", InstanceSafety);
            Test("R5 area recording filters, pause balancing and live snapshots", AreaRecording);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Test("Silent startup creates its message HWND without showing the main window", () => {
                DesktopApp.LoadTheme(app); var hidden = new ShellWindow(true);
                IntPtr handle = new WindowInteropHelper(hidden).EnsureHandle();
                Check(handle != IntPtr.Zero && !hidden.IsVisible && !IsWindowVisible(handle), "Hidden startup revealed its main HWND");
                hidden.ClosePreview();
            });
            Test("WPF theme loads, all pages render and rapid navigation stops hidden animations", () => {
                Ui.ReducedMotion = true;
                var window = new ShellWindow(true); window.Preferences.Language = "zh"; window.Preferences.ReducedMotion = true; window.RebuildPages(0);
                window.ShowActivated = false; window.ShowInTaskbar = false; window.Show(); window.InitializeRuntime().GetAwaiter().GetResult(); Pump();
                Check(window.Title == AppVersion.DisplayName, "Window title uses current version");
                try
                {
                    Ui.ReducedMotion = false;
                    for (int i = 0; i < 100; i++) window.Navigate(i % 4);
                    foreach (var view in Field<FrameworkElement[]>(window, "views"))
                        if (view.Visibility != Visibility.Visible) { Check(!view.HasAnimatedProperties, "Hidden page has no opacity clock"); var transform = view.RenderTransform as System.Windows.Media.Animation.Animatable; Check(transform == null || !transform.HasAnimatedProperties, "Hidden page has no transform clock"); }
                    Ui.ReducedMotion = true;
                    for (int i = 0; i < 4; i++) { window.Navigate(i); Pump(); Snapshot(window, "page-" + i + ".png"); }
                    Check(Descendants<TextBlock>(Field<FrameworkElement[]>(window, "views")[2]).Any(t => t.Text == AppVersion.DisplayName), "About page uses current version");
                    window.Navigate(0);
                    var device = Field<DevicePage>(window, "devicePage");
                    DpiSliderRange(window, device);
                    Field<Slider>(device, "angle").Value = -8;
                    Check(Field<MouseOrientation>(device, "mouse").Angle == -8, "Model follows slider");
                    Check(Field<TextBox>(device, "angleValue").Text == "-8", "Angle editor follows slider");
                    Field<Slider>(device, "angle").Value = -7;
                    Check(Field<Button>(device, "applyRotation").IsEnabled, "Verified device enables rotation apply only for a pending change");
                    Invoke(device, "RestoreRotationPreview");
                    Check(Field<Slider>(device, "angle").Value == -8 && !Field<Button>(device, "applyRotation").IsEnabled, "Restore readback cancels the pending preview");
                    Field<Slider>(device, "dpi").Value = DpiScale.ToPosition(1200);
                    Check(Field<TextBox>(device, "dpiValue").Text == "1200", "DPI bubble follows slider");
                    Invoke(device, "OpenCalibration"); Pump(); Snapshot(window, "calibration.png"); window.CloseDrawer();
                    window.Width = 1000; Pump(); MacroUi(window);
                    RevisionUi(window);
                    RevisionThreeUi(window);
                    RevisionFiveRecordingUi(window);
                    window.Preferences.Language = "en"; window.RebuildPages(2); Pump(); Snapshot(window, "settings-en.png");
                    Check(window.Draft.Macros.Count == 2, "Localization retains macro draft");
                    window.Preferences.Language = "zh";
                    RevisionFourUi(window);
                    UpdatePolicyUi(window);
                    window.DraftDirty = true; window.Close(); Check(window.IsVisible && window.DrawerOpen, "Unsaved draft stops exit"); window.CloseDrawer();
                }
                finally { window.ClosePreview(); }
            });
            app.Shutdown();
            results.Add(string.Format("RESULT: {0} passed, {1} failed", passed, failed));
            Console.WriteLine(results.Last()); File.WriteAllLines(Path.Combine(artifacts, "desktop-results.txt"), results);
            return failed == 0 ? 0 : 1;
        }
        private static void Test(string name, Action action) { try { action(); passed++; results.Add("PASS: " + name); } catch (Exception ex) { failed++; results.Add("FAIL: " + name + "\n" + ex); } Console.WriteLine(results.Last()); }
        private static void Check(bool value, string text) { if (!value) throw new Exception(text); }
        private static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value); }
        private static void Invoke(object value, string name, params object[] args) { value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, args); }
        private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        private static void PumpFor(int milliseconds) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        private static void Snapshot(Window window, string file)
        {
            window.UpdateLayout(); var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using (var stream = File.Create(Path.Combine(artifacts, file))) encoder.Save(stream);
        }
        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T) yield return (T)child; foreach (var item in Descendants<T>(child)) yield return item; }
        }
        private static void MacroUi(ShellWindow window)
        {
            window.Navigate(1); var page = Field<MacroPage>(window, "macroPage"); page.Width = 760; window.UpdateLayout(); Pump(); Invoke(page, "NewMacro"); Invoke(page, "NewMacro");
            var m = page.Selected; m.Steps.Add(new MacroStep { Kind = ActionKind.Delay, Number = 100 }); page.RefreshRows();
            var selectedId = m.Id;
            window.Navigate(0); window.Navigate(1); Check(page.Selected.Id == selectedId, "Selection survives navigation");
            Field<ListBox>(page, "steps").SelectedIndex = 0;
            for (int i = 0; i < 9; i++)
            {
                Invoke(page, "EditStep", true); Pump(); var overlay = Field<Grid>(window, "overlay");
                var combo = Descendants<ComboBox>(overlay).First(); combo.SelectedIndex = i; Pump();
                Check(window.DrawerOpen, "Action drawer remains open");
                if (i == 1 || i == 6) Snapshot(window, "action-" + i + ".png");
                window.CloseDrawer();
            }
            Invoke(page, "EditStep", false); Pump();
            Descendants<ComboBox>(Field<Grid>(window, "overlay")).First().SelectedIndex = 1; Pump();
            Descendants<Button>(Field<Grid>(window, "overlay")).First(b => (string)b.Content == "完成").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(m.Steps.Count == 2 && m.Steps[1].Kind == ActionKind.Keyboard, "Drawer commits keyboard action to draft");
            window.SaveMacros();
            Invoke(page, "EditBinding", false); Pump(); Snapshot(window, "binding.png");
            var trigger = Descendants<ComboBox>(Field<Grid>(window, "overlay")).Skip(1).First(); trigger.SelectedIndex = 6; Pump();
            Check(trigger.SelectedIndex == 6, "Wheel binding offered");
            Descendants<Button>(Field<Grid>(window, "overlay")).First(b => (string)b.Content == "确认绑定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.ActiveMacros.Bindings.Count == 1 && window.ActiveMacros.Bindings[0].Trigger == TriggerKind.WheelUp, "Wheel binding applied immediately");
            Snapshot(window, "macros.png");
            window.SaveMacros(); Check(!window.DraftDirty, "Valid draft saved to preview memory");
        }
        private static void Capabilities()
        {
            CapabilityChecks();
        }
        private static void DpiSliderRange(ShellWindow window, DevicePage device)
        {
            var slider = Field<Slider>(device, "dpi"); var value = Field<TextBox>(device, "dpiValue");
            var pending = typeof(DevicePage).GetField("pendingDpi", BindingFlags.NonPublic | BindingFlags.Instance);
            int original = window.Reading.Dpi;
            Check(slider.Minimum == 0 && slider.Maximum == 1, "DPI drag bounds are 100-8000");
            Check(!slider.IsSnapToTickEnabled && DpiScale.Step == 50, "DPI value snapping is independent of nonlinear positions");
            foreach (int reading in new[] { 100, 800, 8000, 16000 }) {
                window.Reading.Dpi = reading; pending.SetValue(device, false); device.UpdateReading(); Pump();
                Check(slider.Maximum == 1 && slider.Minimum == 0, "Device refresh cannot expand slider range");
                Check(Math.Abs(slider.Value - DpiScale.ToPosition(reading)) < 0.000001 && value.Text == reading.ToString(), "High readback stays truthful without expanding the track");
                Check(!(bool)pending.GetValue(device) && window.Reading.Dpi == reading, "Passive refresh never queues DPI write");
            }
            slider.Value = 0; Check(value.Text == "100", "Lower endpoint is reachable");
            slider.Value = 1; Check(value.Text == "8000", "Upper endpoint is reachable");
            slider.Value = 2; Check(slider.Value == 1, "Dragging cannot exceed 8000");
            Check(DeviceCapabilities.For(window.Reading.ProductId).AcceptsDpi(16000), "Typed hardware capability was not reduced");
            pending.SetValue(device, false); window.Reading.Dpi = original; device.UpdateReading(); Pump();
            Snapshot(window, "r5-dpi-scale.png");
        }
        private static void RevisionUi(ShellWindow window)
        {
            window.Width = 1180; window.Height = 840; window.Navigate(1); Pump();
            var page = Field<MacroPage>(window, "macroPage"); var steps = Field<ListBox>(page, "steps");
            page.Width = double.NaN; window.UpdateLayout(); Pump();
            steps.SelectedIndex = 0; Invoke(page, "EditStep", true); Pump();
            Check(!window.DrawerOpen, "Wide layout uses inline properties"); Snapshot(window, "r2-macros-dark.png");
            var propertyEditor = Field<StackPanel>(page, "editor"); Descendants<TextBox>(propertyEditor).First().Text = "125";
            Check(page.HasPendingEdit, "Property changes marked unsaved"); window.Navigate(0); window.Navigate(1); Pump();
            Check(Descendants<TextBox>(propertyEditor).First().Text == "125", "Uncommitted property editor survives navigation");
            window.SaveMacros(); Check(page.Selected.Steps[0].Number == 125 && !page.HasPendingEdit && !window.DraftDirty, "Save includes pending property edits");
            Invoke(page, "EditStep", true); Pump(); Descendants<TextBox>(propertyEditor).First().Text = "not a number"; window.SaveMacros();
            Check(page.HasPendingEdit && page.Selected.Steps[0].Number == 125, "Invalid property edit cannot silently save");
            Invoke(page, "CloseStepEditor");
            var macro = page.Selected; int before = macro.Steps.Count;
            page.GetType().GetField("addingKind", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(page, ActionKind.LoopStart);
            Invoke(page, "EditStep", false); Pump(); Descendants<Button>(propertyEditor).First(b => b.Content as string == "完成").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(macro.Steps.Count == before + 2 && macro.Steps[1].Kind == ActionKind.LoopStart && macro.Steps[2].Kind == ActionKind.LoopEnd, "Palette inserts paired loop after selection");
            Invoke(page, "DeleteBlock"); Check(macro.Steps.Count == before, "Paired loop removed together");
            macro.Steps.Add(new MacroStep { Kind = ActionKind.LoopStart, Number = 2 }); macro.Steps.Add(new MacroStep { Kind = ActionKind.Delay, Number = 20 }); macro.Steps.Add(new MacroStep { Kind = ActionKind.LoopEnd }); page.RefreshRows();
            Invoke(page, "Reorder", before, 0); Check(macro.Steps[0].Kind == ActionKind.LoopStart && macro.Steps[2].Kind == ActionKind.LoopEnd, "Drag moves loop as intact block");
            steps.SelectedIndex = 2; Invoke(page, "DeleteBlock"); Check(macro.Steps.Count == before, "Deleting loop end removes pair and contents"); MacroValidation.Validate(window.Draft);
            Invoke(page, "SelectTab", true); Pump(); Snapshot(window, "r2-bindings-dark.png");
            window.Navigate(2); Pump();
            var settings = Field<FrameworkElement[]>(window, "views")[2];
            var switches = Descendants<CheckBox>(settings).ToList(); Check(switches.Any(x => (string)x.Content == "开机自启动"), "Autostart concise label");
            foreach (var sw in switches) { var dot = (System.Windows.Shapes.Ellipse)sw.Template.FindName("Dot", sw); var offset = ((TransformGroup)dot.RenderTransform).Children[1] as TranslateTransform; Check(offset.X == (sw.IsChecked == true ? 18 : 0), "Initial toggle position matches state: " + sw.Content); }
            Check(!Descendants<TextBlock>(settings).Any(x => x.Text.Contains("浮窗样式") || x.Text.Contains("托盘图标")), "Obsolete style settings removed");
            Check(!switches.Any(x => ((string)x.Content).Contains("DPI")), "DPI OSD toggle removed");
            Check(typeof(ShellWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).All(f => f.FieldType != typeof(DpiOsdForm)), "No OSD instance in new shell");
            var toggle = switches.First(x => (string)x.Content == "关闭窗口时最小化到托盘");
            Ui.ReducedMotion = false;
            if (SystemParameters.ClientAreaAnimation) { toggle.IsChecked = false; PumpFor(35); toggle.IsChecked = true; PumpFor(45); var moving = (TranslateTransform)((TransformGroup)((System.Windows.Shapes.Ellipse)toggle.Template.FindName("Dot", toggle)).RenderTransform).Children[1]; Check(moving.X >= 0 && moving.X <= 18, "Interrupted elastic travel stays inside track"); PumpFor(500); Check(Math.Abs(moving.X - 18) < .001, "Animated toggle settles to final state"); }
            for (int i = 0; i < 50; i++) toggle.IsChecked = i % 2 == 0;
            toggle.IsChecked = true; Ui.ReducedMotion = true; ElasticSwitch.RefreshAll(); Pump();
            var move = (TranslateTransform)((TransformGroup)((System.Windows.Shapes.Ellipse)toggle.Template.FindName("Dot", toggle)).RenderTransform).Children[1];
            Check(move.X == 18 && !move.HasAnimatedProperties, "Reduced motion settles interrupted toggle immediately: X=" + move.X + ", animated=" + move.HasAnimatedProperties + ", checked=" + toggle.IsChecked);
            var failToggle = Ui.Toggle("Failure test", false, b => { throw new IOException("simulated"); }); failToggle.IsChecked = true; failToggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Check(failToggle.IsChecked == false, "Failed setting rolls back toggle");
            var foreground = (SolidColorBrush)Ui.Foreground;
            var themeCombo = Descendants<ComboBox>(settings).First(c => c.Items.Contains("白天")); themeCombo.SelectedIndex = 1; Pump();
            Check(window.Preferences.Theme == "light", "Theme selector updates model");
            Check(foreground.Color == (Color)ColorConverter.ConvertFromString("#1B1B1B"), "Theme updates existing brushes without rebuilding drafts");
            window.Navigate(0); Pump(); Snapshot(window, "r2-device-light.png");
            window.Navigate(2); Pump(); Snapshot(window, "r2-settings-light.png");
            window.Navigate(1); Invoke(page, "SelectTab", false); steps.SelectedIndex = 0; Invoke(page, "EditStep", true); Pump(); Snapshot(window, "r2-macros-light.png"); Invoke(page, "CloseStepEditor");
            foreach (bool light in new[] { false, true })
            {
                Ui.ApplyTheme(light ? "light" : "dark");
                foreach (bool menu in new[] { true, false })
                {
                    var popup = new TrayFlyout(window, menu, false, b => { }); popup.ShowActivated = false; popup.Show(); Pump(); Snapshot(popup, "r2-tray-" + (menu ? "menu" : "card") + (light ? "-light.png" : "-dark.png")); popup.Close();
                }
            }
            var position = TrayFlyout.Place(new Point(-1900, 4), new Size(300, 240), new Rect(-1920, 0, 1920, 1040)); Check(position.X >= -1920 && position.Y >= 0, "Negative monitor / top taskbar clamped");
            var positioned = new TrayFlyout(window, true, false, b => { });
            var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            positioned.ShowAt(new System.Drawing.Point(area.Right - 4, area.Bottom - 4)); Pump();
            var dpiTransform = PresentationSource.FromVisual(positioned).CompositionTarget.TransformToDevice;
            var physicalBottom = dpiTransform.Transform(new Point(positioned.Left + positioned.ActualWidth, positioned.Top + positioned.ActualHeight));
            Check(physicalBottom.X <= area.Right + 1 && physicalBottom.Y <= area.Bottom + 1, "Actual HWND tray flyout stays inside working area"); positioned.Close();
            position = TrayFlyout.Place(new Point(1920, 1080), new Size(300, 240), new Rect(0, 0, 1920, 1040)); Check(position.X + 300 <= 1920 && position.Y + 240 <= 1040, "Bottom/right boundaries clamped");
            foreach (int size in new[] { 16, 20, 24, 32 }) using (var icon = TrayController.BrandIcon(true, size)) { Check(icon.Width == size, "Tray icon size"); using (var bitmap = icon.ToBitmap()) bitmap.Save(Path.Combine(artifacts, "tray-icon-" + size + ".png")); }
            using (var tray = new TrayController(window))
            {
                Invoke(tray, "Show", true); Pump(); var first = Field<TrayFlyout>(tray, "flyout"); Check(first.IsMenu, "Right-click route opens operation menu");
                Invoke(tray, "Show", false); Pump(); var second = Field<TrayFlyout>(tray, "flyout"); Check(!second.IsMenu && !first.IsVisible, "Left/right flyouts are mutually exclusive");
                Invoke(tray, "Show", false); Check(ReferenceEquals(Field<TrayFlyout>(tray, "flyout"), second) && second.IsVisible, "Repeated left press keeps the same card HWND");
                var notify = Field<System.Windows.Forms.NotifyIcon>(tray, "icon");
                window.Hide(); var latency = System.Diagnostics.Stopwatch.StartNew();
                for (int clicks = 1; clicks <= 2; clicks++) Invoke(notify, "OnMouseDown", new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, clicks, 0, 0, 0));
                Check(latency.ElapsedMilliseconds < 300 && !window.IsVisible && ReferenceEquals(Field<TrayFlyout>(tray, "flyout"), second), "Actual NotifyIcon event route immediately reuses card, never opens main on double press");
                Invoke(second, "OnDeactivated", EventArgs.Empty);
                Invoke(notify, "OnMouseDown", new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 0, 0, 0)); Pump();
                Check(ReferenceEquals(Field<TrayFlyout>(tray, "flyout"), second), "Queued deactivate does not destroy reactivated tray card");
                window.Show();
                Invoke(tray, "Notify", "连接提醒测试 / Connection test"); Pump(); var notification = Field<Window>(tray, "notification"); Check(!notification.ShowActivated, "Notifications do not steal focus"); Snapshot(notification, "r2-notification-light.png");
            }
            Ui.ApplyTheme("dark"); window.Navigate(0); Pump(); Snapshot(window, "r2-device-dark.png"); window.Navigate(2); Pump(); Snapshot(window, "r2-settings-dark.png");
        }
        private static void CapabilityChecks()
        {
            var se = DeviceCapabilities.For(0x00DF); Check(se.AcceptsRate(500) && !se.AcceptsRate(8000), "SE receiver rates");
            Check(se.AcceptsDpi(100) && se.AcceptsDpi(35000) && !se.AcceptsDpi(35001), "DPI bounds");
            Check(!DeviceCapabilities.For(0x1234).AcceptsDpi(800), "Unknown DPI");
            Check(!DeviceCapabilities.For(0x1234).AcceptsRate(1000), "Unknown rate");
            Check(se.AcceptsRotation(-44) && se.AcceptsRotation(44) && !se.AcceptsRotation(45), "00DF rotation bounds");
            Check(!DeviceCapabilities.For(0x00DE).AcceptsRotation(0) && !DeviceCapabilities.For(0x00C1).AcceptsRotation(0), "Unverified connections cannot write rotation");
        }
        private static void Dpi()
        {
            foreach (int length in new[] { 90, 91 })
            {
                var fake = new DeviceFake(length); var client = new RazerDeviceClient(new Transport(fake), new HardwareCacheStore(null));
                client.QueryRazerDeviceInfo(); Check(client.SetDpiVerified(0x00DF, 1250), "DPI set + readback");
                Check(fake.Stages[0] == 400 && fake.Stages[1] == 1250 && fake.Stages[2] == 1600, "Other stages preserved");
                Check(fake.Writes == 1, "Exactly one write");
            }
        }
        private static void Rejections()
        {
            var f = new DeviceFake(91); var c = new RazerDeviceClient(new Transport(f), new HardwareCacheStore(null));
            c.QueryRazerDeviceInfo(); Check(!c.SetDpiVerified(0x00DE, 800) && f.Writes == 0, "Wrong PID blocked");
            Check(!c.SetDpiVerified(0x00DF, 99999) && f.Writes == 0, "Invalid value blocked");
            f.IgnoreWrites = true; Check(!c.SetDpiVerified(0x00DF, 1200), "ACK without effect rejected");
            f.WrongEcho = true; int count = f.Writes; Check(!c.SetDpiVerified(0x00DF, 1200) && f.Writes == count, "Wrong command echo rejected before write");
        }
        private static void Rates()
        {
            var f = new DeviceFake(91); var c = new RazerDeviceClient(new Transport(f), new HardwareCacheStore(null));
            c.QueryRazerDeviceInfo(); Check(c.SetRateVerified(0x00DF, 500), "500 set/read");
            Check(!c.SetRateVerified(0x00DF, 8000), "8k rejected");
            f.IgnoreWrites = true; Check(!c.SetRateVerified(0x00DF, 125), "False ACK rejected");
        }
        private static void RotationCommands()
        {
            byte[] payload;
            Check(RazerProtocol.TryEncodeRotation(-44, out payload) && payload[2] == 0xD4, "Negative boundary encoding");
            Check(RazerProtocol.TryEncodeRotation(44, out payload) && payload[2] == 44, "Positive boundary encoding");
            Check(!RazerProtocol.TryEncodeRotation(-45, out payload) && !RazerProtocol.TryEncodeRotation(45, out payload), "Out-of-range encoding rejected");
            var f = new DeviceFake(91); var c = new RazerDeviceClient(new Transport(f), new HardwareCacheStore(null));
            var reading = c.QueryRazerDeviceInfo();
            Check(reading.RotationKnown && reading.RotationAngle == -8 && reading.IsRotationWriteSupported && reading.IsRotationHardwareVerified, "Accepted device revision exposes live readback and rotation write capability");
            int angle; Check(c.TryGetRotationVerified(0x00DF, reading.DeviceKey, out angle) && angle == -8, "Bound read returns signed angle");
            Check(!c.SetRotationVerified(0x00DF, 10).WriteAttempted && f.Writes == 0, "Rotation requires an explicit instance key");
            var written = c.SetRotationVerified(0x00DF, 10, reading.DeviceKey);
            Check(written.Success && written.Before == -8 && written.After == 10 && f.Rotation == 10 && f.Writes == 1, "One rotation write with readback");
            int before = f.Writes;
            Check(!c.SetRotationForResearch(0x00DF, 45, reading.DeviceKey).Success && f.Writes == before, "Invalid angle never writes");
            Check(!c.SetRotationForResearch(0x00DF, 0, "stale-instance").Success && f.Writes == before, "Stale instance never writes");
            f.IgnoreWrites = true; var ignored = c.SetRotationForResearch(0x00DF, -10, reading.DeviceKey);
            Check(!ignored.Success && ignored.RollbackAttempted && ignored.RollbackSucceeded && f.Rotation == 10, "False ACK/effect mismatch rolls back to original state");
        }
        private static void Rotation()
        {
            var original = new Vector(100, 30); var result = RotationMath.Rotate(original, -Math.Atan2(30, 100) * 180 / Math.PI);
            Check(Math.Abs(result.Y) < 0.00001, "Straight after correction"); Check(Math.Abs(result.Length - original.Length) < 0.00001, "Length preserved");
            Check(Math.Abs(RotationMath.Rotate(new Vector(4, 8), 0).Y - 8) < 0.00001, "Does not force Y to zero");
        }
        private sealed class RotationFault : IHidDevice, IHidDescriptor
        {
            internal readonly DeviceFake Inner = new DeviceFake(91);
            internal int Mode, SetCount;
            public int ProductId { get { return Inner.ProductId; } }
            public HidDescriptor Descriptor { get { return Inner.Descriptor; } }
            public string ProductName { get { return Inner.ProductName; } }
            public int ReportLength { get { return Inner.ReportLength; } }
            public byte[] Exchange(byte[] request, int delay)
            {
                bool get = request[7] == 0x0B && request[8] == 0x94;
                bool set = request[7] == 0x0B && request[8] == 0x14;
                if (set) SetCount++;
                Inner.IgnoreWrites = Mode == 3 || Mode == 4 && SetCount == 2;
                var reply = Inner.Exchange(request, delay);
                if (set && SetCount == 1 && Mode == 1) throw new IOException("delivered but transport threw");
                if (get && SetCount == 1 && Mode == 2) return null;
                if (set && SetCount == 1 && Mode == 4) return null;
                if (get && Mode == 5) reply[2] ^= 1;
                if (get && Mode == 6) return reply.Take(10).ToArray();
                if (get && Mode == 7) { reply[9] = 0; reply[89] = RazerProtocol.CalculateCrc(reply, 1); }
                return reply;
            }
        }
        private static void RotationFailures()
        {
            for (int mode = 1; mode <= 7; mode++) {
                var fault = new RotationFault(); var client = new RazerDeviceClient(new Transport(fault), new HardwareCacheStore(null));
                var reading = client.QueryRazerDeviceInfo(); fault.Mode = mode;
                var result = client.SetRotationVerified(0x00DF, 10, reading.DeviceKey);
                Check(!result.Success, "Fault must not be reported as success: " + mode);
                if (mode <= 4) {
                    Check(result.RollbackAttempted && fault.SetCount == 2, "One target plus one recovery, never blind retries: " + mode);
                    Check(result.RollbackSucceeded == (mode != 4), "Recovery truthfully reported: " + mode);
                    Check(fault.Inner.Rotation == (mode == 4 ? 10 : -8), "Final simulated angle: " + mode);
                } else Check(!result.WriteAttempted && fault.SetCount == 0, "Invalid initial read never writes: " + mode);
            }
            foreach (int mode in new[] { 0, 1, 2 }) {
                var fake = new DeviceFake(mode == 2 ? 90 : 91);
                if (mode == 0) fake.Descriptor.Version = 0x0101;
                if (mode == 1) fake.Descriptor.ProductId = 0x00DE;
                var client = new RazerDeviceClient(new Transport(fake), new HardwareCacheStore(null));
                var reading = client.QueryRazerDeviceInfo();
                Check(!reading.IsRotationWriteSupported && !client.SetRotationVerified(fake.ProductId, 10, reading.DeviceKey).WriteAttempted && fake.Writes == 0, "Unverified revision/connection/report length denied");
            }
        }
        private static void Calibration()
        {
            var points = Enumerable.Range(0, 21).Select(i => new Point(i * 10, 40 + i * 10 * Math.Tan(8 * Math.PI / 180))).ToList();
            double angle; Check(RotationMath.TryStroke(points, out angle) && Math.Abs(angle - 8) < 0.001, "Forward angle");
            points.Reverse(); Check(RotationMath.TryStroke(points, out angle) && Math.Abs(angle - 8) < 0.001, "Reverse angle");
            Check(!RotationMath.TryStroke(points.Take(5).ToList(), out angle), "Short rejected");
            var angles = Enumerable.Repeat(3.0, 10).Concat(new[] { -30.0 }).ToList(); double correction;
            Check(RotationMath.TryCorrection(angles, 8, out correction) && correction == 5, "Subtract residual from prior correction");
            Check(!RotationMath.TryCorrection(angles.Take(9).ToList(), 0, out correction), "Ten required");
            Check(!RotationMath.TryCorrection(Enumerable.Repeat(-40.0, 10).ToList(), 40, out correction), "Out of range rejected");
        }
        private static void Settings()
        {
            string dir = Path.Combine(artifacts, "preferences-" + Guid.NewGuid().ToString("N")); var store = new DesktopSettings(dir);
            store.Save(new DesktopPreferences { Language = "en", ReducedMotion = true, CloseToTray = false, Theme = "light", TrayAnimation = false, LowBatteryThreshold = 15, ConnectionNotifications = true }); var loaded = store.Load();
            Check(loaded.Language == "en" && loaded.ReducedMotion && !loaded.CloseToTray && loaded.Theme == "light" && !loaded.TrayAnimation && loaded.LowBatteryThreshold == 15 && loaded.ConnectionNotifications, "Settings roundtrip");
            File.WriteAllText(Path.Combine(dir, "desktop.xml"), "<broken>"); bool rejected = false; try { store.Load(); } catch { rejected = true; } Check(rejected, "Corruption is surfaced");
        }
        private sealed class Transport : IHidTransport { private readonly IHidDevice d; public Transport(IHidDevice device) { d = device; } public void Visit(Func<IHidDevice, bool> visitor) { visitor(d); } }
        private sealed class DeviceFake : IHidDevice, IHidDescriptor
        {
            public int ProductId { get { return Descriptor.ProductId; } }
            public HidDescriptor Descriptor { get; private set; }
            public string ProductName { get { return "Test SE"; } }
            public int ReportLength { get; private set; }
            public int[] Stages = { 400, 800, 1600 }; public int Writes, Reads, Rotation = -8; public bool IgnoreWrites, WrongEcho; private byte rate = 1;
            public DeviceFake(int length) { ReportLength = length; Descriptor = new HidDescriptor { VendorId = 0x1532, ProductId = 0x00DF, Version = 0x0100, ReportLength = length, UsagePage = 0xFF00, Path = Guid.NewGuid().ToString(), ContainerId = Guid.NewGuid().ToString() }; }
            public byte[] Exchange(byte[] request, int delay)
            {
                Reads++; int o = ReportLength == 91 ? 1 : 0; var r = (byte[])request.Clone(); r[o] = 2; byte cls = r[o + 6], cmd = r[o + 7];
                if (cls == 4 && cmd == 6) { Writes++; if (!IgnoreWrites) for (int i = 0; i < 3; i++) Stages[i] = (r[o + 12 + i * 7] << 8) | r[o + 13 + i * 7]; }
                if (cls == 4 && cmd == 0x86) { r[o + 8] = 1; r[o + 9] = 2; r[o + 10] = 3; for (int i = 0; i < 3; i++) { int at = o + 11 + i * 7; r[at] = (byte)(i + 1); r[at + 1] = r[at + 3] = (byte)(Stages[i] >> 8); r[at + 2] = r[at + 4] = (byte)Stages[i]; } }
                if (cls == 0 && cmd == 0x40) { Writes++; if (!IgnoreWrites) rate = r[o + 9]; }
                if (cls == 0 && cmd == 0xC0) r[o + 9] = rate;
                if (cls == 0 && cmd == 0x05) { Writes++; if (!IgnoreWrites) rate = r[o + 8]; }
                if (cls == 0 && cmd == 0x85) r[o + 8] = rate;
                if (cls == 0x0B && cmd == 0x14) { Writes++; if (!IgnoreWrites) Rotation = unchecked((sbyte)r[o + 10]); }
                if (cls == 0x0B && cmd == 0x94) { r[o + 8] = 1; r[o + 9] = 1; r[o + 10] = unchecked((byte)(sbyte)Rotation); }
                if (WrongEcho) r[o + 7] = 0; r[o + 88] = RazerProtocol.CalculateCrc(r, o); return r;
            }
        }
    }
}
