using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using CIE1931xy.Core.Rendering;

namespace CIE1931xy.Core.Export
{
    /// <summary>位图 / 矢量 / 数据导出。</summary>
    internal static class Exporter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------------------------------------------------------------- 图片

        /// <summary>按目标像素尺寸渲染整张图；DPI 只写入图片元数据（用于排版/打印密度，不再放大像素）。</summary>
        public static Bitmap RenderToBitmap(DiagramStyle style, IList<CieCurve> curves, IList<CiePoint> points,
                                            int width, int height, int dpi, IList<CieArrow> arrows = null)
        {
            PlotTransform unused;
            return RenderToBitmap(style, curves, points, width, height, dpi, out unused, arrows);
        }

        public static Bitmap RenderToBitmap(DiagramStyle style, IList<CieCurve> curves, IList<CiePoint> points,
                                            int width, int height, int dpi, out PlotTransform transform, IList<CieArrow> arrows = null)
        {
            transform = null;
            int pw = width;
            int ph = height;
            Bitmap bmp = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
            // 先按 96 DPI 建画布再画：本机系统 DPI 为 120（125% 缩放），
            // Graphics 会按 120/96 的比例再缩放一次图片，导致底图放大、右边被裁。
            bmp.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(bmp))
            using (GdiCanvas canvas = new GdiCanvas(g, 1.0))
            {
                g.Clear(Color.White);
                DiagramStyle exportStyle = style;
                DiagramStyle scaled = style.Clone();
                scaled.FontScale = Math.Max(1.0, Math.Min(2.4, (width + height) / 2000.0));
                DiagramRenderer renderer = new DiagramRenderer(scaled, curves, points, arrows);
                renderer.Render(canvas, width, height);
                transform = renderer.LastTransform;
            }
            // DPI 只写元数据：在 96 DPI 的位图上按逻辑坐标画完再改分辨率，
            // 这样绘制过程与屏幕预览完全一致（否则 GDI+ 会按画布 DPI 再缩放一次底图）。
            bmp.SetResolution(dpi, dpi);
            return bmp;
        }

        /// <summary>导出 PNG / TIFF（按 <paramref name="dpi"/> 放大绘制，保证清晰度）。</summary>
        public static void SaveBitmap(DiagramStyle style, IList<CieCurve> curves, IList<CiePoint> points,
                                      string path, ImageFormat format, int width, int height, int dpi, IList<CieArrow> arrows = null)
        {
            using (Bitmap bmp = RenderToBitmap(style, curves, points, width, height, dpi, arrows))
            {
                if (format == ImageFormat.Tiff)
                {
                    bmp.Save(path, ImageFormat.Tiff);
                }
                else
                {
                    bmp.Save(path, ImageFormat.Png);
                }
            }
        }

        /// <summary>导出 SVG 矢量图（文字为 &lt;text&gt;，可无损放大；真彩背景内嵌为 PNG）。</summary>
        public static void SaveSvg(DiagramStyle style, IList<CieCurve> curves, IList<CiePoint> points,
                                   string path, int width, int height, IList<CieArrow> arrows = null)
        {
            SvgCanvas canvas = new SvgCanvas(width, height, 1.0);
            DiagramStyle exportStyle = style;
                DiagramStyle scaled = style.Clone();
                scaled.FontScale = Math.Max(1.0, Math.Min(2.4, (width + height) / 2000.0));
                new DiagramRenderer(scaled, curves, points, arrows).Render(canvas, width, height);
            File.WriteAllText(path, canvas.ToSvg(), new UTF8Encoding(false));
        }

        // ---------------------------------------------------------------- 数据

        /// <summary>导出结果 CSV（带 UTF-8 BOM，Excel 直接双击不乱码）。</summary>
        public static void SaveCsv(string path, IList<CieCurve> curves, IList<CiePoint> points)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("类型,名称,X,Y,Z,x,y,u',v',峰值波长(nm),峰值强度,相关色温(K),Δuv,点数,波长范围");
            for (int i = 0; i < curves.Count; i++)
            {
                CieCurve c = curves[i];
                if (c == null || c.Result == null) continue;
                CurveResult r = c.Result;
                sb.Append("曲线,").Append(Q(c.Name)).Append(',')
                  .Append(N(r.X)).Append(',').Append(N(r.Y)).Append(',').Append(N(r.Z)).Append(',')
                  .Append(N(r.x)).Append(',').Append(N(r.y)).Append(',')
                  .Append(N(r.u)).Append(',').Append(N(r.v)).Append(',')
                  .Append(N(r.PeakWavelength)).Append(',').Append(N(r.PeakValue)).Append(',')
                  .Append(r.HasCct ? N(r.Cct) : "").Append(',').Append(r.HasCct ? N(r.Duv) : "").Append(',')
                  .Append(r.Count).Append(',').Append(N(r.WlMin)).Append("–").Append(N(r.WlMax))
                  .AppendLine();
            }
            for (int i = 0; i < points.Count; i++)
            {
                CiePoint p = points[i];
                if (p == null) continue;
                double Y = 1.0;
                double X = p.x / Math.Max(1e-9, p.y);
                double Z = (1.0 - p.x - p.y) / Math.Max(1e-9, p.y);
                sb.Append("坐标点,").Append(Q(p.Name)).Append(',')
                  .Append(N(X)).Append(',').Append(N(Y)).Append(',').Append(N(Z)).Append(',')
                  .Append(N(p.x)).Append(',').Append(N(p.y)).Append(',')
                  .Append(N(p.U)).Append(',').Append(N(p.V)).Append(',')
                  .Append(",,")
                  .Append(double.IsNaN(p.Cct) ? "" : N(p.Cct)).Append(',')
                  .Append(double.IsNaN(p.Duv) ? "" : N(p.Duv)).Append(',')
                  .Append(',').AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>导出可读的 TXT 报告（含计算公式说明，同 CIEdraw 的做法）。</summary>
        public static void SaveTxt(string path, IList<CieCurve> curves, IList<CiePoint> points,
                                   DiagramStyle style, int width, int height)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CIE1931 色度计算报告（CIE1931xy 重建版 2.1.0）");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            sb.AppendLine("图型：" + PlotSpace.Title(style.Kind) + "，画布 " + width + " × " + height + " px");
            sb.AppendLine();
            sb.AppendLine("【计算公式】");
            sb.AppendLine("  X = Σ S(λ)·x̄(λ)   Y = Σ S(λ)·ȳ(λ)   Z = Σ S(λ)·z̄(λ)   （与原版程序同一口径，1 nm 采样求和）");
            sb.AppendLine("  x = X/(X+Y+Z)   y = Y/(X+Y+Z)");
            sb.AppendLine("  u' = 4x/(-2x+12y+3)   v' = 9y/(-2x+12y+3)         （CIE 1976 UCS）");
            sb.AppendLine("  CCT：黑体轨迹按普朗克定律逐点积分，在 CIE 1960 uv 中取最近点，Δuv 为到轨迹的最小距离；");
            sb.AppendLine("        Δuv > 0.05 表示偏离黑体轨迹较远，CCT 仅供参考。");
            sb.AppendLine();
            sb.AppendLine("【光谱曲线】");
            for (int i = 0; i < curves.Count; i++)
            {
                CieCurve c = curves[i];
                if (c == null || c.Result == null) continue;
                CurveResult r = c.Result;
                sb.AppendLine("  " + (i + 1) + ". " + c.Name);
                sb.AppendLine("     波长范围：" + N(r.WlMin) + "–" + N(r.WlMax) + " nm，点数 " + r.Count + "，步长 " + N(r.Step) + " nm");
                sb.AppendLine("     峰值：" + N(r.PeakWavelength) + " nm @" + N(r.PeakValue));
                sb.AppendLine("     X=" + N(r.X) + "  Y=" + N(r.Y) + "  Z=" + N(r.Z));
                sb.AppendLine("     x=" + r.x.ToString("0.000000", Inv) + "  y=" + r.y.ToString("0.000000", Inv) +
                              "  u'=" + r.u.ToString("0.000000", Inv) + "  v'=" + r.v.ToString("0.000000", Inv));
                if (r.HasCct)
                {
                    sb.AppendLine("     CCT = " + r.Cct.ToString("0", Inv) + " K，Δuv = " + r.Duv.ToString("0.00000", Inv) +
                                  (r.CctReliable ? "" : "（偏离黑体轨迹较远，仅供参考）"));
                }
                else
                {
                    sb.AppendLine("     CCT = —（无法计算）");
                }
            }
            sb.AppendLine();
            sb.AppendLine("【色坐标点】");
            for (int i = 0; i < points.Count; i++)
            {
                CiePoint p = points[i];
                if (p == null) continue;
                sb.AppendLine("  " + (i + 1) + ". " + p.Name + "  x=" + p.x.ToString("0.000000", Inv) +
                              "  y=" + p.y.ToString("0.000000", Inv) + "  u'=" + p.U.ToString("0.000000", Inv) +
                              "  v'=" + p.V.ToString("0.000000", Inv) +
                              (double.IsNaN(p.Cct) ? "" : "  CCT=" + p.Cct.ToString("0", Inv) + " K  Δuv=" + p.Duv.ToString("0.00000", Inv)));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "";
            return v.ToString("0.######", Inv);
        }

        private static string Q(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
