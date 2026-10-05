using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace RazerBatteryTray.Desktop
{
    // Isolated WPF UI. No real input injection, hooks, HID, user settings writes
    // or runtime initialization. The real scheduler and real templates are used.
    internal static class MotionTests
    {
        private static readonly List<string> results = new List<string>();
        private static int failures;
        private static string only;
        private static string output;
        private static Window window;
        private static StackPanel panel;
        private static Button primary, ordinary;
        private static CheckBox toggle;
        private static ComboBox combo;
        private static Border entry;
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Test(string name, Action test) {
            if (!string.IsNullOrEmpty(only) && !name.StartsWith(only, StringComparison.Ordinal)) return;
            Ui.ReducedMotion = false; window.Activate(); Pump(40); UiMotion.SettleAll();
            try { test(); results.Add("PASS\t" + name); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; results.Add("FAIL\t" + name + "\t" + ex.Message); Console.WriteLine("FAIL " + name + ": " + ex); }
            finally { combo.IsDropDownOpen = false; UiMotion.SettleAll(); }
        }
        private static void Pump(int ms) {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement {
            var element = root as T; if (element != null && element.Name == name) return element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = Find<T>(VisualTreeHelper.GetChild(root, i), name); if (found != null) return found; }
            return null;
        }
        private static void Mouse(Button button, bool down) { button.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = down ? UIElement.PreviewMouseLeftButtonDownEvent : UIElement.PreviewMouseLeftButtonUpEvent }); }
        private static void Key(Button button, bool down, Key key) { button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key) { RoutedEvent = down ? UIElement.PreviewKeyDownEvent : UIElement.PreviewKeyUpEvent }); }
        private static UiMotion.MotionTransform Parts(Button button) { return UiMotion.Transform(UiMotion.ButtonVisual(button)); }
        private static TranslateTransform Thumb() { return (TranslateTransform)((TransformGroup)((Ellipse)toggle.Template.FindName("Dot", toggle)).RenderTransform).Children[1]; }
        private static void Capture(string name, FrameworkElement visual) {
            visual.UpdateLayout(); if (visual.ActualWidth <= 0 || visual.ActualHeight <= 0) throw new Exception("Empty preview");
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var file = new FileStream(Path.Combine(output, name + ".png"), FileMode.CreateNew)) encoder.Save(file);
        }
        private static void Series(string name, Action begin, Action middle, FrameworkElement visual) {
            begin(); var rows = new List<string> { "elapsed_ms,press_scale,magnetic_x,toggle_x,entry_x,entry_y,active" }; var clock = Stopwatch.StartNew();
            for (int i = 0; i < 18; i++) { if (i == 5 && middle != null) middle(); Pump(35); Capture(name + "-" + i.ToString("D2"), visual);
                var p = Parts(primary); var e = UiMotion.Transform(entry).Entry;
                rows.Add(string.Join(",", clock.ElapsedMilliseconds, p.Press.ScaleX.ToString("R", System.Globalization.CultureInfo.InvariantCulture), p.Magnetic.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Thumb().X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), e.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), e.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), SpringMotion.ActiveCount)); }
            File.WriteAllLines(Path.Combine(output, name + ".csv"), rows); UiMotion.SettleAll();
        }
        [STAThread] private static int Main(string[] args) {
            output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            only = args.Length > 1 ? args[1] : null;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; DesktopApp.LoadTheme(app);
            panel = new StackPanel { Margin = new Thickness(25) };
            primary = Ui.PrimaryButton("保存并应用 · Primary", () => {}); ordinary = Ui.Button("普通按钮 · Space / Enter", () => {});
            toggle = Ui.Toggle("Toggle / 连续切换", false, value => {}); combo = Ui.Combo(new[] { "选项一", "选项二", "选项三" });
            entry = Ui.Card(Ui.Text("Page / Drawer entrance · 阻尼弹簧", 19)); entry.Height = 90;
            panel.Children.Add(primary); panel.Children.Add(ordinary); panel.Children.Add(toggle); panel.Children.Add(combo); panel.Children.Add(entry);
            window = new Window { Title = "雷云 Lite Motion 2.0 · 隔离测试", Width = 530, Height = 450, Content = panel, Background = Ui.Background, ShowInTaskbar = false };
            app.MainWindow = window; window.Show(); window.Activate(); Pump(100);
            try {
                Test("F1 retarget keeps position and velocity; stable variable dt; presets settle", () => {
                    foreach (var preset in new[] { SpringPreset.Snappy, SpringPreset.Smooth, SpringPreset.Magnetic }) {
                        var state = new SpringState(0, preset); state.Retarget(18); state.Advance(.045); double p = state.Position, v = state.Velocity;
                        Check(Math.Abs(v) > 1, "No momentum"); state.Retarget(0); Check(p == state.Position && v == state.Velocity, "Retarget reset state");
                        for (int i = 0; i < 500; i++) state.Advance(i % 2 == 0 ? .007 : .026);
                        Check(state.Position == 0 && state.Velocity == 0, "Did not settle"); state.Retarget(180); state.Advance(3); Check(state.Position == 180 && state.Velocity == 0, "Long frame unstable");
                    }
                    var smooth = new SpringState(16, SpringPreset.Smooth); smooth.Retarget(0); double minimum = 0;
                    for (int i = 0; i < 1000; i++) { smooth.Advance(.001); minimum = Math.Min(minimum, smooth.Position); }
                    Check(minimum < -1 && minimum > -2, "Drawer overshoot budget: " + minimum); File.WriteAllText(Path.Combine(output, "drawer-overshoot.txt"), minimum.ToString("R"));
                });
                Test("F2 real Rendering settles and detaches", () => { Ui.Enter(entry, true); Check(SpringMotion.ActiveCount > 0 && SpringMotion.RenderingSubscribed, "No scheduler"); Pump(1300); Check(SpringMotion.ActiveCount == 0 && !SpringMotion.RenderingSubscribed && UiMotion.Transform(entry).Entry.X == 0, "Rendering leaked"); });
                Test("F3 ReducedMotion immediately resets springs, magnetic and fade", () => {
                    Mouse(primary, true); MagneticMotion.For(primary).UpdateTarget(new Point(primary.ActualWidth, 0)); Ui.Enter(entry); toggle.IsChecked = true;
                    Ui.ReducedMotion = true; Check(Parts(primary).Press.ScaleX == 1 && Parts(primary).Magnetic.X == 0 && Thumb().X == 18 && entry.Opacity == 1, "Reduced fallback");
                    Check(SpringMotion.ActiveCount == 0 && !SpringMotion.RenderingSubscribed, "Reduced loop"); Mouse(primary, true); MagneticMotion.For(primary).UpdateTarget(new Point(500, 0)); Check(SpringMotion.ActiveCount == 0, "Reduced reactivated");
                });
                Test("F4 SettleAll + hidden + Unloaded stop nested controls", () => {
                    Ui.Enter(entry); Mouse(primary, true); UiMotion.SettleAll(); Check(SpringMotion.ActiveCount == 0 && Parts(primary).Press.ScaleX == 1, "SettleAll");
                    Mouse(primary, true); panel.Visibility = Visibility.Collapsed; Pump(40); Check(SpringMotion.ActiveCount == 0, "Hidden leak"); panel.Visibility = Visibility.Visible; Pump(40);
                    Mouse(primary, true); panel.Children.Remove(primary); Pump(40); Check(SpringMotion.ActiveCount == 0, "Unload leak"); panel.Children.Insert(0, primary); Pump(40);
                });
                Test("B1 routed mouse + Space/Enter press / leave; ordinary no magnetic", () => {
                    Mouse(ordinary, true); var x = SpringMotion.Get(ordinary, Parts(ordinary).Press, ScaleTransform.ScaleXProperty, SpringPreset.Snappy, 1);
                    Check(x.State.Target == .985, "Press budget"); Pump(60); double p = x.State.Position, v = x.State.Velocity; Mouse(ordinary, false); Check(x.State.Position == p && x.State.Velocity == v, "Release discontinuity");
                    for (int i = 0; i < 20; i++) { Mouse(ordinary, i % 2 == 0); Pump(6); }
                    ordinary.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                    foreach (var key in new[] { System.Windows.Input.Key.Space, System.Windows.Input.Key.Enter }) { Key(ordinary, true, key); Check(x.State.Target == .985, "Keyboard press"); Key(ordinary, false, key); }
                    Pump(900); Check(Parts(ordinary).Press.ScaleX == 1 && MagneticMotion.For(ordinary) == null, "Ordinary leaked");
                });
                Test("B2 magnetic targets capped; reversal preserves velocity; hit-test fixed at all edges", () => {
                    var magnetic = MagneticMotion.For(primary); var baselineSize = primary.RenderSize;
                    var points = new[] { new Point(1, 1), new Point(primary.ActualWidth-1, 1), new Point(1, primary.ActualHeight-1), new Point(primary.ActualWidth-1, primary.ActualHeight-1) };
                    foreach (var point in points) Check(primary.InputHitTest(point) != null, "Baseline miss");
                    magnetic.UpdateTarget(new Point(primary.ActualWidth, primary.ActualHeight)); Pump(80);
                    var p = Parts(primary); var x = SpringMotion.Get(primary, p.Magnetic, TranslateTransform.XProperty, SpringPreset.Magnetic, 0); double velocity = x.State.Velocity, position = x.State.Position;
                    magnetic.UpdateTarget(new Point(0, 0)); Check(velocity == x.State.Velocity && position == x.State.Position, "Magnetic interruption");
                    Check(Math.Sqrt(x.State.Target*x.State.Target + SpringMotion.Get(primary, p.Magnetic, TranslateTransform.YProperty, SpringPreset.Magnetic, 0).State.Target*SpringMotion.Get(primary, p.Magnetic, TranslateTransform.YProperty, SpringPreset.Magnetic, 0).State.Target) <= 2.00001, "Magnetic exceeded cap");
                    Pump(80); Mouse(primary, true); foreach (var point in points) Check(primary.InputHitTest(point) != null, "Moved hit area"); Check(primary.RenderSize == baselineSize && primary.RenderTransform.Value.IsIdentity, "Layout/root moved");
                    primary.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent }); Check(x.State.Target == 0, "Leave target"); Pump(1100); Check(p.Magnetic.X == 0 && p.Magnetic.Y == 0 && p.Press.ScaleX == 1, "Sticky return");
                    Ui.Enter(primary); Check(ReferenceEquals(p, Parts(primary)), "Entry replaced Press/Magnetic");
                });
                Test("B3 Toggle rapid retarget keeps velocity, both scale axes, state and rollback", () => {
                    toggle.IsChecked = false; UiMotion.SettleAll(); toggle.IsChecked = true; Pump(40); var state = SpringMotion.Get(toggle, Thumb(), TranslateTransform.XProperty, SpringPreset.Snappy).State;
                    double v = state.Velocity, p = state.Position; toggle.IsChecked = false; Check(v == state.Velocity && p == state.Position, "Toggle reset");
                    for (int i = 0; i < 30; i++) { toggle.IsChecked = i % 2 == 0; Pump(7); } toggle.IsChecked = true; Pump(900); Check(Thumb().X == 18, "Final state");
                    toggle.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                    var scale = (ScaleTransform)((TransformGroup)((Ellipse)toggle.Template.FindName("Dot", toggle)).RenderTransform).Children[0];
                    Check(SpringMotion.Get(toggle, scale, ScaleTransform.ScaleYProperty, SpringPreset.Snappy, 1).State.Target == 1.10, "Y press missing");
                    var rollback = Ui.Toggle("Rollback", false, b => { throw new IOException(); }); panel.Children.Add(rollback); Pump(30); rollback.IsChecked = true; rollback.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Check(rollback.IsChecked == false, "Business rollback changed"); panel.Children.Remove(rollback);
                });
                Test("B4 Dropdown opens above/below; fixed arrow retarget; close stops popup; no magnetic", () => {
                    var arrow = Find<FrameworkElement>(combo, "DropArrow"); Check(arrow != null, "No arrow");
                    foreach (var placement in new[] { PlacementMode.Bottom, PlacementMode.Top }) {
                        var popup = (Popup)combo.Template.FindName("PART_Popup", combo); popup.Placement = placement; combo.IsDropDownOpen = true; Pump(55);
                        var surface = Find<Border>(popup.Child, "DropSurface"); Check(surface != null, "No surface"); var rotate = arrow.RenderTransform as RotateTransform; Check(rotate != null, "No rotate");
                        var state = SpringMotion.Get(combo, rotate, RotateTransform.AngleProperty, SpringPreset.Snappy).State; double p = state.Position, v = state.Velocity;
                        // WPF raises DropDownClosed later through its popup queue.
                        // Check continuity synchronously at the shared helper,
                        // then check the actual close event after dispatcher work.
                        ElasticDropdown.Close(combo); Check(ReferenceEquals(rotate, arrow.RenderTransform) && state.Position == p && state.Velocity == v && state.Target == 0, "Arrow discontinuity");
                        combo.IsDropDownOpen = false; Pump(30); Check(state.Target == 0, "Actual close did not retarget");
                        Check(UiMotion.Transform(surface).Entry.Y == 0, "Closed popup spring leak"); Pump(900);
                    }
                });
                Test("B5 Drawer/page/theme/resize; handle independent magnetic + press; preserved fixed hit root", () => {
                    var shell = new ShellWindow(true) { ShowInTaskbar = false }; shell.Show(); shell.Activate(); Pump(60);
                    try { foreach (var theme in new[] { "classic", "fluent" }) foreach (double width in new[] { 760.0, 980.0, 1180.0 }) {
                        Ui.ApplyTheme(theme); shell.Width = width; Pump(40); shell.OpenDrawer("Motion preview", Ui.Text("Drawer content")); Pump(70);
                        var flags = BindingFlags.Instance | BindingFlags.NonPublic; var drawer = (Border)typeof(ShellWindow).GetField("activeDrawer", flags).GetValue(shell); var handle = (Button)typeof(ShellWindow).GetField("drawerCollapseButton", flags).GetValue(shell);
                        Check(ReferenceEquals(drawer.RenderTransform, handle.RenderTransform), "Handle entrance lost");
                        Check(SpringMotion.Get(drawer, UiMotion.Transform(drawer).Entry, TranslateTransform.XProperty, SpringPreset.Smooth, 0).State.Target == 0, "Drawer target");
                        var hm = MagneticMotion.For(handle); Check(hm != null, "No handle magnet"); hm.UpdateTarget(new Point(handle.ActualWidth, handle.ActualHeight / 2)); Mouse(handle, true);
                        Check(SpringMotion.Get(handle, Parts(handle).Press, ScaleTransform.ScaleXProperty, SpringPreset.Snappy, 1).State.Target == .975, "Handle scale");
                        Check(drawer.Width + 14 == ((Grid)typeof(ShellWindow).GetField("activeDrawerHost", flags).GetValue(shell)).Width && handle.InputHitTest(new Point(1,1)) != null, "Responsive/hit root");
                        shell.CloseDrawer(); Pump(40); Check(SpringMotion.ActiveCount == 0, "Drawer close leak");
                    } } finally { shell.ClosePreview(); window.Activate(); Pump(60); }
                });
                Test("F5 background/minimized/no pointer disable motion; popup context follows window", () => {
                    Mouse(primary, true); MagneticMotion.For(primary).UpdateTarget(new Point(primary.ActualWidth, 0)); combo.IsDropDownOpen = true; Pump(50);
                    var other = new Window { Width=100, Height=100, ShowInTaskbar=false }; other.Show(); other.Activate(); Pump(60);
                    Check(!window.IsActive && SpringMotion.ActiveCount == 0 && !SpringMotion.RenderingSubscribed && Parts(primary).Magnetic.X == 0, "Background loop");
                    MagneticMotion.For(primary).UpdateTarget(new Point(primary.ActualWidth,0)); Check(SpringMotion.ActiveCount == 0, "Background pointer followed"); other.Close(); window.Activate(); combo.IsDropDownOpen = false; Pump(50);
                    Mouse(primary,true); window.WindowState = WindowState.Minimized; Pump(60); Check(SpringMotion.ActiveCount == 0 && !SpringMotion.RenderingSubscribed, "Minimized leak");
                    window.WindowState=WindowState.Normal; window.Activate(); Pump(80); primary.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) {RoutedEvent=UIElement.MouseLeaveEvent}); Pump(1100); Check(SpringMotion.ActiveCount==0 && !SpringMotion.RenderingSubscribed,"No-pointer loop");
                });
                Test("P1 idle ten seconds CPU/GC; active CPU/GC; hot path state reuse", () => {
                    var process=Process.GetCurrentProcess(); var cpu=process.TotalProcessorTime; int[] gc={GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)}; var clock=Stopwatch.StartNew(); Pump(10000); process.Refresh();
                    string idle="duration_ms="+clock.ElapsedMilliseconds+"; cpu_ms="+(process.TotalProcessorTime-cpu).TotalMilliseconds+"; active="+SpringMotion.ActiveCount+"; rendering="+SpringMotion.RenderingSubscribed+"; gc="+(GC.CollectionCount(0)-gc[0])+","+(GC.CollectionCount(1)-gc[1])+","+(GC.CollectionCount(2)-gc[2]);
                    Check(SpringMotion.ActiveCount==0 && !SpringMotion.RenderingSubscribed,"Idle active"); cpu=process.TotalProcessorTime; clock.Restart(); int before=GC.CollectionCount(0);
                    var binding=SpringMotion.Get(primary,Parts(primary).Magnetic,TranslateTransform.XProperty,SpringPreset.Magnetic,0);
                    for(int i=0;i<100;i++){ MagneticMotion.For(primary).UpdateTarget(new Point(i%2==0 ? primary.ActualWidth : 0, primary.ActualHeight/2)); Pump(16); }
                    process.Refresh(); File.WriteAllText(Path.Combine(output,"performance.txt"),idle+Environment.NewLine+"active_duration_ms="+clock.ElapsedMilliseconds+"; cpu_ms="+(process.TotalProcessorTime-cpu).TotalMilliseconds+"; gen0="+(GC.CollectionCount(0)-before)+Environment.NewLine+"Same reused binding="+ReferenceEquals(binding,SpringMotion.Get(primary,Parts(primary).Magnetic,TranslateTransform.XProperty,SpringPreset.Magnetic,0)));
                    MagneticMotion.For(primary).Leave(); Pump(1000); Check(SpringMotion.ActiveCount==0 && !SpringMotion.RenderingSubscribed,"Active workload failed to settle");
                });
                Test("V1 continuous native WPF preview frames (Classic / Fluent / Reduced)", () => {
                    foreach(var theme in new[]{"classic","fluent"}) {
                        Ui.ApplyTheme(theme); toggle.IsChecked=false; UiMotion.SettleAll();
                        Series(theme+"-button",()=>Mouse(primary,true),()=>Mouse(primary,false),panel);
                        Series(theme+"-toggle",()=>toggle.IsChecked=true,()=>toggle.IsChecked=false,panel);
                        Series(theme+"-drawer",()=>Ui.Enter(entry,true),null,panel);
                        Series(theme+"-page",()=>Ui.Enter(entry),null,panel);
                        combo.IsDropDownOpen=true; Pump(45); var popup=(Popup)combo.Template.FindName("PART_Popup",combo); combo.IsDropDownOpen=false;
                        Series(theme+"-dropdown",()=>combo.IsDropDownOpen=true,null,(FrameworkElement)popup.Child); combo.IsDropDownOpen=false;
                    }
                    Ui.ReducedMotion=true; Ui.Enter(entry,true); Mouse(primary,true); toggle.IsChecked=true; Capture("reduced-motion",panel); Check(SpringMotion.ActiveCount==0,"Reduced preview active");
                });
            } finally { UiMotion.SettleAll(); window.Close(); app.Shutdown(); File.WriteAllLines(Path.Combine(output,"results.tsv"),results); }
            Console.WriteLine("MOTION: "+(results.Count-failures)+" PASS / "+failures+" FAIL"); return failures==0?0:1;
        }
    }
}
