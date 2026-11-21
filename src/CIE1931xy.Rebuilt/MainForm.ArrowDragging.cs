using System;
using System.Drawing;
using System.Windows.Forms;
using CIE1931xy.Core;
using CIE1931xy.Core.Rendering;

namespace CIE1931xy
{
    internal sealed partial class MainForm
    {
        private CieArrow _draggedArrow;
        private PointF _arrowDragOrigin;
        private double _arrowDragOffsetX, _arrowDragOffsetY;
        private bool _arrowClickHandled;
        private long _lastArrowDragRender;

        private bool TryCanvasPoint(MouseEventArgs e, out PointF point)
        {
            point = PointF.Empty;
            if (_canvas.Image == null || _lastTransform == null) return false;
            Size image = _canvas.Image.Size;
            double scale = Math.Min((double)_canvas.ClientSize.Width / image.Width, (double)_canvas.ClientSize.Height / image.Height);
            if (scale <= 0) return false;
            point = new PointF((float)((e.X - (_canvas.ClientSize.Width - image.Width * scale) / 2) / scale),
                               (float)((e.Y - (_canvas.ClientSize.Height - image.Height * scale) / 2) / scale));
            return true;
        }

        private CieArrow HitArrow(PointF point)
        {
            PlotTransform tr = _lastTransform;
            if (tr == null || point.X < tr.Left || point.X > tr.Left + tr.Width ||
                point.Y < tr.Top || point.Y > tr.Top + tr.Height) return null;
            foreach (CiePoint data in _points)
                if (data != null && HitsMarker(point, data.x, data.y)) return null;
            foreach (CieCurve curve in _curves)
                if (curve != null && curve.Visible && curve.Result != null && HitsMarker(point, curve.Result.x, curve.Result.y)) return null;
            for (int i = _arrows.Count - 1; i >= 0; i--)
            {
                ArrowGeometry geometry;
                CieArrow arrow = _arrows[i];
                if (ArrowRenderer.TryGeometry(_style, tr, arrow, _curves, _points, 1, out geometry) &&
                    geometry.DistanceTo(point) <= Math.Max(8, arrow.LineWidth / 2 + 5)) return arrow;
            }
            return null;
        }

        private bool HitsMarker(PointF point, double x, double y)
        {
            PlotSpace.ToDiagram(_style.Kind, x, y, out x, out y);
            double dx = _lastTransform.X(x) - point.X, dy = _lastTransform.Y(y) - point.Y;
            double radius = _style.MarkerSize + 3;
            return dx * dx + dy * dy <= radius * radius;
        }

        private void ClearArrowSelection()
        {
            bool changed = false;
            foreach (CieArrow arrow in _arrows) { changed |= arrow.Selected; arrow.Selected = false; }
            if (changed) ScheduleRender();
        }

        private void CanvasArrowMouseDown(object sender, MouseEventArgs e)
        {
            _arrowClickHandled = false;
            if (e.Button != MouseButtons.Left) return;
            PointF point;
            if (!TryCanvasPoint(e, out point)) return;
            CieArrow arrow = HitArrow(point);
            if (arrow == null) return;
            ClearArrowSelection(); ClearSelection(); arrow.Selected = true;
            RenderNow();
            TryCanvasPoint(e, out point);
            _draggedArrow = arrow; _arrowDragOrigin = point;
            _arrowDragOffsetX = arrow.OffsetX; _arrowDragOffsetY = arrow.OffsetY;
            _arrowClickHandled = true; _canvas.Capture = true; _canvas.Cursor = Cursors.SizeAll;
            _lastArrowDragRender = 0;
            SetStatus("拖动箭头到点列旁边，松开鼠标完成；方向保持起点 → 终点。");
        }

        private void CanvasArrowMouseMove(object sender, MouseEventArgs e)
        {
            PointF point;
            if (!TryCanvasPoint(e, out point)) return;
            if (_draggedArrow == null)
            {
                _canvas.Cursor = HitArrow(point) == null ? Cursors.Default : Cursors.SizeAll;
                return;
            }
            PositionArrow(_draggedArrow,
                _arrowDragOffsetX + (point.X - _arrowDragOrigin.X) / _lastTransform.Width,
                _arrowDragOffsetY + (point.Y - _arrowDragOrigin.Y) / _lastTransform.Height);
            long now = Environment.TickCount64;
            if (now - _lastArrowDragRender >= 30) { _lastArrowDragRender = now; RenderNow(); }
            else ScheduleRender();
        }

        private void CanvasArrowMouseUp(object sender, MouseEventArgs e)
        {
            if (_draggedArrow == null || e.Button != MouseButtons.Left) return;
            CanvasArrowMouseMove(sender, e);
            _draggedArrow = null;
            _canvas.Capture = false; _canvas.Cursor = Cursors.Default; _arrowClickHandled = true;
            RenderNow(); SetStatus("箭头位置已调整，图片导出使用当前位置。");
        }

        private void CanvasArrowCaptureChanged(object sender, EventArgs e)
        {
            if (_canvas.Capture || _draggedArrow == null) return;
            _draggedArrow = null; _canvas.Cursor = Cursors.Default; ScheduleRender();
        }

        private void PlaceArrowBesidePoints(CieArrow arrow)
        {
            double sx, sy, ex, ey;
            arrow.Start.TryGetXy(out sx, out sy); arrow.End.TryGetXy(out ex, out ey);
            PlotSpace.ToDiagram(_style.Kind, sx, sy, out sx, out sy);
            PlotSpace.ToDiagram(_style.Kind, ex, ey, out ex, out ey);
            double dx = ex - sx, dy = sy - ey, length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-10) return;
            AxisBounds bounds = _style.Bounds;
            double gap = Math.Min(bounds.Width, bounds.Height) * 0.07;
            arrow.OffsetX = dy / length * gap / bounds.Width;
            arrow.OffsetY = -dx / length * gap / bounds.Height;
            PositionArrow(arrow, arrow.OffsetX, arrow.OffsetY);
        }

        /// <summary>整条箭头平移，常规形状保持在绘图区内，防止拖到看不见的位置。</summary>
        private void PositionArrow(CieArrow arrow, double offsetX, double offsetY)
        {
            arrow.OffsetX = Math.Clamp(offsetX, -2, 2); arrow.OffsetY = Math.Clamp(offsetY, -2, 2);
            if (_lastTransform == null) return;
            ArrowGeometry geometry;
            PlotTransform tr = _lastTransform;
            if (!ArrowRenderer.TryGeometry(_style, tr, arrow, _curves, _points, 1, out geometry)) return;
            RectangleF bounds = geometry.Bounds();
            double pad = arrow.LineWidth / 2 + 5;
            double shiftX = ClampShift(bounds.Left, bounds.Right, tr.Left + pad, tr.Left + tr.Width - pad);
            double shiftY = ClampShift(bounds.Top, bounds.Bottom, tr.Top + pad, tr.Top + tr.Height - pad);
            arrow.OffsetX += shiftX / tr.Width; arrow.OffsetY += shiftY / tr.Height;
        }

        private static double ClampShift(double first, double last, double minimum, double maximum)
        {
            if (last - first > maximum - minimum)
            {
                double center = (first + last) / 2;
                return Math.Clamp(center, minimum, maximum) - center;
            }
            return Math.Clamp(0, minimum - first, maximum - last);
        }
    }
}
