using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class ModernDarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color bgCol = Theme.Black;
        private static readonly Color hoverCol = Color.FromArgb(38, 44, 58);
        private static readonly Color hoverBorder = Color.FromArgb(58, 68, 90);
        private static readonly Color textCol = Color.FromArgb(235, 240, 250);
        private static readonly Color disabledCol = Color.FromArgb(100, 110, 128);
        private static readonly Color separatorCol = Color.FromArgb(42, 48, 62);
        private static readonly Color accentGreen = Theme.Green;

        public ModernDarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, 4))
                {
                    using (SolidBrush b = new SolidBrush(hoverCol))
                    {
                        e.Graphics.FillPath(b, path);
                    }
                    using (Pen p = new Pen(hoverBorder, 1f))
                    {
                        e.Graphics.DrawPath(p, path);
                    }
                }
            }
            else if (e.Item.OwnerItem != null && e.Item.Pressed)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, 4))
                using (SolidBrush b = new SolidBrush(hoverCol))
                {
                    e.Graphics.FillPath(b, path);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color color = textCol;
            if (!e.Item.Enabled)
            {
                color = disabledCol;
            }
            else if (e.Item.Tag != null && e.Item.Tag.ToString() == "Header")
            {
                color = accentGreen;

                // Draw status dot in left image margin column (exactly aligned with checkmarks)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float dotY = e.Item.Height / 2f;
                float dotX = 17f;
                float radius = 3.5f;
                using (SolidBrush dotBrush = new SolidBrush(accentGreen))
                {
                    e.Graphics.FillEllipse(dotBrush, dotX - radius, dotY - radius, radius * 2f, radius * 2f);
                }
            }
            else if (e.Item.Selected)
            {
                color = Color.White;
            }

            // Text is drawn starting at e.TextRectangle.X (aligning Header 'R' with other text below)
            Rectangle textRect = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textRect, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(separatorCol, 1f))
            {
                e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float centerY = e.Item.Height / 2f;
            float cx = 17f;

            PointF[] pts = new PointF[] {
                new PointF(cx - 5.0f, centerY - 0.5f),
                new PointF(cx - 1.5f, centerY + 3.5f),
                new PointF(cx + 5.5f, centerY - 4.5f)
            };
            using (Pen p = new Pen(accentGreen, 2.2f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(p, pts);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float centerY = e.Item.Height / 2f;
            float cx = e.ArrowRectangle.X + e.ArrowRectangle.Width / 2f;

            Color arrowColor = e.Item.Selected ? Color.White : Color.FromArgb(145, 155, 175);
            PointF[] arrow = new PointF[] {
                new PointF(cx - 2.5f, centerY - 4.5f),
                new PointF(cx + 2.0f, centerY),
                new PointF(cx - 2.5f, centerY + 4.5f)
            };
            using (Pen p = new Pen(arrowColor, 1.8f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(p, arrow);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        private class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder { get { return Color.FromArgb(48, 54, 68); } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(38, 44, 58); } }
            public override Color ToolStripDropDownBackground { get { return Theme.Black; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Black; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Black; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Black; } }
        }
    }
}
