using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace RazerBatteryTray
{
    internal static class TrayIconRenderer
    {
        private static readonly byte[][] Digits3 = new byte[][] {
            new byte[] { 0x7, 0x5, 0x5, 0x5, 0x7 }, // 0
            new byte[] { 0x1, 0x3, 0x1, 0x1, 0x1 }, // 1 (width 2, or 1 in 100)
            new byte[] { 0x7, 0x1, 0x7, 0x4, 0x7 }, // 2
            new byte[] { 0x7, 0x1, 0x7, 0x1, 0x7 }, // 3
            new byte[] { 0x5, 0x5, 0x7, 0x1, 0x1 }, // 4
            new byte[] { 0x7, 0x4, 0x7, 0x1, 0x7 }, // 5
            new byte[] { 0x7, 0x4, 0x7, 0x5, 0x7 }, // 6
            new byte[] { 0x7, 0x1, 0x1, 0x1, 0x1 }, // 7
            new byte[] { 0x7, 0x5, 0x7, 0x5, 0x7 }, // 8
            new byte[] { 0x7, 0x5, 0x7, 0x1, 0x7 }, // 9
        };

        private static readonly byte[][] Digits4x7 = new byte[][] {
            new byte[] { 0x6, 0x9, 0x9, 0x9, 0x9, 0x9, 0x6 }, // 0
            new byte[] { 0x2, 0x6, 0x2, 0x2, 0x2, 0x2, 0x7 }, // 1 (width 3)
            new byte[] { 0x6, 0x9, 0x1, 0x2, 0x4, 0x8, 0xF }, // 2
            new byte[] { 0xE, 0x1, 0x1, 0x6, 0x1, 0x1, 0xE }, // 3
            new byte[] { 0x9, 0x9, 0x9, 0xF, 0x1, 0x1, 0x1 }, // 4
            new byte[] { 0xF, 0x8, 0xE, 0x1, 0x1, 0x9, 0x6 }, // 5
            new byte[] { 0x6, 0x8, 0xE, 0x9, 0x9, 0x9, 0x6 }, // 6
            new byte[] { 0xF, 0x1, 0x2, 0x2, 0x4, 0x4, 0x4 }, // 7
            new byte[] { 0x6, 0x9, 0x9, 0x6, 0x9, 0x9, 0x6 }, // 8
            new byte[] { 0x6, 0x9, 0x9, 0x7, 0x1, 0x1, 0x6 }, // 9
        };

        internal static Bitmap DrawTrayBitmap(int percent, bool isCharging, bool isConnected, int style, int iconSize, bool isSleeping = false)
        {
            if (iconSize >= 24)
            {
                return DrawTrayBitmap24(percent, isCharging, isConnected, style, isSleeping);
            }
            else
            {
                return DrawTrayBitmap16(percent, isCharging, isConnected, style, isSleeping);
            }
        }

        private static Bitmap DrawTrayBitmap24(int percent, bool isCharging, bool isConnected, int style, bool isSleeping = false)
        {
            Bitmap bmp = new Bitmap(24, 24);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (!isConnected)
                {
                    Color whiteCol = Color.White;
                    using (SolidBrush bg = new SolidBrush(Theme.Black))
                    {
                        g.FillRectangle(bg, 2, 4, 18, 16);
                    }
                    using (Pen p = new Pen(whiteCol, 1f))
                    {
                        g.DrawLine(p, 2, 3, 19, 3);
                        g.DrawLine(p, 2, 20, 19, 20);
                        g.DrawLine(p, 1, 4, 1, 19);
                        g.DrawLine(p, 20, 4, 20, 19);
                        bmp.SetPixel(1, 3, whiteCol);
                        bmp.SetPixel(1, 20, whiteCol);
                        bmp.SetPixel(20, 3, whiteCol);
                        bmp.SetPixel(20, 20, whiteCol);
                    }
                    using (SolidBrush cap = new SolidBrush(whiteCol))
                    {
                        g.FillRectangle(cap, 21, 8, 2, 8);
                    }
                    DrawQuestionMark24(bmp, 10, 8, Color.FromArgb(160, 165, 180));
                    return bmp;
                }

                Color accentColor = isSleeping ? Color.FromArgb(255, 183, 77) :
                                    ((percent > 40 || isCharging) ? Theme.Green :
                                    ((percent > 20) ? Color.FromArgb(255, 214, 0) : Color.FromArgb(255, 50, 65)));

                if (style == 1) // 醒目数字能量表 (Centered digits)
                {
                    using (SolidBrush bg = new SolidBrush(Theme.Black))
                        g.FillRectangle(bg, 0, 0, 24, 24);
                    using (Pen border = new Pen(Color.FromArgb(48, 56, 74), 1f))
                        g.DrawRectangle(border, 0, 0, 23, 23);

                    if (isCharging)
                        DrawBolt24(bmp, 11, 10, Color.White);
                    else
                        DrawDigits24(bmp, percent, 11, 5, Color.White, false);

                    int barW = Math.Max(2, (int)(18 * (percent / 100.0)));
                    using (SolidBrush barB = new SolidBrush(accentColor))
                        g.FillRectangle(barB, 3, 17, barW, 4);
                }
                else // 现代胶囊电池 (Default, sealed closed white corners)
                {
                    Color whiteCol = Color.White;

                    using (SolidBrush bg = new SolidBrush(Theme.Black))
                        g.FillRectangle(bg, 2, 4, 18, 16);

                    int fillW = Math.Max(1, (int)(18 * (percent / 100.0)));
                    using (SolidBrush fb = new SolidBrush(accentColor))
                        g.FillRectangle(fb, 2, 4, fillW, 16);

                    if (isCharging)
                        DrawBolt24(bmp, 10, 11, Color.White);
                    else
                        DrawDigits24(bmp, percent, 10, 8, Color.White, true);

                    using (Pen p = new Pen(whiteCol, 1f))
                    {
                        g.DrawLine(p, 2, 3, 19, 3);
                        g.DrawLine(p, 2, 20, 19, 20);
                        g.DrawLine(p, 1, 4, 1, 19);
                        g.DrawLine(p, 20, 4, 20, 19);
                    }
                    bmp.SetPixel(1, 3, whiteCol);
                    bmp.SetPixel(1, 20, whiteCol);
                    bmp.SetPixel(20, 3, whiteCol);
                    bmp.SetPixel(20, 20, whiteCol);

                    using (SolidBrush cap = new SolidBrush(whiteCol))
                        g.FillRectangle(cap, 21, 8, 2, 8);
                }
            }
            return bmp;
        }

        private static Bitmap DrawTrayBitmap16(int percent, bool isCharging, bool isConnected, int style, bool isSleeping = false)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (!isConnected)
                {
                    Color whiteCol = Color.White;
                    using (SolidBrush bg = new SolidBrush(Theme.Black))
                        g.FillRectangle(bg, 1, 3, 12, 10);
                    using (Pen p = new Pen(whiteCol, 1f))
                    {
                        g.DrawLine(p, 1, 2, 12, 2);
                        g.DrawLine(p, 1, 13, 12, 13);
                        g.DrawLine(p, 0, 3, 0, 12);
                        g.DrawLine(p, 13, 3, 13, 12);
                    }
                    bmp.SetPixel(0, 2, whiteCol);
                    bmp.SetPixel(0, 13, whiteCol);
                    bmp.SetPixel(13, 2, whiteCol);
                    bmp.SetPixel(13, 13, whiteCol);
                    using (SolidBrush cap = new SolidBrush(whiteCol))
                        g.FillRectangle(cap, 14, 5, 2, 6);
                    DrawQuestionMark16(bmp, 6, 5, Color.FromArgb(160, 165, 180));
                    return bmp;
                }

                Color accentColor = isSleeping ? Color.FromArgb(255, 183, 77) :
                                    ((percent > 40 || isCharging) ? Theme.Green :
                                    ((percent > 20) ? Color.FromArgb(255, 214, 0) : Color.FromArgb(255, 50, 65)));

                if (style == 1) // 醒目数字能量表 (Centered digits)
                {
                    using (SolidBrush bg = new SolidBrush(Theme.Black))
                        g.FillRectangle(bg, 0, 0, 16, 16);
                    using (Pen border = new Pen(Color.FromArgb(48, 56, 74), 1f))
                        g.DrawRectangle(border, 0, 0, 15, 15);

                    if (isCharging)
                    {
                        bmp.SetPixel(14, 1, Theme.Green);
                        bmp.SetPixel(13, 2, Theme.Green);
                        bmp.SetPixel(14, 2, Theme.Green);
                        bmp.SetPixel(13, 3, Theme.Green);
                        DrawBolt16(bmp, 6, 4, Color.White);
                    }
                    else
                    {
                        DrawDigits16(bmp, percent, 7, 3, Color.White, false);
                    }

                    int barW = Math.Max(2, (int)(12 * (percent / 100.0)));
                    using (SolidBrush barB = new SolidBrush(accentColor))
                        g.FillRectangle(barB, 2, 12, barW, 3);
                }
                else // 现代胶囊电池 (Default, sealed closed white corners)
                {
                    Color whiteCol = Color.White;

                    using (SolidBrush bg = new SolidBrush(Theme.Black))
                        g.FillRectangle(bg, 1, 3, 12, 10);

                    int fillW = Math.Max(1, (int)(12 * (percent / 100.0)));
                    using (SolidBrush fb = new SolidBrush(accentColor))
                        g.FillRectangle(fb, 1, 3, fillW, 10);

                    if (isCharging)
                        DrawBolt16(bmp, 6, 5, Color.White);
                    else
                        DrawDigits16(bmp, percent, 7, 5, Color.White, true);

                    using (Pen p = new Pen(whiteCol, 1f))
                    {
                        g.DrawLine(p, 1, 2, 12, 2);
                        g.DrawLine(p, 1, 13, 12, 13);
                        g.DrawLine(p, 0, 3, 0, 12);
                        g.DrawLine(p, 13, 3, 13, 12);
                    }
                    bmp.SetPixel(0, 2, whiteCol);
                    bmp.SetPixel(0, 13, whiteCol);
                    bmp.SetPixel(13, 2, whiteCol);
                    bmp.SetPixel(13, 13, whiteCol);

                    using (SolidBrush cap = new SolidBrush(whiteCol))
                        g.FillRectangle(cap, 14, 5, 2, 6);
                }
            }
            return bmp;
        }

