using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace CIE1931xy.Core.Rendering
{
    /// <summary>
    /// SVG 画布：把同一套绘图指令写成矢量元素（文字为 &lt;text&gt;，位图背景内嵌为 base64 PNG）。
    /// 坐标单位是像素，viewBox 为 1:1，因此任意放大都清晰。
    /// </summary>
    internal sealed class SvgCanvas : IPlotCanvas
    {
        private readonly StringBuilder _body = new StringBuilder();
        private readonly int _width;
        private readonly int _height;
        private readonly double _scale;
        private int _clipSeq;
        private static Graphics s_measure;
        private static Bitmap s_measureBmp;

        public SvgCanvas(int width, int height, double pixelScale)
        {
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
            _scale = pixelScale <= 0 ? 1.0 : pixelScale;
            _body.Append("<rect x=\"0\" y=\"0\" width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>\n");
        }

        public double PixelScale { get { return _scale; } }

        private static string F(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Col(Color c)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        private static string Opacity(Color c)
        {
            if (c.A >= 255) return "";
            return " opacity=\"" + F(c.A / 255.0) + "\"";
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        public void DrawLine(Color color, double width, double x1, double y1, double x2, double y2)
        {
            _body.Append("<line x1=\"").Append(F(x1)).Append("\" y1=\"").Append(F(y1))
                 .Append("\" x2=\"").Append(F(x2)).Append("\" y2=\"").Append(F(y2))
                 .Append("\" stroke=\"").Append(Col(color)).Append("\" stroke-width=\"").Append(F(width * _scale))
                 .Append("\" stroke-linecap=\"round\"").Append(Opacity(color)).Append("/>\n");
        }

        public void DrawPolyline(Color color, double width, float[] xs, float[] ys)
        {
            if (xs == null || xs.Length < 2) return;
            // 用二次贝塞尔把折线画成光滑曲线（顶点取相邻点中点），
            // 否则放大 SVG 会看到一段段的直线接缝。
            StringBuilder path = new StringBuilder();
            AppendSmoothPath(path, xs, ys, xs.Length, 1.0);
            _body.Append("<path d=\"").Append(path).Append("\" fill=\"none\" stroke=\"")
                 .Append(Col(color)).Append("\" stroke-width=\"").Append(F(width * _scale))
                 .Append("\" stroke-linejoin=\"round\" stroke-linecap=\"round\"").Append(Opacity(color)).Append("/>\n");
        }

        private static void AppendSmoothPath(StringBuilder path, float[] xs, float[] ys, int count, double scale)
        {
            path.Append("M ").Append(F(xs[0] * scale)).Append(' ').Append(F(ys[0] * scale));
            for (int i = 1; i + 1 < count; i++)
            {
                double mx = (xs[i] + xs[i + 1]) / 2.0;
                double my = (ys[i] + ys[i + 1]) / 2.0;
                path.Append(" Q ").Append(F(xs[i] * scale)).Append(' ').Append(F(ys[i] * scale))
                    .Append(' ').Append(F(mx * scale)).Append(' ').Append(F(my * scale));
            }
            path.Append(" L ").Append(F(xs[count - 1] * scale)).Append(' ').Append(F(ys[count - 1] * scale));
        }

        /// <summary>以连续横向渐变和细扫描带填充颜色场，由平滑光谱轮廓裁剪；不嵌入位图。</summary>
        public void FillChromaGradients(Color[] colors, int cols, int rows, double x, double y, double w, double h,
                                       float[] clipXs, float[] clipYs)
        {
            if (colors == null || cols < 2 || rows <= 0 || colors.Length != cols * rows) return;
            string clipId = "chroma" + (++_clipSeq).ToString(CultureInfo.InvariantCulture);
            bool useClip = clipXs != null && clipYs != null && clipXs.Length >= 3 && clipXs.Length == clipYs.Length;
            _body.Append("<defs>\n");
            if (useClip)
            {
                int count = clipXs.Length;
                if (clipXs[0] == clipXs[count - 1] && clipYs[0] == clipYs[count - 1]) count--;
                _body.Append("<clipPath id=\"").Append(clipId).Append("\" clipPathUnits=\"userSpaceOnUse\"><path d=\"");
                AppendSmoothPath(_body, clipXs, clipYs, count, _scale);
                _body.Append(" Z\" shape-rendering=\"geometricPrecision\"/></clipPath>\n");
            }
            for (int r = 0; r < rows; r++)
            {
                _body.Append("<linearGradient id=\"").Append(clipId).Append('-').Append(r)
                    .Append("\" gradientUnits=\"userSpaceOnUse\" x1=\"").Append(F(x * _scale))
                    .Append("\" x2=\"").Append(F((x + w) * _scale))
                    .Append("\" y1=\"0\" y2=\"0\" color-interpolation=\"sRGB\">\n");
                List<int> stops = new List<int> { 0 };
                SimplifyGradient(colors, r * cols, 0, cols - 1, stops);
                foreach (int c in stops)
                {
                    Color col = colors[r * cols + c];
                    _body.Append("<stop offset=\"").Append((c / (double)(cols - 1)).ToString("0.######", CultureInfo.InvariantCulture))
                        .Append("\" stop-color=\"").Append(Col(col)).Append("\"/>\n");
                }
                _body.Append("</linearGradient>\n");
            }
            _body.Append("</defs>\n<g");
            if (useClip) _body.Append(" clip-path=\"url(#").Append(clipId).Append(")\"");
            _body.Append(">\n");
            double ch = h / rows;
            for (int r = 0; r < rows; r++)
            {
                // 不透明扫描带覆盖整个裁剪区；关闭内部边缘抗锯齿，防止露出底层网格。
                _body.Append("<rect x=\"").Append(F(x * _scale)).Append("\" y=\"").Append(F((y + r * ch) * _scale))
                    .Append("\" width=\"").Append(F(w * _scale)).Append("\" height=\"").Append(F((ch + 0.01) * _scale))
                    .Append("\" fill=\"url(#").Append(clipId).Append('-').Append(r)
                    .Append(")\" stroke=\"none\" shape-rendering=\"crispEdges\"/>\n");
            }
            _body.Append("</g>\n");
        }

        private static void SimplifyGradient(Color[] colors, int row, int first, int last, List<int> stops)
        {
            // 仅省略可由端点插值还原的色标，RGB 各通道误差不超过一个 8 位色阶。
            Color a = colors[row + first], b = colors[row + last];
            double maxError = 1.0;
            int split = -1;
            for (int c = first + 1; c < last; c++)
            {
                double t = (c - first) / (double)(last - first);
                Color color = colors[row + c];
                double error = Math.Max(Math.Abs(color.R - (a.R + (b.R - a.R) * t)),
                    Math.Max(Math.Abs(color.G - (a.G + (b.G - a.G) * t)),
                             Math.Abs(color.B - (a.B + (b.B - a.B) * t))));
                if (error > maxError) { maxError = error; split = c; }
            }
            if (split >= 0)
            {
                SimplifyGradient(colors, row, first, split, stops);
                SimplifyGradient(colors, row, split, last, stops);
            }
            else stops.Add(last);
        }

        public void DrawPolygon(Color color, float width, float[] xs, float[] ys)
        {
            if (xs == null || ys == null || xs.Length < 2) return;
            _body.Append("<polygon points=\"");
            for (int i = 0; i < xs.Length; i++)
            {
                if (i > 0) _body.Append(' ');
                _body.Append(F(xs[i] * _scale)).Append(',').Append(F(ys[i] * _scale));
            }
            _body.Append("\" fill=\"none\" stroke=\"").Append(Col(color)).Append("\" stroke-width=\"").Append(F(width * _scale))
                 .Append("\" stroke-linejoin=\"round\"").Append(Opacity(color)).Append("/>\n");
        }

        public void FillPolygon(Color color, float[] xs, float[] ys)
        {
            if (xs == null || xs.Length < 3) return;
            StringBuilder pts = new StringBuilder();
            for (int i = 0; i < xs.Length; i++)
            {
                if (i > 0) pts.Append(' ');
                pts.Append(F(xs[i])).Append(',').Append(F(ys[i]));
            }
            _body.Append("<polygon points=\"").Append(pts).Append("\" fill=\"").Append(Col(color))
                 .Append("\"").Append(Opacity(color)).Append("/>\n");
        }

        public void DrawRect(Color color, double width, double x, double y, double w, double h)
        {
            _body.Append("<rect x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
                 .Append("\" width=\"").Append(F(w)).Append("\" height=\"").Append(F(h))
                 .Append("\" fill=\"none\" stroke=\"").Append(Col(color)).Append("\" stroke-width=\"")
                 .Append(F(width * _scale)).Append("\"").Append(Opacity(color)).Append("/>\n");
        }

        public void FillRect(Color color, double x, double y, double w, double h)
        {
            _body.Append("<rect x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
                 .Append("\" width=\"").Append(F(w)).Append("\" height=\"").Append(F(h))
                 .Append("\" fill=\"").Append(Col(color)).Append("\"").Append(Opacity(color)).Append("/>\n");
        }

        public void DrawEllipse(Color color, double width, double x, double y, double w, double h)
        {
            _body.Append("<ellipse cx=\"").Append(F(x + w / 2)).Append("\" cy=\"").Append(F(y + h / 2))
                 .Append("\" rx=\"").Append(F(w / 2)).Append("\" ry=\"").Append(F(h / 2))
                 .Append("\" fill=\"none\" stroke=\"").Append(Col(color)).Append("\" stroke-width=\"")
                 .Append(F(width * _scale)).Append("\"").Append(Opacity(color)).Append("/>\n");
        }

        public void FillEllipse(Color color, double cx, double cy, double rx, double ry)
        {
            _body.Append("<ellipse cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
                 .Append("\" rx=\"").Append(F(rx)).Append("\" ry=\"").Append(F(ry))
                 .Append("\" fill=\"").Append(Col(color)).Append("\"").Append(Opacity(color)).Append("/>\n");
        }

        public void DrawText(string text, Color color, string fontName, double fontSizePt, bool bold,
                             double x, double y, TextAlignH alignH, TextAlignV alignV, double rotationDeg)
        {
            if (string.IsNullOrEmpty(text)) return;
            double px = fontSizePt * _scale;
            string anchor = alignH == TextAlignH.Left ? "start" : alignH == TextAlignH.Right ? "end" : "middle";
            string baseline = alignV == TextAlignV.Top ? "hanging" : alignV == TextAlignV.Bottom ? "alphabetic" : "middle";
            _body.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
                 .Append("\" font-family=\"").Append(Escape(fontName))
                 .Append(", Microsoft YaHei, Arial, sans-serif\" font-size=\"").Append(F(px))
                 .Append("\" fill=\"").Append(Col(color)).Append("\" text-anchor=\"").Append(anchor)
                 .Append("\" dominant-baseline=\"").Append(baseline).Append("\"");
            if (bold) _body.Append(" font-weight=\"bold\"");
            if (Math.Abs(rotationDeg) > 1e-6)
                _body.Append(" transform=\"rotate(").Append(F(rotationDeg)).Append(' ').Append(F(x)).Append(' ').Append(F(y)).Append(")\"");
            _body.Append('>').Append(Escape(text)).Append("</text>\n");
        }

        public SizeD MeasureText(string text, string fontName, double fontSizePt, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return new SizeD(0, 0);
            if (s_measure == null)
            {
                s_measureBmp = new Bitmap(4, 4);
                s_measure = Graphics.FromImage(s_measureBmp);
                s_measure.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            }
            float px = (float)(fontSizePt * _scale);
            using (Font font = new Font(fontName, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat fmt = new StringFormat(StringFormat.GenericTypographic))
            {
                fmt.FormatFlags |= StringFormatFlags.NoWrap;
                SizeF s = s_measure.MeasureString(text, font, PointF.Empty, fmt);
                return new SizeD(s.Width, s.Height);
            }
        }

        public void DrawImage(Bitmap bitmap, double x, double y, double w, double h)
        {
            if (bitmap == null) return;
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                string b64 = Convert.ToBase64String(ms.ToArray());
                _body.Append("<image x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
                     .Append("\" width=\"").Append(F(w)).Append("\" height=\"").Append(F(h))
                     .Append("\" preserveAspectRatio=\"none\" xlink:href=\"data:image/png;base64,")
                     .Append(b64).Append("\"/>\n");
            }
        }

        /// <summary>写出完整 SVG 文档。</summary>
        public string ToSvg()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" ");
            sb.Append("width=\"").Append(_width).Append("\" height=\"").Append(_height);
            sb.Append("\" viewBox=\"0 0 ").Append(_width).Append(' ').Append(_height).Append("\">\n");
            sb.Append(_body);
            sb.Append("</svg>\n");
            return sb.ToString();
        }
    }
}
