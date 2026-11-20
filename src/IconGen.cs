using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace QuickConvertTools
{
    public static class IconGen
    {
        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: IconGen.exe <output.ico>");
                return 1;
            }

            string outPath = args[0];
            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            int[] sizes = new int[] { 16, 32, 48, 256 };
            List<byte[]> pngFrames = new List<byte[]>();

            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = RenderIcon(sizes[i]))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngFrames.Add(ms.ToArray());
                }
            }

            WriteMultiSizeIco(outPath, sizes, pngFrames);
            Console.WriteLine("Generated icon: " + outPath);
            return 0;
        }

        private static Bitmap RenderIcon(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float pad = size * 0.06f;
                float rectSize = size - pad * 2f;
                float radius = size * 0.22f;

                using (GraphicsPath bgPath = CreateRoundedRect(pad, pad, rectSize, rectSize, radius))
                using (LinearGradientBrush bgBrush = new LinearGradientBrush(
                    new PointF(0f, 0f),
                    new PointF(size, size),
                    Color.FromArgb(255, 14, 165, 233),
                    Color.FromArgb(255, 37, 99, 235)))
                {
                    g.FillPath(bgBrush, bgPath);
                }

                float stroke = Math.Max(1.6f, size * 0.105f);
                float cx = size * 0.5f;
                float cy = size * 0.5f;
                float r = size * 0.255f;

                using (Pen pen = new Pen(Color.White, stroke))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;

                    // Top arc: from 205 deg sweep 135 deg
                    g.DrawArc(pen, cx - r, cy - r, r * 2f, r * 2f, 205f, 130f);
                    // Bottom arc: from 25 deg sweep 135 deg
                    g.DrawArc(pen, cx - r, cy - r, r * 2f, r * 2f, 25f, 130f);
                }

                // Draw arrow heads
                float arrowLen = Math.Max(2.5f, size * 0.15f);
                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                {
                    DrawArrowHead(g, whiteBrush, cx, cy, r, 335f, arrowLen, true);
                    DrawArrowHead(g, whiteBrush, cx, cy, r, 155f, arrowLen, true);
                }
            }
            return bmp;
        }

        private static void DrawArrowHead(Graphics g, Brush brush, float cx, float cy, float r, float angleDeg, float len, bool clockwise)
        {
            double rad = angleDeg * Math.PI / 180.0;
            float tipX = cx + (float)(r * Math.Cos(rad));
            float tipY = cy + (float)(r * Math.Sin(rad));

            double dirAngle = rad + (clockwise ? Math.PI / 2.0 : -Math.PI / 2.0);
            float forwardX = tipX + (float)(len * 0.45 * Math.Cos(dirAngle));
            float forwardY = tipY + (float)(len * 0.45 * Math.Sin(dirAngle));

            double backLeft = dirAngle + Math.PI * 0.78;
            double backRight = dirAngle - Math.PI * 0.78;

            PointF[] pts = new PointF[]
            {
                new PointF(forwardX, forwardY),
                new PointF(forwardX + (float)(len * Math.Cos(backLeft)), forwardY + (float)(len * Math.Sin(backLeft))),
                new PointF(forwardX + (float)(len * Math.Cos(backRight)), forwardY + (float)(len * Math.Sin(backRight)))
            };
            g.FillPolygon(brush, pts);
        }

        private static GraphicsPath CreateRoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath path = new GraphicsPath();
            float d = r * 2f;
            path.AddArc(x, y, d, d, 180f, 90f);
            path.AddArc(x + w - d, y, d, d, 270f, 90f);
            path.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
            path.AddArc(x, y + h - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private static void WriteMultiSizeIco(string path, int[] sizes, List<byte[]> frames)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0); // reserved
                bw.Write((ushort)1); // type = icon
                bw.Write((ushort)frames.Count);

                int offset = 6 + 16 * frames.Count;
                for (int i = 0; i < frames.Count; i++)
                {
                    int sz = sizes[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)0); // colors
                    bw.Write((byte)0); // reserved
                    bw.Write((ushort)1); // planes
                    bw.Write((ushort)32); // bpp
                    bw.Write((uint)frames[i].Length);
                    bw.Write((uint)offset);
                    offset += frames[i].Length;
                }

                for (int i = 0; i < frames.Count; i++)
                {
                    bw.Write(frames[i]);
                }
            }
        }
    }
}
