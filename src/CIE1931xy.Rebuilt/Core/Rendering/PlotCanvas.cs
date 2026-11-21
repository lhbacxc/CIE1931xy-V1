using System;
using System.Drawing;

namespace CIE1931xy.Core.Rendering
{
    /// <summary>画布抽象：同一套绘图指令既能落到 GDI+ 位图，也能落到 SVG 矢量文件。</summary>
    internal enum TextAlignH { Left, Center, Right }
    internal enum TextAlignV { Top, Middle, Bottom }

    internal readonly struct SizeD
    {
        public readonly double Width;
        public readonly double Height;
        public SizeD(double w, double h) { Width = w; Height = h; }
    }

    internal readonly struct RectangleD
    {
        public readonly double Left;
        public readonly double Top;
        public readonly double Width;
        public readonly double Height;
        public RectangleD(double left, double top, double width, double height)
        {
            Left = left; Top = top; Width = width; Height = height;
        }
        public double Right { get { return Left + Width; } }
        public double Bottom { get { return Top + Height; } }
    }

    /// <summary>绘图原语接口（坐标和文字大小使用输出像素，按 PixelScale 缩放）。</summary>
    internal interface IPlotCanvas
    {
        double PixelScale { get; }
        string ChineseFontFamily { get; set; }
        void DrawLine(Color color, double width, double x1, double y1, double x2, double y2);
        void DrawPolyline(Color color, double width, float[] xs, float[] ys);
        void DrawBezier(Color color, double width, PointF start, PointF control1, PointF control2, PointF end);
        void PushClip(RectangleD rect);
        void PopClip();
        void FillPolygon(Color color, float[] xs, float[] ys);
        void DrawPolygon(Color color, float width, float[] xs, float[] ys);
        void DrawRect(Color color, double width, double x, double y, double w, double h);
        void FillRect(Color color, double x, double y, double w, double h);
        void DrawEllipse(Color color, double width, double x, double y, double w, double h);
        void FillEllipse(Color color, double cx, double cy, double rx, double ry);
        void DrawText(string text, Color color, string fontName, double fontSizePt, bool bold,
                      double x, double y, TextAlignH alignH, TextAlignV alignV, double rotationDeg);
        SizeD MeasureText(string text, string fontName, double fontSizePt, bool bold);
        void DrawImage(Bitmap bitmap, double x, double y, double w, double h);
    }
}
