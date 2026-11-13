using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanImageViewer
{
    public static class ImageHelper
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jpe", ".jfif",
            ".png",
            ".bmp", ".dib",
            ".gif",
            ".ico",
            ".tiff", ".tif",
            ".webp",
            ".svg",
            ".wmp", ".hdp", ".wdp",
            ".heic", ".heif",
            ".avif"
        };

        public static bool IsSupportedExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            return SupportedExtensions.Contains(ext);
        }

        public static BitmapSource LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("Image file not found: " + path);
            }

            string ext = Path.GetExtension(path);

            // Special handling for SVG vector format
            if (string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase))
            {
                return RenderSvgToBitmapSource(path);
            }

            byte[] bytes = File.ReadAllBytes(path);
            return LoadImageFromBytes(bytes, ext);
        }

        public static BitmapSource LoadImageFromBytes(byte[] bytes, string ext)
        {
            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException("Byte array is empty or null", "bytes");
            }

            if (string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase))
            {
                string svgText = System.Text.Encoding.UTF8.GetString(bytes);
                return RenderSvgContentToBitmapSource(svgText);
            }

            // Attempt 1: WPF native WIC decoder
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(
                        ms,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                    if (decoder.Frames.Count > 0)
                    {
                        BitmapFrame bestFrame = decoder.Frames[0];
                        if (string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase) && decoder.Frames.Count > 1)
                        {
                            // Choose largest resolution frame
                            int maxPixels = 0;
                            foreach (BitmapFrame frame in decoder.Frames)
                            {
                                int pixels = frame.PixelWidth * frame.PixelHeight;
                                if (pixels > maxPixels)
                                {
                                    maxPixels = pixels;
                                    bestFrame = frame;
                                }
                            }
                        }

                        return ConvertToWritableBitmap(bestFrame);
                    }
                }
            }
            catch
            {
                // Fall back to GDI+ decoder below
            }

            // Attempt 2: System.Drawing GDI+ fallback
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                using (System.Drawing.Image gdiImg = System.Drawing.Image.FromStream(ms))
                using (System.Drawing.Bitmap gdiBmp = new System.Drawing.Bitmap(gdiImg))
                {
                    IntPtr hBmp = gdiBmp.GetHbitmap();
                    try
                    {
                        BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBmp,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        return ConvertToWritableBitmap(bs);
                    }
                    finally
                    {
                        DeleteObject(hBmp);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Could not load image format: " + ex.Message, ex);
            }
        }

        #region SVG Rendering

        [ComImport]
        [Guid("0000010d-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IViewObject
        {
            void Draw(
                [MarshalAs(UnmanagedType.U4)] int dwAspect,
                int lindex,
                IntPtr pvAspect,
                [In] IntPtr ptd,
                IntPtr hdcTargetDev,
                IntPtr hdcDraw,
                [In] tagRECT lprcBounds,
                [In] IntPtr lprcWBounds,
                IntPtr pfnContinue,
                [MarshalAs(UnmanagedType.U4)] int dwContinue);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct tagRECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private static BitmapSource RenderSvgToBitmapSource(string svgPath)
        {
            string svgText = File.ReadAllText(svgPath);
            return RenderSvgContentToBitmapSource(svgText);
        }

        public static BitmapSource RenderSvgContentToBitmapSource(string svgText)
        {
            Point naturalSize = ParseSvgDimensions(svgText);

            double targetW = naturalSize.X;
            double targetH = naturalSize.Y;
            double maxDim = Math.Max(targetW, targetH);

            // Scale up small SVGs so vector elements render sharp
            if (maxDim > 0 && maxDim < 1200)
            {
                double multiplier = 1200.0 / maxDim;
                targetW = Math.Round(targetW * multiplier);
                targetH = Math.Round(targetH * multiplier);
            }
            else if (maxDim > 4000)
            {
                double multiplier = 4000.0 / maxDim;
                targetW = Math.Round(targetW * multiplier);
                targetH = Math.Round(targetH * multiplier);
            }

            int w = (int)Math.Max(100, targetW);
            int h = (int)Math.Max(100, targetH);

            string html = "<!DOCTYPE html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=Edge\"/>" +
                          "<style>" +
                          "html, body { margin:0; padding:0; width:100%; height:100%; overflow:hidden; background-color:transparent; }" +
                          "svg { width:100% !important; height:100% !important; display:block; }" +
                          "</style></head>" +
                          "<body allowtransparency=\"true\">" + svgText + "</body></html>";

            using (System.Windows.Forms.WebBrowser wb = new System.Windows.Forms.WebBrowser())
            {
                wb.ScrollBarsEnabled = false;
                wb.Width = w;
                wb.Height = h;
                wb.DocumentText = html;

                DateTime start = DateTime.Now;
                while (wb.ReadyState != System.Windows.Forms.WebBrowserReadyState.Complete && (DateTime.Now - start).TotalSeconds < 5)
                {
                    System.Windows.Forms.Application.DoEvents();
                }

                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(System.Drawing.Color.White);
                        IntPtr hdc = g.GetHdc();
                        try
                        {
                            IViewObject vo = wb.Document.DomDocument as IViewObject;
                            if (vo != null)
                            {
                                tagRECT rect = new tagRECT { left = 0, top = 0, right = w, bottom = h };
                                vo.Draw(1, -1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, hdc, rect, IntPtr.Zero, IntPtr.Zero, 0);
                            }
                        }
                        finally
                        {
                            g.ReleaseHdc(hdc);
                        }
                    }

                    IntPtr hBmp = bmp.GetHbitmap();
                    try
                    {
                        BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBmp,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        return ConvertToWritableBitmap(bs);
                    }
                    finally
                    {
                        DeleteObject(hBmp);
                    }
                }
            }
        }

        private static Point ParseSvgDimensions(string svgContent)
        {
            try
            {
                // 1. Try viewBox attribute
                Match vbMatch = Regex.Match(svgContent, @"viewBox\s*=\s*[""']\s*(-?[\d\.]+)[,\s]+(-?[\d\.]+)[,\s]+([\d\.]+)[,\s]+([\d\.]+)\s*[""']", RegexOptions.IgnoreCase);
                if (vbMatch.Success)
                {
                    double w = double.Parse(vbMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                    double h = double.Parse(vbMatch.Groups[4].Value, CultureInfo.InvariantCulture);
                    if (w > 0 && h > 0) return new Point(w, h);
                }

                // 2. Try width & height attributes
                Match wMatch = Regex.Match(svgContent, @"<svg[^>]*\bwidth\s*=\s*[""']([\d\.]+)", RegexOptions.IgnoreCase);
                Match hMatch = Regex.Match(svgContent, @"<svg[^>]*\bheight\s*=\s*[""']([\d\.]+)", RegexOptions.IgnoreCase);
                if (wMatch.Success && hMatch.Success)
                {
                    double w = double.Parse(wMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    double h = double.Parse(hMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (w > 0 && h > 0) return new Point(w, h);
                }
            }
            catch { }

            return new Point(1000, 1000);
        }

        #endregion

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static WriteableBitmap ConvertToWritableBitmap(BitmapSource source)
        {
            if (source == null) return null;

            int width = source.PixelWidth;
            int height = source.PixelHeight;

            FormatConvertedBitmap formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formatted.CopyPixels(pixels, stride, 0);

            WriteableBitmap result = new WriteableBitmap(
                width,
                height,
                source.DpiX > 0 ? source.DpiX : 96,
                source.DpiY > 0 ? source.DpiY : 96,
                PixelFormats.Bgra32,
                null);

            result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            result.Freeze();
            return result;
        }

        public static BitmapSource Rotate(BitmapSource source, double angle)
        {
            if (source == null) return null;
            TransformedBitmap tb = new TransformedBitmap(source, new RotateTransform(angle));
            return ConvertToWritableBitmap(tb);
        }

        public static BitmapSource Flip(BitmapSource source, bool horizontal, bool vertical)
        {
            if (source == null) return null;
            double scaleX = horizontal ? -1.0 : 1.0;
            double scaleY = vertical ? -1.0 : 1.0;
            TransformedBitmap tb = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
            return ConvertToWritableBitmap(tb);
        }

        public static BitmapSource InvertColors(BitmapSource source)
        {
            if (source == null) return null;

            FormatConvertedBitmap formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formatted.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i]     = (byte)(255 - pixels[i]);     // Blue
                pixels[i + 1] = (byte)(255 - pixels[i + 1]); // Green
                pixels[i + 2] = (byte)(255 - pixels[i + 2]); // Red
                // Alpha is at pixels[i + 3], kept unchanged
            }

            WriteableBitmap result = new WriteableBitmap(
                width,
                height,
                source.DpiX > 0 ? source.DpiX : 96,
                source.DpiY > 0 ? source.DpiY : 96,
                PixelFormats.Bgra32,
                null);

            result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            result.Freeze();
            return result;
        }

        public static BitmapSource Crop(BitmapSource source, Int32Rect cropRect)
        {
            if (source == null) return null;

            int x = Math.Max(0, cropRect.X);
            int y = Math.Max(0, cropRect.Y);
            int w = Math.Min(cropRect.Width, source.PixelWidth - x);
            int h = Math.Min(cropRect.Height, source.PixelHeight - y);

            if (w <= 0 || h <= 0) return source;

            CroppedBitmap cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
            return ConvertToWritableBitmap(cropped);
        }

        public static void SaveImage(BitmapSource source, string path)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is empty", "path");

            string ext = Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder encoder;

            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                case ".jpe":
                case ".jfif":
                    JpegBitmapEncoder jpg = new JpegBitmapEncoder();
                    jpg.QualityLevel = 95;
                    encoder = jpg;
                    break;
                case ".bmp":
                case ".dib":
                    encoder = new BmpBitmapEncoder();
                    break;
                case ".gif":
                    encoder = new GifBitmapEncoder();
                    break;
                case ".tif":
                case ".tiff":
                    encoder = new TiffBitmapEncoder();
                    break;
                case ".png":
                default:
                    encoder = new PngBitmapEncoder();
                    break;
            }

            encoder.Frames.Add(BitmapFrame.Create(source));

            // Write to temp file first to ensure safe atomic overwrite
            string tempPath = path + ".tmp_" + Guid.NewGuid().ToString("N");
            using (FileStream fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(fs);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tempPath, path);
        }
    }
}
