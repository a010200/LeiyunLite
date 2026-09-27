using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace RazerBatteryTray.Desktop
{
    // Original code-native mark: split green disc + an angular L / lightning.
    // Not the official Razer or Synapse emblem. All sizes share exact HEX colors.
    internal static class LiteIconBuilder
    {
        private static void Draw(Graphics g, int size)
        {
            g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size / 128f, size / 128f);
            using (var green = new SolidBrush(Color.FromArgb(0x44, 0xD6, 0x2C)))
            using (var black = new SolidBrush(Color.FromArgb(0x14, 0x14, 0x14)))
            {
                g.FillEllipse(green, 8, 8, 112, 112);
                g.FillPolygon(black, new[] { new PointF(40, 30), new PointF(58, 30), new PointF(58, 76), new PointF(90, 76), new PointF(79, 96), new PointF(40, 96) });
                if (size >= 24) g.FillPolygon(black, new[] { new PointF(78, 29), new PointF(97, 29), new PointF(79, 61), new PointF(67, 61) });
                using (var erase = new Pen(Color.Transparent, 7))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawLine(erase, 94, 108, 122, 108);
                }
            }
        }
        private static void Main(string[] args)
        {
            int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 }; var bytes = new List<byte[]>();
            foreach (int size in sizes)
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var stream = new MemoryStream())
            { Draw(graphics, size); bitmap.Save(stream, ImageFormat.Png); bytes.Add(stream.ToArray()); if (size == 256) bitmap.Save(args[1], ImageFormat.Png); }
            using (var writer = new BinaryWriter(File.Create(args[0])))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] % 256)); writer.Write((byte)(sizes[i] % 256)); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(bytes[i].Length); writer.Write(offset); offset += bytes[i].Length; }
                foreach (var b in bytes) writer.Write(b);
            }
        }
    }
}
