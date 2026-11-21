using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using CIE1931xy.Core.Export;
using CIE1931xy.Core.Rendering;

namespace CIE1931xy.Core
{
    internal static class ArrowSelfTest
    {
        public static int Run(string outDir, TextWriter log)
        {
            int failures = 0;
            Action<string, bool> check = delegate (string name, bool ok)
            {
                log.WriteLine((ok ? "PASS  箭头：" : "FAIL  箭头：") + name);
                if (!ok) failures++;
            };
            CiePoint a = new CiePoint("起点", 0.20, 0.30), b = new CiePoint("终点", 0.45, 0.40);
            CieArrow arrow = new CieArrow { Start = new ArrowEndpoint(a), End = new ArrowEndpoint(b) };
            try
            {
                check("默认白色与黑色描边", arrow.Color == Color.White && arrow.ShowOutline && arrow.OutlineColor == Color.Black);
                CieArrow invalid = arrow.Clone(); invalid.End = invalid.Start;
                check("相同端点被拒绝", invalid.ValidationError() != null);
                invalid.End = new ArrowEndpoint(new CiePoint("重合", a.x, a.y));
                check("重合坐标被拒绝", invalid.ValidationError() != null);
                a.Name = "起点已改名";
                check("绑定数据对象保留改名", arrow.Start.Name == a.Name);
                PointF p0 = new PointF(30, 100), p1 = new PointF(250, 100);
                ArrowGeometry straight, zero, left, right;
                bool made = ArrowGeometry.TryCreate(arrow, p0, p1, 10, 1, out straight);
                check("直线朝向终点且避开点标记", made && straight.Tip.X > straight.Start.X &&
                    Math.Abs(straight.Start.X - 40) < 0.01 && Math.Abs(straight.Tip.X - 240) < 0.01);
                arrow.Kind = ArrowKind.Curved; arrow.Curvature = 0;
                check("零曲率等同直线", ArrowGeometry.TryCreate(arrow, p0, p1, 10, 1, out zero) &&
                    !zero.Curved && zero.Start == straight.Start && zero.Tip == straight.Tip);
                arrow.Curvature = 0.6; arrow.BendLeft = true;
                bool madeLeft = ArrowGeometry.TryCreate(arrow, p0, p1, 10, 1, out left);
                arrow.BendLeft = false;
                bool madeRight = ArrowGeometry.TryCreate(arrow, p0, p1, 10, 1, out right);
                check("左右弯形状与终点方向", madeLeft && madeRight && left.Control1.Y < p0.Y && right.Control1.Y > p0.Y &&
                    left.Tip.X > left.End.X && right.Tip.X > right.End.X);
                check("极近点安全省略", !ArrowGeometry.TryCreate(arrow, p0, new PointF(31, 100), 10, 1, out zero));
                CheckPlacement(arrow, a, b, check);
                CheckExports(outDir, arrow, a, b, check);
            }
            catch (Exception ex) { check("异常：" + ex.Message, false); }
            log.WriteLine(failures == 0 ? "ARROW SELFTEST OK" : "ARROW SELFTEST FAILED: " + failures);
            return failures;
        }

