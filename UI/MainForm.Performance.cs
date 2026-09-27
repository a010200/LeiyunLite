using System;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        private DpiMonitor dpiMonitor;
        private DpiOsdForm osdForm;

        private void StartDpiMonitor()
        {
            if (dpiMonitor != null) return;
            dpiMonitor = new DpiMonitor(device, reading => {
                try
                {
                    if (IsDisposed || Disposing) return;
                    BeginInvoke(new Action(() => {
                        if (IsDisposed || Disposing) return;
                        if (isMouseSleeping || lastInfo == null || !lastInfo.IsConnected || lastInfo.BatteryPercent <= 0)
                        {
                            isMouseSleeping = false;
                            RefreshBatteryStatus(false);
                        }
                        if (reading.Changed) OnDpiChanged(reading.Dpi, reading.Stage, reading.Count);
                    }));
                }
                catch (InvalidOperationException) { }
            });
            dpiMonitor.Start();
        }

        private void StopDpiMonitor()
        {
            if (dpiMonitor != null) { dpiMonitor.Dispose(); dpiMonitor = null; }
        }

        private void OnDpiChanged(int newDpi, int newStage, int stageCount)
        {
            if (dpiOsdEnabled && osdForm != null)
            {
                osdForm.ShowDpi(newDpi, newStage, stageCount);
            }

            if (lastInfo != null)
            {
                lastInfo.Dpi = newDpi;
                lastInfo.DpiStage = newStage;
                lastInfo.DpiStageCount = stageCount;
            }

            // Highlight corresponding segment button
            if (btnDpiStages != null)
            {
                for (int i = 0; i < btnDpiStages.Length; i++)
                {
                    if (i < dpiStageValues.Length)
                    {
                        btnDpiStages[i].Selected = (dpiStageValues[i] == newDpi);
                    }
                }
            }

            // Update Tray DPI submenu checks
            if (dpiMenu != null)
            {
                foreach (ToolStripItem item in dpiMenu.DropDownItems)
                {
                    var mi = item as ToolStripMenuItem;
                    if (mi != null && mi.Tag != null)
                    {
                        mi.Checked = ((int)mi.Tag == newDpi);
                    }
                }
            }

            // Update status text
            UpdateStatusDisplay();
        }

        public void SetDpiFromUI(int dpi)
        {
            new Thread(() => {
                int targetStage = -1;
                for (int i = 0; i < dpiStageValues.Length; i++)
                {
                    if (dpiStageValues[i] == dpi)
                    {
                        targetStage = i + 1;
                        break;
                    }
                }

                if (targetStage > 0)
                {
                    device.SetRazerDpiStage(targetStage);
                }
                device.SetRazerDpi(dpi);

                int queryDpi, stage, count;
                if (device.FastQueryDpi(out queryDpi, out stage, out count))
                {
                    this.BeginInvoke(new Action(() => {
                        OnDpiChanged(queryDpi, stage, count);
                    }));
                }
                else
                {
                    this.BeginInvoke(new Action(() => {
                        OnDpiChanged(dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length);
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        public void SetPollingRateFromUI(int hz)
        {
            new Thread(() => {
                bool ok = device.SetRazerPollingRate(hz);
                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        if (lastInfo != null) lastInfo.PollingRate = hz;

                        if (btnRates != null)
                        {
                            for (int i = 0; i < btnRates.Length; i++)
                            {
                                if (i < pollingRateValues.Length)
                                {
                                    btnRates[i].Selected = (pollingRateValues[i] == hz);
                                }
                            }
                        }

                        if (rateMenu != null)
                        {
                            foreach (ToolStripItem item in rateMenu.DropDownItems)
                            {
                                var mi = item as ToolStripMenuItem;
                                if (mi != null && mi.Tag != null)
                                {
                                    mi.Checked = ((int)mi.Tag == hz);
                                }
                            }
                        }

                        UpdateStatusDisplay();
                        if (trayIcon != null)
                        {
                            trayIcon.ShowBalloonTip(1200, "回报率已切换", "鼠标回报率已设置为: " + hz + " Hz", ToolTipIcon.Info);
                        }
                    }));
                }
            }) { IsBackground = true }.Start();
        }
    }
}
