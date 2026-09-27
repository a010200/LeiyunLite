using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        // Windows 11 DWM Immersive Dark Mode & Styling API
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_CAPTION_COLOR = 35;
        const int DWMWA_TEXT_COLOR = 36;
        private void ApplyModernWin11Theme()
        {
            try
            {
                int trueVal = 1;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueVal, sizeof(int));
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueVal, sizeof(int));

                int roundVal = 2;
                DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundVal, sizeof(int));

                int captionBg = 0x00141414;
                DwmSetWindowAttribute(this.Handle, DWMWA_CAPTION_COLOR, ref captionBg, sizeof(int));

                int captionText = 0x00FFFFFF;
                DwmSetWindowAttribute(this.Handle, DWMWA_TEXT_COLOR, ref captionText, sizeof(int));

                int borderCol = 0x003A2E2A;
                DwmSetWindowAttribute(this.Handle, DWMWA_BORDER_COLOR, ref borderCol, sizeof(int));
            }
            catch { }
        }
        private void InitializeFormUI()
        {
            this.Text = Theme.Title;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Theme.Black;
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ShowInTaskbar = true;

            int padX = (int)(18 * dpiScale);
            int baseW = (int)(470 * dpiScale);
            int cardW = baseW - (padX * 2);

            // ================= CARD 1: BATTERY & DEVICE STATUS =================
            int card1Y = (int)(14 * dpiScale);
            int card1H = (int)(176 * dpiScale);

            cardBattery = new RoundedCard();
            cardBattery.Location = new Point(padX, card1Y);
            cardBattery.Size = new Size(cardW, card1H);
            cardBattery.CornerRadius = (int)(10 * dpiScale);
            cardBattery.BackColor = Theme.Black;
            cardBattery.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardBattery);

            lblDeviceName = new Label();
            lblDeviceName.Text = "正在检测雷蛇设备...";
            lblDeviceName.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            lblDeviceName.ForeColor = Color.White;
            lblDeviceName.Location = new Point((int)(18 * dpiScale), (int)(16 * dpiScale));
            lblDeviceName.AutoSize = true;
            cardBattery.Controls.Add(lblDeviceName);

            lblConnDot = new Label();
            lblConnDot.Text = "● 已连接";
            lblConnDot.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
            lblConnDot.ForeColor = Theme.Green;
            lblConnDot.Location = new Point(cardW - (int)(105 * dpiScale), (int)(18 * dpiScale));
            lblConnDot.Size = new Size((int)(90 * dpiScale), (int)(22 * dpiScale));
            lblConnDot.TextAlign = ContentAlignment.MiddleRight;
            cardBattery.Controls.Add(lblConnDot);

            lblBatteryBig = new Label();
            lblBatteryBig.Text = "--%";
            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
            lblBatteryBig.ForeColor = Theme.Green;
            lblBatteryBig.Location = new Point((int)(16 * dpiScale), (int)(46 * dpiScale));
            lblBatteryBig.AutoSize = true;
            cardBattery.Controls.Add(lblBatteryBig);

            // Status Pill: Power / Charging State (sole status badge in Card 1)
            pillStatus = new StatusPill();
            pillStatus.Location = new Point((int)(180 * dpiScale), (int)(60 * dpiScale));
            pillStatus.Size = new Size((int)(110 * dpiScale), (int)(32 * dpiScale));
            pillStatus.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold, GraphicsUnit.Point);
            pillStatus.SetStatus("⚡ 充电中", Theme.Green);
            cardBattery.Controls.Add(pillStatus);

            barBattery = new ModernProgressBar();
            barBattery.Location = new Point((int)(18 * dpiScale), (int)(118 * dpiScale));
            barBattery.Size = new Size(cardW - (int)(36 * dpiScale), (int)(10 * dpiScale));
            barBattery.Value = 0;
            barBattery.TrackColor = Color.FromArgb(38, 42, 53);
            barBattery.ProgressColor = Theme.Green;
            cardBattery.Controls.Add(barBattery);

            lblUpdateTime = new Label();
            lblUpdateTime.Text = "最后同步: --:--:-- · 自动侦测硬件插拔";
            lblUpdateTime.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            lblUpdateTime.ForeColor = Color.FromArgb(135, 142, 156);
            lblUpdateTime.Location = new Point((int)(18 * dpiScale), (int)(142 * dpiScale));
            lblUpdateTime.AutoSize = true;
            cardBattery.Controls.Add(lblUpdateTime);

            // ================= CARD 2: PERFORMANCE & TUNING =================
            int card2Y = card1Y + card1H + (int)(12 * dpiScale);
            int card2H = (int)(122 * dpiScale);

            cardPerformance = new RoundedCard();
            cardPerformance.Location = new Point(padX, card2Y);
            cardPerformance.Size = new Size(cardW, card2H);
            cardPerformance.CornerRadius = (int)(10 * dpiScale);
            cardPerformance.BackColor = Theme.Black;
            cardPerformance.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardPerformance);

            int cPad = (int)(18 * dpiScale);
            int secW = cardW - (cPad * 2);

            lblPerfTitle = new Label();
            lblPerfTitle.Text = "鼠标性能与档位调节";
            lblPerfTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            lblPerfTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblPerfTitle.Location = new Point(cPad, (int)(14 * dpiScale));
            lblPerfTitle.AutoSize = true;
            cardPerformance.Controls.Add(lblPerfTitle);

            // Row 1: DPI 档位
            int r1Y = (int)(40 * dpiScale);
            int lblW = (int)(75 * dpiScale);
            int segH = (int)(28 * dpiScale);

            lblDpiTitle = new Label();
            lblDpiTitle.Text = "DPI 档位";
            lblDpiTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblDpiTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblDpiTitle.Location = new Point(cPad, r1Y + (int)(4 * dpiScale));
            lblDpiTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            cardPerformance.Controls.Add(lblDpiTitle);

            int dpiStartX = cPad + lblW;
            int dpiGap = (int)(6 * dpiScale);
            int dpiSegW = (secW - lblW - (dpiGap * 4)) / 5;

            btnDpiStages = new ModernSegmentButton[5];
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                int dpiVal = dpiStageValues[i];
                var btn = new ModernSegmentButton();
                btn.Text = dpiVal.ToString();
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Location = new Point(dpiStartX + i * (dpiSegW + dpiGap), r1Y);
                btn.Size = new Size(dpiSegW, segH);
                btn.Click += (s, e) => SetDpiFromUI(dpiStageValues[index]);
                cardPerformance.Controls.Add(btn);
                btnDpiStages[i] = btn;
            }

            // Row 2: 回报率
            int r2Y = (int)(76 * dpiScale);

            lblRateTitle = new Label();
            lblRateTitle.Text = "回报率";
            lblRateTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblRateTitle.Location = new Point(cPad, r2Y + (int)(4 * dpiScale));
            lblRateTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            cardPerformance.Controls.Add(lblRateTitle);

            int hzStartX = cPad + lblW;
            int hzGap = (int)(6 * dpiScale);
            int hzSegW = (secW - lblW - (hzGap * 3)) / 4;

            btnRates = new ModernSegmentButton[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                int hzVal = pollingRateValues[i];
                var btn = new ModernSegmentButton();
                btn.Text = hzVal + " Hz";
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Location = new Point(hzStartX + i * (hzSegW + hzGap), r2Y);
                btn.Size = new Size(hzSegW, segH);
                btn.Click += (s, e) => SetPollingRateFromUI(pollingRateValues[index]);
                cardPerformance.Controls.Add(btn);
                btnRates[i] = btn;
            }

            // ================= CARD 3: CONFIGURATION & PREFERENCES =================
            int card3Y = card2Y + card2H + (int)(12 * dpiScale);
            int card3H = (int)(244 * dpiScale);

            cardSettings = new RoundedCard();
            cardSettings.Location = new Point(padX, card3Y);
            cardSettings.Size = new Size(cardW, card3H);
            cardSettings.CornerRadius = (int)(10 * dpiScale);
            cardSettings.BackColor = Theme.Black;
            cardSettings.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardSettings);

            lblSettingsTitle = new Label();
            lblSettingsTitle.Text = "功能设置与偏好";
            lblSettingsTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            lblSettingsTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblSettingsTitle.Location = new Point(cPad, (int)(14 * dpiScale));
            lblSettingsTitle.AutoSize = true;
            cardSettings.Controls.Add(lblSettingsTitle);

            // Row 1: 托盘图标样式 (Capsule / Badge)
            int row1Y = (int)(40 * dpiScale);
            int lblTitleW = (int)(95 * dpiScale);

            lblStyleTitle = new Label();
            lblStyleTitle.Text = "托盘图标样式";
            lblStyleTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblStyleTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblStyleTitle.Location = new Point(cPad, row1Y + (int)(4 * dpiScale));
            lblStyleTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblStyleTitle);

            int seg1W = (secW - lblTitleW - (int)(8 * dpiScale)) / 2;
            int seg1X = cPad + lblTitleW;

            btnStyleCapsule = new ModernSegmentButton();
            btnStyleCapsule.Text = "现代胶囊电池";
            btnStyleCapsule.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnStyleCapsule.Location = new Point(seg1X, row1Y);
            btnStyleCapsule.Size = new Size(seg1W, segH);
            btnStyleCapsule.Click += (s, e) => SetTrayStyle(0, false);
            cardSettings.Controls.Add(btnStyleCapsule);

            btnStyleNum = new ModernSegmentButton();
            btnStyleNum.Text = "醒目数字能量表";
            btnStyleNum.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnStyleNum.Location = new Point(seg1X + seg1W + (int)(8 * dpiScale), row1Y);
            btnStyleNum.Size = new Size(seg1W, segH);
            btnStyleNum.Click += (s, e) => SetTrayStyle(1, false);
            cardSettings.Controls.Add(btnStyleNum);

            // Row 2: 自动刷新频率 (30s / 1m / 5m)
            int row2Y = (int)(76 * dpiScale);

            lblIntervalTitle = new Label();
            lblIntervalTitle.Text = "自动刷新频率";
            lblIntervalTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblIntervalTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblIntervalTitle.Location = new Point(cPad, row2Y + (int)(4 * dpiScale));
            lblIntervalTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblIntervalTitle);

            int seg2Gap = (int)(6 * dpiScale);
            int seg2W = (secW - lblTitleW - (seg2Gap * 2)) / 3;

            btnInt30s = new ModernSegmentButton();
            btnInt30s.Text = "30 秒";
            btnInt30s.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt30s.Location = new Point(seg1X, row2Y);
            btnInt30s.Size = new Size(seg2W, segH);
            btnInt30s.Click += (s, e) => SetInterval(30000, false);
            cardSettings.Controls.Add(btnInt30s);

            btnInt1m = new ModernSegmentButton();
            btnInt1m.Text = "1 分钟";
            btnInt1m.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt1m.Location = new Point(seg1X + seg2W + seg2Gap, row2Y);
            btnInt1m.Size = new Size(seg2W, segH);
            btnInt1m.Click += (s, e) => SetInterval(60000, false);
            cardSettings.Controls.Add(btnInt1m);

            btnInt5m = new ModernSegmentButton();
            btnInt5m.Text = "5 分钟";
            btnInt5m.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt5m.Location = new Point(seg1X + (seg2W + seg2Gap) * 2, row2Y);
            btnInt5m.Size = new Size(seg2W, segH);
            btnInt5m.Click += (s, e) => SetInterval(300000, false);
            cardSettings.Controls.Add(btnInt5m);

            // Row 3: DPI 浮窗样式 (居中胶囊 / 阶梯能量 / 顶置微标)
            int row3Y = (int)(112 * dpiScale);

            lblOsdStyleTitle = new Label();
            lblOsdStyleTitle.Text = "DPI 浮窗样式";
            lblOsdStyleTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblOsdStyleTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblOsdStyleTitle.Location = new Point(cPad, row3Y + (int)(4 * dpiScale));
            lblOsdStyleTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblOsdStyleTitle);

            btnOsdStyleCapsule = new ModernSegmentButton();
            btnOsdStyleCapsule.Text = "居中胶囊";
            btnOsdStyleCapsule.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleCapsule.Location = new Point(seg1X, row3Y);
            btnOsdStyleCapsule.Size = new Size(seg2W, segH);
            btnOsdStyleCapsule.Click += (s, e) => SetOsdStyle(0, true);
            cardSettings.Controls.Add(btnOsdStyleCapsule);

            btnOsdStyleGauge = new ModernSegmentButton();
            btnOsdStyleGauge.Text = "阶梯能量";
            btnOsdStyleGauge.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleGauge.Location = new Point(seg1X + seg2W + seg2Gap, row3Y);
            btnOsdStyleGauge.Size = new Size(seg2W, segH);
            btnOsdStyleGauge.Click += (s, e) => SetOsdStyle(1, true);
            cardSettings.Controls.Add(btnOsdStyleGauge);

            btnOsdStyleCompact = new ModernSegmentButton();
            btnOsdStyleCompact.Text = "顶置微标";
            btnOsdStyleCompact.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleCompact.Location = new Point(seg1X + (seg2W + seg2Gap) * 2, row3Y);
            btnOsdStyleCompact.Size = new Size(seg2W, segH);
            btnOsdStyleCompact.Click += (s, e) => SetOsdStyle(2, true);
            cardSettings.Controls.Add(btnOsdStyleCompact);

            // Row 4: Modern subtle divider
            divSettings = new SubtleDivider();
            divSettings.Location = new Point(cPad, (int)(150 * dpiScale));
            divSettings.Size = new Size(secW, (int)(8 * dpiScale));
            cardSettings.Controls.Add(divSettings);

            // Row 5: Checkboxes in 2x2 grid
            int chkY1 = (int)(162 * dpiScale);
            int chkY2 = (int)(190 * dpiScale);
            int chkGap = (int)(14 * dpiScale);
            int chkW = (secW - chkGap) / 2;
            int chkH = (int)(24 * dpiScale);

            chkAutoStart = new ModernCheckBox();
            chkAutoStart.Text = "开机自动启动";
            chkAutoStart.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoStart.Location = new Point(cPad, chkY1);
            chkAutoStart.Size = new Size(chkW, chkH);
            chkAutoStart.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetAutoStart(chkAutoStart.Checked);
            };
            cardSettings.Controls.Add(chkAutoStart);

            chkAutoStartShowUI = new ModernCheckBox();
            chkAutoStartShowUI.Text = "开机弹出主窗口";
            chkAutoStartShowUI.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoStartShowUI.Location = new Point(cPad + chkW + chkGap, chkY1);
            chkAutoStartShowUI.Size = new Size(chkW, chkH);
            chkAutoStartShowUI.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetAutoStartShowUI(chkAutoStartShowUI.Checked, true);
            };
            cardSettings.Controls.Add(chkAutoStartShowUI);

            chkLowAlert = new ModernCheckBox();
            chkLowAlert.Text = "低电量提醒 (≤20%)";
            chkLowAlert.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            chkLowAlert.Location = new Point(cPad, chkY2);
            chkLowAlert.Size = new Size(chkW, chkH);
            chkLowAlert.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetLowBatteryAlert(chkLowAlert.Checked);
            };
            cardSettings.Controls.Add(chkLowAlert);

            chkDpiOsd = new ModernCheckBox();
            chkDpiOsd.Text = "DPI 切换屏幕提示 (OSD)";
            chkDpiOsd.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            chkDpiOsd.Location = new Point(cPad + chkW + chkGap, chkY2);
            chkDpiOsd.Size = new Size(chkW, chkH);
            chkDpiOsd.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetDpiOsdEnabled(chkDpiOsd.Checked);
            };
            cardSettings.Controls.Add(chkDpiOsd);

            // Row 6: Hint inside Card 3
            lblSettingsTip = new Label();
            lblSettingsTip.Text = "注：切换线缆/接收器或按键调 DPI 时将自动即时同步，无需等待计时周期";
            lblSettingsTip.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblSettingsTip.ForeColor = Color.FromArgb(120, 128, 142);
            lblSettingsTip.Location = new Point(cPad, (int)(218 * dpiScale));
            lblSettingsTip.AutoSize = true;
            cardSettings.Controls.Add(lblSettingsTip);

            // ================= BOTTOM ROW: ACTIONS =================
            int card4Y = card3Y + card3H + (int)(14 * dpiScale);
            int btnH = (int)(38 * dpiScale);
            int btnGap = (int)(14 * dpiScale);
            int btnW = (cardW - btnGap) / 2;

            btnRefresh = new ModernButton();
            btnRefresh.Text = "立即刷新";
            btnRefresh.Location = new Point(padX, card4Y);
            btnRefresh.Size = new Size(btnW, btnH);
            btnRefresh.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnRefresh.NormalColor = Theme.Green;
            btnRefresh.HoverColor = Theme.Green;
            btnRefresh.PressedColor = Theme.Green;
            btnRefresh.ForeColor = Theme.Black;
            btnRefresh.CornerRadius = (int)(8 * dpiScale);
            btnRefresh.Click += (s, e) => RefreshBatteryStatus(true);
            this.Controls.Add(btnRefresh);

            btnHideToTray = new ModernButton();
            btnHideToTray.Text = "最小化到托盘";
            btnHideToTray.Location = new Point(padX + btnW + btnGap, card4Y);
            btnHideToTray.Size = new Size(btnW, btnH);
            btnHideToTray.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnHideToTray.NormalColor = Theme.Black;
            btnHideToTray.HoverColor = Color.FromArgb(48, 52, 65);
            btnHideToTray.PressedColor = Theme.Black;
            btnHideToTray.BorderColor = Color.FromArgb(58, 63, 78);
            btnHideToTray.ForeColor = Color.White;
            btnHideToTray.CornerRadius = (int)(8 * dpiScale);
            btnHideToTray.Click += (s, e) => {
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "已最小化到托盘", "雷云lite正在系统托盘运行，随时点击右下角托盘图标唤出。", ToolTipIcon.Info);
            };
            this.Controls.Add(btnHideToTray);

            var macroButton = new ModernButton();
            macroButton.Text = "宏与按键 / 滚轮绑定";
            macroButton.Location = new Point(padX, card4Y + btnH + (int)(8 * dpiScale));
            macroButton.Size = new Size(cardW, btnH);
            macroButton.Font = btnRefresh.Font;
            macroButton.NormalColor = Theme.Black; macroButton.HoverColor = Color.FromArgb(45, 45, 45);
            macroButton.PressedColor = Theme.Black; macroButton.ForeColor = Theme.Green; macroButton.BorderColor = Theme.Green;
            macroButton.Click += (s, e) => ShowMacroEditor();
            Controls.Add(macroButton);
            int clientH = macroButton.Bottom + (int)(16 * dpiScale);
            int availableH = Math.Max(300, Screen.FromControl(this).WorkingArea.Height - (int)(70 * dpiScale));
            if (clientH > availableH)
            {
                AutoScroll = true; AutoScrollMinSize = new Size(baseW, clientH);
                ClientSize = new Size(baseW + SystemInformation.VerticalScrollBarWidth, availableH);
            }
            else this.ClientSize = new Size(baseW, clientH);
        }
    }
}
