using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using RazerBatteryTray.Macros;
using Forms = System.Windows.Forms;

namespace RazerBatteryTray.Desktop
{
    internal sealed partial class ShellWindow : Window
    {
        internal readonly bool Demo;
        internal readonly RazerDeviceClient Device = new RazerDeviceClient();
        internal readonly AppSettings LegacySettings;
        internal readonly DesktopPreferences Preferences;
        internal MacroLibrary Draft;
        internal bool DraftDirty;
        internal MouseBatteryInfo Reading = new MouseBatteryInfo();
        internal readonly MacroController Macros;
        private readonly SettingsStore legacyStore = new SettingsStore(throwOnSave: true);
        private readonly DesktopSettings store;
        private readonly AutoStartService autoStart;
        private readonly Grid pages = new Grid(), overlay = new Grid();
        private readonly Button[] navigation = new Button[4];
        private readonly FrameworkElement[] views = new FrameworkElement[4];
        private readonly TextBlock toast = Ui.Text("");
        private DevicePage devicePage;
        private MacroPage macroPage;
        private UpdatePage updatePage;
        private TrayController tray;
        private DpiMonitor monitor;
        private readonly DispatcherTimer refreshTimer = new DispatcherTimer(), toastTimer = new DispatcherTimer();
        private int currentPage = -1;
        private bool refreshing, exiting, disposed, discardApproved, runtimeInitialized;
        private FrameworkElement previousFocus;
        internal bool StartHidden { get; private set; }
        internal bool DrawerOpen { get { return overlay.Visibility == Visibility.Visible; } }
        internal event Action Refreshed;

