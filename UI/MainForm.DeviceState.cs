using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        private void RefreshBatteryStatus(bool showTipIfManual)
        {
            try
            {
                var info = device.QueryRazerDeviceInfo();
                lastInfo = info;
                UpdateUI(info, showTipIfManual);
            }
            catch (Exception ex)
            {
                statusMenuItem.Text = "读取失败: " + ex.Message;
                trayIcon.Text = "雷蛇鼠标检测异常";
                UpdateTrayIcon(-1, false, false);
            }
        }

        private void UpdateStatusDisplay()
        {
            if (lastInfo == null || !lastInfo.IsConnected) return;

            string dpiPart = lastInfo.Dpi > 0 ? (" · " + lastInfo.Dpi + " DPI") : "";
            string ratePart = lastInfo.PollingRate > 0 ? (" · " + lastInfo.PollingRate + "Hz") : "";
            string chgPart = lastInfo.IsSleeping ? "休眠待机" : (lastInfo.IsCharging ? "充电中" : "电池供电");
            string menuStatus = string.Format("{0} ({1}% · {2})", lastInfo.DeviceName, lastInfo.BatteryPercent, chgPart);
            statusMenuItem.Text = menuStatus;

            string timeStr = lastInfo.LastUpdated.ToString("HH:mm:ss");
            string chgStr = lastInfo.IsSleeping ? "休眠待机 (移动唤醒)" : (lastInfo.IsCharging ? "正在充电" : "电池供电");
            string tipText = string.Format(AppVersion.DisplayName + "\n{0} · {1}%\n状态: {2}{3}{4}\n最后同步: {5}",
                lastInfo.DeviceName, lastInfo.BatteryPercent, chgStr, dpiPart, ratePart, timeStr);

            if (tipText.Length > 63)
            {
                tipText = string.Format("{0}: {1}%\n{2}{3}", lastInfo.DeviceName, lastInfo.BatteryPercent, chgStr, dpiPart);
                if (tipText.Length > 63)
                {
                    tipText = string.Format("电量: {0}% ({1})", lastInfo.BatteryPercent, chgPart);
                }
            }
            trayIcon.Text = tipText;
        }

        private void UpdateUI(MouseBatteryInfo info, bool showTipIfManual)
        {
            if (info == null || !info.IsConnected)
            {
                isMouseSleeping = false;
                if (updateTimer != null) updateTimer.Interval = 3000;

                lblDeviceName.Text = "未检测到雷蛇鼠标";
                lblConnDot.Text = "○ 未连接";
                lblConnDot.ForeColor = Color.FromArgb(140, 145, 155);

                lblBatteryBig.Text = "--%";
                lblBatteryBig.ForeColor = Color.FromArgb(140, 145, 155);

                pillStatus.SetStatus("未连接", Color.FromArgb(140, 145, 155));
                pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(60 * dpiScale));

                barBattery.Value = 0;
                lblUpdateTime.Text = "最后同步: " + DateTime.Now.ToString("HH:mm:ss") + " · 未检测到设备";

                statusMenuItem.Text = "未检测到雷蛇鼠标 (未连接)";
                trayIcon.Text = "未检测到雷蛇鼠标 (未连接)";
                UpdateTrayIcon(-1, false, false);
                lastLowAlertFired = false;
                if (showTipIfManual)
                {
                    trayIcon.ShowBalloonTip(2000, "雷蛇鼠标未连接", "未能找到已连接或唤醒的雷蛇鼠标，请移动鼠标唤醒后重试。", ToolTipIcon.Warning);
                }
                return;
            }

            // Absolute safety guard: connected mouse should NEVER show 0% (indicates sleep / no telemetry)
            if (info.BatteryPercent <= 0)
            {
                if (device.CachedBatteryPercent > 0)
                {
                    info.BatteryPercent = device.CachedBatteryPercent;
                }
                else
                {
                    info.BatteryPercent = 50;
                }
                info.IsSleeping = true;
            }

            if (info.IsSleeping)
            {
                isMouseSleeping = true;
                if (updateTimer != null) updateTimer.Interval = userSelectedInterval;

                Color sleepColor = Color.FromArgb(255, 183, 77); // Warm amber
                lblDeviceName.Text = info.DeviceName;
                lblConnDot.Text = "◐ 休眠待机";
                lblConnDot.ForeColor = sleepColor;

                lblBatteryBig.Text = info.BatteryPercent + "%";
                lblBatteryBig.ForeColor = Color.FromArgb(220, 225, 235);

                pillStatus.SetStatus("休眠待机", sleepColor);
                pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(60 * dpiScale));

                barBattery.Value = info.BatteryPercent;
                barBattery.ProgressColor = sleepColor;

                string timeStr = info.LastUpdated.ToString("HH:mm:ss");
                lblUpdateTime.Text = "最后同步: " + timeStr + " · 移动鼠标即刻唤醒";

                // Update Performance Card with cached stages
                if (info.DpiStages != null && info.DpiStages.Length > 0)
                {
                    dpiStageValues = info.DpiStages;
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (i < dpiStageValues.Length)
                        {
                            btnDpiStages[i].Text = dpiStageValues[i].ToString();
                            btnDpiStages[i].Selected = (dpiStageValues[i] == info.Dpi);
                            btnDpiStages[i].Visible = true;
                        }
                        else
                        {
                            btnDpiStages[i].Visible = false;
                        }
                    }
                }

                if (info.PollingRate > 0)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (i < pollingRateValues.Length)
                        {
                            btnRates[i].Selected = (pollingRateValues[i] == info.PollingRate);
                        }
                    }
                }

                UpdateStatusDisplay();
                UpdateTrayIcon(info.BatteryPercent, false, true, true);

                if (showTipIfManual)
                {
                    trayIcon.ShowBalloonTip(1500, info.DeviceName, string.Format("电量: {0}% (休眠待机)\n移动鼠标将即刻恢复工作", info.BatteryPercent), ToolTipIcon.Info);
                }
                return;
            }

            // Normal awake state
            isMouseSleeping = false;
            if (updateTimer != null) updateTimer.Interval = userSelectedInterval;

            Color accentColor;
            if (info.BatteryPercent > 40 || info.IsCharging)
                accentColor = Theme.Green;
            else if (info.BatteryPercent > 20)
                accentColor = Color.FromArgb(255, 214, 0);
            else
                accentColor = Color.FromArgb(255, 45, 85);

            string chgStr = info.IsCharging ? "正在充电" : "电池供电";
            lblDeviceName.Text = info.DeviceName;
            lblConnDot.Text = "● 已连接";
            lblConnDot.ForeColor = Theme.Green;

            lblBatteryBig.Text = info.BatteryPercent + "%";
            lblBatteryBig.ForeColor = accentColor;

            pillStatus.SetStatus(chgStr, accentColor);
            pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(60 * dpiScale));

            barBattery.Value = info.BatteryPercent;
            barBattery.ProgressColor = accentColor;

            string normalTimeStr = info.LastUpdated.ToString("HH:mm:ss");
            lblUpdateTime.Text = "最后同步: " + normalTimeStr + " · 自动侦测硬件插拔";

            // Update Performance Card dynamic stages
            if (info.DpiStages != null && info.DpiStages.Length > 0)
            {
                dpiStageValues = info.DpiStages;
                for (int i = 0; i < btnDpiStages.Length; i++)
                {
                    if (i < dpiStageValues.Length)
                    {
                        btnDpiStages[i].Text = dpiStageValues[i].ToString();
                        btnDpiStages[i].Selected = (dpiStageValues[i] == info.Dpi);
                        btnDpiStages[i].Visible = true;
                    }
                    else
                    {
                        btnDpiStages[i].Visible = false;
                    }
                }

                // Update tray menu items
                if (dpiMenu != null)
                {
                    dpiMenu.DropDownItems.Clear();
                    for (int i = 0; i < dpiStageValues.Length; i++)
                    {
                        int val = dpiStageValues[i];
                        var sub = new ToolStripMenuItem(val + " DPI", null, (s, e) => SetDpiFromUI(val));
                        sub.Tag = val;
                        sub.Checked = (val == info.Dpi);
                        dpiMenu.DropDownItems.Add(sub);
                    }
                }
            }

            if (info.PollingRate > 0)
            {
                for (int i = 0; i < btnRates.Length; i++)
                {
                    if (i < pollingRateValues.Length)
                    {
                        btnRates[i].Selected = (pollingRateValues[i] == info.PollingRate);
                    }
                }

                if (rateMenu != null)
                {
                    foreach (ToolStripItem item in rateMenu.DropDownItems)
                    {
                        var mi = item as ToolStripMenuItem;
                        if (mi != null && mi.Tag != null)
                        {
                            mi.Checked = ((int)mi.Tag == info.PollingRate);
                        }
                    }
                }
            }

            UpdateStatusDisplay();
            UpdateTrayIcon(info.BatteryPercent, info.IsCharging, true, false);

            if (lowBatteryAlertEnabled && !info.IsCharging)
            {
                if (info.BatteryPercent <= 20)
                {
                    if (!lastLowAlertFired)
                    {
                        trayIcon.ShowBalloonTip(4000, "雷蛇鼠标电量不足", string.Format("当前电量仅剩 {0}%，请及时连接充电器！", info.BatteryPercent), ToolTipIcon.Warning);
                        lastLowAlertFired = true;
                    }
                }
                else
                {
                    lastLowAlertFired = false;
                }
            }
            else
            {
                lastLowAlertFired = false;
            }

            if (showTipIfManual)
            {
                string perfTip = (info.Dpi > 0 && info.PollingRate > 0) ? string.Format("\n性能: {0} DPI · {1} Hz", info.Dpi, info.PollingRate) : "";
                trayIcon.ShowBalloonTip(1500, info.DeviceName, string.Format("电量: {0}% ({1}){2}\n更新时间: {3}", info.BatteryPercent, chgStr, perfTip, normalTimeStr), ToolTipIcon.Info);
            }
        }
    }
}