        private static void CheckPlacement(CieArrow arrow, CiePoint a, CiePoint b, Action<string, bool> check)
        {
            DiagramStyle style = new DiagramStyle { Bounds = PlotSpace.DefaultBounds(DiagramKind.Xy1931) };
            PlotTransform tr = new PlotTransform(style.Bounds, 10, 10, 600, 675);
            List<CiePoint> points = new List<CiePoint> { a, b };
            List<CieCurve> curves = new List<CieCurve>();
            arrow.Kind = ArrowKind.Straight; arrow.OffsetX = 0; arrow.OffsetY = -0.07;
            ArrowGeometry before, after;
            bool made = ArrowRenderer.TryGeometry(style, tr, arrow, curves, points, 1, out before);
            PointF midpoint = new PointF((tr.X(a.x) + tr.X(b.x)) / 2, (tr.Y(a.y) + tr.Y(b.y)) / 2);
            check("箭头位于点列旁边", made && before.DistanceTo(midpoint) > 10);
            double dx = tr.X(b.x) - tr.X(a.x), dy = tr.Y(b.y) - tr.Y(a.y);
            check("直线与参考起终点方向平行", made && Math.Abs((before.Tip.X - before.Start.X) * dy - (before.Tip.Y - before.Start.Y) * dx) < 0.1);
            arrow.OffsetX += 0.1; arrow.OffsetY += 0.12;
            bool moved = ArrowRenderer.TryGeometry(style, tr, arrow, curves, points, 1, out after);
            check("位置改变整条平移而不改方向", moved && Math.Abs(after.Start.X - before.Start.X - 60) < 0.01 &&
                Math.Abs(after.Tip.X - before.Tip.X - 60) < 0.01 && Math.Abs(after.Tip.Y - before.Tip.Y - 81) < 0.01);
            check("箭身与箭头内部可命中", moved && after.DistanceTo(after.AtShaft(0.5)) < 0.05 &&
                after.DistanceTo(new PointF(after.HeadX.Average(), after.HeadY.Average())) == 0);
            arrow.OffsetX = 0; arrow.OffsetY = -0.07;
            arrow.Selected = true;
            style.HighlightSelection = false;
            string selected = RenderSvg(style, curves, points, new List<CieArrow> { arrow });
            arrow.Selected = false;
            check("导出样式不包含箭头选中高亮", selected == RenderSvg(style, curves, points, new List<CieArrow> { arrow }));
        }

