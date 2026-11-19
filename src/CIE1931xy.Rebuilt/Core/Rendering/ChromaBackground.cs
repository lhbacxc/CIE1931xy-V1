using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace CIE1931xy.Core.Rendering
{
    /// <summary>
    /// 自算真彩色度背景（逐像素 XYZ → sRGB）。
    /// 颜色按像素最大通道归一化（与 CIEdraw 一致），范围限制在光谱轨迹多边形内，其余留白。
    /// 结果按 (图型, 范围, 尺寸) 缓存，缩放窗口时只在尺寸变化后重算。
    /// </summary>
    internal static class ChromaBackground
    {
        private const double GammaA = 0.055;

        private static readonly double[,] SrgbMatrix =
        {
            { 3.2406, -1.5372, -0.4986 },
            { -0.9689, 1.8758, 0.0415 },
            { 0.0557, -0.2040, 1.0570 }
        };

        private sealed class CacheEntry
        {
            public string Key;
            public Bitmap Bitmap;
        }

        private static readonly object SyncRoot = new object();
        private static readonly List<CacheEntry> Cache = new List<CacheEntry>();

        public static Bitmap Get(DiagramKind kind, AxisBounds bounds, int width, int height)
        {
            width = Math.Max(2, width);
            height = Math.Max(2, height);
            string key = kind + "|" + bounds.XMin.ToString("0.####") + "," + bounds.XMax.ToString("0.####") + "," +
                         bounds.YMin.ToString("0.####") + "," + bounds.YMax.ToString("0.####") + "|" + width + "x" + height;
            lock (SyncRoot)
            {
                for (int i = 0; i < Cache.Count; i++)
                {
                    if (Cache[i].Key == key) return Cache[i].Bitmap;
                }
            }

            Bitmap bmp = Render(kind, bounds, width, height);

            lock (SyncRoot)
            {
                Cache.Add(new CacheEntry { Key = key, Bitmap = bmp });
                while (Cache.Count > 6)
                {
                    Bitmap old = Cache[0].Bitmap;
                    Cache.RemoveAt(0);
                    if (old != null) old.Dispose();
                }
            }
            return bmp;
        }

        private static Bitmap Render(DiagramKind kind, AxisBounds bounds, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            // 扫描线填充光谱轨迹多边形，得到“有效色域”掩码（比逐像素多边形测试快得多）
            bool[] mask = RasterMask(kind, bounds, width, height);

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] buffer = new byte[Math.Abs(data.Stride) * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * Math.Abs(data.Stride);
                for (int x = 0; x < width; x++)
                {
                    int idx = row + x * 4;
                    if (!mask[y * width + x])
                    {
                        // 轨迹多边形之外保持透明：网格线先画在底层，从这里透出来，
                        // 于是网格不会被彩色色域盖住（用户要求网格置于底层）。
                        buffer[idx + 0] = 255;
                        buffer[idx + 1] = 255;
                        buffer[idx + 2] = 255;
                        buffer[idx + 3] = 0;
                        continue;
                    }
                    double dx = bounds.XMin + (x + 0.5) / width * bounds.Width;
                    double dy = bounds.YMax - (y + 0.5) / height * bounds.Height;
                    double cx, cy;
                    if (!PlotSpace.FromDiagram(kind, dx, dy, out cx, out cy) || cy <= 1e-6 || cx <= 0 || cy <= 0)
                    {
                        buffer[idx + 0] = 255; buffer[idx + 1] = 255; buffer[idx + 2] = 255; buffer[idx + 3] = 0;
                        continue;
                    }

                    Color color = XyColor(cx, cy);
                    buffer[idx + 2] = color.R;
                    buffer[idx + 1] = color.G;
                    buffer[idx + 0] = color.B;
                    buffer[idx + 3] = 255;
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        /// <summary>独立采样不带像素掩码的颜色场，SVG 由矢量轮廓统一裁剪。</summary>
        public static Color SampleColor(DiagramKind kind, double dx, double dy)
        {
            double x, y;
            if (!PlotSpace.FromDiagram(kind, dx, dy, out x, out y)) return Color.Black;
            return XyColor(x, Math.Max(y, 1e-6));
        }

        private static Color XyColor(double x, double y)
        {
            double X = x / y;
            double Y = 1.0;
            double Z = (1.0 - x - y) / y;
            double r = Math.Max(0, SrgbMatrix[0, 0] * X + SrgbMatrix[0, 1] * Y + SrgbMatrix[0, 2] * Z);
            double g = Math.Max(0, SrgbMatrix[1, 0] * X + SrgbMatrix[1, 1] * Y + SrgbMatrix[1, 2] * Z);
            double b = Math.Max(0, SrgbMatrix[2, 0] * X + SrgbMatrix[2, 1] * Y + SrgbMatrix[2, 2] * Z);
            double max = Math.Max(r, Math.Max(g, b));
            if (max > 1e-12) { r /= max; g /= max; b /= max; }
            return Color.FromArgb(SrgbChannel(r), SrgbChannel(g), SrgbChannel(b));
        }

        private static byte SrgbChannel(double v)
        {
            if (v <= 0) return 0;
            if (v > 1) v = 1;
            double s = v <= 0.0031308 ? 12.92 * v : (1 + GammaA) * Math.Pow(v, 1.0 / 2.4) - GammaA;
            int i = (int)Math.Round(s * 255.0);
            if (i < 0) i = 0;
            if (i > 255) i = 255;
            return (byte)i;
        }

        private static bool[] RasterMask(DiagramKind kind, AxisBounds bounds, int width, int height)
        {
            bool[] mask = new bool[width * height];
            float[] px, py;
            PlotSpace.SpectralLocus(kind, out px, out py, false);
            if (px.Length < 3) return mask;

            // 多边形顶点：光谱轨迹 + 紫线（首尾自动闭合）
            double[] vx = new double[px.Length];
            double[] vy = new double[py.Length];
            for (int i = 0; i < px.Length; i++) { vx[i] = px[i]; vy[i] = py[i]; }

            for (int y = 0; y < height; y++)
            {
                double dy = bounds.YMax - (y + 0.5) / height * bounds.Height;
                List<double> xs = new List<double>(vx.Length);
                for (int i = 0; i < vx.Length; i++)
                {
                    int j = (i + 1) % vx.Length;
                    double y1 = vy[i], y2 = vy[j];
                    if ((y1 > dy) == (y2 > dy)) continue;
                    double t = (dy - y1) / (y2 - y1);
                    xs.Add(vx[i] + t * (vx[j] - vx[i]));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                int rowBase = y * width;
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    double xa = xs[k], xb = xs[k + 1];
                    int ia = (int)Math.Ceiling((xa - bounds.XMin) / bounds.Width * width - 0.5);
                    int ib = (int)Math.Floor((xb - bounds.XMin) / bounds.Width * width - 0.5);
                    if (ia < 0) ia = 0;
                    if (ib >= width) ib = width - 1;
                    for (int x = ia; x <= ib; x++) mask[rowBase + x] = true;
                }
            }
            return mask;
        }
    }
}
