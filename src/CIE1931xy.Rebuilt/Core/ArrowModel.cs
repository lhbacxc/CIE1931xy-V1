using System;
using System.Collections.Generic;
using System.Drawing;

namespace CIE1931xy.Core
{
    internal enum ArrowKind { Straight, Curved }

    /// <summary>绑定原数据对象，避免改名、排序或刷新表格后箭头指向错误数据。</summary>
    internal sealed class ArrowEndpoint
    {
        public readonly CiePoint Point;
        public readonly CieCurve Curve;
        public ArrowEndpoint(CiePoint point) { Point = point; }
        public ArrowEndpoint(CieCurve curve) { Curve = curve; }
        public object Source { get { return (object)Point ?? Curve; } }
        public string Name { get { return Point != null ? Point.Name : Curve != null ? Curve.Name : ""; } }
        public bool Selected { get { return Point != null ? Point.Selected : Curve != null && Curve.Selected; } }
        public bool Visible { get { return Point != null || Curve != null && Curve.Visible; } }

        public bool BelongsTo(IList<CieCurve> curves, IList<CiePoint> points)
        {
            return Point != null ? points.Contains(Point) : Curve != null && curves.Contains(Curve);
        }

        public bool SameSource(ArrowEndpoint other)
        {
            return other != null && Source != null && ReferenceEquals(Source, other.Source);
        }

        public bool TryGetXy(out double x, out double y)
        {
            x = y = double.NaN;
            if (Point != null) { x = Point.x; y = Point.y; }
            else if (Curve != null && Curve.Result != null) { x = Curve.Result.x; y = Curve.Result.y; }
            return double.IsFinite(x) && double.IsFinite(y);
        }
    }

    /// <summary>当前会话的方向标注，不进入偏好配置或色度计算。</summary>
    internal sealed class CieArrow
    {
        public ArrowEndpoint Start;
        public ArrowEndpoint End;
        public ArrowKind Kind;
        public Color Color = Color.White;
        public double LineWidth = 2.5;
        public double Curvature = 0.35;
        public bool BendLeft = true;
        public bool ShowOutline = true;
        public Color OutlineColor = Color.Black;
        // 绘图区宽/高的相对偏移；正 X 向右，正 Y 向下，平移不改变箭头方向。
        public double OffsetX;
        public double OffsetY = -0.07;
        public bool Selected;

        public CieArrow Clone() { return (CieArrow)MemberwiseClone(); }

        public string ValidationError()
        {
            if (Start == null || End == null) return "请选择起点和终点。";
            if (Start.SameSource(End)) return "起点和终点不能是同一个数据点。";
            double sx, sy, ex, ey;
            if (!Start.TryGetXy(out sx, out sy) || !End.TryGetXy(out ex, out ey)) return "端点坐标无效。";
            if (Math.Abs(sx - ex) < 1e-10 && Math.Abs(sy - ey) < 1e-10) return "两个点的坐标重合，请选择其他端点。";
            if (!double.IsFinite(LineWidth) || LineWidth < 1 || LineWidth > 12) return "线宽须在 1–12 之间。";
            if (!double.IsFinite(Curvature) || Curvature < 0 || Curvature > 1) return "弯曲程度须在 0–100% 之间。";
            if (Kind != ArrowKind.Straight && Kind != ArrowKind.Curved) return "箭头类型无效。";
            if (!double.IsFinite(OffsetX) || !double.IsFinite(OffsetY) || Math.Abs(OffsetX) > 2 || Math.Abs(OffsetY) > 2)
                return "箭头位置无效。";
            return null;
        }
    }
}
