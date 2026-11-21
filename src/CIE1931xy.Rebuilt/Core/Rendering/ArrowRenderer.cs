using System.Collections.Generic;
using System.Drawing;

namespace CIE1931xy.Core.Rendering
{
    internal static class ArrowRenderer
    {
        public static void Draw(IPlotCanvas canvas, DiagramStyle style, PlotTransform tr,
                                IList<CieArrow> arrows, IList<CieCurve> curves, IList<CiePoint> points)
        {
            if (arrows == null || arrows.Count == 0) return;
            bool clipped = false;
            try
            {
                foreach (CieArrow arrow in arrows)
                {
                    ArrowGeometry geometry;
                    if (!TryGeometry(style, tr, arrow, curves, points, canvas.PixelScale, out geometry)) continue;
                    if (!clipped)
                    {
                        canvas.PushClip(new RectangleD(tr.Left, tr.Top, tr.Width, tr.Height));
                        clipped = true;
                    }
                    if (style.HighlightSelection && arrow.Selected)
                    {
                        DrawShaft(canvas, geometry, style.SelectionColor, arrow.LineWidth + 6);
                        canvas.DrawPolygon(style.SelectionColor, 3, geometry.HeadX, geometry.HeadY);
                    }
                    DrawShape(canvas, arrow, geometry);
                }
            }
            finally { if (clipped) canvas.PopClip(); }
        }

        public static bool TryGeometry(DiagramStyle style, PlotTransform tr, CieArrow arrow,
                                       IList<CieCurve> curves, IList<CiePoint> points, double scale, out ArrowGeometry geometry)
        {
            geometry = null;
            if (arrow == null || arrow.ValidationError() != null ||
                !arrow.Start.BelongsTo(curves, points) || !arrow.End.BelongsTo(curves, points) ||
                !arrow.Start.Visible || !arrow.End.Visible) return false;
            double sx, sy, ex, ey;
            arrow.Start.TryGetXy(out sx, out sy); arrow.End.TryGetXy(out ex, out ey);
            PlotSpace.ToDiagram(style.Kind, sx, sy, out sx, out sy);
            PlotSpace.ToDiagram(style.Kind, ex, ey, out ex, out ey);
            PointF start = new PointF(tr.X(sx), tr.Y(sy)), end = new PointF(tr.X(ex), tr.Y(ey));
            if (!Inside(tr, start) || !Inside(tr, end)) return false;
            float shiftX = (float)(arrow.OffsetX * tr.Width), shiftY = (float)(arrow.OffsetY * tr.Height);
            start.X += shiftX; start.Y += shiftY; end.X += shiftX; end.Y += shiftY;
            return ArrowGeometry.TryCreate(arrow, start, end, (style.MarkerSize + 3) * scale, scale, out geometry);
        }

        public static void DrawShape(IPlotCanvas canvas, CieArrow arrow, ArrowGeometry geometry, double widthScale = 1)
        {
            if (arrow.ShowOutline) DrawShaft(canvas, geometry, arrow.OutlineColor, arrow.LineWidth + 2 * widthScale);
            DrawShaft(canvas, geometry, arrow.Color, arrow.LineWidth);
            canvas.FillPolygon(arrow.Color, geometry.HeadX, geometry.HeadY);
            if (arrow.ShowOutline) canvas.DrawPolygon(arrow.OutlineColor, (float)(1.1 * widthScale), geometry.HeadX, geometry.HeadY);
        }

        private static void DrawShaft(IPlotCanvas canvas, ArrowGeometry geometry, Color color, double width)
        {
            if (geometry.Curved) canvas.DrawBezier(color, width, geometry.Start, geometry.Control1, geometry.Control2, geometry.End);
            else canvas.DrawLine(color, width, geometry.Start.X, geometry.Start.Y, geometry.End.X, geometry.End.Y);
        }

        private static bool Inside(PlotTransform tr, PointF p)
        {
            return p.X >= tr.Left && p.X <= tr.Left + tr.Width && p.Y >= tr.Top && p.Y <= tr.Top + tr.Height;
        }
    }
}
