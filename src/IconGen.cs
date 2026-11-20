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
        private static readonly string[] AllTargets = new string[]
        {
            "mp3", "wav", "m4a", "flac", "ogg",
            "mp4", "mov", "mp4-lite", "webm", "gif",
            "png", "jpg", "bmp", "webp", "ico", "pdf"
        };

        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: IconGen.exe <output.ico> [--assets <assetsDir>] [--target-icons <iconsDir>] [--preview <preview.png>]");
                return 1;
            }

            string outPath = args[0];
            string assetsDir = null;
            string targetIconsDir = null;
            string previewPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--assets", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    assetsDir = args[++i];
                }
                else if (string.Equals(args[i], "--target-icons", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    targetIconsDir = args[++i];
                }
                else if (string.Equals(args[i], "--preview", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    previewPath = args[++i];
                }
            }

            EnsureDirForFile(outPath);
            SaveIconForKey("root", outPath);
            Console.WriteLine("Generated icon: " + outPath);

            if (!string.IsNullOrEmpty(assetsDir))
            {
                if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);
                SavePng("root", Path.Combine(assetsDir, "StoreLogo.png"), 50);
                SavePng("root", Path.Combine(assetsDir, "Square44x44Logo.png"), 44);
                SavePng("root", Path.Combine(assetsDir, "Square150x150Logo.png"), 150);
                Console.WriteLine("Generated MSIX assets in: " + assetsDir);
            }

            if (!string.IsNullOrEmpty(targetIconsDir))
            {
                if (!Directory.Exists(targetIconsDir)) Directory.CreateDirectory(targetIconsDir);
                for (int i = 0; i < AllTargets.Length; i++)
                {
                    string key = AllTargets[i];
                    string icoPath = Path.Combine(targetIconsDir, key + ".ico");
                    SaveIconForKey(key, icoPath);
                }
                Console.WriteLine("Generated " + AllTargets.Length + " format icons in: " + targetIconsDir);
            }

            if (!string.IsNullOrEmpty(previewPath))
            {
                EnsureDirForFile(previewPath);
                SavePreviewSheet(previewPath);
                Console.WriteLine("Generated preview sheet: " + previewPath);
            }

            return 0;
        }

        private static void EnsureDirForFile(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void SaveIconForKey(string key, string path)
        {
            int[] sizes = new int[] { 16, 20, 24, 32, 48, 256 };
            List<byte[]> pngFrames = new List<byte[]>();

            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = RenderIcon(key, sizes[i]))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngFrames.Add(ms.ToArray());
                }
            }

            WriteMultiSizeIco(path, sizes, pngFrames);
        }

        private static void SavePng(string key, string path, int size)
        {
            using (Bitmap bmp = RenderIcon(key, size))
            {
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void SavePreviewSheet(string path)
        {
            List<string> keys = new List<string>();
            keys.Add("root");
            for (int i = 0; i < AllTargets.Length; i++) keys.Add(AllTargets[i]);

            int cols = 6;
            int rows = (keys.Count + cols - 1) / cols;
            int cellW = 136;
            int cellH = 128;
            int width = cols * cellW + 24;
            int height = rows * cellH + 40;

            using (Bitmap sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font font = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(24, 26, 32));

                for (int i = 0; i < keys.Count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    int cx = 12 + col * cellW;
                    int cy = 18 + row * cellH;

                    using (Bitmap big = RenderIcon(keys[i], 64))
                    using (Bitmap small = RenderIcon(keys[i], 20))
                    {
                        g.DrawImage(big, cx + 22, cy + 8, 64, 64);
                        g.DrawImage(small, cx + 94, cy + 52, 20, 20);
                    }

                    string label = keys[i] == "root" ? "root" : keys[i].ToUpperInvariant();
                    g.DrawString(label, font, textBrush, cx + 22, cy + 82);
                }

                sheet.Save(path, ImageFormat.Png);
            }
        }

        private static Color GetBaseColor(string key)
        {
            switch (key)
            {
                case "root":     return Color.FromArgb(255, 39, 50, 68);   // Deep Slate
                case "mp3":      return Color.FromArgb(255, 17, 94, 89);   // Deep Teal
                case "wav":      return Color.FromArgb(255, 21, 94, 117);  // Dark Cyan
                case "m4a":      return Color.FromArgb(255, 49, 46, 129);  // Deep Indigo
                case "flac":     return Color.FromArgb(255, 19, 78, 74);   // Slate Teal
                case "ogg":      return Color.FromArgb(255, 22, 78, 99);   // Deep Petrol
                case "mp4":      return Color.FromArgb(255, 30, 58, 138);  // Deep Navy Blue
                case "mov":      return Color.FromArgb(255, 76, 29, 149);  // Deep Plum
                case "mp4-lite": return Color.FromArgb(255, 51, 65, 85);   // Slate Gray
                case "webm":     return Color.FromArgb(255, 88, 28, 135);  // Dark Purple
                case "gif":      return Color.FromArgb(255, 120, 53, 15);  // Warm Dark Amber
                case "png":      return Color.FromArgb(255, 20, 83, 45);   // Deep Forest
                case "jpg":      return Color.FromArgb(255, 6, 95, 70);    // Dark Emerald
                case "bmp":      return Color.FromArgb(255, 54, 83, 20);   // Deep Olive
                case "webp":     return Color.FromArgb(255, 15, 76, 92);   // Deep Ocean Teal
                case "ico":      return Color.FromArgb(255, 39, 39, 42);   // Graphite
                case "pdf":      return Color.FromArgb(255, 127, 29, 29);  // Deep Crimson
                default:         return Color.FromArgb(255, 39, 50, 68);
            }
        }

        private static Bitmap RenderIcon(string key, int size)
        {
            // Render at supersampled resolution for crisp small sizes (16/20/24px)
            int scale = size <= 32 ? 4 : (size <= 64 ? 2 : 1);
            int hi = size * scale;

            using (Bitmap hiBmp = new Bitmap(hi, hi, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(hiBmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    float pad = hi * 0.05f;
                    float rectSize = hi - pad * 2f;
                    float radius = hi * 0.22f;
                    Color baseColor = GetBaseColor(key);

                    using (GraphicsPath bgPath = CreateRoundedRect(pad, pad, rectSize, rectSize, radius))
                    using (SolidBrush bgBrush = new SolidBrush(baseColor))
                    using (Pen borderPen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, hi * 0.025f)))
                    {
                        g.FillPath(bgBrush, bgPath);
                        g.DrawPath(borderPen, bgPath);
                    }

                    DrawMonotoneGlyph(g, key, hi);
                }

                if (scale == 1)
                {
                    return (Bitmap)hiBmp.Clone();
                }

                Bitmap outBmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics gOut = Graphics.FromImage(outBmp))
                {
                    gOut.SmoothingMode = SmoothingMode.HighQuality;
                    gOut.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gOut.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gOut.DrawImage(hiBmp, 0, 0, size, size);
                }
                return outBmp;
            }
        }

        private static void DrawMonotoneGlyph(Graphics g, string key, float s)
        {
            Color fg = Color.FromArgb(248, 250, 252);
            float stroke = s * 0.082f;

            using (Pen pen = new Pen(fg, stroke))
            using (SolidBrush brush = new SolidBrush(fg))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (key)
                {
                    case "root":
                        DrawCycleArrows(g, pen, brush, s);
                        break;
                    case "mp3":
                        DrawBeamedNotes(g, pen, brush, s);
                        break;
                    case "wav":
                        DrawWaveform(g, pen, s);
                        break;
                    case "m4a":
                        DrawHeadphones(g, pen, brush, s);
                        break;
                    case "flac":
                        DrawDiamondWave(g, pen, s);
                        break;
                    case "ogg":
                        DrawSpeaker(g, pen, brush, s);
                        break;
                    case "mp4":
                        DrawPlayFrame(g, pen, brush, s);
                        break;
                    case "mov":
                        DrawFilmStrip(g, pen, brush, s);
                        break;
                    case "mp4-lite":
                        DrawCompress(g, pen, s);
                        break;
                    case "webm":
                        DrawGlobePlay(g, pen, brush, s);
                        break;
                    case "gif":
                        DrawStackedFrames(g, pen, brush, s);
                        break;
                    case "png":
                        DrawMountainFrame(g, pen, brush, s);
                        break;
                    case "jpg":
                        DrawCamera(g, pen, brush, s);
                        break;
                    case "bmp":
                        DrawPixelGrid(g, brush, s);
                        break;
                    case "webp":
                        DrawFastImage(g, pen, brush, s);
                        break;
                    case "ico":
                        DrawAppTiles(g, brush, s);
                        break;
                    case "pdf":
                        DrawDocument(g, pen, s);
                        break;
                    default:
                        DrawCycleArrows(g, pen, brush, s);
                        break;
                }
            }
        }

        // root: clean circular convert arrows
        private static void DrawCycleArrows(Graphics g, Pen pen, Brush brush, float s)
        {
            float cx = s * 0.50f;
            float cy = s * 0.50f;
            float r = s * 0.255f;
            g.DrawArc(pen, cx - r, cy - r, r * 2f, r * 2f, 205f, 130f);
            g.DrawArc(pen, cx - r, cy - r, r * 2f, r * 2f, 25f, 130f);

            float arrowLen = s * 0.145f;
            DrawArcArrowHead(g, brush, cx, cy, r, 335f, arrowLen);
            DrawArcArrowHead(g, brush, cx, cy, r, 155f, arrowLen);
        }

        private static void DrawArcArrowHead(Graphics g, Brush brush, float cx, float cy, float r, float angleDeg, float len)
        {
            double rad = angleDeg * Math.PI / 180.0;
            float tipX = cx + (float)(r * Math.Cos(rad));
            float tipY = cy + (float)(r * Math.Sin(rad));
            double dir = rad + Math.PI / 2.0;
            float fx = tipX + (float)(len * 0.45 * Math.Cos(dir));
            float fy = tipY + (float)(len * 0.45 * Math.Sin(dir));
            double bl = dir + Math.PI * 0.78;
            double br = dir - Math.PI * 0.78;

            PointF[] pts = new PointF[]
            {
                new PointF(fx, fy),
                new PointF(fx + (float)(len * Math.Cos(bl)), fy + (float)(len * Math.Sin(bl))),
                new PointF(fx + (float)(len * Math.Cos(br)), fy + (float)(len * Math.Sin(br)))
            };
            g.FillPolygon(brush, pts);
        }

        // mp3: beamed eighth notes
        private static void DrawBeamedNotes(Graphics g, Pen pen, Brush brush, float s)
        {
            float x1 = s * 0.38f;
            float x2 = s * 0.68f;
            float yTop1 = s * 0.30f;
            float yTop2 = s * 0.24f;
            float yBot1 = s * 0.68f;
            float yBot2 = s * 0.62f;

            g.DrawLine(pen, x1, yBot1, x1, yTop1);
            g.DrawLine(pen, x2, yBot2, x2, yTop2);
            using (Pen beamPen = new Pen(pen.Color, s * 0.105f))
            {
                beamPen.StartCap = LineCap.Round;
                beamPen.EndCap = LineCap.Round;
                g.DrawLine(beamPen, x1, yTop1, x2, yTop2);
            }

            float rx = s * 0.105f;
            float ry = s * 0.085f;
            g.FillEllipse(brush, x1 - rx * 1.55f, yBot1 - ry, rx * 2f, ry * 2f);
            g.FillEllipse(brush, x2 - rx * 1.55f, yBot2 - ry, rx * 2f, ry * 2f);
        }

        // wav: 5-bar symmetrical audio waveform
        private static void DrawWaveform(Graphics g, Pen pen, float s)
        {
            float[] heights = new float[] { 0.16f, 0.34f, 0.54f, 0.38f, 0.20f };
            float startX = s * 0.24f;
            float step = s * 0.13f;
            float cy = s * 0.50f;
            for (int i = 0; i < heights.Length; i++)
            {
                float x = startX + i * step;
                float halfH = s * heights[i] * 0.5f;
                g.DrawLine(pen, x, cy - halfH, x, cy + halfH);
            }
        }

        // m4a: minimalist headphones
        private static void DrawHeadphones(Graphics g, Pen pen, Brush brush, float s)
        {
            float pad = s * 0.23f;
            float w = s - pad * 2f;
            g.DrawArc(pen, pad, s * 0.23f, w, w, 180f, 180f);

            float cupW = s * 0.14f;
            float cupH = s * 0.25f;
            float cupY = s * 0.49f;
            using (GraphicsPath left = CreateRoundedRect(s * 0.19f, cupY, cupW, cupH, s * 0.06f))
            using (GraphicsPath right = CreateRoundedRect(s * 0.67f, cupY, cupW, cupH, s * 0.06f))
            {
                g.FillPath(brush, left);
                g.FillPath(brush, right);
            }
        }

        // flac: diamond lossless emblem with center sound bars
        private static void DrawDiamondWave(Graphics g, Pen pen, float s)
        {
            PointF[] diamond = new PointF[]
            {
                new PointF(s * 0.50f, s * 0.17f),
                new PointF(s * 0.83f, s * 0.50f),
                new PointF(s * 0.50f, s * 0.83f),
                new PointF(s * 0.17f, s * 0.50f)
            };
            using (Pen thin = new Pen(pen.Color, s * 0.068f))
            {
                thin.LineJoin = LineJoin.Round;
                thin.StartCap = LineCap.Round;
                thin.EndCap = LineCap.Round;
                g.DrawPolygon(thin, diamond);
                g.DrawLine(thin, s * 0.40f, s * 0.44f, s * 0.40f, s * 0.56f);
                g.DrawLine(thin, s * 0.50f, s * 0.37f, s * 0.50f, s * 0.63f);
                g.DrawLine(thin, s * 0.60f, s * 0.44f, s * 0.60f, s * 0.56f);
            }
        }

        // ogg: speaker with sound arcs
        private static void DrawSpeaker(Graphics g, Pen pen, Brush brush, float s)
        {
            PointF[] body = new PointF[]
            {
                new PointF(s * 0.20f, s * 0.40f),
                new PointF(s * 0.34f, s * 0.40f),
                new PointF(s * 0.52f, s * 0.24f),
                new PointF(s * 0.52f, s * 0.76f),
                new PointF(s * 0.34f, s * 0.60f),
                new PointF(s * 0.20f, s * 0.60f)
            };
            g.FillPolygon(brush, body);

            using (Pen arcPen = new Pen(pen.Color, s * 0.072f))
            {
                arcPen.StartCap = LineCap.Round;
                arcPen.EndCap = LineCap.Round;
                g.DrawArc(arcPen, s * 0.44f, s * 0.35f, s * 0.24f, s * 0.30f, -50f, 100f);
                g.DrawArc(arcPen, s * 0.50f, s * 0.25f, s * 0.32f, s * 0.50f, -52f, 104f);
            }
        }

        // mp4: clean rounded frame with play triangle
        private static void DrawPlayFrame(Graphics g, Pen pen, Brush brush, float s)
        {
            using (Pen framePen = new Pen(pen.Color, s * 0.068f))
            using (GraphicsPath rect = CreateRoundedRect(s * 0.18f, s * 0.22f, s * 0.64f, s * 0.56f, s * 0.11f))
            {
                g.DrawPath(framePen, rect);
            }

            PointF[] tri = new PointF[]
            {
                new PointF(s * 0.42f, s * 0.35f),
                new PointF(s * 0.42f, s * 0.65f),
                new PointF(s * 0.65f, s * 0.50f)
            };
            g.FillPolygon(brush, tri);
        }

        // mov: film strip with side perforations + play triangle
        private static void DrawFilmStrip(Graphics g, Pen pen, Brush brush, float s)
        {
            using (Pen framePen = new Pen(pen.Color, s * 0.065f))
            using (GraphicsPath rect = CreateRoundedRect(s * 0.18f, s * 0.20f, s * 0.64f, s * 0.60f, s * 0.09f))
            {
                g.DrawPath(framePen, rect);
                g.DrawLine(framePen, s * 0.31f, s * 0.20f, s * 0.31f, s * 0.80f);
                g.DrawLine(framePen, s * 0.69f, s * 0.20f, s * 0.69f, s * 0.80f);

                g.DrawLine(framePen, s * 0.18f, s * 0.40f, s * 0.31f, s * 0.40f);
                g.DrawLine(framePen, s * 0.18f, s * 0.60f, s * 0.31f, s * 0.60f);
                g.DrawLine(framePen, s * 0.69f, s * 0.40f, s * 0.82f, s * 0.40f);
                g.DrawLine(framePen, s * 0.69f, s * 0.60f, s * 0.82f, s * 0.60f);
            }

            PointF[] tri = new PointF[]
            {
                new PointF(s * 0.44f, s * 0.39f),
                new PointF(s * 0.44f, s * 0.61f),
                new PointF(s * 0.61f, s * 0.50f)
            };
            g.FillPolygon(brush, tri);
        }

        // mp4-lite: inward vertical compression arrows + center line
        private static void DrawCompress(Graphics g, Pen pen, float s)
        {
            float cx = s * 0.50f;
            // Center bar
            g.DrawLine(pen, s * 0.22f, s * 0.50f, s * 0.78f, s * 0.50f);

            // Top arrow pointing down
            g.DrawLine(pen, cx, s * 0.17f, cx, s * 0.37f);
            g.DrawLine(pen, s * 0.37f, s * 0.28f, cx, s * 0.38f);
            g.DrawLine(pen, s * 0.63f, s * 0.28f, cx, s * 0.38f);

            // Bottom arrow pointing up
            g.DrawLine(pen, cx, s * 0.83f, cx, s * 0.63f);
            g.DrawLine(pen, s * 0.37f, s * 0.72f, cx, s * 0.62f);
            g.DrawLine(pen, s * 0.63f, s * 0.72f, cx, s * 0.62f);
        }

        // webm: minimalist globe + crisp play emblem
        private static void DrawGlobePlay(Graphics g, Pen pen, Brush brush, float s)
        {
            using (Pen thin = new Pen(pen.Color, s * 0.064f))
            {
                g.DrawEllipse(thin, s * 0.20f, s * 0.20f, s * 0.60f, s * 0.60f);
                g.DrawEllipse(thin, s * 0.35f, s * 0.20f, s * 0.30f, s * 0.60f);
                g.DrawLine(thin, s * 0.20f, s * 0.50f, s * 0.80f, s * 0.50f);
            }
            // Clear small halo behind play triangle so lines never clash at 16-20px
            using (SolidBrush halo = new SolidBrush(GetBaseColor("webm")))
            {
                g.FillEllipse(halo, s * 0.34f, s * 0.34f, s * 0.32f, s * 0.32f);
            }
            PointF[] tri = new PointF[]
            {
                new PointF(s * 0.44f, s * 0.39f),
                new PointF(s * 0.44f, s * 0.61f),
                new PointF(s * 0.61f, s * 0.50f)
            };
            g.FillPolygon(brush, tri);
        }

        // gif: stacked animation frames
        private static void DrawStackedFrames(Graphics g, Pen pen, Brush brush, float s)
        {
            using (Pen thin = new Pen(pen.Color, s * 0.066f))
            {
                thin.StartCap = LineCap.Round;
                thin.EndCap = LineCap.Round;
                // Back frame outline (top-left hint)
                g.DrawLine(thin, s * 0.20f, s * 0.60f, s * 0.20f, s * 0.24f);
                g.DrawLine(thin, s * 0.20f, s * 0.24f, s * 0.64f, s * 0.24f);

                // Front frame
                using (GraphicsPath front = CreateRoundedRect(s * 0.30f, s * 0.34f, s * 0.50f, s * 0.44f, s * 0.08f))
                {
                    g.DrawPath(thin, front);
                }
            }

            PointF[] tri = new PointF[]
            {
                new PointF(s * 0.49f, s * 0.46f),
                new PointF(s * 0.49f, s * 0.66f),
                new PointF(s * 0.64f, s * 0.56f)
            };
            g.FillPolygon(brush, tri);
        }

        // png: classic clean mountain & sun picture frame
        private static void DrawMountainFrame(Graphics g, Pen pen, Brush brush, float s)
        {
            using (Pen framePen = new Pen(pen.Color, s * 0.068f))
            using (GraphicsPath rect = CreateRoundedRect(s * 0.18f, s * 0.20f, s * 0.64f, s * 0.60f, s * 0.10f))
            {
                g.DrawPath(framePen, rect);
            }

            // Sun dot
            g.FillEllipse(brush, s * 0.58f, s * 0.31f, s * 0.12f, s * 0.12f);

            // Mountains
            PointF[] mtn = new PointF[]
            {
                new PointF(s * 0.24f, s * 0.72f),
                new PointF(s * 0.43f, s * 0.46f),
                new PointF(s * 0.56f, s * 0.60f),
                new PointF(s * 0.65f, s * 0.51f),
                new PointF(s * 0.76f, s * 0.72f)
            };
            g.FillPolygon(brush, mtn);
        }

        // jpg: clean minimalist camera
        private static void DrawCamera(Graphics g, Pen pen, Brush brush, float s)
        {
            using (Pen camPen = new Pen(pen.Color, s * 0.068f))
            {
                camPen.LineJoin = LineJoin.Round;
                using (GraphicsPath body = CreateRoundedRect(s * 0.17f, s * 0.30f, s * 0.66f, s * 0.46f, s * 0.10f))
                {
                    g.DrawPath(camPen, body);
                }
                // Top viewfinder bump
                PointF[] bump = new PointF[]
                {
                    new PointF(s * 0.36f, s * 0.30f),
                    new PointF(s * 0.42f, s * 0.21f),
                    new PointF(s * 0.58f, s * 0.21f),
                    new PointF(s * 0.64f, s * 0.30f)
                };
                g.DrawLines(camPen, bump);

                // Lens ring
                g.DrawEllipse(camPen, s * 0.37f, s * 0.40f, s * 0.26f, s * 0.26f);
            }
            g.FillEllipse(brush, s * 0.46f, s * 0.49f, s * 0.08f, s * 0.08f);
        }

        // bmp: crisp 3x3 pixel grid
        private static void DrawPixelGrid(Graphics g, Brush brush, float s)
        {
            float cell = s * 0.15f;
            float gap = s * 0.075f;
            float total = cell * 3f + gap * 2f;
            float start = (s - total) * 0.5f;

            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float x = start + c * (cell + gap);
                    float y = start + r * (cell + gap);
                    // Subtle checkerboard density for bitmap feel
                    bool solid = ((r + c) % 2 == 0) || (r == 1 && c == 1);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(solid ? 250 : 120, 248, 250, 252)))
                    using (GraphicsPath sq = CreateRoundedRect(x, y, cell, cell, s * 0.03f))
                    {
                        g.FillPath(b, sq);
                    }
                }
            }
        }

        // webp: lightweight bolt inside image frame
        private static void DrawFastImage(Graphics g, Pen pen, Brush brush, float s)
        {
            using (Pen framePen = new Pen(pen.Color, s * 0.068f))
            using (GraphicsPath rect = CreateRoundedRect(s * 0.18f, s * 0.20f, s * 0.64f, s * 0.60f, s * 0.10f))
            {
                g.DrawPath(framePen, rect);
            }

            PointF[] bolt = new PointF[]
            {
                new PointF(s * 0.54f, s * 0.29f),
                new PointF(s * 0.37f, s * 0.53f),
                new PointF(s * 0.49f, s * 0.53f),
                new PointF(s * 0.45f, s * 0.71f),
                new PointF(s * 0.63f, s * 0.47f),
                new PointF(s * 0.51f, s * 0.47f)
            };
            g.FillPolygon(brush, bolt);
        }

        // ico: 2x2 rounded app icon tiles
        private static void DrawAppTiles(Graphics g, Brush brush, float s)
        {
            float tile = s * 0.25f;
            float gap = s * 0.10f;
            float start = (s - (tile * 2f + gap)) * 0.5f;
            float rad = s * 0.065f;

            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 2; c++)
                {
                    float x = start + c * (tile + gap);
                    float y = start + r * (tile + gap);
                    using (GraphicsPath p = CreateRoundedRect(x, y, tile, tile, rad))
                    {
                        if (r == 0 && c == 1)
                        {
                            using (Pen outline = new Pen(Color.FromArgb(248, 250, 252), s * 0.06f))
                            {
                                g.DrawPath(outline, p);
                            }
                        }
                        else
                        {
                            g.FillPath(brush, p);
                        }
                    }
                }
            }
        }

        // pdf: folded-corner document sheet with 3 lines
        private static void DrawDocument(Graphics g, Pen pen, float s)
        {
            using (Pen docPen = new Pen(pen.Color, s * 0.068f))
            {
                docPen.LineJoin = LineJoin.Round;
                docPen.StartCap = LineCap.Round;
                docPen.EndCap = LineCap.Round;

                PointF[] outline = new PointF[]
                {
                    new PointF(s * 0.25f, s * 0.17f),
                    new PointF(s * 0.57f, s * 0.17f),
                    new PointF(s * 0.75f, s * 0.35f),
                    new PointF(s * 0.75f, s * 0.83f),
                    new PointF(s * 0.25f, s * 0.83f)
                };
                g.DrawPolygon(docPen, outline);

                // Fold corner
                g.DrawLine(docPen, s * 0.57f, s * 0.17f, s * 0.57f, s * 0.35f);
                g.DrawLine(docPen, s * 0.57f, s * 0.35f, s * 0.75f, s * 0.35f);

                // 3 clean content lines
                g.DrawLine(docPen, s * 0.36f, s * 0.49f, s * 0.64f, s * 0.49f);
                g.DrawLine(docPen, s * 0.36f, s * 0.61f, s * 0.64f, s * 0.61f);
                g.DrawLine(docPen, s * 0.36f, s * 0.73f, s * 0.54f, s * 0.73f);
            }
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
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)frames.Count);

                int offset = 6 + 16 * frames.Count;
                for (int i = 0; i < frames.Count; i++)
                {
                    int sz = sizes[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
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
