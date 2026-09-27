using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class DpiOsdForm : Form
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (displayTimer != null) displayTimer.Dispose();
                if (fadeTimer != null) fadeTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        private int currentDpi = 3000;
        private int currentStage = 4;
        private int totalStages = 5;
        private int osdStyle = 0; // 0 = Centered Capsule, 1 = Stepped Gauge, 2 = Compact Top-Right
        private System.Windows.Forms.Timer displayTimer;
        private System.Windows.Forms.Timer fadeTimer;
        private float dpiScale = 1.0f;

        public int OsdStyle
        {
            get { return osdStyle; }
            set { osdStyle = value; }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: never steal focus from full-screen games
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: hide from Alt+Tab
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST: render above foreground windows
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT: mouse clicks pass directly through to games
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        public DpiOsdForm(float scale)
        {
            this.dpiScale = scale;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            // Note: Do NOT set this.TopMost = true; WinForms TopMost setter calls SetWindowPos without SWP_NOACTIVATE!
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Theme.Black;
            this.DoubleBuffered = true;
            this.Size = new Size((int)(160 * dpiScale), (int)(58 * dpiScale));

            // Pre-create native HWND so it never incurs creation latency during gaming
            IntPtr forceHandle = this.Handle;

            displayTimer = new System.Windows.Forms.Timer();
            displayTimer.Interval = 2000;
            displayTimer.Tick += (s, e) =>
            {
                displayTimer.Stop();
                fadeTimer.Start();
            };

            fadeTimer = new System.Windows.Forms.Timer();
            fadeTimer.Interval = 20;
            fadeTimer.Tick += (s, e) =>
            {
                if (this.Opacity > 0.08)
                {
                    this.Opacity -= 0.12;
                }
                else
                {
                    fadeTimer.Stop();
                    HideOsd();
                }
            };
        }

        public void HideOsd()
        {
            try
            {
                if (this.IsHandleCreated)
                {
                    SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_NOSENDCHANGING);
                }
            }
            catch { }
        }

        public void ShowDpi(int dpi, int stage, int count)
        {
            this.currentDpi = dpi;
            this.currentStage = stage;
            this.totalStages = count > 0 ? count : 5;

            displayTimer.Stop();
            fadeTimer.Stop();
            this.Opacity = 1.0;

            int targetW;
            int targetH;
            if (osdStyle == 0)
            {
                targetW = 160;
                targetH = 58;
            }
            else if (osdStyle == 1)
            {
                targetW = (dpi >= 10000) ? 192 : 184;
                targetH = 62;
            }
            else
            {
                targetW = (dpi >= 10000) ? 178 : 170;
                targetH = 62;
            }

            int scaledW = (int)(targetW * dpiScale);
            int scaledH = (int)(targetH * dpiScale);

            Screen targetScreen = Screen.FromPoint(Cursor.Position);
            if (targetScreen == null) targetScreen = Screen.PrimaryScreen;
            Rectangle wa = targetScreen.WorkingArea;
            int margin = (int)(24 * dpiScale);

            int targetX = wa.Right - scaledW - margin;
            int targetY = wa.Bottom - scaledH - margin;

            // Pure Win32 display without activation or Z-order change that could minimize full-screen games
            SetWindowPos(this.Handle, HWND_TOPMOST, targetX, targetY, scaledW, scaledH,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOSENDCHANGING);

            this.Invalidate();
            displayTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 1. Common Card Background & Subtle Dark Edge
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = RoundedCard.GetRoundRectF(rect, 8f * dpiScale))
            {
                using (SolidBrush bgBrush = new SolidBrush(Theme.Black))
                {
                    g.FillPath(bgBrush, path);
                }
                using (Pen borderPen = new Pen(Color.FromArgb(42, 47, 60), 1.0f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            using (FontFamily ffYaHei = new FontFamily("Microsoft YaHei UI"))
            using (FontFamily ffSegoe = new FontFamily("Segoe UI"))
            {
                StringFormat sf = StringFormat.GenericTypographic;

                if (osdStyle == 0)
                {
                    // ================= STYLE 0: 居中电竞胶囊 (Ultra-Compact, Symmetrical HUD) =================
                    // 1. Top Title (Centered Dot + "鼠标 DPI")
                    float topY = 7f * dpiScale;
                    float titleSize = 10f * dpiScale;
                    using (GraphicsPath titlePath = new GraphicsPath())
                    {
                        titlePath.AddString("鼠标 DPI", ffYaHei, (int)FontStyle.Regular, titleSize, PointF.Empty, sf);
                        RectangleF titleBounds = titlePath.GetBounds();

                        float dotD = 4f * dpiScale;
                        float dotGap = 4.5f * dpiScale;
                        float topContentW = dotD + dotGap + titleBounds.Width;
                        float topStartX = (Width - topContentW) / 2f;

                        float dotY = topY + (titleBounds.Height - dotD) / 2f;
                        using (SolidBrush dotBrush = new SolidBrush(Theme.Green))
                            g.FillEllipse(dotBrush, topStartX, dotY, dotD, dotD);

                        using (Matrix mTitle = new Matrix())
                        {
                            mTitle.Translate(topStartX + dotD + dotGap - titleBounds.Left, topY - titleBounds.Top);
                            titlePath.Transform(mTitle);
                        }
                        using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
                            g.FillPath(titleBrush, titlePath);

                        // 3. Bottom Capsules (Row 3)
                        int segCount = totalStages > 0 ? totalStages : 5;
                        float segW = 19f * dpiScale;
                        float segH = 3.5f * dpiScale;
                        float segGap = 4f * dpiScale;
                        float totalSegW = segCount * segW + (segCount - 1) * segGap;
                        float segStartX = (Width - totalSegW) / 2f;
                        float bottomMargin = 8f * dpiScale;
                        float segY = Height - bottomMargin - segH;

                        for (int i = 1; i <= segCount; i++)
                        {
                            RectangleF segRect = new RectangleF(segStartX + (i - 1) * (segW + segGap), segY, segW, segH);
                            using (GraphicsPath sp = RoundedCard.GetRoundRectF(segRect, 1.75f * dpiScale))
                            {
                                Color c = (i == currentStage) ? Theme.Green : Color.FromArgb(36, 40, 52);
                                using (SolidBrush b = new SolidBrush(c))
                                    g.FillPath(b, sp);
                            }
                        }

                        // 2. Middle Value ("3000" + "DPI") - Perfectly Centered & Exact Baseline Aligned
                        float numFontSize = 20f * dpiScale;
                        float unitFontSize = 10f * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            string numStr = currentDpi.ToString();
                            numPath.AddString(numStr, ffSegoe, (int)FontStyle.Bold, numFontSize, PointF.Empty, sf);
                            RectangleF numBounds = numPath.GetBounds();

                            unitPath.AddString("DPI", ffSegoe, (int)FontStyle.Bold, unitFontSize, PointF.Empty, sf);
                            RectangleF unitBounds = unitPath.GetBounds();

                            float valGap = 4f * dpiScale;
                            float totalValW = numBounds.Width + valGap + unitBounds.Width;
                            float valStartX = (Width - totalValW) / 2f;

                            float midZoneTop = topY + titleBounds.Height;
                            float midZoneBottom = segY;
                            float midZoneH = midZoneBottom - midZoneTop;
                            float targetNumTop = midZoneTop + (midZoneH - numBounds.Height) / 2f;
                            float targetBaseline = targetNumTop + numBounds.Height;

                            using (Matrix mNum = new Matrix())
                            {
                                mNum.Translate(valStartX - numBounds.Left, targetNumTop - numBounds.Top);
                                numPath.Transform(mNum);
                            }
                            using (SolidBrush whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 255)))
                                g.FillPath(whiteBrush, numPath);

                            float targetUnitTop = targetBaseline - unitBounds.Height;
                            using (Matrix mUnit = new Matrix())
                            {
                                mUnit.Translate(valStartX + numBounds.Width + valGap - unitBounds.Left, targetUnitTop - unitBounds.Top);
                                unitPath.Transform(mUnit);
                            }
                            using (SolidBrush greenBrush = new SolidBrush(Theme.Green))
                                g.FillPath(greenBrush, unitPath);
                        }
                    }
                }
                else if (osdStyle == 1)
                {
                    // ================= STYLE 1: 右侧阶梯能量计 (Faithful & Optical Left Aligned) =================
                    float padLeft = 14f * dpiScale;
                    float padRight = 14f * dpiScale;

                    float titleFontSize = 9.5f * dpiScale;
                    using (GraphicsPath titlePath = new GraphicsPath())
                    {
                        titlePath.AddString("鼠标 DPI", ffYaHei, (int)FontStyle.Regular, titleFontSize, PointF.Empty, sf);
                        RectangleF titleBounds = titlePath.GetBounds();

                        float numFontSize = (currentDpi >= 10000 ? 19.5f : 21.5f) * dpiScale;
                        float unitFontSize = 10f * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            string numStr = currentDpi.ToString();
                            numPath.AddString(numStr, ffSegoe, (int)FontStyle.Bold, numFontSize, PointF.Empty, sf);
                            RectangleF numBounds = numPath.GetBounds();

                            unitPath.AddString("DPI", ffSegoe, (int)FontStyle.Bold, unitFontSize, PointF.Empty, sf);
                            RectangleF unitBounds = unitPath.GetBounds();

                            float rowGap = 3.5f * dpiScale;
                            float totalLeftBlockH = titleBounds.Height + rowGap + numBounds.Height;
                            float startY = (Height - totalLeftBlockH) / 2f;

                            // 1. Top Left: Dot + "鼠标 DPI"
                            float topY = startY;
                            float dotD = 4.5f * dpiScale;
                            float dotGap = 5f * dpiScale;
                            float dotX = padLeft;
                            float dotY = topY + (titleBounds.Height - dotD) / 2f;

                            using (SolidBrush dotBrush = new SolidBrush(Theme.Green))
                                g.FillEllipse(dotBrush, dotX, dotY, dotD, dotD);

                            float titleX = dotX + dotD + dotGap;
                            using (Matrix mTitle = new Matrix())
                            {
                                mTitle.Translate(titleX - titleBounds.Left, topY - titleBounds.Top);
                                titlePath.Transform(mTitle);
                            }
                            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
                                g.FillPath(titleBrush, titlePath);

                            // 2. Large Value: Optical left anchor with dotX
                            float numY = topY + titleBounds.Height + rowGap;
                            float baselineY = numY + numBounds.Height;

                            using (Matrix mNum = new Matrix())
                            {
                                mNum.Translate(padLeft - numBounds.Left, numY - numBounds.Top);
                                numPath.Transform(mNum);
                            }
                            using (SolidBrush whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 255)))
                                g.FillPath(whiteBrush, numPath);

                            // Unit: Exact baseline alignment
                            float valGap = 4f * dpiScale;
                            float unitX = padLeft + numBounds.Width + valGap;
                            float unitY = baselineY - unitBounds.Height;

                            using (Matrix mUnit = new Matrix())
                            {
                                mUnit.Translate(unitX - unitBounds.Left, unitY - unitBounds.Top);
                                unitPath.Transform(mUnit);
                            }
                            using (SolidBrush greenBrush = new SolidBrush(Theme.Green))
                                g.FillPath(greenBrush, unitPath);

                            // 3. Right Stepped Energy Bars
                            int barCount = totalStages > 0 ? totalStages : 5;
                            float barW = 4.5f * dpiScale;
                            float barGap = 4f * dpiScale;
                            float totalBarsW = barCount * barW + (barCount - 1) * barGap;
                            float barStartX = Width - padRight - totalBarsW;
                            float barBaseY = baselineY + (0.5f * dpiScale);

                            float minH = 8f * dpiScale;
                            float maxH = 25f * dpiScale;
                            float stepH = (maxH - minH) / (barCount - 1);

                            for (int i = 1; i <= barCount; i++)
                            {
                                float barH = minH + (i - 1) * stepH;
                                float barY = barBaseY - barH;
                                RectangleF barRect = new RectangleF(barStartX + (i - 1) * (barW + barGap), barY, barW, barH);
                                using (GraphicsPath bp = RoundedCard.GetRoundRectF(barRect, 1.75f * dpiScale))
                                {
                                    Color c;
                                    if (i == currentStage)
                                        c = Theme.Green; // Active stage: Razer green
                                    else if (i < currentStage)
                                        c = Theme.Green;  // Lower stages: deep green
                                    else
                                        c = Color.FromArgb(38, 43, 56);  // Higher stages: dark grey track

                                    using (SolidBrush b = new SolidBrush(c))
                                        g.FillPath(b, bp);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // ================= STYLE 2: 顶置微型指示段 (Faithful & Balanced Compact) =================
                    float padLeft = 14f * dpiScale;
                    float padRight = 14f * dpiScale;

                    float titleFontSize = 9.5f * dpiScale;
                    using (GraphicsPath titlePath = new GraphicsPath())
                    {
                        titlePath.AddString("鼠标 DPI", ffYaHei, (int)FontStyle.Regular, titleFontSize, PointF.Empty, sf);
                        RectangleF titleBounds = titlePath.GetBounds();

                        float numFontSize = (currentDpi >= 10000 ? 19.5f : 21.5f) * dpiScale;
                        float unitFontSize = 10f * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            string numStr = currentDpi.ToString();
                            numPath.AddString(numStr, ffSegoe, (int)FontStyle.Bold, numFontSize, PointF.Empty, sf);
                            RectangleF numBounds = numPath.GetBounds();

                            unitPath.AddString("DPI", ffSegoe, (int)FontStyle.Bold, unitFontSize, PointF.Empty, sf);
                            RectangleF unitBounds = unitPath.GetBounds();

                            float rowGap = 3.5f * dpiScale;
                            float totalLeftBlockH = titleBounds.Height + rowGap + numBounds.Height;
                            float startY = (Height - totalLeftBlockH) / 2f;
                            float topY = startY;

                            // 1. Top Left: Accent Bar + "鼠标 DPI"
                            float barW = 3f * dpiScale;
                            float barGap = 5.5f * dpiScale;
                            float barH = titleBounds.Height - 1f * dpiScale;
                            RectangleF pillRect = new RectangleF(padLeft, topY + 0.5f * dpiScale, barW, barH);
                            using (GraphicsPath vp = RoundedCard.GetRoundRectF(pillRect, 1.25f * dpiScale))
                            using (SolidBrush vb = new SolidBrush(Theme.Green))
                                g.FillPath(vb, vp);

                            float titleX = padLeft + barW + barGap;
                            using (Matrix mTitle = new Matrix())
                            {
                                mTitle.Translate(titleX - titleBounds.Left, topY - titleBounds.Top);
                                titlePath.Transform(mTitle);
                            }
                            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
                                g.FillPath(titleBrush, titlePath);

                            // 2. Top Right: 5 Mini Capsules
                            int segCount = totalStages > 0 ? totalStages : 5;
                            float segW = 11f * dpiScale;
                            float segH = 4.2f * dpiScale;
                            float segGap = 3.5f * dpiScale;
                            float totalSegW = segCount * segW + (segCount - 1) * segGap;
                            float segStartX = Width - padRight - totalSegW;
                            float segY = topY + (titleBounds.Height - segH) / 2f;

                            for (int i = 1; i <= segCount; i++)
                            {
                                RectangleF segRect = new RectangleF(segStartX + (i - 1) * (segW + segGap), segY, segW, segH);
                                using (GraphicsPath sp = RoundedCard.GetRoundRectF(segRect, 1.75f * dpiScale))
                                {
                                    Color c = (i == currentStage) ? Theme.Green : Color.FromArgb(38, 43, 56);
                                    using (SolidBrush b = new SolidBrush(c))
                                        g.FillPath(b, sp);
                                }
                            }

                            // 3. Bottom Row: Number + Unit
                            float numY = topY + titleBounds.Height + rowGap;
                            float baselineY = numY + numBounds.Height;

                            using (Matrix mNum = new Matrix())
                            {
                                mNum.Translate(padLeft - numBounds.Left, numY - numBounds.Top);
                                numPath.Transform(mNum);
                            }
                            using (SolidBrush whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 255)))
                                g.FillPath(whiteBrush, numPath);

                            float valGap = 4f * dpiScale;
                            float unitX = padLeft + numBounds.Width + valGap;
                            float unitY = baselineY - unitBounds.Height;

                            using (Matrix mUnit = new Matrix())
                            {
                                mUnit.Translate(unitX - unitBounds.Left, unitY - unitBounds.Top);
                                unitPath.Transform(mUnit);
                            }
                            using (SolidBrush greenBrush = new SolidBrush(Theme.Green))
                                g.FillPath(greenBrush, unitPath);
                        }
                    }
                }
            }
        }
    }
}
