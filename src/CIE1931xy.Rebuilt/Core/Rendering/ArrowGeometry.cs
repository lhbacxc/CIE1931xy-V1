using System;
using System.Drawing;

namespace CIE1931xy.Core.Rendering
{
    /// <summary>GDI 与 SVG 共用的箭身、终点切线与箭头几何。</summary>
    internal sealed class ArrowGeometry
    {
        public PointF Start, Control1, Control2, End, Tip;
        public float[] HeadX, HeadY;
        public bool Curved;

        public PointF AtShaft(double t)
        {
            double s = 1 - t;
            return new PointF((float)(s * s * s * Start.X + 3 * s * s * t * Control1.X + 3 * s * t * t * Control2.X + t * t * t * End.X),
                              (float)(s * s * s * Start.Y + 3 * s * s * t * Control1.Y + 3 * s * t * t * Control2.Y + t * t * t * End.Y));
        }

        public RectangleF Bounds()
        {
            float left = Tip.X, top = Tip.Y, right = Tip.X, bottom = Tip.Y;
            for (int i = 0; i <= 64; i++)
            {
                PointF p = AtShaft(i / 64.0);
                left = Math.Min(left, p.X); right = Math.Max(right, p.X);
                top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
            }
            for (int i = 0; i < HeadX.Length; i++)
            {
                left = Math.Min(left, HeadX[i]); right = Math.Max(right, HeadX[i]);
                top = Math.Min(top, HeadY[i]); bottom = Math.Max(bottom, HeadY[i]);
            }
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        public double DistanceTo(PointF point)
        {
            double s0 = Side(point, 0, 1), s1 = Side(point, 1, 2), s2 = Side(point, 2, 0);
            if ((s0 >= 0 && s1 >= 0 && s2 >= 0) || (s0 <= 0 && s1 <= 0 && s2 <= 0)) return 0;
            double distance = double.MaxValue;
            PointF previous = Start;
            for (int i = 1; i <= 64; i++)
            {
                PointF next = AtShaft(i / 64.0);
                distance = Math.Min(distance, SegmentDistance(point, previous, next));
                previous = next;
            }
            for (int i = 0; i < HeadX.Length; i++)
            {
                int j = (i + 1) % HeadX.Length;
                distance = Math.Min(distance, SegmentDistance(point, new PointF(HeadX[i], HeadY[i]), new PointF(HeadX[j], HeadY[j])));
            }
            return distance;
        }

        private double Side(PointF p, int a, int b)
        {
            return (HeadX[b] - HeadX[a]) * (p.Y - HeadY[a]) - (HeadY[b] - HeadY[a]) * (p.X - HeadX[a]);
        }

        private static double SegmentDistance(PointF point, PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared < 1e-10 ? 0 : Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
            return Distance(point, new PointF((float)(a.X + t * dx), (float)(a.Y + t * dy)));
        }

        public static bool TryCreate(CieArrow arrow, PointF from, PointF to,
                                     double clearance, double scale, out ArrowGeometry result)
        {
            result = null;
            double dx = to.X - from.X, dy = to.Y - from.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(chord) || chord < clearance * 2 + 6 * scale) return false;
            double bend = arrow.Kind == ArrowKind.Curved ? arrow.Curvature * (arrow.BendLeft ? 1 : -1) : 0;
            PointF control = new PointF((float)((from.X + to.X) / 2.0 + dy * bend),
                                       (float)((from.Y + to.Y) / 2.0 - dx * bend));
            double t0 = Trim(from, control, to, clearance);
            double t1 = 1 - Trim(to, control, from, clearance);
            PointF start = At(from, control, to, t0), tip = At(from, control, to, t1);
            PointF tangent = Derivative(from, control, to, t1);
            double tangentLength = Length(tangent);
            double available = Distance(start, tip);
            if (tangentLength < 1e-6 || available < 4 * scale) return false;
            double headLength = Math.Min(Math.Max(9, arrow.LineWidth * 3.8) * scale, available * 0.45);
            double headWidth = Math.Min(Math.Max(3.6, arrow.LineWidth * 1.9) * scale, headLength * 0.58);
            double ux = tangent.X / tangentLength, uy = tangent.Y / tangentLength;
            PointF center = new PointF((float)(tip.X - ux * headLength), (float)(tip.Y - uy * headLength));

            // 从终点向前找箭头底部，箭身以原曲线子段接到箭头中心。
            double low = t0, high = t1;
            for (int i = 0; i < 28; i++)
            {
                double mid = (low + high) / 2;
                if (Distance(At(from, control, to, mid), tip) > headLength) low = mid;
                else high = mid;
            }
            double shaftT = (low + high) / 2, span = shaftT - t0;
            PointF d0 = Derivative(from, control, to, t0), d1 = Derivative(from, control, to, shaftT);
            result = new ArrowGeometry
            {
                Start = start, End = center, Tip = tip,
                Control1 = new PointF((float)(start.X + d0.X * span / 3), (float)(start.Y + d0.Y * span / 3)),
                Control2 = new PointF((float)(center.X - d1.X * span / 3), (float)(center.Y - d1.Y * span / 3)),
                Curved = Math.Abs(bend) > 1e-9,
                HeadX = new[] { tip.X, (float)(center.X - uy * headWidth), (float)(center.X + uy * headWidth) },
                HeadY = new[] { tip.Y, (float)(center.Y + ux * headWidth), (float)(center.Y - ux * headWidth) }
            };
            return true;
        }

        private static double Trim(PointF from, PointF control, PointF to, double clearance)
        {
            double low = 0, high = 0.5;
            for (int i = 0; i < 28; i++)
            {
                double mid = (low + high) / 2;
                if (Distance(from, At(from, control, to, mid)) < clearance) low = mid;
                else high = mid;
            }
            return (low + high) / 2;
        }

        private static PointF At(PointF a, PointF b, PointF c, double t)
        {
            double s = 1 - t;
            return new PointF((float)(s * s * a.X + 2 * s * t * b.X + t * t * c.X),
                              (float)(s * s * a.Y + 2 * s * t * b.Y + t * t * c.Y));
        }

        private static PointF Derivative(PointF a, PointF b, PointF c, double t)
        {
            return new PointF((float)(2 * ((1 - t) * (b.X - a.X) + t * (c.X - b.X))),
                              (float)(2 * ((1 - t) * (b.Y - a.Y) + t * (c.Y - b.Y))));
        }

        private static double Length(PointF p) { return Math.Sqrt(p.X * p.X + p.Y * p.Y); }
        private static double Distance(PointF a, PointF b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
