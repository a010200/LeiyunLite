using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class RoundedCard : Panel
    {
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        public RoundedCard()
        {
            BorderColor = Color.FromArgb(42, 46, 58);
            CornerRadius = 10;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Theme.Black;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GetRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(BackColor))
                {
                    g.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        public static GraphicsPath GetRoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > bounds.Width) d = bounds.Width;
            if (d > bounds.Height) d = bounds.Height;
            if (d <= 0) d = 1;

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath GetRoundRectF(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class ModernButton : Control
    {
        private bool isHovered = false;
        private bool isPressed = false;

        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        public ModernButton()
        {
            NormalColor = Theme.Green;
            HoverColor = Theme.Green;
            PressedColor = Theme.Green;
            BorderColor = Color.Transparent;
            CornerRadius = 8;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; isPressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); isPressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color fill = NormalColor;
            if (isPressed) fill = PressedColor;
            else if (isHovered) fill = HoverColor;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    public class ModernCheckBox : Control
    {
        private bool isChecked = false;
        private bool isHovered = false;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernCheckBox()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int boxSize = 16;
            int boxY = (Height - boxSize) / 2;
            Rectangle boxRect = new Rectangle(1, boxY, boxSize, boxSize);

            Color boxBg = isChecked ? (isHovered ? Theme.Green : Theme.Green) : (isHovered ? Color.FromArgb(38, 42, 54) : Theme.Black);
            Color boxBorder = isChecked ? Theme.Green : (isHovered ? Color.FromArgb(90, 98, 120) : Color.FromArgb(58, 63, 78));

            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(boxRect, 4))
            {
                using (SolidBrush brush = new SolidBrush(boxBg))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(boxBorder, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            if (isChecked)
            {
                using (Pen checkPen = new Pen(Theme.Black, 2.0f))
                {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    PointF[] checkPoints = new PointF[]
                    {
                        new PointF(boxRect.Left + 3.5f, boxRect.Top + 8.5f),
                        new PointF(boxRect.Left + 6.5f, boxRect.Top + 11.5f),
                        new PointF(boxRect.Left + 12.5f, boxRect.Top + 4.5f)
                    };
                    g.DrawLines(checkPen, checkPoints);
                }
            }

            int textX = boxRect.Right + 8;
            Rectangle textRect = new Rectangle(textX, 0, Width - textX, Height);
            Color textCol = isHovered ? Color.White : Color.FromArgb(220, 226, 238);
            TextRenderer.DrawText(g, Text, Font, textRect, textCol,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    public class ModernSegmentButton : Control
    {
        private bool isSelected = false;
        private bool isHovered = false;

        public bool Selected
        {
            get { return isSelected; }
            set { if (isSelected != value) { isSelected = value; Invalidate(); } }
        }

        public ModernSegmentButton()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg;
            Color border;
            Color fg;

            if (isSelected)
            {
                bg = isHovered ? Theme.Green : Theme.Green;
                border = Theme.Green;
                fg = Theme.Black;
            }
            else
            {
                bg = isHovered ? Color.FromArgb(38, 42, 54) : Theme.Black;
                border = isHovered ? Color.FromArgb(80, 88, 108) : Color.FromArgb(48, 52, 65);
                fg = isHovered ? Color.White : Color.FromArgb(180, 188, 205);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, 6))
            {
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(border, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            Font useFont = isSelected ? new Font(Font, FontStyle.Bold) : Font;
            TextRenderer.DrawText(g, Text, useFont, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    public class SubtleDivider : Control
    {
        public SubtleDivider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int y = Height / 2;
            using (Pen pen = new Pen(Color.FromArgb(38, 42, 54), 1f))
            {
                g.DrawLine(pen, 0, y, Width, y);
            }
        }
    }

    public class ModernProgressBar : Control
    {
        private int value = 0;
        public int Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        public Color TrackColor { get; set; }
        public Color ProgressColor { get; set; }

        public ModernProgressBar()
        {
            TrackColor = Color.FromArgb(38, 42, 53);
            ProgressColor = Theme.Green;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int h = Height;
            int radius = h / 2;
            Rectangle rect = new Rectangle(0, 0, Width, h);

            using (GraphicsPath trackPath = RoundedCard.GetRoundedRectangle(rect, radius))
            {
                using (SolidBrush brush = new SolidBrush(TrackColor))
                {
                    g.FillPath(brush, trackPath);
                }
            }

            if (value > 0)
            {
                int fillWidth = (int)((Width * (value / 100.0f)));
                if (fillWidth < radius * 2) fillWidth = radius * 2;
                if (fillWidth > Width) fillWidth = Width;

                Rectangle fillRect = new Rectangle(0, 0, fillWidth, h);
                using (GraphicsPath fillPath = RoundedCard.GetRoundedRectangle(fillRect, radius))
                {
                    using (SolidBrush brush = new SolidBrush(ProgressColor))
                    {
                        g.FillPath(brush, fillPath);
                    }
                }
            }
        }
    }

    public class StatusPill : Control
    {
        private string statusText = "检测中...";
        private Color statusColor = Theme.Green;

        public void SetStatus(string text, Color color)
        {
            this.statusText = text;
            this.statusColor = color;
            Invalidate();
        }

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, Height / 2))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(35, statusColor.R, statusColor.G, statusColor.B)))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Color.FromArgb(100, statusColor.R, statusColor.G, statusColor.B), 1.2f))
                {
                    g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(g, statusText, Font, ClientRectangle, statusColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