        private static void CheckExports(string outDir, CieArrow arrow, CiePoint a, CiePoint b, Action<string, bool> check)
        {
            List<CiePoint> points = new List<CiePoint> { a, b };
            List<CieCurve> curves = new List<CieCurve>();
            List<CieArrow> arrows = new List<CieArrow> { arrow };
            foreach (DiagramKind kind in new[] { DiagramKind.Xy1931, DiagramKind.Uv1976 })
            foreach (ArrowKind arrowKind in new[] { ArrowKind.Straight, ArrowKind.Curved })
            {
                DiagramStyle style = new DiagramStyle { Kind = kind, Bounds = PlotSpace.DefaultBounds(kind),
                    UseBitmapBackground = false, ShowBlackbody = false, ShowWavelengthLabels = false };
                arrow.Kind = arrowKind; arrow.Color = arrowKind == ArrowKind.Straight ? Color.White : Color.Magenta;
                arrow.Curvature = 0.6; arrow.ShowOutline = true;
                string tag = (kind == DiagramKind.Xy1931 ? "xy" : "uv") + "-" + arrowKind.ToString().ToLowerInvariant();
                string svg = Path.Combine(outDir, "selftest-arrow-" + tag + ".svg");
                string png = Path.Combine(outDir, "selftest-arrow-" + tag + ".png");
                string tif = Path.Combine(outDir, "selftest-arrow-" + tag + ".tif");
                Exporter.SaveSvg(style, curves, points, svg, 900, 900, arrows);
                Exporter.SaveBitmap(style, curves, points, png, ImageFormat.Png, 900, 900, 300, arrows);
                Exporter.SaveBitmap(style, curves, points, tif, ImageFormat.Tiff, 900, 900, 300, arrows);
                XDocument doc = XDocument.Load(svg);
                XNamespace ns = "http://www.w3.org/2000/svg";
                XElement group = doc.Descendants(ns + "g").FirstOrDefault(e => ((string)e.Attribute("clip-path") ?? "").Contains("annotation-clip-"));
                check(tag + " SVG 箭头/颜色/裁剪与纯矢量", group != null && group.Elements(ns + "polygon").Count() == 2 &&
                    group.Descendants().Any(e => (string)e.Attribute("fill") == (arrowKind == ArrowKind.Straight ? "#FFFFFF" : "#FF00FF")) &&
                    !doc.Descendants(ns + "image").Any() &&
                    (arrowKind == ArrowKind.Straight ? group.Elements(ns + "line").Count() == 2 : group.Elements(ns + "path").Count() == 2));
                using (Bitmap savedPng = new Bitmap(png))
                using (Bitmap savedTif = new Bitmap(tif))
                {
                    check(tag + " PNG/TIFF 格式、尺寸与 DPI", savedPng.Width == 900 && savedPng.Height == 900 &&
                        savedTif.Width == 900 && savedTif.Height == 900 && savedTif.RawFormat.Guid == ImageFormat.Tiff.Guid &&
                        Math.Abs(savedTif.HorizontalResolution - 300) < 1 && Math.Abs(savedPng.HorizontalResolution - 300) < 1);
                }
                PlotTransform tr;
                using (Bitmap before = Exporter.RenderToBitmap(style, curves, points, 900, 900, 96, out tr))
                using (Bitmap after = Exporter.RenderToBitmap(style, curves, points, 900, 900, 96, arrows))
                {
                    check(tag + " 位图实际绘制且不越出绘图区", DifferenceOnlyInPlot(before, after, tr));
                }
                string baseline = RenderSvg(style, curves, points, null);
                CieCurve curve = new CieCurve { Name = "光谱端点", Result = new CurveResult { x = a.x, y = a.y }, Visible = false };
                curves.Add(curve); arrow.Start = new ArrowEndpoint(curve);
                check(tag + " 隐藏端点不留绘制痕迹", RenderSvg(style, curves, points, arrows) == baseline);
                curve.Visible = true;
                string visibleBaseline = RenderSvg(style, curves, points, null);
                check(tag + " 恢复端点显示箭头", RenderSvg(style, curves, points, arrows) != visibleBaseline);
                curves.Clear();
                check(tag + " 删除端点不绘制孤立箭头", RenderSvg(style, curves, points, arrows) == baseline);
                arrow.Start = new ArrowEndpoint(a);
                arrow.ShowOutline = false;
                XDocument noOutline = XDocument.Parse(RenderSvg(style, curves, points, arrows));
                XElement plainGroup = noOutline.Descendants(ns + "g").First(e => ((string)e.Attribute("clip-path") ?? "").Contains("annotation-clip-"));
                check(tag + " 描边可关闭", plainGroup.Elements().Count() == 2);
            }
        }

        private static string RenderSvg(DiagramStyle style, IList<CieCurve> curves, IList<CiePoint> points, IList<CieArrow> arrows)
        {
            SvgCanvas canvas = new SvgCanvas(900, 900, 1);
            new DiagramRenderer(style, curves, points, arrows).Render(canvas, 900, 900);
            return canvas.ToSvg();
        }

        private static bool DifferenceOnlyInPlot(Bitmap before, Bitmap after, PlotTransform tr)
        {
            byte[] original = Pixels(before), drawn = Pixels(after);
            int changed = 0;
            for (int y = 0; y < before.Height; y++)
            for (int x = 0; x < before.Width; x++)
            {
                int offset = (y * before.Width + x) * 4;
                if (original[offset] == drawn[offset] && original[offset + 1] == drawn[offset + 1] && original[offset + 2] == drawn[offset + 2]) continue;
                changed++;
                if (x < tr.Left - 1 || x > tr.Left + tr.Width + 1 || y < tr.Top - 1 || y > tr.Top + tr.Height + 1) return false;
            }
            return changed > 50;
        }

        private static byte[] Pixels(Bitmap bitmap)
        {
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4];
                for (int row = 0; row < bitmap.Height; row++) Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), pixels, row * bitmap.Width * 4, bitmap.Width * 4);
                return pixels;
            }
            finally { bitmap.UnlockBits(data); }
        }
    }
}
