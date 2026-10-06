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
using System.Windows.Threading;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    // Uses only Demo windows and memory fixtures; never initializes native macro input.
    internal static class FirstStageUiTests
    {
        private static readonly List<string> results = new List<string>();
        private static int passed, failed;
        private static string artifacts;
        [StructLayout(LayoutKind.Sequential)] private struct TestMonitorInfo { internal int Size; internal ShellWindow.NativeRect Monitor,Work; internal int Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,int flags);
        [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor,ref TestMonitorInfo info);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out ShellWindow.NativeRect bounds);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private static ShellWindow.NativeRect RectPixels(int l,int t,int r,int b)
        { return new ShellWindow.NativeRect {Left=l,Top=t,Right=r,Bottom=b}; }
        private static void WorkAreaGeometry()
        {
            var cases=new[]{
                new[]{0,0,1920,1080,0,0,1920,1040,0,0,1920,1040},
                new[]{0,0,1920,1080,0,40,1920,1080,0,40,1920,1040},
                new[]{0,0,1920,1080,40,0,1920,1080,40,0,1880,1080},
                new[]{0,0,1920,1080,0,0,1880,1080,0,0,1880,1080},
                new[]{-1920,-100,0,980,-1880,-100,0,940,40,0,1880,1040}
            };
            foreach(var c in cases) {
                var limits=new ShellWindow.MinMaxInfo {MinTrackSize=new ShellWindow.NativePoint {X=700,Y=560}};
                Check(ShellWindow.ApplyMonitorWorkArea(ref limits,RectPixels(c[0],c[1],c[2],c[3]),RectPixels(c[4],c[5],c[6],c[7])),"Valid geometry");
                Check(limits.MaxPosition.X==c[8] && limits.MaxPosition.Y==c[9] && limits.MaxSize.X==c[10] && limits.MaxSize.Y==c[11] && limits.MaxTrackSize.X==c[10] && limits.MaxTrackSize.Y==c[11] && limits.MinTrackSize.X==700 && limits.MinTrackSize.Y==560,"Physical, monitor-relative limits preserve minimum");
            }
        }
        private static void MaximizeWorkArea(ShellWindow window)
        {
            IntPtr hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var info=new TestMonitorInfo {Size=Marshal.SizeOf(typeof(TestMonitorInfo))};
            Check(GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info),"Current monitor work area");
            var max=Field<Button>(window,"maximizeButton");
            window.ShowActivated=true; window.ShowInTaskbar=true; window.Title="雷云 Lite · 最大化隔离预览"; window.Activate(); Pump();
            hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
            try {
            for(int i=0;i<2;i++) {
                max.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(150);
                var bounds=new ShellWindow.NativeRect(); Check(window.WindowState==WindowState.Maximized && GetWindowRect(hwnd,out bounds),"Native maximize");
                Check(bounds.Left>=info.Work.Left-1 && bounds.Top>=info.Work.Top-1 && bounds.Right<=info.Work.Right+1 && bounds.Bottom<=info.Work.Bottom+1,"Maximized physical window extends into taskbar: "+bounds.Left+","+bounds.Top+","+bounds.Right+","+bounds.Bottom);
                foreach(var button in Field<Button[]>(window,"navigation").Skip(2)) {
                    var top=button.PointToScreen(new Point());var bottom=button.PointToScreen(new Point(button.ActualWidth,button.ActualHeight));
                    Check(button.IsVisible && top.Y>=info.Work.Top && bottom.Y<info.Work.Bottom-3,"Settings/Updates and bottom spacing clipped");
                }
                if(i==0) {
                    var wait=System.Diagnostics.Stopwatch.StartNew();
                    if(GetForegroundWindow()!=hwnd) Console.WriteLine("WAIT: click the Leiyun Lite maximized isolated preview within 60 seconds; no input is sent; hwnd="+hwnd+" foreground="+GetForegroundWindow());
                    while(GetForegroundWindow()!=new System.Windows.Interop.WindowInteropHelper(window).Handle && wait.ElapsedMilliseconds<60000) Pump(50);
                    hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    Check(GetForegroundWindow()==hwnd,"Demo must be foreground for taskbar screenshot");
                    int width=info.Monitor.Right-info.Monitor.Left,height=info.Monitor.Bottom-info.Monitor.Top;
                    using(var bitmap=new System.Drawing.Bitmap(width,height)) {
                        using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(info.Monitor.Left,info.Monitor.Top,0,0,new System.Drawing.Size(width,height));
                        bitmap.Save(Path.Combine(artifacts,"maximized-taskbar.png"),System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Capture(window,"maximized-navigation");
                }
                max.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(window.WindowState==WindowState.Normal,"Native restore");
            }
            } finally { window.WindowState=WindowState.Normal; window.ShowActivated=false; window.ShowInTaskbar=false; Pump(); }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
        private static void Call(object o, string name, params object[] args) { o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args); }
        private static void Test(string name, Action action)
        {
            try { action(); passed++; results.Add("PASS\t" + name); }
            catch (Exception e) { failed++; results.Add("FAIL\t" + name + "\t" + e); }
            Console.WriteLine(results.Last());
        }
        private static void Pump(int milliseconds = 60)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static void Capture(Window window, string name)
        {
            window.UpdateLayout();
            var visual = (FrameworkElement)window.Content;
            int width = (int)Math.Round(visual.ActualWidth), height = (int)Math.Round(visual.ActualHeight);
            Check(width > 600 && height > 400, "Empty render dimensions");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(artifacts, name + ".png"))) encoder.Save(stream);
        }
        private static void WithinPage(FrameworkElement page)
        {
            foreach (var button in Find<Button>(page).Where(b => b.IsVisible && b.ActualWidth > 0))
            {
                Point p = button.TranslatePoint(new Point(), page);
                Check(p.X >= -1 && p.X + button.ActualWidth <= page.ActualWidth + 1,
                    "Horizontal clipping: " + button.Content + ", x=" + p.X + ", width=" + button.ActualWidth + ", page=" + page.ActualWidth);
            }
        }
        [STAThread]
        private static int Main(string[] args)
        {
            artifacts = Path.GetFullPath(args[0]); Directory.CreateDirectory(artifacts);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ShellWindow window = null;
            try
            {
                DesktopApp.LoadTheme(app); Ui.ReducedMotion = true;
                window = new ShellWindow(true) { ShowActivated = false, ShowInTaskbar = false };
                window.Preferences.Language = "zh"; window.Preferences.ReducedMotion = true; window.RebuildPages(0);
                window.Show(); window.InitializeRuntime().GetAwaiter().GetResult(); Pump();
                var first = new MacroDefinition { Id = "preview-first", Name = "连点示例 · 仅预览", Steps = new List<MacroStep> {
                    new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.Left, Press = PressMode.Down },
                    new MacroStep { Kind = ActionKind.Delay, Number = 1 },
                    new MacroStep { Kind = ActionKind.Mouse, Mouse = MouseAction.Left, Press = PressMode.Up },
                    new MacroStep { Kind = ActionKind.Delay, Number = 10 } } };
                var second = new MacroDefinition { Id = "preview-second", Name = "键盘示例 · 仅预览", Steps = new List<MacroStep> {
                    new MacroStep { Kind = ActionKind.Keyboard, KeyCode = 65, Press = PressMode.Tap } } };
                window.Draft.Macros.Add(first); window.Draft.Macros.Add(second);
                window.Draft.Bindings.Add(new MacroBinding { MacroId = first.Id, Trigger = TriggerKind.Middle, Mode = RunMode.WhileHeld, SuppressOriginal = true });
                // Bindings are a separately saved library in production; seed both sides in Demo.
                window.GetType().GetField("demoActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, window.Draft.Clone());
                var page = Field<MacroPage>(window, "macroPage"); page.ReloadLibrary(first.Id); window.SaveMacros(); window.Notice("");
                Test("Demo has no macro controller / tray / DPI monitor", () => {
                    Check(window.Demo && window.Macros == null && Field<TrayController>(window, "tray") == null && Field<DpiMonitor>(window, "monitor") == null, "Preview services must be isolated");
                });
                Test("Legacy themes and XML safety / no read-side overwrite", () => {
                    string dir = Path.Combine(artifacts, "isolated-settings"); Directory.CreateDirectory(dir);
                    var store = new DesktopSettings(dir);
                    foreach (string theme in new[] { "dark", "light", "classic", "fluent" }) {
                        store.Save(new DesktopPreferences { Theme = theme }); byte[] before = File.ReadAllBytes(Path.Combine(dir, "desktop.xml"));
                        Check(store.Load().Theme == theme && before.SequenceEqual(File.ReadAllBytes(Path.Combine(dir, "desktop.xml"))), "Theme XML migration wrote or lost value");
                    }
                    string xml = "<!DOCTYPE DesktopPreferences [<!ENTITY x SYSTEM 'file:///unreachable'>]><DesktopPreferences><Theme>&x;</Theme></DesktopPreferences>";
                    File.WriteAllText(Path.Combine(dir, "desktop.xml"), xml);
                    Check(store.Load().Theme == "dark" && File.ReadAllText(Path.Combine(dir, "desktop.xml")) == xml, "DTD not rejected or file overwritten");
                    Check(ThemeTokens.Normalize("light") == "fluent" && ThemeTokens.Normalize("dark") == "classic" && ThemeTokens.Normalize(null) == "classic" && ThemeTokens.Normalize("unknown") == "classic", "Legacy theme mapping");
                });
                Test("Theme selector preserves pages / selection / tab / draft / active bindings", () => {
                    var views = Field<FrameworkElement[]>(window, "views").ToArray(); var draft = window.Draft;
                    page.ReloadLibrary(second.Id); Call(page, "SelectTab", true); window.DraftDirty = true;
                    window.Navigate(2); Pump();
                    var combo = Find<ComboBox>(views[2]).First(c => c.Name == "ThemeSelector"); Brush shared = Ui.Foreground;
                    foreach (int index in new[] { 1, 0, 1, 0 }) {
                        combo.SelectedIndex = index; Pump();
                        Check(window.Preferences.Theme == (index == 1 ? "fluent" : "classic"), "Theme preference");
                        Check(ReferenceEquals(shared, Ui.Foreground) && ((SolidColorBrush)shared).Color == ThemeTokens.ColorFor("TextPrimary", index == 1 ? "fluent" : "classic"), "Brush must change in place");
                        Check(views.SequenceEqual(Field<FrameworkElement[]>(window, "views")) && ReferenceEquals(page, Field<MacroPage>(window, "macroPage")), "Pages rebuilt");
                        Check(ReferenceEquals(draft, window.Draft) && window.DraftDirty && page.Selected.Id == second.Id && page.IsBindingsTab && Field<int>(window, "currentPage") == 2, "UI state lost");
                        Check(window.Draft.Bindings.Count == 1 && window.ActiveMacros.Bindings.Count == 1 && first.Steps[1].Number == 1, "Binding/macro changed");
                    }
                    window.DraftDirty = false; window.Notice("");
                });
                Test("Reduced motion settles opacity / transforms / color immediately", () => {
                    Ui.ReducedMotion = false;
                    var element = Ui.Text("motion"); var move = new TranslateTransform(); var scale = new ScaleTransform();
                    var color = new SolidColorBrush(Colors.Red);
                    UiMotion.Fade(element); UiMotion.To(move, TranslateTransform.XProperty, 0, UiMotion.Slow, 8);
                    UiMotion.Press(scale, false); UiMotion.ColorTo(color, Colors.Red, Colors.Blue); Ui.ReducedMotion = true;
                    Check(element.Opacity == 1 && !element.HasAnimatedProperties && move.X == 0 && !move.HasAnimatedProperties && scale.ScaleX == 1 && !scale.HasAnimatedProperties && color.Color == Colors.Blue && !color.HasAnimatedProperties, "Motion clocks remain");
                    bool completed = false; Ui.ReducedMotion = false; UiMotion.FadeOut(element, () => completed = true); Ui.ReducedMotion = true;
                    Check(completed && element.Opacity == 0 && !element.HasAnimatedProperties, "Reduced motion must complete a closing fade");
                    var button = Ui.Button("Stop interrupted press", () => { }); var pressed = UiMotion.Transform(UiMotion.ButtonVisual(button)).Press;
                    Ui.ReducedMotion = false; UiMotion.Press(button, true); UiMotion.Stop(button); Ui.ReducedMotion = true;
                    Check(pressed.ScaleX == 1 && pressed.ScaleY == 1 && !pressed.HasAnimatedProperties, "Stopped press destination returned after reducing motion");
                });
                Test("InfoTip / WarningTip and Snackbar", () => {
                    var info = Ui.InfoTip("Info", "Explanation"); var warning = Ui.WarningTip("Risk", "Explanation");
                    Check(info.ToolTip is ToolTip && warning.ToolTip is ToolTip && ToolTipService.GetInitialShowDelay(info) == 350, "Tips not configured");
                    var host = new Window { Width = 300, Height = 160, ShowActivated = false, ShowInTaskbar = false, Content = Ui.Row(info, warning) };
                    host.Show(); Pump();
                    try {
                        var session = (UiFeedback.TipSession)info.Tag; Ui.ReducedMotion = false; session.Open(); Pump(30);
                        Check(((ToolTip)info.ToolTip).IsOpen, "Tip popup did not open"); session.Close(); Ui.ReducedMotion = true;
                        Check(!((ToolTip)info.ToolTip).IsOpen, "Reduced motion left a closing popup open");
                        Ui.ReducedMotion = false; session.Open(); Pump(30); session.Close(); Pump(UiMotion.Fast + 50);
                        Check(!((ToolTip)info.ToolTip).IsOpen, "Closing tip did not settle");
                    } finally { host.Close(); Ui.ReducedMotion = true; }
                    var snackbar = Field<UiSnackbar>(window, "toast"); window.Notice("Preview message");
                    Check(snackbar.Visibility == Visibility.Visible, "Snackbar not shown"); Pump(3100);
                    Check(snackbar.Visibility == Visibility.Collapsed, "Snackbar not dismissed");
                });
                Test("DIP breakpoint boundaries and minimum size", () => {
                    Check(ResponsiveLayout.ForWidth(1100) == LayoutMode.Wide && ResponsiveLayout.ForWidth(1099) == LayoutMode.Medium && ResponsiveLayout.ForWidth(820) == LayoutMode.Medium && ResponsiveLayout.ForWidth(819) == LayoutMode.Compact && ResponsiveLayout.ForWidth(double.NaN) == LayoutMode.Compact && ResponsiveLayout.ForWidth(double.PositiveInfinity) == LayoutMode.Compact, "Breakpoints");
                    Check(window.MinWidth == 700 && window.MinHeight == 560, "Minimum bounds");
                });
                Test("Caption coordinate conversion / maximize / restore", () => {
                    var max = Field<Button>(window, "maximizeButton"); var center = max.PointToScreen(new Point(max.ActualWidth / 2, max.ActualHeight / 2));
                    Check(window.InMaximizeButton(center) && !window.InMaximizeButton(max.PointToScreen(new Point(-10, -10))), "Caption DIP hit bounds");
                    long packed = ((long)unchecked((ushort)(short)-40) << 16) | unchecked((ushort)(short)-1920);
                    Check(ShellWindow.ScreenPoint(new IntPtr(packed)) == new Point(-1920, -40), "Signed screen coordinates");
                    max.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Check(window.WindowState == WindowState.Maximized, "Maximize");
                    max.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Check(window.WindowState == WindowState.Normal, "Restore");
                });
                Test("Work-area physical geometry: four taskbar edges and negative secondary monitor",WorkAreaGeometry);
                Test("Native current-monitor maximize, full bottom navigation, taskbar screenshot and twice restore",()=>MaximizeWorkArea(window));
                string[] modes = { "wide", "medium", "compact", "minimum" };
                // Stage C now guards dirty-to-bindings navigation; keep this layout fixture saved.
                Check(window.SaveMacros(), "Layout fixture could not save");
                int[] widths = { 1180, 960, 760, 700 }, heights = { 840, 760, 650, 560 };
                string[] names = { "device", "macro", "settings", "update" };
                foreach (string theme in new[] { "classic", "fluent" })
                for (int size = 0; size < modes.Length; size++)
                {
                    int i = size;
                    Find<ComboBox>(Field<FrameworkElement[]>(window, "views")[2]).First(c => c.Name == "ThemeSelector").SelectedIndex = theme == "fluent" ? 1 : 0;
                    window.Width = widths[i]; window.Height = heights[i]; Pump();
                    for (int view = 0; view < 4; view++)
                    {
                        int v = view; string name = theme + "-" + modes[i] + "-" + names[v];
                        Test(name + " layout and WPF render", () => {
                            window.Navigate(v); if (v == 1) { Call(page, "SelectTab", false); page.ReloadLibrary(first.Id); }
                            window.Notice(""); Pump(); WithinPage(Field<FrameworkElement[]>(window, "views")[v]);
                            Check(window.Layout == ResponsiveLayout.ForWidth(widths[i]), "Layout mode mismatch");
                            Capture(window, name);
                        });
                    }
                    Test(theme + "-" + modes[i] + "-bindings controls and WPF render", () => {
                        window.Navigate(1); Call(page, "SelectTab", true); Pump(); WithinPage(page);
                        var toggle = Field<CheckBox>(page, "bindingSwitch"); var advanced = Find<Button>(page).First(b => b.Name == "AdvancedBindings");
                        Check(!new Rect(toggle.TranslatePoint(new Point(), page), toggle.RenderSize).IntersectsWith(new Rect(advanced.TranslatePoint(new Point(), page), advanced.RenderSize)), "Binding toggle overlaps advanced button");
                        Capture(window, theme + "-" + modes[i] + "-bindings");
                    });
                }
                Test("Pending editor survives Wide to Compact resize without applying", () => {
                    Ui.ApplyTheme("fluent"); window.Width = 1180; window.Height = 840; window.Navigate(1); Call(page, "SelectTab", false); page.ReloadLibrary(first.Id); Pump();
                    Field<ListBox>(page, "steps").SelectedIndex = 1; Call(page, "EditStep", true); Pump();
                    var editor = Field<StackPanel>(page, "editor"); var input = Find<TextBox>(editor).First(); input.Text = "23";
                    Check(page.HasPendingEdit, "Pending edit not tracked"); window.Width = 760; window.Height = 650; Pump();
                    Check(ReferenceEquals(editor, Field<StackPanel>(page, "editor")) && input.Text == "23" && page.HasPendingEdit && first.Steps[1].Number == 1, "Resize lost or applied edit");
                    Capture(window, "fluent-compact-pending-editor"); Call(page, "CloseStepEditor");
                });
                Test("Compact action palette and property drawer remain accessible", () => {
                    window.Width = 700; window.Height = 560; window.Navigate(1); Call(page, "SelectTab", false); Pump();
                    var add = Field<Button>(page, "compactPaletteButton"); Check(add.IsVisible, "Compact palette entry hidden");
                    int before = first.Steps.Count; add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    var overlay = Field<Grid>(window, "overlay"); Check(window.DrawerOpen, "Palette drawer did not open"); WithinPage(overlay);
                    var action = Find<Button>(overlay).First(b => (b.Content as string ?? "").StartsWith("+  "));
                    action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    Check(window.DrawerOpen && first.Steps.Count == before, "Opening an action editor changed the macro");
                    WithinPage(overlay); Check(Find<Button>(overlay).Any(b => (b.Content as string) == "完成"), "Done action inaccessible");
                    Capture(window, "fluent-minimum-action-drawer"); window.CloseDrawer();
                });
                foreach (string theme in new[] { "classic", "fluent" })
                Test(theme + " shared components render and resources", () => {
                    Ui.ApplyTheme(theme); Ui.ReducedMotion = true;
                    var primary = Ui.PrimaryButton("Primary", () => { }); var secondary = Ui.SecondaryButton("Secondary", () => { });
                    var danger = Ui.DangerButton("Danger", () => { }); var disabled = Ui.PrimaryButton("Disabled", () => { }); disabled.IsEnabled = false;
                    var tabs = Ui.TabStrip(Ui.SecondaryButton("First tab", () => { }), Ui.SecondaryButton("Second tab", () => { }));
                    var snackbar = new UiSnackbar(); snackbar.Show("Snackbar · 3 seconds");
                    var content = Ui.Stack(Ui.SectionTitle("Shared Fluent components / " + theme),
                        Ui.Row(primary, secondary, danger, disabled, Ui.IconButton("＋", "Icon button", () => { })), tabs,
                        Ui.Card(Ui.Stack(Ui.Row(Ui.Text("Info / warning"), Ui.InfoTip("Info", "Short explanation."), Ui.WarningTip("Warning", "Short risk explanation.")),
                            Ui.Toggle("Toggle off", false, b => { }), Ui.Toggle("Toggle on", true, b => { }), Ui.Input("Text input"), Ui.Combo(new[] { "Dropdown", "Other" }))),
                        Ui.Expander("Expander", Ui.Text("Expanded content"), true), Ui.StatusBar("Status bar"), snackbar);
                    content.Margin = new Thickness(24);
                    var host = new Window { Width = 900, Height = 740, Background = Ui.Background, Content = new Border { Background = Ui.Background, Child = content }, ShowInTaskbar = false, ShowActivated = false };
                    host.Show(); Pump();
                    try {
                        Check(primary.Style != null && danger.Style != null && primary.Foreground == Ui.Ink, "Shared button style / contrast");
                        UiMotion.Stop(disabled); Check(disabled.Opacity <= .6, "Motion cleanup overrides disabled styling");
                        tabs.Select(1); Pump(); Capture(host, theme + "-components");
                    } finally { host.Close(); snackbar.Clear(); }
                });
                results.Add("BLOCKED\tWindows 11 Snap hover / Win+Z / top Snap UI: native Windows build " + ShellWindow.NativeWindowsBuild + ", native Snap menu available=" + ShellWindow.SupportsSnapMenu);
            }
            catch (Exception e) { failed++; results.Add("FAIL\tHarness startup / runtime\t" + e); Console.WriteLine(e); }
            finally
            {
                if (window != null) window.ClosePreview();
                File.WriteAllLines(Path.Combine(artifacts, "results.tsv"), results);
                Console.WriteLine("FIRST STAGE UI: " + passed + " PASS / " + failed + " FAIL");
                app.Shutdown();
            }
            return failed == 0 ? 0 : 1;
        }
    }
}
