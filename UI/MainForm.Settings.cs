using System;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        private void SetDpiOsdEnabled(bool enabled, bool showNotification = false)
        {
            dpiOsdEnabled = enabled;

            isUpdatingUI = true;
            try
            {
                if (chkDpiOsd != null && chkDpiOsd.Checked != enabled) chkDpiOsd.Checked = enabled;
                if (dpiOsdMenuItem != null) dpiOsdMenuItem.Checked = enabled;
            }
            finally
            {
                isUpdatingUI = false;
            }

            SaveConfig();

            if (showNotification && trayIcon != null)
            {
                if (enabled)
                    trayIcon.ShowBalloonTip(1500, "DPI 屏幕提示已开启", "按键切换 DPI 时将在屏幕右下角弹出浮窗提示。", ToolTipIcon.Info);
                else
                    trayIcon.ShowBalloonTip(1500, "DPI 屏幕提示已关闭", "已关闭按键切换 DPI 的屏幕浮窗提示。", ToolTipIcon.None);
            }
        }

        private void SetInterval(int ms, bool showNotification = false)
        {
            userSelectedInterval = ms;
            if (updateTimer != null) updateTimer.Interval = ms;
            SaveConfig();

            isUpdatingUI = true;
            try
            {
                if (int30sMenuItem != null) int30sMenuItem.Checked = (ms == 30000);
                if (int1mMenuItem != null) int1mMenuItem.Checked = (ms == 60000);
                if (int5mMenuItem != null) int5mMenuItem.Checked = (ms == 300000);

                if (btnInt30s != null) btnInt30s.Selected = (ms == 30000);
                if (btnInt1m != null) btnInt1m.Selected = (ms == 60000);
                if (btnInt5m != null) btnInt5m.Selected = (ms == 300000);
            }
            finally
            {
                isUpdatingUI = false;
            }

            if (showNotification && trayIcon != null)
            {
                string sec = (ms >= 60000) ? ((ms / 60000) + " 分钟") : ((ms / 1000) + " 秒");
                trayIcon.ShowBalloonTip(1500, "刷新频率已设置", "已设置为: " + sec, ToolTipIcon.Info);
            }
        }

        private void SetTrayStyle(int style, bool showNotification = false)
        {
            trayStyle = style;
            SaveConfig();

            isUpdatingUI = true;
            try
            {
                if (styleCapsuleItem != null) styleCapsuleItem.Checked = (trayStyle == 0);
                if (styleNumItem != null) styleNumItem.Checked = (trayStyle == 1);

                if (btnStyleCapsule != null) btnStyleCapsule.Selected = (trayStyle == 0);
                if (btnStyleNum != null) btnStyleNum.Selected = (trayStyle == 1);
            }
            finally
            {
                isUpdatingUI = false;
            }

            if (lastInfo != null)
            {
                UpdateTrayIcon(lastInfo.BatteryPercent, lastInfo.IsCharging, lastInfo.IsConnected);
            }
            else
            {
                UpdateTrayIcon(-1, false, false);
            }

            if (showNotification && trayIcon != null)
            {
                string name = (trayStyle == 0) ? "现代胶囊电池" : "醒目数字能量表";
                trayIcon.ShowBalloonTip(1500, "托盘样式已切换", "当前显示样式: " + name, ToolTipIcon.Info);
            }
        }

        private void SetOsdStyle(int style, bool showNotification = false)
        {
            osdStyle = style;
            if (osdStyle < 0 || osdStyle > 2) osdStyle = 0;
            SaveConfig();

            if (osdForm != null)
            {
                osdForm.OsdStyle = osdStyle;
            }

            isUpdatingUI = true;
            try
            {
                if (osdStyleCapsuleItem != null) osdStyleCapsuleItem.Checked = (osdStyle == 0);
                if (osdStyleGaugeItem != null) osdStyleGaugeItem.Checked = (osdStyle == 1);
                if (osdStyleCompactItem != null) osdStyleCompactItem.Checked = (osdStyle == 2);

                if (btnOsdStyleCapsule != null) btnOsdStyleCapsule.Selected = (osdStyle == 0);
                if (btnOsdStyleGauge != null) btnOsdStyleGauge.Selected = (osdStyle == 1);
                if (btnOsdStyleCompact != null) btnOsdStyleCompact.Selected = (osdStyle == 2);
            }
            finally
            {
                isUpdatingUI = false;
            }

            // Trigger instant live preview so the user immediately sees their chosen style!
            if (dpiOsdEnabled && osdForm != null)
            {
                int previewDpi = (lastInfo != null && lastInfo.Dpi > 0) ? lastInfo.Dpi : 3000;
                int previewStage = (lastInfo != null && lastInfo.DpiStage > 0) ? lastInfo.DpiStage : 4;
                int previewCount = (lastInfo != null && lastInfo.DpiStageCount > 0) ? lastInfo.DpiStageCount : 5;
                osdForm.ShowDpi(previewDpi, previewStage, previewCount);
            }

            if (showNotification && trayIcon != null)
            {
                string name = (osdStyle == 0) ? "居中电竞胶囊" : ((osdStyle == 1) ? "右侧阶梯能量计" : "顶置微型指示段");
                trayIcon.ShowBalloonTip(1500, "DPI 浮窗样式已切换", "当前浮窗样式: " + name, ToolTipIcon.Info);
            }
        }

        private void UpdateMenuStatusTexts()
        {
            if (lowBatteryAlertMenuItem != null)
            {
                lowBatteryAlertMenuItem.Text = "低电量气泡通知 (≤20%)";
                lowBatteryAlertMenuItem.Checked = lowBatteryAlertEnabled;
            }
            bool autoStart = IsAutoStartEnabled();
            if (autoStartMenuItem != null)
            {
                autoStartMenuItem.Text = "开机自动启动";
                autoStartMenuItem.Checked = autoStart;
            }
            if (autoStartShowUIMenuItem != null)
            {
                autoStartShowUIMenuItem.Text = "开机弹出主窗口";
                autoStartShowUIMenuItem.Checked = autoStartShowMainWindow;
                autoStartShowUIMenuItem.Enabled = autoStart;
            }
            if (chkAutoStartShowUI != null)
            {
                chkAutoStartShowUI.Checked = autoStartShowMainWindow;
                chkAutoStartShowUI.Enabled = autoStart;
            }
            if (dpiOsdMenuItem != null)
            {
                dpiOsdMenuItem.Text = "DPI 切换屏幕提示 (OSD)";
                dpiOsdMenuItem.Checked = dpiOsdEnabled;
            }
        }

        private void LoadConfig()
        {
            AppSettings settings = settingsStore.Load();
            lowBatteryAlertEnabled = settings.LowBatteryAlert;
            dpiOsdEnabled = settings.DpiOsdAlert;
            autoStartShowMainWindow = settings.AutoStartShowUI;
            trayStyle = settings.TrayIconStyle;
            osdStyle = settings.OsdStyle;
            userSelectedInterval = settings.RefreshInterval;

            bool autoStart = IsAutoStartEnabled();
            if (autoStart)
            {
                SyncAutoStartRegistry();
            }

            isUpdatingUI = true;
            try
            {
                UpdateMenuStatusTexts();
                if (chkLowAlert != null) chkLowAlert.Checked = lowBatteryAlertEnabled;
                if (chkDpiOsd != null) chkDpiOsd.Checked = dpiOsdEnabled;
                if (chkAutoStart != null) chkAutoStart.Checked = autoStart;
                if (chkAutoStartShowUI != null)
                {
                    chkAutoStartShowUI.Checked = autoStartShowMainWindow;
                    chkAutoStartShowUI.Enabled = autoStart;
                }
                if (autoStartShowUIMenuItem != null)
                {
                    autoStartShowUIMenuItem.Checked = autoStartShowMainWindow;
                    autoStartShowUIMenuItem.Enabled = autoStart;
                }

                if (styleCapsuleItem != null) styleCapsuleItem.Checked = (trayStyle == 0);
                if (styleNumItem != null) styleNumItem.Checked = (trayStyle == 1);
                if (btnStyleCapsule != null) btnStyleCapsule.Selected = (trayStyle == 0);
                if (btnStyleNum != null) btnStyleNum.Selected = (trayStyle == 1);

                if (osdForm != null) osdForm.OsdStyle = osdStyle;
                if (osdStyleCapsuleItem != null) osdStyleCapsuleItem.Checked = (osdStyle == 0);
                if (osdStyleGaugeItem != null) osdStyleGaugeItem.Checked = (osdStyle == 1);
                if (osdStyleCompactItem != null) osdStyleCompactItem.Checked = (osdStyle == 2);
                if (btnOsdStyleCapsule != null) btnOsdStyleCapsule.Selected = (osdStyle == 0);
                if (btnOsdStyleGauge != null) btnOsdStyleGauge.Selected = (osdStyle == 1);
                if (btnOsdStyleCompact != null) btnOsdStyleCompact.Selected = (osdStyle == 2);

                if (int30sMenuItem != null) int30sMenuItem.Checked = (userSelectedInterval == 30000);
                if (int1mMenuItem != null) int1mMenuItem.Checked = (userSelectedInterval == 60000);
                if (int5mMenuItem != null) int5mMenuItem.Checked = (userSelectedInterval == 300000);
                if (btnInt30s != null) btnInt30s.Selected = (userSelectedInterval == 30000);
                if (btnInt1m != null) btnInt1m.Selected = (userSelectedInterval == 60000);
                if (btnInt5m != null) btnInt5m.Selected = (userSelectedInterval == 300000);
            }
            finally
            {
                isUpdatingUI = false;
            }
        }

        private void SaveConfig()
        {
            settingsStore.Save(new AppSettings {
                LowBatteryAlert = lowBatteryAlertEnabled, DpiOsdAlert = dpiOsdEnabled,
                AutoStartShowUI = autoStartShowMainWindow, TrayIconStyle = trayStyle,
                OsdStyle = osdStyle, RefreshInterval = userSelectedInterval
            });
        }

        private bool IsAutoStartEnabled() { return autoStartService.IsEnabled(); }
        private void SyncAutoStartRegistry() { autoStartService.Sync(); }

        private void SetAutoStart(bool enabled, bool showNotification = false)
        {
            try
            {
                autoStartService.SetEnabled(enabled);
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机启动失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            isUpdatingUI = true;
            try
            {
                UpdateMenuStatusTexts();
                if (chkAutoStart != null && chkAutoStart.Checked != enabled) chkAutoStart.Checked = enabled;
                if (chkAutoStartShowUI != null) chkAutoStartShowUI.Enabled = enabled;
                if (autoStartShowUIMenuItem != null) autoStartShowUIMenuItem.Enabled = enabled;
            }
            finally
            {
                isUpdatingUI = false;
            }

            if (showNotification)
            {
                if (enabled)
                {
                    trayIcon.ShowBalloonTip(2000, "开机自启已开启", "雷云lite已设置为开机自动启动并在托盘静默运行。", ToolTipIcon.Info);
                }
                else
                {
                    trayIcon.ShowBalloonTip(2000, "开机自启已关闭", "已取消开机自动启动。", ToolTipIcon.None);
                }
            }
        }

        private void SetAutoStartShowUI(bool showUI, bool showNotification = false)
        {
            autoStartShowMainWindow = showUI;
            SaveConfig();

            isUpdatingUI = true;
            try
            {
                if (chkAutoStartShowUI != null && chkAutoStartShowUI.Checked != showUI) chkAutoStartShowUI.Checked = showUI;
                if (autoStartShowUIMenuItem != null) autoStartShowUIMenuItem.Checked = showUI;
            }
            finally
            {
                isUpdatingUI = false;
            }

            if (showNotification && trayIcon != null)
            {
                if (showUI)
                    trayIcon.ShowBalloonTip(1500, "开机行为已更新", "开机启动时将自动显示主窗口面板。", ToolTipIcon.Info);
                else
                    trayIcon.ShowBalloonTip(1500, "开机行为已更新", "开机启动时将保持静默，仅常驻系统托盘。", ToolTipIcon.Info);
            }
        }

private void SetLowBatteryAlert(bool enabled, bool showNotification = false)
        {
            lowBatteryAlertEnabled = enabled;

            isUpdatingUI = true;
            try
            {
                UpdateMenuStatusTexts();
                if (chkLowAlert != null && chkLowAlert.Checked != enabled) chkLowAlert.Checked = enabled;
            }
            finally
            {
                isUpdatingUI = false;
            }

            SaveConfig();

            if (showNotification)
            {
                if (enabled)
                {
                    trayIcon.ShowBalloonTip(2500, "低电量提醒已开启", "当鼠标电量 ≤ 20% 且未充电时，系统托盘将弹出气泡提醒您及时充电。", ToolTipIcon.Info);
                }
                else
                {
                    trayIcon.ShowBalloonTip(2000, "低电量提醒已关闭", "已关闭低电量气泡通知功能。", ToolTipIcon.None);
                }
            }
        }
    }
}
