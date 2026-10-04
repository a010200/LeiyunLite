using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using RazerBatteryTray.Macros;
using Path = System.IO.Path;

namespace RazerBatteryTray.Desktop
{
    // Safe preview only: no TrayController, hooks, HID, SendInput, online updates or user writes.
    internal static class TrayDrawerUiTests
    {
        private static readonly List<string> results = new List<string>();
        private static string artifacts, sourceArtifacts;
        private static string[] only;
        private static int failed;
        private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
        private static void Call(object target, string name, params object[] args) { target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); Pump(); }
        private static void Test(string name, Action action)
        {
            if (only != null && !only.Any(part => name.Contains(part))) return;
            try { action(); results.Add("PASS\t" + name); }
            catch (Exception ex) { failed++; results.Add("FAIL\t" + name + "\t" + ex); }
            Console.WriteLine(results.Last());
        }
        private static void Pump(int ms = 35)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var item in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
        }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        private static MouseButtonEventArgs MouseDown(UIElement source)
        {
            var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            source.RaiseEvent(e); Pump(); return e;
        }
        private static KeyEventArgs KeyDown(Window window, UIElement source, Key key)
        {
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = UIElement.PreviewKeyDownEvent };
            source.RaiseEvent(e);
            if (!e.Handled) { e.RoutedEvent = UIElement.KeyDownEvent; source.RaiseEvent(e); }
            Pump(); return e;
        }
        private static void Capture(Window window, string filename)
        {
            window.UpdateLayout(); var visual = (FrameworkElement)window.Content;
            var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(artifacts, filename + ".png"))) encoder.Save(stream);
        }
        private static string IconData(DependencyObject root) { return Find<System.Windows.Shapes.Path>(root).Single().Data.ToString(CultureInfo.InvariantCulture); }
        private static string State(ShellWindow window)
        {
            Func<MacroLibrary, string> dump = library => library.BindingsEnabled + ":" + string.Join("|", library.Macros.Select(m => m.Id + ":" + m.Name + ":" + string.Join(",", m.Steps.Select(s => s.Kind + ":" + s.Number + ":" + s.KeyCode))))
                + ":" + string.Join("|", library.Bindings.Select(b => b.Id + ":" + b.Enabled + ":" + b.Trigger + ":" + b.MacroId));
            return dump(window.Draft) + "\n" + dump(window.ActiveMacros);
        }
        private static void Closed(ShellWindow window)
        {
            Check(!window.DrawerOpen && Field<Grid>(window, "overlay").Children.Count == 0, "Drawer surface still present");
            Check(Field<Border>(window, "activeDrawer") == null && Field<Grid>(window, "activeDrawerHost") == null && Field<Button>(window, "drawerCollapseButton") == null, "Active references were not cleared");
        }
        private static void Dismiss(ShellWindow window, int method)
        {
            if (method == 0) Click(Field<Button>(window, "drawerCollapseButton"));
            else if (method == 1) Check(MouseDown(Field<Grid>(window, "overlay")).Handled, "Outside event should be consumed");
            else KeyDown(window, Field<Button>(window, "drawerCollapseButton"), Key.Escape);
            Closed(window);
        }
        private static void DrawerLayout(ShellWindow window, double preferred)
        {
            var overlay = Field<Grid>(window, "overlay"); var drawer = Field<Border>(window, "activeDrawer");
            var host = Field<Grid>(window, "activeDrawerHost"); var handle = Field<Button>(window, "drawerCollapseButton");
            Check(Math.Abs(drawer.ActualWidth - Math.Min(preferred, overlay.ActualWidth)) < 1, "Drawer content width changed");
            Check(Math.Abs(host.ActualWidth - drawer.ActualWidth - 14) < 1 && handle.ActualWidth == 30 && handle.ActualHeight == 50, "Handle/host dimensions");
            var hp = handle.TranslatePoint(new Point(), host); var dp = drawer.TranslatePoint(new Point(), host);
            Check(Math.Abs(dp.X - hp.X - 14) < 1 && Math.Abs(hp.Y + 25 - host.ActualHeight / 2) < 1, "Handle not at left boundary / vertical midpoint");
            Check(!Find<Button>(drawer).Any(b => (b.Content as string) == "×"), "Top X remains");
            Check(IconData(handle) == IconData(UiIcons.ChevronRight()), "Collapse icon direction");
            Check(ReferenceEquals(Find<System.Windows.Shapes.Path>(handle).Single().Fill, handle.Foreground), "Handle foreground binding");
            var chrome = (Border)handle.Template.FindName("HandleChrome", handle);
            Check(chrome.CornerRadius == new CornerRadius(6, 0, 0, 6) && chrome.BorderThickness == new Thickness(1, 1, 0, 1), "Handle corner/border");
            Check(ReferenceEquals(overlay.Background, Ui.Brush("Overlay")), "Overlay brush changed");
        }
        [STAThread] private static int Main(string[] args)
        {
            artifacts = args[0]; sourceArtifacts = args[1];
            only = args.Length > 2 && args[2] != "" ? args[2].Split('|') : null;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; ShellWindow window = null;
            app.DispatcherUnhandledException += (s, e) => { failed++; results.Add("FAIL\tDispatcher\t" + e.Exception); e.Handled = true; };
            try
            {
                DesktopApp.LoadTheme(app); Ui.ReducedMotion = true;
                window = new ShellWindow(true) { ShowInTaskbar = false };
                window.Preferences.Language = "zh"; window.Preferences.ReducedMotion = true; window.RebuildPages(1); window.Show();
                window.InitializeRuntime().GetAwaiter().GetResult(); Pump(); window.Activate();
                window.Draft.Macros.Add(new MacroDefinition { Id = "drawer-safe", Name = "Drawer 安全预览", Steps = new List<MacroStep> { new MacroStep { Kind = ActionKind.Delay, Number = 10 } } });
                var page = Field<MacroPage>(window, "macroPage"); page.ReloadLibrary("drawer-safe"); Check(page.SaveDraft(), "Seed save failed");
                Check(window.SaveBinding(new MacroBinding { Id = "drawer-side", MacroId = "drawer-safe", Trigger = TriggerKind.X1, Enabled = true, Mode = RunMode.WhileHeld }, false), "Seed binding failed");
                Test("Six icons exactly match fixed-commit SVG / MIT attribution retained", () => {
                    string[] names = { "Home", "Pause", "Play", "Stop", "Exit", "ChevronRight" };
                    string[] files = { "home", "pause", "play", "stop", "arrow_exit", "chevron_right" };
                    for (int i = 0; i < names.Length; i++) {
                        var icon = (Viewbox)typeof(UiIcons).GetMethod(names[i], BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                        var svg = new XmlDocument(); svg.Load(Path.Combine(sourceArtifacts, files[i] + ".svg"));
                        var paths = svg.GetElementsByTagName("path"); Check(paths.Count == 1, "Path count changed");
                        Check(IconData(icon) == Geometry.Parse(((XmlElement)paths[0]).GetAttribute("d")).ToString(CultureInfo.InvariantCulture), "Upstream geometry mismatch " + names[i]);
                        Check(icon.Width == 20 && icon.Height == 20 && !icon.IsHitTestVisible, "Icon sizing/hit test");
                    }
                    Check(File.ReadAllText("Desktop/UiIcons.cs").Contains("a563cf9166f4f91aa617557ed272612b7f0a2f72") && File.ReadAllText("ThirdParty/FluentSystemIcons/LICENSE").Contains("MIT License"), "Attribution missing");
                    string traySource = File.ReadAllText("Desktop/TrayController.cs"); foreach (string glyph in new[] { "⌂", "⌘", "⚙", "▷", "Ⅱ", "□", "×" }) Check(!traySource.Contains("\"" + glyph + "\""), "Old tray glyph " + glyph);
                });
                foreach (string theme in new[] { "classic", "fluent" }) {
                    string t = theme;
                    Test(t + " tray icons / pause-play switch / foreground / alignment", () => {
                        Ui.ApplyTheme(t);
                        foreach (bool paused in new[] { false, true }) {
                            var flyout = new TrayFlyout(window, true, paused, b => { });
                            try {
                                flyout.Show(); Pump(); var buttons = Field<List<Button>>(flyout, "options"); Check(buttons.Count == 6 && flyout.Width == 256, "Tray structure changed");
                                var expected = new[] { UiIcons.Home(), UiIcons.Macros(), UiIcons.Settings(), paused ? UiIcons.Play() : UiIcons.Pause(), UiIcons.Stop(), UiIcons.Exit() };
                                double labelX = -1;
                                for (int i = 0; i < 6; i++) {
                                    Check(IconData(buttons[i]) == IconData(expected[i]), "Tray semantic mapping " + i);
                                    var icon = Find<Viewbox>(buttons[i]).Single(); Check(icon.ActualWidth == 20 && icon.ActualHeight == 20 && ((Grid)icon.Parent).ActualWidth == 29, "Tray icon column");
                                    Check(ReferenceEquals(Find<System.Windows.Shapes.Path>(buttons[i]).Single().Fill, buttons[i].Foreground), "Tray foreground binding");
                                    var text = Find<TextBlock>((DependencyObject)buttons[i].Content).Single(); double x = text.TranslatePoint(new Point(), buttons[i]).X;
                                    if (labelX < 0) labelX = x; Check(Math.Abs(x - labelX) < .1, "Tray label shifted");
                                    Check(Math.Abs(icon.TranslatePoint(new Point(0, 10), buttons[i]).Y - buttons[i].ActualHeight / 2) < 1, "Tray vertical alignment");
                                }
                                buttons[0].Foreground = Brushes.Magenta; Pump(); Check(ReferenceEquals(Find<System.Windows.Shapes.Path>(buttons[0]).Single().Fill, buttons[0].Foreground), "Live foreground update failed"); buttons[0].ClearValue(Control.ForegroundProperty); Pump();
                                Check((string)buttons[4].ToolTip == "Ctrl + Shift + F12", "Stop shortcut tooltip lost");
                                Capture(flyout, "tray-" + t + (paused ? "-paused" : ""));
                            } finally { flyout.Close(); }
                        }
                    });
                    foreach (int width in new[] { 1180, 960, 700 }) {
                        int w = width;
                        Test(t + " " + w + " rotation / advanced / mouse drawer visuals and all close routes", () => {
                            window.Width = w; window.Height = w == 1180 ? 840 : w == 960 ? 760 : 650; Pump();
                            Action[] open = {
                                () => { window.Navigate(0); Call(Field<DevicePage>(window, "devicePage"), "OpenCalibration"); },
                                () => { window.Navigate(1); Call(page, "OpenAdvancedBindings"); },
                                () => { window.Navigate(1); Call(page, "OpenMouseBinding", TriggerKind.X1, null); }
                            };
                            string[] names = { "rotation", "advanced-binding", "mouse-binding" };
                            for (int i = 0; i < open.Length; i++) {
                                string state = State(window);
                                for (int close = 0; close < 3; close++) {
                                    open[i](); Pump();
                                    double expected = i == 1 ? w >= 1100 ? 580 : w >= 820 ? 520 : window.ActualWidth - ResponsiveLayout.NavigationWidth(window.Layout) - 24 : 390;
                                    DrawerLayout(window, expected);
                                    if (close == 0) Capture(window, "drawer-" + names[i] + "-" + t + (w == 1180 ? "" : "-" + w));
                                    Dismiss(window, close); Check(State(window) == state, "Cancellation changed macros/bindings");
                                }
                            }
                        });
                    }
                }
                Test("Tray Up / Down / Enter / Esc / hover / action closes flyout", () => {
                    window.Width = 1180; window.Height = 840; Pump(); bool paused = false;
                    var flyout = new TrayFlyout(window, true, false, b => paused = b); bool closed = false; flyout.Closed += (s, e) => closed = true;
                    try {
                        flyout.ShowAt(new System.Drawing.Point(900, 800)); Pump(); var buttons = Field<List<Button>>(flyout, "options");
                        Check(buttons[0].IsKeyboardFocused, "Initial menu focus"); KeyDown(flyout, buttons[0], Key.Down); Check(buttons[1].IsKeyboardFocused, "Down focus");
                        KeyDown(flyout, buttons[1], Key.Up); Check(buttons[0].IsKeyboardFocused, "Up focus");
                        buttons[3].RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent }); Check(ReferenceEquals(buttons[3].Background, Ui.Brush("#292929")), "Hover changed");
                        buttons[3].RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                        buttons[3].Focus(); KeyDown(flyout, buttons[3], Key.Enter); Check(paused && closed, "Pause / Enter / close");
                    } finally { if (!closed) flyout.Close(); }
                    var resume = new TrayFlyout(window, true, true, b => paused = b); resume.Show(); Pump(); Click(Field<List<Button>>(resume, "options")[3]); Check(!paused, "Resume callback");
                    var esc = new TrayFlyout(window, true, false, b => { }); closed = false; esc.Closed += (s, e) => closed = true; esc.Show(); Pump(); KeyDown(esc, esc, Key.Escape); Check(closed, "Tray Esc");
                    var stop = new TrayFlyout(window, true, false, b => { }); closed = false; stop.Closed += (s, e) => closed = true; stop.Show(); Pump(); Click(Field<List<Button>>(stop, "options")[4]); Check(closed, "Stop did not close menu");
                });
                Test("Tray deactivation closes / navigation and exit callbacks preserved", () => {
                    var flyout = new TrayFlyout(window, true, false, b => { }); bool closed = false; flyout.Closed += (s, e) => closed = true;
                    try {
                        flyout.Show(); Pump();
                        // Foreground activation is not guaranteed for unattended test desktops.
                        // Exercise the existing Window.Deactivated handler deterministically.
                        flyout.GetType().GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(flyout, true);
                        var activeKey = (DependencyPropertyKey)typeof(Window).GetField("IsActivePropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        flyout.SetValue(activeKey, false);
                        typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(flyout, new object[] { EventArgs.Empty });
                        Pump(100); Check(closed && flyout.DismissedOnDeactivate, "Deactivated handler failed to close");
                    } finally { if (!closed) flyout.Close(); }
                    var navigation = new TrayFlyout(window, true, false, b => { }); navigation.Show(); Pump(); Click(Field<List<Button>>(navigation, "options")[0]); Check(Field<int>(window, "currentPage") == 0, "Main page callback");
                    var exitShell = new ShellWindow(true) { ShowInTaskbar = false }; exitShell.Show(); Pump(); bool exited = false; exitShell.Closed += (s, e) => exited = true;
                    try { var exitMenu = new TrayFlyout(exitShell, true, false, b => { }); exitMenu.Show(); Pump(); Click(Field<List<Button>>(exitMenu, "options")[5]); Check(exited, "Exit callback"); } finally { if (!exited) exitShell.ClosePreview(); }
                });
                Test("Drawer internal text / input / scrollbar / button / popup never cancel", () => {
                    int clicks = 0; var text = Ui.Text("Internal text"); var input = Ui.Input("draft"); var combo = Ui.Combo(new[] { "One", "Two" }); var action = Ui.Button("Local action", () => clicks++);
                    window.OpenDrawer("Internal controls", Ui.Stack(text, input, combo, action, new Border { Height = 1200 })); Pump();
                    foreach (UIElement control in new UIElement[] { text, input, combo, action, Find<ScrollBar>(Field<Border>(window, "activeDrawer")).First() }) {
                        var e = MouseDown(control); Check(window.DrawerOpen && !e.Handled, "Internal click cancelled drawer: " + control.GetType().Name);
                    }
                    Click(action); Check(clicks == 1 && window.DrawerOpen, "Inner action misrouted");
                    combo.IsDropDownOpen = true; Pump(); var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
                    var row = Find<ComboBoxItem>(popup.Child).First(); MouseDown(row); Check(window.DrawerOpen, "Popup row cancelled drawer");
                    combo.IsDropDownOpen = false; window.CloseDrawer(); Closed(window);
                });
                Test("Esc respects child consumption / ComboBox / DPI cancel / recording", () => {
                    var editor = Ui.Input("draft"); editor.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) e.Handled = true; };
                    var combo = Ui.Combo(new[] { "One", "Two" }); window.OpenDrawer("Esc priorities", Ui.Stack(editor, combo)); Pump();
                    KeyDown(window, editor, Key.Escape); Check(window.DrawerOpen, "Child Escape stolen");
                    combo.IsDropDownOpen = true; Pump(); KeyDown(window, combo, Key.Escape); Check(window.DrawerOpen && !combo.IsDropDownOpen, "ComboBox Escape stolen");
                    var dpiInput = Field<TextBox>(Field<DevicePage>(window, "devicePage"), "dpiValue"); KeyDown(window, dpiInput, Key.Escape); Check(window.DrawerOpen, "DPI Escape stolen");
                    var recordingTimer = Field<DispatcherTimer>(page, "recordingTimer"); recordingTimer.Stop();
                    page.GetType().GetField("recording", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(page, true);
                    try { KeyDown(window, Field<Button>(window, "drawerCollapseButton"), Key.Escape); Check(window.DrawerOpen, "Recording Escape stolen"); }
                    finally { page.GetType().GetField("recording", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(page, false); recordingTimer.Start(); }
                    KeyDown(window, Field<Button>(window, "drawerCollapseButton"), Key.Escape); Closed(window);
                });
                Test("Pending action edit cancels without applying / original focus survives replacement", () => {
                    window.Navigate(1); window.Width = 760; Pump(); var selected = page.Selected; int before = selected.Steps[0].Number;
                    Field<ListBox>(page, "steps").SelectedIndex = 0; Call(page, "EditStep", true); Check(window.DrawerOpen, "Compact edit drawer absent");
                    Find<TextBox>(Field<Border>(window, "activeDrawer")).First().Text = "99"; Check(page.HasPendingEdit, "Pending edit missing");
                    Dismiss(window, 1); Check(!page.HasPendingEdit && selected.Steps[0].Number == before, "Cancel applied pending action");
                    window.Activate(); var original = Field<Button[]>(window, "navigation")[1]; original.Focus(); Pump();
                    window.OpenDrawer("First", Ui.Input()); Pump(); window.OpenDrawer("Nested", Ui.Input()); Pump(); Dismiss(window, 0);
                    Check(original.IsKeyboardFocused && Field<FrameworkElement>(window, "previousFocus") == null, "Original focus not restored");
                });
                Test("All remaining Drawer callers cancel without delete/save/disable/record/run/exit", () => {
                    window.Width = 1180; Pump(); window.Navigate(1); Call(page, "SelectTab", false);
                    Action[] open = {
                        () => Call(page, "DeleteMacro"), () => Call(page, "Preview"), () => Call(page, "EditBinding", false),
                        () => Call(page, "OpenRecording"), () => Call(page, "Rename"),
                        () => Click(Find<Button>(page).First(b => (b.Content as string) == "更多 ···")),
                        () => { window.Width = 760; Pump(); Click(Field<Button>(page, "compactPaletteButton")); },
                        () => { window.DraftDirty = true; Call(page, "SelectTab", true); },
                        () => { window.DraftDirty = true; window.RequestExit(); Pump(); }
                    };
                    foreach (var action in open) {
                        window.Width = 1180; Pump(); string before = State(window); action(); Check(window.DrawerOpen, "Caller did not open drawer");
                        Dismiss(window, 1); Check(State(window) == before && window.IsVisible && !page.IsRecording && window.Macros == null, "Cancellation performed a business action");
                        window.DraftDirty = false;
                    }
                });
                Test("Handle hover / pressed template and existing entrance share transform and opacity", () => {
                    window.DraftDirty = false;
                    foreach (string theme in new[] { "classic", "fluent" }) {
                        Ui.ApplyTheme(theme); window.OpenDrawer("Handle states", Ui.Text("Safe preview")); Pump(); var handle = Field<Button>(window, "drawerCollapseButton");
                        var drawer = Field<Border>(window, "activeDrawer"); Check(ReferenceEquals(handle.RenderTransform, drawer.RenderTransform) && handle.Opacity == drawer.Opacity, "Entrance desynchronized");
                        Check(handle.Template.Triggers.OfType<Trigger>().Any(t => t.Property == ButtonBase.IsPressedProperty), "Pressed feedback absent");
                        var hover = handle.Template.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
                        Check(ReferenceEquals(((Setter)hover.Setters[0]).Value, Ui.Brush("ControlHoverBackground")), "Hover theme token");
                        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        handle.SetValue(hoverKey, true);
                        Check((bool)handle.GetValue(UIElement.IsMouseOverProperty) && ((SolidColorBrush)((Border)handle.Template.FindName("HandleChrome", handle)).Background).Color == ((SolidColorBrush)Ui.Brush("ControlHoverBackground")).Color, "Hover trigger did not apply");
                        Capture(window, "drawer-handle-hover-" + theme);
                        handle.SetValue(hoverKey, false);
                        window.CloseDrawer();
                    }
                });
            }
            catch (Exception ex) { failed++; results.Add("FAIL\tHarness\t" + ex); Console.WriteLine(ex); }
            finally {
                if (window != null) window.ClosePreview();
                File.WriteAllLines(Path.Combine(artifacts, "tray-drawer-results.tsv"), results);
                Console.WriteLine("TRAY/DRAWER UI: " + results.Count(r => r.StartsWith("PASS")) + " PASS / " + failed + " FAIL"); app.Shutdown();
            }
            return failed == 0 ? 0 : 1;
        }
    }
}
