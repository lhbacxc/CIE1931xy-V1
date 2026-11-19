using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace CIE1931xy.Core.Rendering
{
    /// <summary>GDI+ 画布实现（屏幕绘制 + 位图导出共用）。</summary>
    internal sealed class GdiCanvas : IPlotCanvas
    {
        private readonly Graphics _g;
        private readonly Dictionary<string, Pen> _pens = new Dictionary<string, Pen>();
        private readonly Dictionary<string, SolidBrush> _brushes = new Dictionary<string, SolidBrush>();
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private readonly double _scale;

        public GdiCanvas(Graphics g, double pixelScale)
        {
            _g = g;
            _scale = pixelScale <= 0 ? 1.0 : pixelScale;
            // Graphics 默认 PageUnit=Display，会按系统 DPI（本机 125% → 120）把逻辑坐标放大 1.25 倍；
            // 统一成像素单位，屏幕与导出才完全一致。
            _g.PageUnit = GraphicsUnit.Pixel;
            _g.SmoothingMode = SmoothingMode.AntiAlias;
            _g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            _g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            _g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public double PixelScale { get { return _scale; } }

        private Pen GetPen(Color color, double width)
        {
            int w = Math.Max(1, (int)Math.Round(width * _scale * 10));
            string key = color.ToArgb() + "|" + w;
            Pen pen;
            if (!_pens.TryGetValue(key, out pen))
            {
                pen = new Pen(color, w / 10.0f);
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                _pens[key] = pen;
            }
            return pen;
        }

        private SolidBrush GetBrush(Color color)
        {
            SolidBrush brush;
            string key = color.ToArgb().ToString(CultureInfo.InvariantCulture);
            if (!_brushes.TryGetValue(key, out brush))
            {
                brush = new SolidBrush(color);
                _brushes[key] = brush;
            }
            return brush;
        }

        private Font GetFont(string family, double sizePt, bool bold)
        {
            string key = family + "|" + Math.Round(sizePt * _scale, 2) + "|" + bold;
            Font font;
            if (!_fonts.TryGetValue(key, out font))
            {
                float px = (float)(sizePt * _scale);
                try
                {
                    font = new Font(family, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                }
                catch (ArgumentException)
                {
                    font = new Font("Microsoft Sans Serif", px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                }
                _fonts[key] = font;
            }
            return font;
        }

        // 所有坐标都乘以 _scale：导出高 DPI 图时用同一套逻辑坐标绘图，
        // 由这里统一换算成设备像素（不再用 Graphics 的缩放变换，避免 GDI+ 对位图再缩放一次）。
        private float P(double v)
        {
            return (float)(v * _scale);
        }

        public void DrawLine(Color color, double width, double x1, double y1, double x2, double y2)
        {
            _g.DrawLine(GetPen(color, width), P(x1), P(y1), P(x2), P(y2));
        }

        public void DrawPolyline(Color color, double width, float[] xs, float[] ys)
        {
            if (xs == null || xs.Length < 2) return;
            PointF[] pts = new PointF[xs.Length];
            for (int i = 0; i < xs.Length; i++) pts[i] = new PointF(P(xs[i]), P(ys[i]));
            _g.DrawLines(GetPen(color, width), pts);
        }

        public void DrawPolygon(Color color, float width, float[] xs, float[] ys)
        {
            if (xs == null || ys == null || xs.Length < 2) return;
            PointF[] pts = new PointF[xs.Length];
            for (int i = 0; i < xs.Length; i++) pts[i] = new PointF(P(xs[i]), P(ys[i]));
            _g.DrawPolygon(GetPen(color, width), pts);
        }

        public void FillPolygon(Color color, float[] xs, float[] ys)
        {
            if (xs == null || xs.Length < 3) return;
            PointF[] pts = new PointF[xs.Length];
            for (int i = 0; i < xs.Length; i++) pts[i] = new PointF(P(xs[i]), P(ys[i]));
            _g.FillPolygon(GetBrush(color), pts);
        }

        public void DrawRect(Color color, double width, double x, double y, double w, double h)
        {
            _g.DrawRectangle(GetPen(color, width), P(x), P(y), P(w), P(h));
        }

        public void FillRect(Color color, double x, double y, double w, double h)
        {
            _g.FillRectangle(GetBrush(color), P(x), P(y), P(w), P(h));
        }

        public void DrawEllipse(Color color, double width, double x, double y, double w, double h)
        {
            _g.DrawEllipse(GetPen(color, width), P(x), P(y), P(w), P(h));
        }

        public void FillEllipse(Color color, double cx, double cy, double rx, double ry)
        {
            _g.FillEllipse(GetBrush(color), P(cx - rx), P(cy - ry), P(rx * 2), P(ry * 2));
        }

        public void DrawText(string text, Color color, string fontName, double fontSizePt, bool bold,
                             double x, double y, TextAlignH alignH, TextAlignV alignV, double rotationDeg)
        {
            if (string.IsNullOrEmpty(text)) return;
            Font font = GetFont(fontName, fontSizePt, bold);
            StringFormat fmt = new StringFormat(StringFormat.GenericTypographic);
            fmt.Alignment = alignH == TextAlignH.Left ? StringAlignment.Near
                          : alignH == TextAlignH.Right ? StringAlignment.Far : StringAlignment.Center;
            fmt.LineAlignment = alignV == TextAlignV.Top ? StringAlignment.Near
                              : alignV == TextAlignV.Bottom ? StringAlignment.Far : StringAlignment.Center;
            fmt.FormatFlags |= StringFormatFlags.NoWrap;

            GraphicsState state = _g.Save();
            if (Math.Abs(rotationDeg) > 1e-6)
            {
                _g.TranslateTransform(P(x), P(y));
                _g.RotateTransform((float)rotationDeg);
                _g.DrawString(text, font, GetBrush(color), 0f, 0f, fmt);
            }
            else
            {
                _g.DrawString(text, font, GetBrush(color), P(x), P(y), fmt);
            }
            _g.Restore(state);
        }

        public SizeD MeasureText(string text, string fontName, double fontSizePt, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return new SizeD(0, 0);
            Font font = GetFont(fontName, fontSizePt, bold);
            StringFormat fmt = new StringFormat(StringFormat.GenericTypographic);
            fmt.FormatFlags |= StringFormatFlags.NoWrap;
            SizeF s = _g.MeasureString(text, font, PointF.Empty, fmt);
            return new SizeD(s.Width, s.Height);
        }

        public void DrawImage(Bitmap bitmap, double x, double y, double w, double h)
        {
            if (bitmap == null) return;
            // 用显式目标矩形贴图：坐标已由 P() 换算成设备像素。
            // 注意不要用 DrawImageUnscaled —— 它会按“图片 DPI / 画布 DPI”再缩放一次
            // （144 DPI 的画布上底图会被放大 1.5 倍并裁掉右侧），这正是之前底图溢出的根因。
            int dx = (int)Math.Round(P(x));
            int dy = (int)Math.Round(P(y));
            int dw = Math.Max(1, (int)Math.Round(P(w)));
            int dh = Math.Max(1, (int)Math.Round(P(h)));
            if (bitmap.Width == dw && bitmap.Height == dh)
            {
                _g.DrawImage(bitmap, dx, dy, dw, dh);
                return;
            }
            // 先在像素单位下缩放到精确尺寸，再用“整数矩形”贴图：
            // 整数重载按设备像素处理，不受系统 DPI（本机 120）影响；
            // 浮点重载会按 PageUnit=Display 再换算一次，是小图被放大的根因。
            using (Bitmap scaled = new Bitmap(dw, dh, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (Graphics tg = Graphics.FromImage(scaled))
                {
                    tg.PageUnit = GraphicsUnit.Pixel;
                    tg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    tg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    tg.DrawImage(bitmap, new Rectangle(0, 0, dw, dh));
                }
                _g.DrawImage(scaled, dx, dy, dw, dh);
            }
        }
    }
}