        internal ShellWindow(bool demo, bool auto = false)
        {
            Demo = demo;
            store = new DesktopSettings(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeiyunLite"));
            Preferences = demo ? new DesktopPreferences() : store.Load();
            LegacySettings = demo ? new AppSettings() : legacyStore.Load();
            string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var installation = RazerBatteryTray.Updates.InstallLayout.Detect(executable);
            autoStart = new AutoStartService(installation == null ? executable : installation.Launcher);
            bool registeredAtSignIn = !demo && autoStart.IsEnabled();
            if (!demo) autoStart.Sync();
            StartHidden = !demo && AutoStartService.ShouldStartHidden(auto, DesktopApp.UpdateHidden,
                LegacySettings.AutoStartShowUI, registeredAtSignIn, AutoStartService.SystemUptimeMilliseconds);
            Updates = new UpdateSession(this);
            ApplyLanguage();
            Ui.ApplyTheme(Preferences.Theme);
            if (!demo)
            {
                Macros = new MacroController(new MacroStore(), new WindowsMacroOutput(), true, DesktopApp.TrialToken != null);
                Macros.StatusChanged += message => Dispatcher.BeginInvoke(new Action(() => { if (!disposed) Notice(message); }));
                Draft = Macros.Snapshot();
            }
            else Draft = new MacroLibrary();
            Title = AppVersion.DisplayName;
            using (var icon = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Lite.png"))
            { if (icon != null) { var bitmap = System.Windows.Media.Imaging.BitmapFrame.Create(icon, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad); bitmap.Freeze(); Icon = bitmap; } }
            Width = 1180; Height = 840; MinWidth = 840; MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Frame; Foreground = Ui.Foreground; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            Build();
            refreshTimer.Interval = TimeSpan.FromMilliseconds(LegacySettings.RefreshInterval);
            refreshTimer.Tick += async (s, e) => await RefreshDevice();
            toastTimer.Interval = TimeSpan.FromSeconds(8);
            toastTimer.Tick += (s, e) => { toast.Text = ""; toastTimer.Stop(); };
            SourceInitialized += (s, e) => {
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) => {
                    if (msg == 0x0219 && !Demo && !disposed) { Device.InvalidateTarget(); Dispatcher.BeginInvoke(new Action(async () => await RefreshDevice())); }
                    return IntPtr.Zero;
                });
            };
            Closing += OnClosing;
            Closed += (s, e) => DisposeServices();
            PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Escape && DrawerOpen) { CloseDrawer(); e.Handled = true; }
                if (e.Key == Key.F12 && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)) { if (Macros != null) Macros.Stop(); e.Handled = true; }
            };
        }
        internal async Task InitializeRuntime()
        {
            if (runtimeInitialized || disposed) return;
            runtimeInitialized = true;
            if (Demo) { await RefreshDevice(); return; }
            if (!await CompleteTrial()) return;
            SetupTray();
            monitor = new DpiMonitor(Device, reading => Dispatcher.BeginInvoke(new Action(() => OnDpi(reading))));
            monitor.Start(); refreshTimer.Start();
            Updates.Start();
            await RefreshDevice();
        }
        internal void AbortStartup()
        {
            exiting = true; discardApproved = true;
            try { Close(); } catch { if (Application.Current != null) Application.Current.Shutdown(); }
        }
        private void ApplyLanguage()
        {
            Ui.English = Preferences.Language == "en" || (Preferences.Language == "system" && !CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            Ui.ReducedMotion = Preferences.ReducedMotion;
        }
        private void Build()
        {
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); root.RowDefinitions.Add(new RowDefinition());
            var caption = new DockPanel { LastChildFill = true, Background = Ui.Frame };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var min = CaptionButton("min", () => WindowState = WindowState.Minimized);
            var max = CaptionButton("max", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            StateChanged += (s, e) => max.Content = CaptionGlyph(WindowState == WindowState.Maximized ? "restore" : "max");
            var close = CaptionButton("close", Close);
            foreach (var b in new[] { min, max, close }) { b.Margin = new Thickness(0); b.Width = 46; b.Padding = new Thickness(0); b.Background = Ui.Frame; b.BorderThickness = new Thickness(0); WindowChrome.SetIsHitTestVisibleInChrome(b, true); actions.Children.Add(b); }
            DockPanel.SetDock(actions, Dock.Right); caption.Children.Add(actions);
            var brandIcon = new Image { Source = Icon, Width = 18, Height = 18, Margin = new Thickness(18, 0, 0, 0) }; DockPanel.SetDock(brandIcon, Dock.Left); caption.Children.Add(brandIcon);
            var title = Ui.Text("雷云lite     /     v" + AppVersion.Number + (Demo ? "  ·  " + Ui.T("安全预览 · 不操作硬件", "Safe preview · no hardware access") : ""), 12, Ui.Muted); title.Margin = new Thickness(10, 13, 0, 0); caption.Children.Add(title);
            root.Children.Add(caption);
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) }); body.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(body, 1); root.Children.Add(body);
            var rail = new DockPanel { Margin = new Thickness(10, 18, 10, 12) };
            var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); rail.Children.Add(bottom);
            var top = new StackPanel(); rail.Children.Add(top);
            var brand = Ui.Text("LEIYUN / LITE", 12, Ui.Muted); brand.Margin = new Thickness(14, 0, 0, 24); top.Children.Add(brand);
            string[] names = { Ui.T("设备", "Device"), Ui.T("宏与绑定", "Macros"), Ui.T("设置", "Settings"), Ui.T("自动更新", "Updates") };
            string[] glyphs = { "◉", "⌘", "⚙", "↓" };
            for (int i = 0; i < 4; i++) { int n = i; var b = Ui.Button(glyphs[i] + "    " + names[i], () => Navigate(n)); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 8); b.Padding = new Thickness(12, 14, 12, 14); b.BorderThickness = new Thickness(0); navigation[i] = b; (i < 2 ? top : bottom).Children.Add(b); }
            body.Children.Add(rail);
            var content = new Grid { Background = Ui.Background }; Grid.SetColumn(content, 1); body.Children.Add(content);
            content.RowDefinitions.Add(new RowDefinition()); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pages.Margin = new Thickness(30, 22, 18, 12); content.Children.Add(pages);
            toast.Margin = new Thickness(30, 0, 30, 12); toast.Foreground = Ui.Foreground; Grid.SetRow(toast, 1); content.Children.Add(toast);
            overlay.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)); overlay.Visibility = Visibility.Collapsed;
            Grid.SetRowSpan(overlay, 2); content.Children.Add(overlay);
            Content = root;
            RebuildPages(0);
        }
        internal void RebuildPages(int destination)
        {
            if (updatePage != null) updatePage.Dispose();
            if (macroPage != null) macroPage.DisposeRecording();
            ApplyLanguage(); pages.Children.Clear();
            devicePage = new DevicePage(this); macroPage = new MacroPage(this);
            views[0] = devicePage; views[1] = macroPage; views[2] = BuildSettings(); views[3] = BuildUpdates();
            foreach (var v in views) { v.Visibility = Visibility.Collapsed; pages.Children.Add(v); }
            string[] names = { "◉    " + Ui.T("设备", "Device"), "⌘    " + Ui.T("宏与绑定", "Macros"), "⚙    " + Ui.T("设置", "Settings"), "↓    " + Ui.T("自动更新", "Updates") };
            for (int i = 0; i < 4; i++) navigation[i].Content = names[i];
            currentPage = -1; Navigate(destination); devicePage.UpdateReading();
        }
        internal void Navigate(int index)
        {
            if (macroPage != null && macroPage.IsRecording) { Notice(Ui.T("请先停止或取消录制。", "Stop or cancel recording first.")); return; }
            if (index == currentPage) return;
            // Do not discard an unfinished drawer when switching pages.
            if (DrawerOpen) { Notice(Ui.T("请先完成或取消当前编辑。", "Finish or cancel the current edit first.")); return; }
            for (int i = 0; i < 4; i++) { Ui.Stop(views[i]); views[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed; navigation[i].Background = i == index ? Ui.Brush("#253A21") : Ui.Frame; navigation[i].Foreground = Ui.Foreground; navigation[i].BorderBrush = Ui.Accent; navigation[i].BorderThickness = new Thickness(i == index ? 2 : 0, 0, 0, 0); }
            currentPage = index; Ui.Enter(views[index]);
        }
        internal void OpenDrawer(string title, UIElement content)
        {
            previousFocus = Keyboard.FocusedElement as FrameworkElement;
            overlay.Children.Clear();
            var panel = Ui.Stack(Ui.Row(Ui.Text(title, 24), Ui.Button("×", CloseDrawer)), content);
            var drawer = new Border { Width = 390, Background = Ui.Background, BorderBrush = Ui.Brush("#353535"), BorderThickness = new Thickness(1, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(24), Child = Ui.Scroll(panel) };
            overlay.Children.Add(drawer); overlay.Visibility = Visibility.Visible; Ui.Enter(drawer, true);
            KeyboardNavigation.SetTabNavigation(drawer, KeyboardNavigationMode.Cycle);
            drawer.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        internal void CloseDrawer() { foreach (FrameworkElement c in overlay.Children) Ui.Stop(c); overlay.Visibility = Visibility.Collapsed; overlay.Children.Clear(); if (macroPage != null) macroPage.DrawerClosed(); if (previousFocus != null) previousFocus.Focus(); }
        internal void Notice(string message) { toast.Text = message; toastTimer.Stop(); toastTimer.Start(); }
        internal async Task RefreshDevice()
        {
            if (refreshing || disposed) return; refreshing = true;
            try
            {
                Reading = Demo ? new MouseBatteryInfo { ProductId = 0x00DF, DeviceKey = "demo", DeviceName = "Razer Viper V3 Pro SE", ProtocolStatus = DeviceProtocolStatus.Ready, IsWriteSupported = true, BatteryKnown = true, IsConnected = true, BatteryPercent = 98, Dpi = 800, DpiStage = 2, DpiStageCount = 5, DpiStages = new[] { 400, 800, 1600, 3200, 6400 }, PollingRate = 1000, RotationKnown = true, RotationAngle = -8, IsRotationWriteSupported = true, IsRotationHardwareVerified = true, LastUpdated = DateTime.Now } : await Task.Run(() => Device.QueryRazerDeviceInfo());
                if (disposed) return;
                devicePage.UpdateReading(); UpdateTray();
                if (Refreshed != null) Refreshed();
            }
            catch (Exception ex) { Notice(Ui.T("读取失败：", "Read failed: ") + ex.Message); }
            finally { refreshing = false; }
        }
        private void OnDpi(DpiReading value)
        {
            if (disposed || value.Dpi <= 0 || refreshing || value.DeviceKey != Reading.DeviceKey) return;
            Reading.Dpi = value.Dpi; Reading.DpiStage = value.Stage; Reading.DpiStageCount = value.Count;
            devicePage.UpdateDpi();
            if (Refreshed != null) Refreshed();
        }
        internal void SaveMacros()
        {
            if (macroPage != null && !macroPage.CommitPending()) return;
            try {
                SyncBindingDraft(); MacroValidation.Validate(Draft);
                if (!Demo) Macros.SaveDefinitions(Draft); else demoActive = Draft.Clone();
                DraftDirty = false; if (macroPage != null) macroPage.RefreshRows();
                Notice(Demo ? Ui.T("预览模式：草稿仅保留在内存。", "Preview: draft remains in memory only.") : Ui.T("宏已保存；已生效绑定保持同步。", "Macros saved; active bindings kept in sync."));
            }
            catch (Exception ex) { Notice(ex.Message); }
        }
        private void SetupTray() { tray = new TrayController(this); }
        internal void Restore() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
        internal void OpenPage(int index) { Restore(); Navigate(index); }
        internal void RequestExit() { if (Macros != null) Macros.Stop(); exiting = true; Close(); }
        private void UpdateTray() { if (tray != null) tray.Update(); }
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (macroPage != null && macroPage.IsRecording) macroPage.EndRecording(false);
            if (!exiting && !Demo && Preferences.CloseToTray) { e.Cancel = true; Hide(); return; }
            if ((DraftDirty || (macroPage != null && macroPage.HasPendingEdit)) && !discardApproved)
            {
                e.Cancel = true;
                if (!IsVisible) Dispatcher.BeginInvoke(new Action(Restore));
                OpenDrawer(Ui.T("未保存的宏草稿", "Unsaved macro draft"), Ui.Stack(Ui.Text(Ui.T("退出会丢弃这次未保存的修改。", "Exiting will discard your unsaved changes.")), Ui.Button(Ui.T("保存后退出", "Save and exit"), () => { SaveMacros(); if (!DraftDirty) { exiting = true; Close(); } }, true), Ui.Button(Ui.T("放弃修改并退出", "Discard and exit"), () => { exiting = true; discardApproved = true; Close(); }), Ui.Button(Ui.T("继续编辑", "Keep editing"), () => { exiting = false; CloseDrawer(); })));
            }
        }
        internal void ClosePreview() { if (!Demo) throw new InvalidOperationException(); discardApproved = true; exiting = true; Close(); }
        private void DisposeServices()
        {
            if (Updates != null) Updates.Dispose();
            if (updatePage != null) updatePage.Dispose();
            if (macroPage != null) macroPage.DisposeRecording();
            disposed = true; refreshTimer.Stop(); toastTimer.Stop();
            if (monitor != null) monitor.Dispose(); if (Macros != null) Macros.Dispose();
            if (tray != null) tray.Dispose();
        }
        private FrameworkElement BuildUpdates()
        {
            updatePage = new UpdatePage(this); return updatePage;
        }
        internal void SaveUpdateChannel(bool enabled)
        {
            bool old = Preferences.IncludePrereleases; Preferences.IncludePrereleases = enabled;
            try { if (!Demo) store.Save(Preferences); } catch { Preferences.IncludePrereleases = old; throw; }
        }
    }
}
