using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace RazerBatteryTray
{
    // Vector-drawn application icon, sharing the UI's exact theme tokens.
    internal static class IconBuilder
    {
        private static void Main(string[] args)
        {
            int[] sizes = { 16, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bitmap))
            using (var green = new SolidBrush(Theme.Green))
            using (var pen = new Pen(Theme.Green, 6))
            using (var stream = new MemoryStream())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(size / 128f, size / 128f);
                g.Clear(Theme.Black); g.DrawEllipse(pen, 26, 11, 76, 106);
                g.DrawLine(pen, 64, 13, 64, 38); g.FillRectangle(green, 60, 29, 8, 16);
                g.DrawRectangle(pen, 43, 64, 39, 24); g.FillRectangle(green, 83, 71, 6, 10);
                g.FillPolygon(green, new Point[] { new Point(66, 65), new Point(55, 78), new Point(64, 78), new Point(60, 90), new Point(74, 73), new Point(66, 73) });
                bitmap.Save(stream, ImageFormat.Png); images.Add(stream.ToArray());
            }
            using (var writer = new BinaryWriter(File.Create(args[0])))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
                }
                foreach (var bytes in images) writer.Write(bytes);
            }
        }
    }
}