private static void DrawDigits24(Bitmap bmp, int value, int centerX, int startY, Color color, bool smartContrast = false)
        {
            string s = value.ToString();
            int totalW = 0;
            int[] widths = new int[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '0';
                int w = (d == 1) ? 3 : 4;
                widths[i] = w;
                totalW += w;
            }
            totalW += s.Length - 1;

            int curX = centerX - (totalW / 2);
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '0';
                int w = widths[i];
                byte[] rows = Digits4x7[d];
                for (int r = 0; r < 7; r++)
                {
                    byte row = rows[r];
                    for (int c = 0; c < w; c++)
                    {
                        int bit = (w == 3) ? (2 - c) : (3 - c);
                        if ((row & (1 << bit)) != 0)
                        {
                            int px = curX + c;
                            int py = startY + r;
                            if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                            {
                                Color drawCol = color;
                                if (smartContrast)
                                {
                                    Color bg = bmp.GetPixel(px, py);
                                    int lum = (int)(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
                                    drawCol = (lum > 110) ? Theme.Black : Color.White;
                                }
                                bmp.SetPixel(px, py, drawCol);
                            }
                        }
                    }
                }
                curX += w + 1;
            }
        }

        private static void DrawBolt24(Bitmap bmp, int cx, int cy, Color color)
        {
            Point[] pts = new Point[] {
                new Point(cx + 1, cy - 5), new Point(cx - 3, cy), new Point(cx, cy),
                new Point(cx - 2, cy + 5), new Point(cx + 3, cy - 1), new Point(cx, cy - 1)
            };
            using (Graphics g = Graphics.FromImage(bmp))
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPolygon(b, pts);
            }
        }

        private static void DrawQuestionMark24(Bitmap bmp, int cx, int cy, Color color)
        {
            int[,] qPts = new int[,] { {0,0}, {1,0}, {2,0}, {3,0}, {3,1}, {3,2}, {2,3}, {1,4}, {1,6} };
            for (int i = 0; i < qPts.GetLength(0); i++)
            {
                int px = cx + qPts[i, 0];
                int py = cy + qPts[i, 1];
                if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                    bmp.SetPixel(px, py, color);
            }
        }

        private static void DrawDigits16(Bitmap bmp, int value, int centerX, int startY, Color color, bool smartContrast = false)
        {
            string s = value.ToString();
            int totalW = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '0';
                int w = (d == 1 && s.Length == 3) ? 1 : (d == 1 ? 2 : 3);
                totalW += w;
                if (i < s.Length - 1) totalW += 1;
            }

            int curX = centerX - (totalW / 2);
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '0';
                byte[] rows = Digits3[d];
                int w = (d == 1 && s.Length == 3) ? 1 : (d == 1 ? 2 : 3);
                for (int r = 0; r < 5; r++)
                {
                    byte row = rows[r];
                    for (int c = 0; c < w; c++)
                    {
                        int bit = (w == 1) ? 0 : ((w == 2) ? (1 - c) : (2 - c));
                        if ((row & (1 << bit)) != 0)
                        {
                            int px = curX + c;
                            int py = startY + r;
                            if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                            {
                                Color drawCol = color;
                                if (smartContrast)
                                {
                                    Color bg = bmp.GetPixel(px, py);
                                    int lum = (int)(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
                                    drawCol = (lum > 110) ? Theme.Black : Color.White;
                                }
                                bmp.SetPixel(px, py, drawCol);
                            }
                        }
                    }
                }
                curX += w + 1;
            }
        }

        private static void DrawBolt16(Bitmap bmp, int startX, int startY, Color color)
        {
            int[,] bolt = new int[,] {
                {0, 2}, {1, 1}, {2, 0},
                {1, 2}, {2, 2}, {3, 2},
                {0, 3}, {1, 3}, {2, 3},
                {1, 4}, {2, 5}
            };
            for (int i = 0; i < bolt.GetLength(0); i++)
            {
                int px = startX + bolt[i, 0];
                int py = startY + bolt[i, 1];
                if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                    bmp.SetPixel(px, py, color);
            }
        }

        private static void DrawQuestionMark16(Bitmap bmp, int cx, int cy, Color color)
        {
            int[,] qPts = new int[,] { {0,0}, {1,0}, {2,0}, {2,1}, {1,2}, {1,4} };
            for (int i = 0; i < qPts.GetLength(0); i++)
            {
                int px = cx + qPts[i, 0];
                int py = cy + qPts[i, 1];
                if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                    bmp.SetPixel(px, py, color);
            }
        }
    }
}
