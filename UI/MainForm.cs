using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        private int userSelectedInterval = 60000;
        // AutoStart Window Visibility Control
        private bool isAutoStartLaunch = false;
        private bool autoStartShowMainWindow = false;
        private bool allowVisibleCore = false;
        // Visual controls - Card 1: Status
        private RoundedCard cardBattery;
        private Label lblDeviceName;
        private Label lblConnDot;
        private Label lblBatteryBig;
        private StatusPill pillStatus;
        private ModernProgressBar barBattery;
        private Label lblUpdateTime;

        // Visual controls - Card 2: Performance & Tuning
        private RoundedCard cardPerformance;
        private Label lblPerfTitle;
        private Label lblDpiTitle;
        private ModernSegmentButton[] btnDpiStages;
        private int[] dpiStageValues = new int[] { 400, 800, 1600, 3000, 6400 };
        private Label lblRateTitle;
        private ModernSegmentButton[] btnRates;
        private int[] pollingRateValues = new int[] { 1000, 2000, 4000, 8000 };

        // Visual controls - Card 3: Settings & Preferences
        private RoundedCard cardSettings;
        private Label lblSettingsTitle;
        private Label lblStyleTitle;
        private ModernSegmentButton btnStyleCapsule;
        private ModernSegmentButton btnStyleNum;
        private Label lblIntervalTitle;
        private ModernSegmentButton btnInt30s;
        private ModernSegmentButton btnInt1m;
        private ModernSegmentButton btnInt5m;
        private Label lblOsdStyleTitle;
        private ModernSegmentButton btnOsdStyleCapsule;
        private ModernSegmentButton btnOsdStyleGauge;
        private ModernSegmentButton btnOsdStyleCompact;
        private SubtleDivider divSettings;
        private ModernCheckBox chkAutoStart;
        private ModernCheckBox chkAutoStartShowUI;
        private ModernCheckBox chkLowAlert;
        private ModernCheckBox chkDpiOsd;
        private Label lblSettingsTip;

        // Visual controls - Bottom Actions
        private ModernButton btnRefresh;
        private ModernButton btnHideToTray;

        private bool lowBatteryAlertEnabled = true;
        private bool dpiOsdEnabled = true;
        private bool lastLowAlertFired = false;
        private bool isUpdatingUI = false;
        private int trayStyle = 0; // 0 = Capsule, 1 = Number
        private int osdStyle = 0; // 0 = Centered Capsule, 1 = Stepped Gauge, 2 = Compact Top-Right
        private float dpiScale = 1.0f;
        private MouseBatteryInfo lastInfo = null;
        private readonly IRazerDeviceClient device;
        private readonly ISettingsStore settingsStore;
        private readonly IAutoStartService autoStartService;
        private readonly bool monitoringEnabled;

        public MainForm(bool isAutoStart = false)
            : this(isAutoStart, new RazerDeviceClient(), new SettingsStore(),
                new AutoStartService(Application.ExecutablePath), true, true) { }

        internal MainForm(bool isAutoStart, IRazerDeviceClient device, ISettingsStore settingsStore,
            IAutoStartService autoStartService, bool monitoringEnabled, bool enableMacros = false)
        {
            this.device = device;
            this.settingsStore = settingsStore;
            this.autoStartService = autoStartService;
            this.monitoringEnabled = monitoringEnabled;
            this.isAutoStartLaunch = isAutoStart;

            using (Graphics g = this.CreateGraphics())
            {
                dpiScale = g.DpiX / 96.0f;
                if (dpiScale < 1.0f) dpiScale = 1.0f;
            }

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LeiyunLite.ico");
                if (File.Exists(icoPath))
                {
                    this.Icon = new Icon(icoPath);
                }
                else
                {
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch { }

            osdForm = new DpiOsdForm(dpiScale);

            InitializeFormUI();
            InitializeTray();
            LoadConfig();
            if (enableMacros) InitializeMacros();

            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = userSelectedInterval;
            updateTimer.Tick += (s, e) => RefreshBatteryStatus(false);
            if (monitoringEnabled) updateTimer.Start();

            RefreshBatteryStatus(false);
            if (monitoringEnabled)
            {
                StartBootPolling();
                StartDpiMonitor();
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (isAutoStartLaunch && !autoStartShowMainWindow && !allowVisibleCore)
            {
                value = false;
                if (!this.IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyModernWin11Theme();
            if (monitoringEnabled)
            {
                RegisterUsbNotification();
                RegisterMouseRawInput();
            }
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                this.TopMost = true;
                this.BringToFront();
                this.Activate();
                this.TopMost = false;
            }
            catch { }
        }
        private void ShowWindow()
        {
            allowVisibleCore = true;
            RefreshBatteryStatus(false);
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.TopMost = true;
            this.BringToFront();
            this.Activate();
            this.TopMost = false;
        }

        private void ToggleWindow()
        {
            if (this.Visible && this.WindowState != FormWindowState.Minimized)
            {
                this.Hide();
            }
            else
            {
                ShowWindow();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon.ShowBalloonTip(1500, "雷云lite已最小化", "程序在系统托盘持续运行，随时双击托盘图标可重新打开面板。", ToolTipIcon.Info);
            }
            else
            {
                StopDpiMonitor();
                base.OnFormClosing(e);
            }
        }
        private void ExitApp()
        {
            if (macroController != null) macroController.Stop();
            StopDpiMonitor();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            if (osdForm != null && !osdForm.IsDisposed)
            {
                osdForm.Dispose();
            }
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (macroEditor != null) { macroEditor.Dispose(); macroEditor = null; }
                if (macroController != null) { macroController.Dispose(); macroController = null; }
                StopDpiMonitor();
                if (updateTimer != null) updateTimer.Dispose();
                if (wakeBurstTimer != null) wakeBurstTimer.Dispose();
                if (bootPollTimer != null) bootPollTimer.Dispose();
                if (deviceChangeTimer1 != null) deviceChangeTimer1.Dispose();
                if (deviceChangeTimer2 != null) deviceChangeTimer2.Dispose();
                if (hDevNotify != IntPtr.Zero)
                {
                    UnregisterDeviceNotification(hDevNotify);
                    hDevNotify = IntPtr.Zero;
                }
                if (trayIcon != null)
                {
                    Icon oldIcon = trayIcon.Icon;
                    trayIcon.Dispose();
                    if (oldIcon != null) oldIcon.Dispose();
                }
                if (contextMenu != null) contextMenu.Dispose();
                if (osdForm != null) osdForm.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
