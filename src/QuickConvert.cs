using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace QuickConvert
{
    internal sealed class ConversionSpec
    {
        public string TargetKey;
        public string DisplayLabel;
        public string OutputExt;
        public string Suffix;
        public bool UsesDotNetImage;
        public bool IsIco;
        public bool IsPdf;
        public string FfmpegArgs;
    }

    internal sealed class ConversionProgress
    {
        public int CurrentIndex;
        public int TotalCount;
        public string FileName;
        public int FilePercent; // -1 = indeterminate
        public string StatusText;
    }

    internal sealed class ConversionResult
    {
        public bool Success;
        public bool Cancelled;
        public int CompletedCount;
        public List<string> OutputFiles;
        public List<string> Errors;
        public bool MissingFfmpeg;

        public ConversionResult()
        {
            OutputFiles = new List<string>();
            Errors = new List<string>();
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }

            string target = null;
            string customFfmpeg = null;
            string customIcon = null;
            bool headless = false;
            List<string> inputFiles = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--to", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    target = args[++i].Trim().ToLowerInvariant();
                }
                else if (string.Equals(a, "--ffmpeg", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    customFfmpeg = args[++i].Trim('"');
                }
                else if (string.Equals(a, "--icon", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    customIcon = args[++i].Trim('"');
                }
                else if (string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase))
                {
                    headless = true;
                }
                else if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    AddParsedPaths(inputFiles, a);
                }
            }

            if (string.IsNullOrEmpty(target) || inputFiles.Count == 0)
            {
                if (headless)
                {
                    PrintUsage();
                    return 1;
                }
                MessageBox.Show(
                    "使い方: QuickConvert.exe --to <形式> <ファイルパス...>\n\n" +
                    "対応形式:\n" +
                    "  音声: mp3, wav, m4a, flac, ogg\n" +
                    "  動画: mp4, mov, mp4-lite, webm, gif\n" +
                    "  画像: png, jpg, bmp, webp, ico, pdf\n\n" +
                    "※通常はエクスプローラーの右クリックメニューから呼び出されます（常駐プロセスなし）。",
                    "QuickConvert",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 1;
            }

            ConversionSpec spec = ConverterEngine.GetSpec(target);
            if (spec == null)
            {
                string msg = "未対応の変換形式です: " + target;
                if (headless)
                {
                    Console.Error.WriteLine(msg);
                }
                else
                {
                    MessageBox.Show(msg, "QuickConvert エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 2;
            }

            if (headless)
            {
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                ConverterEngine engine = new ConverterEngine(customFfmpeg);
                ConversionResult res = engine.RunBatch(spec, inputFiles, delegate(ConversionProgress p)
                {
                    string pct = p.FilePercent >= 0 ? (p.FilePercent + "%") : "...";
                    Console.WriteLine(string.Format("[{0}/{1}] {2} ({3}) - {4}", p.CurrentIndex, p.TotalCount, p.FileName, pct, p.StatusText));
                });

                for (int i = 0; i < res.OutputFiles.Count; i++)
                {
                    Console.WriteLine("OUT: " + res.OutputFiles[i]);
                }
                for (int i = 0; i < res.Errors.Count; i++)
                {
                    Console.Error.WriteLine("ERR: " + res.Errors[i]);
                }
                return res.Success ? 0 : 1;
            }

            Application.EnableVisualStyles();
            try
            {
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch
            {
            }
            using (ProgressForm form = new ProgressForm(spec, inputFiles, customFfmpeg, customIcon))
            {
                Application.Run(form);
                return form.ExitCode;
            }
        }

        private static void AddParsedPaths(List<string> list, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string cleaned = raw.Trim().Trim('"');
            if (File.Exists(cleaned))
            {
                list.Add(cleaned);
                return;
            }

            // Fallback: handle comma-joined paths like "C:\a.mp4","C:\b.mp4" if passed as single arg
            if (raw.IndexOf("\",\"", StringComparison.Ordinal) >= 0)
            {
                string[] parts = raw.Split(new string[] { "\",\"" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim().Trim('"');
                    if (!string.IsNullOrEmpty(p))
                    {
                        list.Add(p);
                    }
                }
                return;
            }

            list.Add(cleaned);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("QuickConvert - Zero-resident on-demand file converter");
            Console.WriteLine("Usage: QuickConvert.exe --to <format> [--headless] [--ffmpeg <path>] <file1> [file2 ...]");
            Console.WriteLine("Formats: mp3, wav, m4a, flac, ogg, mp4, mov, mp4-lite, webm, gif, png, jpg, bmp, webp, ico, pdf");
        }
    }

    internal sealed class ConverterEngine
    {
        private static readonly HashSet<string> DotNetReadableImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
        };

        private static readonly Regex DurationRegex = new Regex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

        private readonly string _explicitFfmpeg;
        private readonly object _procLock = new object();
        private Process _activeProcess;
        private volatile bool _cancelRequested;

        public ConverterEngine(string explicitFfmpeg)
        {
            _explicitFfmpeg = explicitFfmpeg;
        }

        public void RequestCancel()
        {
            _cancelRequested = true;
            lock (_procLock)
            {
                if (_activeProcess != null)
                {
                    try
                    {
                        if (!_activeProcess.HasExited)
                        {
                            _activeProcess.Kill();
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static ConversionSpec GetSpec(string key)
        {
            switch (key)
            {
                case "mp3":
                    return new ConversionSpec
                    {
                        TargetKey = "mp3",
                        DisplayLabel = "MP3 (音声)",
                        OutputExt = ".mp3",
                        FfmpegArgs = "-vn -c:a libmp3lame -b:a 192k"
                    };
                case "wav":
                    return new ConversionSpec
                    {
                        TargetKey = "wav",
                        DisplayLabel = "WAV (無圧縮音声)",
                        OutputExt = ".wav",
                        FfmpegArgs = "-vn -c:a pcm_s16le"
                    };
                case "m4a":
                    return new ConversionSpec
                    {
                        TargetKey = "m4a",
                        DisplayLabel = "M4A (AAC音声)",
                        OutputExt = ".m4a",
                        FfmpegArgs = "-vn -c:a aac -b:a 192k"
                    };
                case "flac":
                    return new ConversionSpec
                    {
                        TargetKey = "flac",
                        DisplayLabel = "FLAC (可逆圧縮音声)",
                        OutputExt = ".flac",
                        FfmpegArgs = "-vn -c:a flac"
                    };
                case "ogg":
                    return new ConversionSpec
                    {
                        TargetKey = "ogg",
                        DisplayLabel = "OGG (Vorbis音声)",
                        OutputExt = ".ogg",
                        FfmpegArgs = "-vn -c:a libvorbis -q:a 5"
                    };
                case "mp4":
                    return new ConversionSpec
                    {
                        TargetKey = "mp4",
                        DisplayLabel = "MP4 (H.264動画)",
                        OutputExt = ".mp4",
                        FfmpegArgs = "-c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:a aac -b:a 192k -movflags +faststart"
                    };
                case "mov":
                    return new ConversionSpec
                    {
                        TargetKey = "mov",
                        DisplayLabel = "MOV (QuickTime動画)",
                        OutputExt = ".mov",
                        FfmpegArgs = "-c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:a aac -b:a 192k -movflags +faststart"
                    };
                case "mp4-lite":
                    return new ConversionSpec
                    {
                        TargetKey = "mp4-lite",
                        DisplayLabel = "MP4 軽量圧縮",
                        OutputExt = ".mp4",
                        Suffix = "_lite",
                        FfmpegArgs = "-c:v libx264 -preset medium -crf 28 -pix_fmt yuv420p -vf \"scale='min(1920,iw)':-2\" -c:a aac -b:a 128k -movflags +faststart"
                    };
                case "webm":
                    return new ConversionSpec
                    {
                        TargetKey = "webm",
                        DisplayLabel = "WebM (VP9動画)",
                        OutputExt = ".webm",
                        FfmpegArgs = "-c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -c:a libopus -b:a 128k"
                    };
                case "gif":
                    return new ConversionSpec
                    {
                        TargetKey = "gif",
                        DisplayLabel = "GIF (高画質アニメ)",
                        OutputExt = ".gif",
                        FfmpegArgs = "-vf \"fps=12,scale='min(720,iw)':-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5\""
                    };
                case "png":
                    return new ConversionSpec
                    {
                        TargetKey = "png",
                        DisplayLabel = "PNG (画像)",
                        OutputExt = ".png",
                        UsesDotNetImage = true,
                        FfmpegArgs = "-frames:v 1"
                    };
                case "jpg":
                case "jpeg":
                    return new ConversionSpec
                    {
                        TargetKey = "jpg",
                        DisplayLabel = "JPG (画像)",
                        OutputExt = ".jpg",
                        UsesDotNetImage = true,
                        FfmpegArgs = "-frames:v 1 -q:v 2"
                    };
                case "bmp":
                    return new ConversionSpec
                    {
                        TargetKey = "bmp",
                        DisplayLabel = "BMP (画像)",
                        OutputExt = ".bmp",
                        UsesDotNetImage = true,
                        FfmpegArgs = "-frames:v 1"
                    };
                case "webp":
                    return new ConversionSpec
                    {
                        TargetKey = "webp",
                        DisplayLabel = "WebP (軽量画像)",
                        OutputExt = ".webp",
                        FfmpegArgs = "-frames:v 1 -c:v libwebp -quality 85 -compression_level 4"
                    };
                case "ico":
                    return new ConversionSpec
                    {
                        TargetKey = "ico",
                        DisplayLabel = "ICO (アイコン)",
                        OutputExt = ".ico",
                        UsesDotNetImage = true,
                        IsIco = true
                    };
                case "pdf":
                    return new ConversionSpec
                    {
                        TargetKey = "pdf",
                        DisplayLabel = "PDF (画像→PDF)",
                        OutputExt = ".pdf",
                        UsesDotNetImage = true,
                        IsPdf = true
                    };
                default:
                    return null;
            }
        }

        public ConversionResult RunBatch(ConversionSpec spec, List<string> files, Action<ConversionProgress> onProgress)
        {
            ConversionResult result = new ConversionResult();
            string ffmpegPath = null;
            bool ffmpegSearched = false;

            for (int i = 0; i < files.Count; i++)
            {
                if (_cancelRequested)
                {
                    result.Cancelled = true;
                    break;
                }

                string inputPath = files[i];
                string fileName = Path.GetFileName(inputPath);

                if (!File.Exists(inputPath))
                {
                    result.Errors.Add("ファイルが見つかりません: " + inputPath);
                    continue;
                }

                Report(onProgress, i + 1, files.Count, fileName, -1, "変換準備中...");

                string finalOutput = GetUniqueOutputPath(inputPath, spec.OutputExt, spec.Suffix);
                string tempOutput = GetTempOutputPath(finalOutput, spec.OutputExt);

                try
                {
                    string inExt = Path.GetExtension(inputPath);
                    bool canUseDotNet = spec.UsesDotNetImage && DotNetReadableImageExts.Contains(inExt);

                    if (canUseDotNet)
                    {
                        Report(onProgress, i + 1, files.Count, fileName, 30, "画像を変換中...");
                        ConvertImageWithDotNet(inputPath, tempOutput, spec);
                        Report(onProgress, i + 1, files.Count, fileName, 100, "完了");
                    }
                    else
                    {
                        if (!ffmpegSearched)
                        {
                            ffmpegPath = FindFfmpeg(_explicitFfmpeg);
                            ffmpegSearched = true;
                        }

                        if (string.IsNullOrEmpty(ffmpegPath))
                        {
                            result.MissingFfmpeg = true;
                            result.Errors.Add(
                                "FFmpeg が見つかりませんでした。\n" +
                                "動画・音声・WebP/AVIF/HEIC の変換には FFmpeg が必要です。\n\n" +
                                "PowerShell またはコマンドプロンプトで次を実行すると導入できます:\n" +
                                "  winget install Gyan.FFmpeg\n\n" +
                                "（または ffmpeg.exe を QuickConvert.exe と同じフォルダの tools\\ffmpeg.exe に配置してください）");
                            break;
                        }

                        if (spec.IsIco || spec.IsPdf)
                        {
                            // Decode exotic image format (webp/avif/heic) to temporary PNG via FFmpeg, then build ICO/PDF
                            string midPng = tempOutput + ".mid.png";
                            try
                            {
                                string errMsg;
                                bool ok = RunFfmpeg(ffmpegPath, inputPath, midPng, "-frames:v 1", i + 1, files.Count, fileName, onProgress, out errMsg);
                                if (!ok)
                                {
                                    if (_cancelRequested)
                                    {
                                        result.Cancelled = true;
                                        break;
                                    }
                                    result.Errors.Add(fileName + ": " + errMsg);
                                    continue;
                                }
                                Report(onProgress, i + 1, files.Count, fileName, 85, spec.IsIco ? "ICO生成中..." : "PDF生成中...");
                                ConvertImageWithDotNet(midPng, tempOutput, spec);
                                Report(onProgress, i + 1, files.Count, fileName, 100, "完了");
                            }
                            finally
                            {
                                SafeDelete(midPng);
                            }
                        }
                        else
                        {
                            string errMsg;
                            bool ok = RunFfmpeg(ffmpegPath, inputPath, tempOutput, spec.FfmpegArgs, i + 1, files.Count, fileName, onProgress, out errMsg);
                            if (!ok)
                            {
                                SafeDelete(tempOutput);
                                if (_cancelRequested)
                                {
                                    result.Cancelled = true;
                                    break;
                                }
                                result.Errors.Add(fileName + ": " + errMsg);
                                continue;
                            }
                        }
                    }

                    if (_cancelRequested)
                    {
                        SafeDelete(tempOutput);
                        result.Cancelled = true;
                        break;
                    }

                    // Re-check final output name to guarantee no overwrite
                    finalOutput = GetUniqueOutputPath(inputPath, spec.OutputExt, spec.Suffix);
                    File.Move(tempOutput, finalOutput);
                    result.OutputFiles.Add(finalOutput);
                    result.CompletedCount++;
                }
                catch (Exception ex)
                {
                    SafeDelete(tempOutput);
                    result.Errors.Add(fileName + ": " + ex.Message);
                }
            }

            result.Success = !result.Cancelled && result.Errors.Count == 0 && result.CompletedCount > 0;
            return result;
        }

        private bool RunFfmpeg(
            string ffmpegPath,
            string inputPath,
            string outputPath,
            string extraArgs,
            int currentIdx,
            int totalCount,
            string fileName,
            Action<ConversionProgress> onProgress,
            out string errorMessage)
        {
            errorMessage = null;
            string arguments = string.Format(
                "-hide_banner -y -i \"{0}\" {1} -progress pipe:1 -nostats \"{2}\"",
                inputPath,
                extraArgs,
                outputPath);

            double durationUs = 0;
            List<string> stderrTail = new List<string>();
            object stderrLock = new object();

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ffmpegPath;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            Report(onProgress, currentIdx, totalCount, fileName, -1, "FFmpeg で変換中...");

            using (Process proc = new Process())
            {
                proc.StartInfo = psi;

                proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    Match m = DurationRegex.Match(e.Data);
                    if (m.Success)
                    {
                        double h, min, sec;
                        if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out h) &&
                            double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out min) &&
                            double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
                        {
                            double totalSec = h * 3600.0 + min * 60.0 + sec;
                            if (totalSec > 0)
                            {
                                durationUs = totalSec * 1000000.0;
                            }
                        }
                    }

                    lock (stderrLock)
                    {
                        stderrTail.Add(e.Data);
                        if (stderrTail.Count > 20)
                        {
                            stderrTail.RemoveAt(0);
                        }
                    }
                };

                proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    if (e.Data.StartsWith("out_time_us=", StringComparison.Ordinal))
                    {
                        long outUs;
                        if (long.TryParse(e.Data.Substring(12).Trim(), out outUs) && durationUs > 0 && outUs >= 0)
                        {
                            int pct = (int)Math.Min(99, Math.Max(0, Math.Round((outUs / durationUs) * 100.0)));
                            Report(onProgress, currentIdx, totalCount, fileName, pct, "変換中... " + pct + "%");
                        }
                    }
                    else if (e.Data == "progress=end")
                    {
                        Report(onProgress, currentIdx, totalCount, fileName, 100, "仕上げ中...");
                    }
                };

                lock (_procLock)
                {
                    if (_cancelRequested)
                    {
                        return false;
                    }
                    _activeProcess = proc;
                }

                try
                {
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                }
                finally
                {
                    lock (_procLock)
                    {
                        _activeProcess = null;
                    }
                }

                if (_cancelRequested)
                {
                    return false;
                }

                if (proc.ExitCode != 0 || !File.Exists(outputPath))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("FFmpeg がエラーコード " + proc.ExitCode + " で終了しました。");
                    lock (stderrLock)
                    {
                        for (int k = 0; k < stderrTail.Count; k++)
                        {
                            sb.AppendLine(stderrTail[k]);
                        }
                    }
                    errorMessage = sb.ToString().Trim();
                    return false;
                }
            }

            return true;
        }

        private static void ConvertImageWithDotNet(string inputPath, string outputPath, ConversionSpec spec)
        {
            using (Image rawImg = Image.FromFile(inputPath))
            {
                ApplyExifOrientation(rawImg);

                if (spec.IsIco)
                {
                    SaveAsMultiSizeIco(rawImg, outputPath);
                    return;
                }

                if (spec.IsPdf)
                {
                    SaveImageAsPdf(rawImg, outputPath);
                    return;
                }

                if (spec.TargetKey == "png")
                {
                    rawImg.Save(outputPath, ImageFormat.Png);
                }
                else if (spec.TargetKey == "bmp")
                {
                    using (Bitmap flattened = FlattenToWhite24bpp(rawImg))
                    {
                        flattened.Save(outputPath, ImageFormat.Bmp);
                    }
                }
                else if (spec.TargetKey == "jpg")
                {
                    using (Bitmap flattened = FlattenToWhite24bpp(rawImg))
                    {
                        SaveJpegWithQuality(flattened, outputPath, 92L);
                    }
                }
                else
                {
                    throw new NotSupportedException("Unsupported image format: " + spec.TargetKey);
                }
            }
        }

        private static void ApplyExifOrientation(Image img)
        {
            const int ExifOrientationId = 0x0112;
            try
            {
                int[] propIds = img.PropertyIdList;
                bool found = false;
                for (int i = 0; i < propIds.Length; i++)
                {
                    if (propIds[i] == ExifOrientationId)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return;

                PropertyItem prop = img.GetPropertyItem(ExifOrientationId);
                if (prop == null || prop.Value == null || prop.Value.Length < 2) return;

                ushort orientation = BitConverter.ToUInt16(prop.Value, 0);
                RotateFlipType rf = RotateFlipType.RotateNoneFlipNone;
                switch (orientation)
                {
                    case 2: rf = RotateFlipType.RotateNoneFlipX; break;
                    case 3: rf = RotateFlipType.Rotate180FlipNone; break;
                    case 4: rf = RotateFlipType.Rotate180FlipX; break;
                    case 5: rf = RotateFlipType.Rotate90FlipX; break;
                    case 6: rf = RotateFlipType.Rotate90FlipNone; break;
                    case 7: rf = RotateFlipType.Rotate270FlipX; break;
                    case 8: rf = RotateFlipType.Rotate270FlipNone; break;
                }

                if (rf != RotateFlipType.RotateNoneFlipNone)
                {
                    img.RotateFlip(rf);
                    img.RemovePropertyItem(ExifOrientationId);
                }
            }
            catch
            {
            }
        }

        private static Bitmap FlattenToWhite24bpp(Image src)
        {
            Bitmap bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            bmp.SetResolution(src.HorizontalResolution > 0 ? src.HorizontalResolution : 96f,
                              src.VerticalResolution > 0 ? src.VerticalResolution : 96f);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return bmp;
        }

        private static void SaveJpegWithQuality(Image img, string outputPath, long quality)
        {
            ImageCodecInfo jpegCodec = GetEncoder(ImageFormat.Jpeg);
            if (jpegCodec == null)
            {
                img.Save(outputPath, ImageFormat.Jpeg);
                return;
            }
            using (EncoderParameters ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                img.Save(outputPath, jpegCodec, ep);
            }
        }

        private static byte[] EncodeJpegBytes(Image img, long quality)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ImageCodecInfo jpegCodec = GetEncoder(ImageFormat.Jpeg);
                if (jpegCodec == null)
                {
                    img.Save(ms, ImageFormat.Jpeg);
                }
                else
                {
                    using (EncoderParameters ep = new EncoderParameters(1))
                    {
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                        img.Save(ms, jpegCodec, ep);
                    }
                }
                return ms.ToArray();
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].FormatID == format.Guid)
                {
                    return codecs[i];
                }
            }
            return null;
        }

        private static void SaveAsMultiSizeIco(Image src, string outputPath)
        {
            int[] sizes = new int[] { 16, 32, 48, 256 };
            List<byte[]> pngFrames = new List<byte[]>();

            for (int i = 0; i < sizes.Length; i++)
            {
                int sz = sizes[i];
                using (Bitmap canvas = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(canvas))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                        float ratio = Math.Min((float)sz / src.Width, (float)sz / src.Height);
                        int drawW = Math.Max(1, (int)Math.Round(src.Width * ratio));
                        int drawH = Math.Max(1, (int)Math.Round(src.Height * ratio));
                        int drawX = (sz - drawW) / 2;
                        int drawY = (sz - drawH) / 2;
                        g.DrawImage(src, drawX, drawY, drawW, drawH);
                    }
                    using (MemoryStream ms = new MemoryStream())
                    {
                        canvas.Save(ms, ImageFormat.Png);
                        pngFrames.Add(ms.ToArray());
                    }
                }
            }

            using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)pngFrames.Count);

                int offset = 6 + 16 * pngFrames.Count;
                for (int i = 0; i < pngFrames.Count; i++)
                {
                    int sz = sizes[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write((uint)pngFrames[i].Length);
                    bw.Write((uint)offset);
                    offset += pngFrames[i].Length;
                }

                for (int i = 0; i < pngFrames.Count; i++)
                {
                    bw.Write(pngFrames[i]);
                }
            }
        }

        private static void SaveImageAsPdf(Image src, string outputPath)
        {
            byte[] jpegData;
            int pxW = src.Width;
            int pxH = src.Height;
            using (Bitmap flattened = FlattenToWhite24bpp(src))
            {
                jpegData = EncodeJpegBytes(flattened, 95L);
            }

            // Convert pixels to PDF points (72 pt/inch, assuming 96 dpi when unset)
            double dpiX = src.HorizontalResolution >= 36f && src.HorizontalResolution <= 1200f ? src.HorizontalResolution : 96.0;
            double dpiY = src.VerticalResolution >= 36f && src.VerticalResolution <= 1200f ? src.VerticalResolution : 96.0;
            double ptW = Math.Max(1.0, pxW * 72.0 / dpiX);
            double ptH = Math.Max(1.0, pxH * 72.0 / dpiY);

            string wStr = ptW.ToString("0.##", CultureInfo.InvariantCulture);
            string hStr = ptH.ToString("0.##", CultureInfo.InvariantCulture);

            byte[] contentBytes = Encoding.ASCII.GetBytes(string.Format("q\n{0} 0 0 {1} 0 0 cm\n/Im0 Do\nQ\n", wStr, hStr));

            using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                List<long> offsets = new List<long>();
                offsets.Add(0); // obj 0 (free)

                WriteAscii(fs, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

                // 1: Catalog
                offsets.Add(fs.Position);
                WriteAscii(fs, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                // 2: Pages
                offsets.Add(fs.Position);
                WriteAscii(fs, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

                // 3: Page
                offsets.Add(fs.Position);
                WriteAscii(fs, string.Format(
                    "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0} {1}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
                    wStr, hStr));

                // 4: Image XObject
                offsets.Add(fs.Position);
                WriteAscii(fs, string.Format(
                    "4 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n",
                    pxW, pxH, jpegData.Length));
                fs.Write(jpegData, 0, jpegData.Length);
                WriteAscii(fs, "\nendstream\nendobj\n");

                // 5: Content stream
                offsets.Add(fs.Position);
                WriteAscii(fs, string.Format("5 0 obj\n<< /Length {0} >>\nstream\n", contentBytes.Length));
                fs.Write(contentBytes, 0, contentBytes.Length);
                WriteAscii(fs, "\nendstream\nendobj\n");

                // xref
                long xrefPos = fs.Position;
                WriteAscii(fs, string.Format("xref\n0 {0}\n", offsets.Count));
                WriteAscii(fs, "0000000000 65535 f \n");
                for (int i = 1; i < offsets.Count; i++)
                {
                    WriteAscii(fs, offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                }

                // trailer
                WriteAscii(fs, string.Format("trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", offsets.Count, xrefPos));
            }
        }

        private static void WriteAscii(Stream s, string text)
        {
            byte[] b = Encoding.GetEncoding(28591).GetBytes(text);
            s.Write(b, 0, b.Length);
        }

        public static string FindFfmpeg(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
            {
                return explicitPath;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] localCandidates = new string[]
            {
                Path.Combine(baseDir, "tools", "ffmpeg.exe"),
                Path.Combine(baseDir, "ffmpeg.exe")
            };
            for (int i = 0; i < localCandidates.Length; i++)
            {
                if (File.Exists(localCandidates[i])) return localCandidates[i];
            }

            // Check PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] dirs = pathEnv.Split(Path.PathSeparator);
            for (int i = 0; i < dirs.Length; i++)
            {
                string d = dirs[i].Trim().Trim('"');
                if (string.IsNullOrEmpty(d)) continue;
                try
                {
                    string candidate = Path.Combine(d, "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }

            // Check WinGet links and common installation locations
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] commonPaths = new string[]
            {
                Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
                Path.Combine(userProfile, "scoop", "shims", "ffmpeg.exe"),
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\File Converter\ffmpeg.exe"
            };
            for (int i = 0; i < commonPaths.Length; i++)
            {
                if (File.Exists(commonPaths[i])) return commonPaths[i];
            }

            // Search WinGet Packages folder for FFmpeg builds
            string wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetPackages))
            {
                try
                {
                    string[] pkgDirs = Directory.GetDirectories(wingetPackages, "*FFmpeg*");
                    for (int i = 0; i < pkgDirs.Length; i++)
                    {
                        string[] found = Directory.GetFiles(pkgDirs[i], "ffmpeg.exe", SearchOption.AllDirectories);
                        if (found.Length > 0) return found[0];
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string GetUniqueOutputPath(string inputPath, string targetExt, string suffix)
        {
            string dir = Path.GetDirectoryName(inputPath) ?? ".";
            string nameNoExt = Path.GetFileNameWithoutExtension(inputPath);
            string sfx = string.IsNullOrEmpty(suffix) ? string.Empty : suffix;

            string candidate = Path.Combine(dir, nameNoExt + sfx + targetExt);
            if (!File.Exists(candidate) && !string.Equals(candidate, inputPath, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            for (int n = 1; n < 10000; n++)
            {
                candidate = Path.Combine(dir, string.Format("{0}{1} ({2}){3}", nameNoExt, sfx, n, targetExt));
                if (!File.Exists(candidate) && !string.Equals(candidate, inputPath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return Path.Combine(dir, string.Format("{0}{1}_{2}{3}", nameNoExt, sfx, Guid.NewGuid().ToString("N").Substring(0, 6), targetExt));
        }

        private static string GetTempOutputPath(string finalOutput, string targetExt)
        {
            string dir = Path.GetDirectoryName(finalOutput) ?? ".";
            string token = Guid.NewGuid().ToString("N").Substring(0, 8);
            return Path.Combine(dir, ".qc_tmp_" + token + targetExt);
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void Report(Action<ConversionProgress> callback, int idx, int total, string fileName, int pct, string status)
        {
            if (callback == null) return;
            callback(new ConversionProgress
            {
                CurrentIndex = idx,
                TotalCount = total,
                FileName = fileName,
                FilePercent = pct,
                StatusText = status
            });
        }
    }

    internal sealed class FlatProgressBar : Control
    {
        private int _value;
        private bool _indeterminate;
        private int _marqueeOffset;
        private System.Windows.Forms.Timer _animTimer;

        public FlatProgressBar()
        {
            DoubleBuffered = true;
            Height = 10;
            BackColor = Color.FromArgb(45, 48, 56);
            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 30;
            _animTimer.Tick += delegate
            {
                _marqueeOffset = (_marqueeOffset + 6) % (Math.Max(1, Width + 90));
                Invalidate();
            };
        }

        public int Value
        {
            get { return _value; }
            set
            {
                _value = Math.Max(0, Math.Min(100, value));
                Invalidate();
            }
        }

        public bool Indeterminate
        {
            get { return _indeterminate; }
            set
            {
                _indeterminate = value;
                if (_indeterminate)
                {
                    _animTimer.Start();
                }
                else
                {
                    _animTimer.Stop();
                }
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            using (SolidBrush accent = new SolidBrush(Color.FromArgb(56, 189, 248)))
            {
                if (_indeterminate)
                {
                    int barW = Math.Max(60, Width / 3);
                    int x = _marqueeOffset - barW;
                    g.FillRectangle(accent, x, 0, barW, Height);
                }
                else
                {
                    int fillW = (int)Math.Round(Width * (_value / 100.0));
                    if (fillW > 0)
                    {
                        g.FillRectangle(accent, 0, 0, fillW, Height);
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
                _animTimer = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ProgressForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private readonly ConversionSpec _spec;
        private readonly List<string> _files;
        private readonly ConverterEngine _engine;
        private readonly string _customIcon;

        private Label _lblHeader;
        private Label _lblFile;
        private Label _lblStatus;
        private FlatProgressBar _progressBar;
        private Button _btnCancel;
        private Button _btnCopyCmd;
        private TextBox _txtError;
        private System.Windows.Forms.Timer _closeTimer;

        private Thread _workerThread;
        private bool _isFinished;

        public int ExitCode { get; private set; }

        public ProgressForm(ConversionSpec spec, List<string> files, string customFfmpeg, string customIcon)
        {
            _spec = spec;
            _files = files;
            _engine = new ConverterEngine(customFfmpeg);
            _customIcon = customIcon;
            ExitCode = 0;

            InitializeUi();
        }

        private void InitializeUi()
        {
            Text = "QuickConvert - " + _spec.DisplayLabel;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 162);
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Yu Gothic UI", 9.5f, FontStyle.Regular);
            TopMost = false;

            try
            {
                string resolvedIcon = _customIcon;
                if (string.IsNullOrEmpty(resolvedIcon) || !File.Exists(resolvedIcon))
                {
                    string targetIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons", _spec.TargetKey + ".ico");
                    if (File.Exists(targetIco))
                    {
                        resolvedIcon = targetIco;
                    }
                }

                if (!string.IsNullOrEmpty(resolvedIcon) && File.Exists(resolvedIcon))
                {
                    Icon = new Icon(resolvedIcon);
                }
                else
                {
                    Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch
            {
            }

            _lblHeader = new Label();
            _lblHeader.Text = _spec.DisplayLabel + " に変換中";
            _lblHeader.Font = new Font("Yu Gothic UI", 11f, FontStyle.Bold);
            _lblHeader.ForeColor = Color.FromArgb(248, 250, 252);
            _lblHeader.Location = new Point(20, 16);
            _lblHeader.Size = new Size(400, 24);
            Controls.Add(_lblHeader);

            _lblFile = new Label();
            _lblFile.Text = Path.GetFileName(_files[0]);
            _lblFile.ForeColor = Color.FromArgb(148, 163, 184);
            _lblFile.AutoEllipsis = true;
            _lblFile.Location = new Point(20, 44);
            _lblFile.Size = new Size(400, 20);
            Controls.Add(_lblFile);

            _progressBar = new FlatProgressBar();
            _progressBar.Location = new Point(20, 74);
            _progressBar.Size = new Size(400, 10);
            _progressBar.Indeterminate = true;
            Controls.Add(_progressBar);

            _lblStatus = new Label();
            _lblStatus.Text = "準備中...";
            _lblStatus.ForeColor = Color.FromArgb(203, 213, 225);
            _lblStatus.Location = new Point(20, 92);
            _lblStatus.Size = new Size(290, 20);
            Controls.Add(_lblStatus);

            _btnCancel = new Button();
            _btnCancel.Text = "キャンセル";
            _btnCancel.FlatStyle = FlatStyle.Flat;
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            _btnCancel.BackColor = Color.FromArgb(38, 42, 52);
            _btnCancel.ForeColor = Color.FromArgb(241, 245, 249);
            _btnCancel.Location = new Point(324, 118);
            _btnCancel.Size = new Size(96, 30);
            _btnCancel.Click += OnCancelOrCloseClick;
            Controls.Add(_btnCancel);

            _btnCopyCmd = new Button();
            _btnCopyCmd.Text = "導入コマンドをコピー";
            _btnCopyCmd.FlatStyle = FlatStyle.Flat;
            _btnCopyCmd.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            _btnCopyCmd.BackColor = Color.FromArgb(14, 116, 144);
            _btnCopyCmd.ForeColor = Color.White;
            _btnCopyCmd.Location = new Point(20, 294);
            _btnCopyCmd.Size = new Size(170, 30);
            _btnCopyCmd.Visible = false;
            _btnCopyCmd.Click += delegate
            {
                try
                {
                    Clipboard.SetText("winget install Gyan.FFmpeg");
                    _btnCopyCmd.Text = "コピーしました";
                }
                catch
                {
                }
            };
            Controls.Add(_btnCopyCmd);

            _txtError = new TextBox();
            _txtError.Multiline = true;
            _txtError.ReadOnly = true;
            _txtError.ScrollBars = ScrollBars.Vertical;
            _txtError.BackColor = Color.FromArgb(15, 17, 21);
            _txtError.ForeColor = Color.FromArgb(252, 165, 165);
            _txtError.BorderStyle = BorderStyle.FixedSingle;
            _txtError.Font = new Font("Consolas", 9f);
            _txtError.Location = new Point(20, 124);
            _txtError.Size = new Size(400, 158);
            _txtError.Visible = false;
            Controls.Add(_txtError);

            Shown += OnFormShown;
            FormClosing += OnFormClosing;
        }

        private void OnFormShown(object sender, EventArgs e)
        {
            try
            {
                ShowWindow(Handle, 1); // SW_SHOWNORMAL (ensures UI is visible even if launched with SW_HIDE)
                Activate();
            }
            catch
            {
            }

            _workerThread = new Thread(RunWorker);
            _workerThread.IsBackground = true;
            _workerThread.Start();
        }

        private void RunWorker()
        {
            ConversionResult result = _engine.RunBatch(_spec, _files, delegate(ConversionProgress p)
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        _lblFile.Text = p.TotalCount > 1
                            ? string.Format("({0}/{1}) {2}", p.CurrentIndex, p.TotalCount, p.FileName)
                            : p.FileName;
                        _lblStatus.Text = p.StatusText;
                        if (p.FilePercent < 0)
                        {
                            _progressBar.Indeterminate = true;
                        }
                        else
                        {
                            _progressBar.Indeterminate = false;
                            _progressBar.Value = p.FilePercent;
                        }
                    });
                }
                catch
                {
                }
            });

            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    OnConversionCompleted(result);
                });
            }
            catch
            {
            }
        }

        private void OnConversionCompleted(ConversionResult result)
        {
            _isFinished = true;
            _progressBar.Indeterminate = false;

            if (result.Cancelled)
            {
                ExitCode = 3;
                Close();
                return;
            }

            if (result.Success)
            {
                ExitCode = 0;
                _progressBar.Value = 100;
                _lblHeader.Text = "変換が完了しました";
                _lblHeader.ForeColor = Color.FromArgb(74, 222, 128);
                _lblStatus.Text = result.CompletedCount + " 件のファイルを保存しました";
                _btnCancel.Text = "閉じる";

                // Auto-close to guarantee zero residency
                _closeTimer = new System.Windows.Forms.Timer();
                _closeTimer.Interval = 1100;
                _closeTimer.Tick += delegate
                {
                    _closeTimer.Stop();
                    Close();
                };
                _closeTimer.Start();
                return;
            }

            // Show error details cleanly
            ExitCode = 1;
            _progressBar.Value = 0;
            _lblHeader.Text = result.MissingFfmpeg ? "FFmpeg が見つかりません" : "変換中にエラーが発生しました";
            _lblHeader.ForeColor = Color.FromArgb(248, 113, 113);
            _lblStatus.Text = result.CompletedCount > 0
                ? string.Format("{0} 件成功 / {1} 件失敗", result.CompletedCount, result.Errors.Count)
                : "詳細は下のログを確認してください";

            ClientSize = new Size(440, 340);
            _txtError.Text = string.Join(Environment.NewLine + Environment.NewLine, result.Errors.ToArray());
            _txtError.Visible = true;
            _btnCopyCmd.Visible = result.MissingFfmpeg;
            _btnCancel.Location = new Point(324, 294);
            _btnCancel.Text = "閉じる";
        }

        private void OnCancelOrCloseClick(object sender, EventArgs e)
        {
            if (_isFinished)
            {
                Close();
            }
            else
            {
                _btnCancel.Enabled = false;
                _lblStatus.Text = "キャンセル中...";
                _engine.RequestCancel();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isFinished)
            {
                _engine.RequestCancel();
            }
            if (_closeTimer != null)
            {
                _closeTimer.Stop();
                _closeTimer.Dispose();
                _closeTimer = null;
            }
        }
    }
}
