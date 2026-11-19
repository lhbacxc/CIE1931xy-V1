using System;
using System.Collections.Generic;
using System.Drawing;

namespace CIE1931xy.Core.Rendering
{
    /// <summary>图型：CIE 1931 xy 或 CIE 1976 u'v'。</summary>
    internal enum DiagramKind
    {
        Xy1931 = 0,
        Uv1976 = 1
    }

    /// <summary>数据坐标范围。</summary>
    internal readonly struct AxisBounds
    {
        public readonly double XMin;
        public readonly double XMax;
        public readonly double YMin;
        public readonly double YMax;

        public AxisBounds(double xmin, double xmax, double ymin, double ymax)
        {
            XMin = xmin; XMax = xmax; YMin = ymin; YMax = ymax;
        }

        public double Width { get { return XMax - XMin; } }
        public double Height { get { return YMax - YMin; } }
    }

    /// <summary>数据坐标 ↔ 像素坐标（y 轴向上）。</summary>
    internal sealed class PlotTransform
    {
        private readonly AxisBounds _b;
        private readonly float _left, _top, _w, _h;

        public PlotTransform(AxisBounds bounds, float left, float top, float width, float height)
        {
            _b = bounds;
            _left = left; _top = top; _w = width; _h = height;
        }

        public AxisBounds Bounds { get { return _b; } }
        public float Left { get { return _left; } }
        public float Top { get { return _top; } }
        public float Width { get { return _w; } }
        public float Height { get { return _h; } }

        public float X(double value)
        {
            double n = (value - _b.XMin) / _b.Width;
            return (float)(_left + n * _w);
        }

        public float Y(double value)
        {
            double n = (value - _b.YMin) / _b.Height;
            return (float)(_top + (1.0 - n) * _h);
        }

        public double XOf(float px)
        {
            return _b.XMin + (px - _left) / _w * _b.Width;
        }

        public double YOf(float py)
        {
            return _b.YMin + (1.0 - (py - _top) / _h) * _b.Height;
        }
    }

    /// <summary>图型坐标换算与轨迹采样。</summary>
    internal static class PlotSpace
    {
        public const double LocusStartNm = 380.0;
        public const double LocusEndNm = 700.0;

        public static string AxisLabelX(DiagramKind kind) { return kind == DiagramKind.Xy1931 ? "x" : "u'"; }
        public static string AxisLabelY(DiagramKind kind) { return kind == DiagramKind.Xy1931 ? "y" : "v'"; }
        public static string Title(DiagramKind kind)
        {
            return kind == DiagramKind.Xy1931 ? "CIE 1931 色度图 (x, y)" : "CIE 1976 UCS 色度图 (u', v')";
        }

        public static AxisBounds DefaultBounds(DiagramKind kind)
        {
            return kind == DiagramKind.Xy1931
                ? new AxisBounds(0.0, 0.8, 0.0, 0.9)
                : new AxisBounds(0.0, 0.65, 0.0, 0.65);
        }

        /// <summary>色坐标 xy → 图型坐标。</summary>
        public static void ToDiagram(DiagramKind kind, double x, double y, out double dx, out double dy)
        {
            if (kind == DiagramKind.Xy1931)
            {
                dx = x; dy = y;
                return;
            }
            double u, v;
            Colorimetry.XyToUv(x, y, out u, out v);
            dx = u; dy = v;
        }

        /// <summary>图型坐标 → 色坐标 xy。</summary>
        public static bool FromDiagram(DiagramKind kind, double dx, double dy, out double x, out double y)
        {
            if (kind == DiagramKind.Xy1931)
            {
                x = dx; y = dy;
                return true;
            }
            return Colorimetry.UvToXy(dx, dy, out x, out y);
        }

        /// <summary>光谱轨迹折线（图型坐标），可选在 380 nm / 700 nm 处闭合（补紫线）。</summary>
        public static void SpectralLocus(DiagramKind kind, out float[] xs, out float[] ys, bool closed)
        {
            double[] wx = Colorimetry.SpectralLocusX;
            double[] wy = Colorimetry.SpectralLocusY;
            double[] wl = Colorimetry.LocusWavelengths;
            List<float> px = new List<float>();
            List<float> py = new List<float>();
            for (int i = 0; i < wl.Length; i++)
            {
                if (wl[i] < LocusStartNm || wl[i] > LocusEndNm) continue;
                double dx, dy;
                ToDiagram(kind, wx[i], wy[i], out dx, out dy);
                px.Add((float)dx);
                py.Add((float)dy);
            }
            if (closed && px.Count > 2)
            {
                px.Add(px[0]);
                py.Add(py[0]);
            }
            xs = px.ToArray();
            ys = py.ToArray();
        }

        /// <summary>sRGB 基色在目标图型中的三角形顶点（闭合）。</summary>
        public static void SrgbTriangle(DiagramKind kind, out float[] xs, out float[] ys)
        {
            double[][] primaries =
            {
                new double[] { 0.64, 0.33 },   // R
                new double[] { 0.30, 0.60 },   // G
                new double[] { 0.15, 0.06 }    // B
            };
            float[] ax = new float[primaries.Length];
            float[] ay = new float[primaries.Length];
            for (int i = 0; i < primaries.Length; i++)
            {
                double dx, dy;
                ToDiagram(kind, primaries[i][0], primaries[i][1], out dx, out dy);
                ax[i] = (float)dx;
                ay[i] = (float)dy;
            }
            xs = ax;
            ys = ay;
        }

        /// <summary>判断点是否在图型坐标系的可见色域三角形（光谱轨迹多边形）内。</summary>
        public static bool InsideSpectrum(DiagramKind kind, double dx, double dy)
        {
            float[] xs, ys;
            SpectralLocus(kind, out xs, out ys, true);
            return PointInPolygon(dx, dy, xs, ys);
        }

        public static bool PointInPolygon(double x, double y, float[] xs, float[] ys)
        {
            if (xs == null || xs.Length < 3) return false;
            bool inside = false;
            int j = xs.Length - 1;
            for (int i = 0; i < xs.Length; i++)
            {
                if (((ys[i] > y) != (ys[j] > y)) &&
                    (x < (xs[j] - xs[i]) * (y - ys[i]) / (ys[j] - ys[i]) + xs[i]))
                {
                    inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}
