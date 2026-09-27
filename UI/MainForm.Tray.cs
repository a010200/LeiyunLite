using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private ToolStripMenuItem statusMenuItem;
        private ToolStripMenuItem autoStartMenuItem;
        private ToolStripMenuItem autoStartShowUIMenuItem;
        private ToolStripMenuItem lowBatteryAlertMenuItem;
        private ToolStripMenuItem dpiOsdMenuItem;
        private ToolStripMenuItem styleCapsuleItem;
        private ToolStripMenuItem styleNumItem;
        private ToolStripMenuItem osdStyleMenu;
        private ToolStripMenuItem osdStyleCapsuleItem;
        private ToolStripMenuItem osdStyleGaugeItem;
        private ToolStripMenuItem osdStyleCompactItem;
        private ToolStripMenuItem int30sMenuItem;
        private ToolStripMenuItem int1mMenuItem;
        private ToolStripMenuItem int5mMenuItem;
        private ToolStripMenuItem dpiMenu;
        private ToolStripMenuItem rateMenu;
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool DestroyIcon(IntPtr handle);
        private void InitializeTray()
        {
            contextMenu = new ContextMenuStrip();
            contextMenu.Renderer = new ModernDarkMenuRenderer();
            contextMenu.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            contextMenu.ShowImageMargin = true;
            contextMenu.ShowCheckMargin = false;
            contextMenu.Padding = new Padding(3, 4, 3, 4);

            statusMenuItem = new ToolStripMenuItem("正在检测设备...");
            statusMenuItem.Tag = "Header";
            statusMenuItem.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            statusMenuItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(statusMenuItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var showItem = new ToolStripMenuItem("打开控制面板 (&O)", null, (s, e) => ShowWindow());
            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new ToolStripMenuItem("宏与按键 / 滚轮绑定", null, (s, e) => ShowMacroEditor()));
            contextMenu.Items.Add(new ToolStripMenuItem("停止所有宏 (Ctrl+Shift+F12)", null, (s, e) => StopAllMacros()));

            var refreshItem = new ToolStripMenuItem("立即刷新电量 (&R)", null, (s, e) => RefreshBatteryStatus(true));
            contextMenu.Items.Add(refreshItem);

            // Submenu: DPI 调节
            dpiMenu = new ToolStripMenuItem("调节 DPI 档位 (&D)");
            dpiMenu.DropDown.Renderer = contextMenu.Renderer;
            for (int i = 0; i < dpiStageValues.Length; i++)
            {
                int val = dpiStageValues[i];
                var sub = new ToolStripMenuItem(val + " DPI", null, (s, e) => SetDpiFromUI(val));
                sub.Tag = val;
                dpiMenu.DropDownItems.Add(sub);
            }
            contextMenu.Items.Add(dpiMenu);

            // Submenu: 回报率调节
            rateMenu = new ToolStripMenuItem("调节回报率 (&P)");
            rateMenu.DropDown.Renderer = contextMenu.Renderer;
            for (int i = 0; i < pollingRateValues.Length; i++)
            {
                int val = pollingRateValues[i];
                var sub = new ToolStripMenuItem(val + " Hz", null, (s, e) => SetPollingRateFromUI(val));
                sub.Tag = val;
                rateMenu.DropDownItems.Add(sub);
            }
            contextMenu.Items.Add(rateMenu);

            var intervalMenu = new ToolStripMenuItem("自动刷新频率 (&I)");
            intervalMenu.DropDown.Renderer = contextMenu.Renderer;
            int30sMenuItem = new ToolStripMenuItem("30 秒", null, (s, e) => SetInterval(30000, true));
            int1mMenuItem = new ToolStripMenuItem("1 分钟", null, (s, e) => SetInterval(60000, true));
            int5mMenuItem = new ToolStripMenuItem("5 分钟", null, (s, e) => SetInterval(300000, true));
            intervalMenu.DropDownItems.AddRange(new ToolStripItem[] { int30sMenuItem, int1mMenuItem, int5mMenuItem });
            contextMenu.Items.Add(intervalMenu);

            var styleMenu = new ToolStripMenuItem("托盘图标样式 (&T)");
            styleMenu.DropDown.Renderer = contextMenu.Renderer;
            styleCapsuleItem = new ToolStripMenuItem("现代胶囊电池", null, (s, e) => SetTrayStyle(0, true));
            styleNumItem = new ToolStripMenuItem("醒目数字能量表", null, (s, e) => SetTrayStyle(1, true));
            styleCapsuleItem.Checked = (trayStyle == 0);
            styleNumItem.Checked = (trayStyle == 1);
            styleMenu.DropDownItems.AddRange(new ToolStripItem[] { styleCapsuleItem, styleNumItem });
            contextMenu.Items.Add(styleMenu);

            osdStyleMenu = new ToolStripMenuItem("DPI 浮窗样式 (&O)");
            osdStyleMenu.DropDown.Renderer = contextMenu.Renderer;
            osdStyleCapsuleItem = new ToolStripMenuItem("居中电竞胶囊", null, (s, e) => SetOsdStyle(0, true));
            osdStyleGaugeItem = new ToolStripMenuItem("右侧阶梯能量计", null, (s, e) => SetOsdStyle(1, true));
            osdStyleCompactItem = new ToolStripMenuItem("顶置微型指示段", null, (s, e) => SetOsdStyle(2, true));
            osdStyleCapsuleItem.Checked = (osdStyle == 0);
            osdStyleGaugeItem.Checked = (osdStyle == 1);
            osdStyleCompactItem.Checked = (osdStyle == 2);
            osdStyleMenu.DropDownItems.AddRange(new ToolStripItem[] { osdStyleCapsuleItem, osdStyleGaugeItem, osdStyleCompactItem });
            contextMenu.Items.Add(osdStyleMenu);

            contextMenu.Items.Add(new ToolStripSeparator());

            dpiOsdMenuItem = new ToolStripMenuItem("DPI 切换屏幕提示 (OSD)", null, (s, e) => {
                SetDpiOsdEnabled(!dpiOsdEnabled, true);
            });
            contextMenu.Items.Add(dpiOsdMenuItem);

            lowBatteryAlertMenuItem = new ToolStripMenuItem("低电量气泡通知 (≤20%)", null, (s, e) => {
                SetLowBatteryAlert(!lowBatteryAlertEnabled, true);
            });
            contextMenu.Items.Add(lowBatteryAlertMenuItem);

            autoStartMenuItem = new ToolStripMenuItem("开机自动启动", null, (s, e) => {
                SetAutoStart(!IsAutoStartEnabled(), true);
            });
            contextMenu.Items.Add(autoStartMenuItem);

            autoStartShowUIMenuItem = new ToolStripMenuItem("开机弹出主窗口", null, (s, e) => {
                SetAutoStartShowUI(!autoStartShowMainWindow, true);
            });
            contextMenu.Items.Add(autoStartShowUIMenuItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出程序 (&X)", null, (s, e) => ExitApp());
            contextMenu.Items.Add(exitItem);

            foreach (ToolStripItem item in contextMenu.Items)
            {
                item.Padding = new Padding(6, 4, 12, 4);
            }

            trayIcon = new NotifyIcon();
            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.Text = "雷蛇鼠标检测中...";
            UpdateTrayIcon(-1, false, false);
            trayIcon.Visible = monitoringEnabled;

            trayIcon.Click += (s, e) => {
                var me = e as MouseEventArgs;
                if (me != null && me.Button == MouseButtons.Left)
                {
                    ToggleWindow();
                }
            };
            trayIcon.DoubleClick += (s, e) => ShowWindow();
        }
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);
        private const int SM_CXSMICON = 49;

        private int GetTrayIconSize()
        {
            try
            {
                int size = GetSystemMetrics(SM_CXSMICON);
                if (size >= 24) return 24;
                if (size >= 20) return 20;
                return 16;
            }
            catch
            {
                return 16;
            }
        }

        private void UpdateTrayIcon(int percent, bool isCharging, bool isConnected, bool isSleeping = false)
        {
            int iconSize = GetTrayIconSize();
            using (Bitmap bmp = TrayIconRenderer.DrawTrayBitmap(percent, isCharging, isConnected, trayStyle, iconSize, isSleeping))
            {
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tempIcon = Icon.FromHandle(hIcon))
                    {
                        Icon oldIcon = trayIcon.Icon;
                        trayIcon.Icon = (Icon)tempIcon.Clone();
                        if (oldIcon != null) oldIcon.Dispose();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
    }
}
