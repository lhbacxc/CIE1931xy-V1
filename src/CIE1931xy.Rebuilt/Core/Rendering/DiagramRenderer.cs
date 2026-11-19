using System;
using System.Collections.Generic;
using System.Drawing;

namespace CIE1931xy.Core.Rendering
{
    /// <summary>渲染样式（对应界面右侧「图层/样式」面板）。</summary>
    internal sealed class DiagramStyle
    {
        public DiagramKind Kind = DiagramKind.Xy1931;
        public AxisBounds Bounds = PlotSpace.DefaultBounds(DiagramKind.Xy1931);
        public bool BoundsFixed = true;

        public bool UseBitmapBackground = true;       // true = 原版位图底图，false = 矢量网格
        public bool BitmapWithBlackbody = true;       // 位图底图是否带烘焙黑体轨迹

        public bool ShowGrid = true;
        public bool ShowSpectralLocus = true;
        public bool ShowWavelengthLabels = false;
        public bool ShowBlackbody = true;
        public bool ShowBlackbodyTicks = true;
        public bool ShowSrgbTriangle = false;
        public bool ShowLegend = false;
        public bool LegendBottomRight = true;
        public bool ShowTitle = false;                // 标题默认不画（用户要求位图/矢量都不需要标题）
        public bool ShowPointLabels = false;
        public bool ShowCctInLabels = false;
        public bool ShowAxisLabels = true;
        public bool ShowClosingLine = true;           // y = -x + 1（紫边线，位图上有）
        public double FontScale = 1.0;                // 字号基准（导出大图时由 Exporter 放大，预览保持 1.0）
        public bool HighlightSelection = true;        // 选中高亮（导出时置 false）
        public bool BlackBackground = false;

        public string XyAxisTitleX = "";
        public string XyAxisTitleY = "";
        public string UvAxisTitleX = "";
        public string UvAxisTitleY = "";
        public double AxisTitleGapX = 16.0;
        public double AxisTitleGapY = 16.0;

        public string AxisTitleX
        {
            get
            {
                string title = Kind == DiagramKind.Xy1931 ? XyAxisTitleX : UvAxisTitleX;
                return string.IsNullOrWhiteSpace(title) ? PlotSpace.AxisLabelX(Kind) : title;
            }
        }

        public string AxisTitleY
        {
            get
            {
                string title = Kind == DiagramKind.Xy1931 ? XyAxisTitleY : UvAxisTitleY;
                return string.IsNullOrWhiteSpace(title) ? PlotSpace.AxisLabelY(Kind) : title;
            }
        }

        public string FontFamily = "Microsoft YaHei UI";
        public double TitleFontSize = 30.0;
        public double AxisFontSize = 26.0;
        public double TickFontSize = 22.0;
        public double WavelengthFontSize = 20.0;
        public double LabelFontSize = 20.0;
        public double LegendFontSize = 20.0;
        public double CctFontSize = 17.0;

        public double MarkerSize = 5.2;               // 半径（px, 96dpi）
        public double CurveWidth = 2.6;
        public double LocusWidth = 2.4;
        public double GridWidth = 0.9;

        public Color Background = Color.White;
        public Color PlotBackground = Color.White;
        public Color GridColor = Color.FromArgb(0xC9, 0xCF, 0xD6);
        public Color GridMajorColor = Color.FromArgb(0x9E, 0xA6, 0xAF);
        public Color ClosingLineColor = Color.FromArgb(0xB4, 0xB9, 0xC0);
        public Color SelectionColor = Color.FromArgb(0x2F, 0x6F, 0xED);
        public Color AxisColor = Color.FromArgb(0x35, 0x3A, 0x40);
        public Color TextColor = Color.FromArgb(0x22, 0x27, 0x2D);
        public Color LocusColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
        public Color BlackbodyColor = Color.FromArgb(0x00, 0x00, 0x00);
        public Color SrgbColor = Color.FromArgb(0x8C, 0x94, 0x9E);
        public Color LegendBorder = Color.FromArgb(0xCC, 0xD2, 0xD9);

        public void SetBlackBackground(bool black)
        {
            BlackBackground = black;
            Background = black ? Color.Black : Color.White;
            PlotBackground = Background;
            TextColor = black ? Color.White : Color.FromArgb(0x22, 0x22, 0x22);
            AxisColor = black ? Color.White : Color.FromArgb(0x35, 0x3A, 0x40);
            LocusColor = black ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);
            GridColor = black ? Color.FromArgb(0x3F, 0x3F, 0x46) : Color.FromArgb(0xC9, 0xCF, 0xD6);
            GridMajorColor = black ? Color.FromArgb(0x62, 0x62, 0x6B) : Color.FromArgb(0x9E, 0xA6, 0xAF);
            ClosingLineColor = black ? Color.FromArgb(0x8D, 0x8D, 0x99) : Color.FromArgb(0xB4, 0xB9, 0xC0);
            LegendBorder = black ? Color.FromArgb(0x77, 0x77, 0x77) : Color.FromArgb(0xCC, 0xCC, 0xCC);
        }

        public DiagramStyle Clone()
        {
            return (DiagramStyle)MemberwiseClone();
        }
    }

    /// <summary>
    /// 色度图渲染器：位图底图 / 矢量背景、网格、光谱轨迹、黑体轨迹、sRGB 三角形、数据点与图例。
    /// 全部通过 <see cref="IPlotCanvas"/> 输出，因此屏幕、PNG/TIFF、SVG 共用同一份绘制代码。
    /// </summary>
    internal sealed class DiagramRenderer
    {
        internal PlotTransform LastTransform { get; private set; }

        private readonly DiagramStyle _style;
        private readonly IList<CieCurve> _curves;
        private readonly IList<CiePoint> _points;

        public DiagramRenderer(DiagramStyle style, IList<CieCurve> curves, IList<CiePoint> points)
        {
            _style = style;
            _curves = curves;
            _points = points;
        }

        public void Render(IPlotCanvas canvas, int width, int height)
        {
            DiagramStyle s = _style;
            double scale = canvas.PixelScale;
            bool bitmapMode = s.UseBitmapBackground && s.Kind == DiagramKind.Xy1931;
            _fs = Math.Max(0.85, s.FontScale);

            canvas.FillRect(s.Background, 0, 0, width, height);

            // --- 布局：位图模式贴边（底图自带白边，留太多会显得四周空白很大）；
            //     矢量模式给坐标轴标签留出空间。标题已按用户要求取消，顶部不再预留。
            double marginL, marginR, marginT, marginB;
            if (bitmapMode)
            {
                double pad = 12 * scale;
                marginL = pad; marginR = pad; marginT = pad; marginB = pad;
            }
            else
            {
                MeasureAxisLayout(canvas, width, height);
                double pad = 12 * scale;
                marginL = Math.Max(width * 0.075, _yTickWidth + 10 * scale + pad
                    + (s.ShowAxisLabels ? _axisGapY + _yTitleSize.Height : 0));
                marginR = Math.Max(width * 0.025, _xTickWidth / 2 + pad);
                marginT = Math.Max(height * 0.040, _yTickHeight / 2 + pad);
                marginB = Math.Max(height * 0.075, 11 * scale + _xTickHeight + pad
                    + (s.ShowAxisLabels ? _axisGapX + _xTitleSize.Height : 0));
            }
            double availW = Math.Max(10, width - marginL - marginR);
            double availH = Math.Max(10, height - marginT - marginB);

            // 字号随画布宽度缩放：预览窗口小时字不至于太大，导出大图时字不会显得很细很小。

            AxisBounds b = s.Bounds;
            PlotTransform tr;
            RectangleD artworkRect = new RectangleD();

            if (bitmapMode)
            {
                // 底图 684×732，其中坐标轴矩形为 (59,12)-(659,687)；
                // 先按底图整体宽高比摆放图片，再由轴矩形反推数据到像素的映射，
                // 这样底图自带网格/刻度/轨迹与数据点严格对齐，也不必再叠一层坐标轴。
                const double artW = 684.0, artH = 732.0;
                const double axLeft = 59.0, axTop = 12.0, axWidth = 600.0, axHeight = 675.0;
                double fit = Math.Min(availW / artW, availH / artH);
                double imgW = artW * fit, imgH = artH * fit;
                double imgL = marginL + (availW - imgW) / 2.0;
                double imgT = marginT + (availH - imgH) / 2.0;
                artworkRect = new RectangleD(imgL, imgT, imgW, imgH);
                // 底图里的坐标轴是固定 0~0.8 / 0~0.9 烘焙好的：映射必须用底图自己的轴范围，
                // 不能用用户的数据包络或手动范围，否则数据点会整体错位（历史 bug：点跑到图外）。
                tr = new PlotTransform(new AxisBounds(0, 0.8, 0, 0.9),
                                       (float)(imgL + axLeft * fit), (float)(imgT + axTop * fit),
                                       (float)(axWidth * fit), (float)(axHeight * fit));
            }
            else
            {
                double dataAspect = b.Width / b.Height;
                double plotW = availW;
                double plotH = plotW / dataAspect;
                if (plotH > availH)
                {
                    plotH = availH;
                    plotW = plotH * dataAspect;
                }
                double plotL = marginL + (availW - plotW) / 2.0;
                double plotT = marginT + (availH - plotH) / 2.0;
                tr = new PlotTransform(b, (float)plotL, (float)plotT, (float)plotW, (float)plotH);
            }
            LastTransform = tr;


            // --- 绘图区背景
            if (bitmapMode)
            {
                DrawBitmapArtwork(canvas, artworkRect, s.ShowBlackbody);
            }
            else
            {
                // 网格先画：色度图（轨迹内）会盖住网格，轨迹外仍能看到网格，
                // 这样网格线不会压在彩色色域上面。
                // 闭合线（x+y=1）画在最底层：它压在网格与色域下面，不会被误当成曲线
                if (s.ShowClosingLine && s.Kind == DiagramKind.Xy1931)
                {
                    AxisBounds cb = tr.Bounds;
                    double cx0 = cb.XMin, cx1 = cb.XMax;
                    DrawClippedPolyline(canvas, s.ClosingLineColor, 1.4f,
                        new[] { tr.X(cx0), tr.X(cx1) }, new[] { tr.Y(1.0 - cx0), tr.Y(1.0 - cx1) }, tr);
                }
                if (s.ShowGrid) DrawGrid(canvas, tr);
                DrawVectorBackground(canvas, tr);
                DrawFrame(canvas, tr);
                DrawAxisTicks(canvas, tr);
                if (s.ShowAxisLabels) DrawAxisTitles(canvas, tr);
            }

            // 光谱轨迹（位图底图已烘焙了轨迹与波长数字，跳过避免重复）
            if (s.ShowSpectralLocus && !bitmapMode) DrawSpectralLocus(canvas, tr);
            if (s.ShowWavelengthLabels && !bitmapMode) DrawWavelengthLabels(canvas, tr);
            if (s.ShowSrgbTriangle) DrawSrgbTriangle(canvas, tr);

            // 黑体轨迹：位图底图已烘焙时若要显示黑体线，则改用干净底图并在其上绘制
            if (s.ShowBlackbody && !bitmapMode)
            {
                DrawBlackbody(canvas, tr);
            }

            // 数据
            DrawCurves(canvas, tr);
            DrawPoints(canvas, tr);

            if (s.ShowLegend) DrawLegend(canvas, tr);
            if (s.ShowTitle) DrawTitle(canvas, width, tr.Top - 10 * scale);
        }


        // ---------------------------------------------------------------- 背景

        /// <summary>
        /// 位图底图：把原版 684×732 的图片整体缩放到指定矩形；
        /// 数据坐标到像素的映射已在布局阶段按底图的轴矩形 (59,12)-(659,687) 推导，因此两者严格对齐。
        /// </summary>
        private void DrawBitmapArtwork(IPlotCanvas canvas, RectangleD rect, bool withBlackbody)
        {
            Bitmap art = AppResources.GetDiagramBitmap(withBlackbody);
            canvas.DrawImage(art, rect.Left, rect.Top, rect.Width, rect.Height);
        }

        private void DrawVectorBackground(IPlotCanvas canvas, PlotTransform tr)
        {
            int w = Math.Max(2, (int)Math.Round(tr.Width));
            int h = Math.Max(2, (int)Math.Round(tr.Height));
            SvgCanvas svg = canvas as SvgCanvas;
            if (svg != null)
            {
                // 横向由 SVG 渐变连续插值，细扫描带避免二维色块拼接的网格感。
                const int cols = 257;
                int rows = Math.Min(2048, Math.Max(512, h * 2));
                Color[] cells = new Color[cols * rows];
                for (int r = 0; r < rows; r++)
                {
                    double dy = tr.Bounds.YMax - (r + 0.5) / rows * tr.Bounds.Height;
                    for (int c = 0; c < cols; c++)
                    {
                        double dx = tr.Bounds.XMin + c / (double)(cols - 1) * tr.Bounds.Width;
                        cells[r * cols + c] = ChromaBackground.SampleColor(_style.Kind, dx, dy);
                    }
                }
                float[] lx, ly;
                PlotSpace.SpectralLocus(_style.Kind, out lx, out ly, true);
                float[] clipX = new float[lx.Length];
                float[] clipY = new float[ly.Length];
                for (int i = 0; i < lx.Length; i++) { clipX[i] = tr.X(lx[i]); clipY[i] = tr.Y(ly[i]); }
                svg.FillChromaGradients(cells, cols, rows, tr.Left, tr.Top, tr.Width, tr.Height, clipX, clipY);
                return;
            }
            canvas.DrawImage(ChromaBackground.Get(_style.Kind, tr.Bounds, w, h), tr.Left, tr.Top, tr.Width, tr.Height);
        }

        // ---------------------------------------------------------------- 网格/坐标

        private void DrawGrid(IPlotCanvas canvas, PlotTransform tr)
        {
            DiagramStyle s = _style;
            AxisBounds b = tr.Bounds;
            // 与位图底图一致：0.05 一条细线、0.10 一条略深的线
            for (double v = b.XMin; v <= b.XMax + 1e-9; v += 0.05)
            {
                float px = tr.X(v);
                bool major = Math.Abs(v * 10 - Math.Round(v * 10)) < 1e-6;
                canvas.DrawLine(major ? s.GridMajorColor : s.GridColor, (major ? s.GridWidth + 0.5 : s.GridWidth) * 1.1,
                    px, tr.Top, px, tr.Top + tr.Height);
            }
            for (double v = b.YMin; v <= b.YMax + 1e-9; v += 0.05)
            {
                float py = tr.Y(v);
                bool major = Math.Abs(v * 10 - Math.Round(v * 10)) < 1e-6;
                canvas.DrawLine(major ? s.GridMajorColor : s.GridColor, (major ? s.GridWidth + 0.5 : s.GridWidth) * 1.1,
                    tr.Left, py, tr.Left + tr.Width, py);
            }
        }

        private static double TickStep(double range)
        {
            return range > 1.2 ? 0.2 : 0.1;
        }

        private void DrawFrame(IPlotCanvas canvas, PlotTransform tr)
        {
            canvas.DrawRect(_style.AxisColor, 2.6 * canvas.PixelScale, tr.Left, tr.Top, tr.Width, tr.Height);
        }

        private double _fs = 1.0;   // 按画布宽度缩放的字号系数
        private double _xTickWidth, _xTickHeight, _yTickWidth, _yTickHeight;
        private double _axisGapX, _axisGapY, _xTitleFont, _yTitleFont;
        private SizeD _xTitleSize, _yTitleSize;

        private void MeasureAxisLayout(IPlotCanvas canvas, int width, int height)
        {
            DiagramStyle s = _style;
            _xTickWidth = _xTickHeight = _yTickWidth = _yTickHeight = 0;
            for (double v = s.Bounds.XMin; v <= s.Bounds.XMax + 1e-9; v += 0.1)
            {
                SizeD size = canvas.MeasureText(v.ToString("0.0"), s.FontFamily, s.TickFontSize * _fs, false);
                _xTickWidth = Math.Max(_xTickWidth, size.Width);
                _xTickHeight = Math.Max(_xTickHeight, size.Height);
            }
            for (double v = s.Bounds.YMin; v <= s.Bounds.YMax + 1e-9; v += 0.1)
            {
                SizeD size = canvas.MeasureText(v.ToString("0.0"), s.FontFamily, s.TickFontSize * _fs, false);
                _yTickWidth = Math.Max(_yTickWidth, size.Width);
                _yTickHeight = Math.Max(_yTickHeight, size.Height);
            }
            _axisGapX = Math.Max(0, s.AxisTitleGapX) * _fs * canvas.PixelScale;
            _axisGapY = Math.Max(0, s.AxisTitleGapY) * _fs * canvas.PixelScale;
            _xTitleFont = FitAxisTitleFont(canvas, s.AxisTitleX, s.AxisFontSize * _fs, Math.Max(1, width - 24));
            _yTitleFont = FitAxisTitleFont(canvas, s.AxisTitleY, s.AxisFontSize * _fs, Math.Max(1, height * 0.75));
            _xTitleSize = canvas.MeasureText(s.AxisTitleX, s.FontFamily, _xTitleFont, false);
            _yTitleSize = canvas.MeasureText(s.AxisTitleY, s.FontFamily, _yTitleFont, false);
        }

        private double FitAxisTitleFont(IPlotCanvas canvas, string title, double fontSize, double maxWidth)
        {
            SizeD size = canvas.MeasureText(title, _style.FontFamily, fontSize, false);
            return size.Width > maxWidth ? fontSize * maxWidth / size.Width : fontSize;
        }

        private void DrawAxisTicks(IPlotCanvas canvas, PlotTransform tr)
        {
            DiagramStyle s = _style;
            AxisBounds b = tr.Bounds;
            double xstep = 0.1;
            double ystep = 0.1;
            double len = 5.0 * canvas.PixelScale;

            for (double v = b.XMin; v <= b.XMax + 1e-9; v += xstep)
            {
                float px = tr.X(v);
                canvas.DrawLine(s.AxisColor, 0.9, px, tr.Top + tr.Height, px, tr.Top + tr.Height + len);
                canvas.DrawText(v.ToString("0.0"), s.TextColor, s.FontFamily, s.TickFontSize * _fs, false,
                    px, tr.Top + tr.Height + len + 6 * canvas.PixelScale, TextAlignH.Center, TextAlignV.Top, 0);
            }
            for (double v = b.YMin; v <= b.YMax + 1e-9; v += ystep)
            {
                float py = tr.Y(v);
                canvas.DrawLine(s.AxisColor, 0.9, tr.Left - len, py, tr.Left, py);
                canvas.DrawText(v.ToString("0.0"), s.TextColor, s.FontFamily, s.TickFontSize * _fs, false,
                    tr.Left - len - 5 * canvas.PixelScale, py, TextAlignH.Right, TextAlignV.Middle, 0);
            }
        }

        private void DrawAxisTitles(IPlotCanvas canvas, PlotTransform tr)
        {
            DiagramStyle s = _style;
            double centerX = tr.Left + tr.Width / 2;
            double maxXWidth = 2 * Math.Max(1, Math.Min(centerX - 12 * canvas.PixelScale,
                tr.Left + tr.Width + 12 * canvas.PixelScale - centerX));
            double fontX = FitAxisTitleFont(canvas, s.AxisTitleX, _xTitleFont, maxXWidth);
            canvas.DrawText(s.AxisTitleX, s.TextColor, s.FontFamily, fontX, false,
                centerX, tr.Top + tr.Height + 11 * canvas.PixelScale + _xTickHeight + _axisGapX,
                TextAlignH.Center, TextAlignV.Top, 0);
            double fontY = FitAxisTitleFont(canvas, s.AxisTitleY, _yTitleFont, Math.Max(1, tr.Height - 24 * canvas.PixelScale));
            SizeD ySize = canvas.MeasureText(s.AxisTitleY, s.FontFamily, fontY, false);
            canvas.DrawText(s.AxisTitleY, s.TextColor, s.FontFamily, fontY, false,
                tr.Left - 10 * canvas.PixelScale - _yTickWidth - _axisGapY - ySize.Height / 2,
                tr.Top + tr.Height / 2, TextAlignH.Center, TextAlignV.Middle, -90);
        }

        // ---------------------------------------------------------------- 轨迹

        private void DrawSpectralLocus(IPlotCanvas canvas, PlotTransform tr)
        {
            float[] dx, dy;
            PlotSpace.SpectralLocus(_style.Kind, out dx, out dy, false);
            float[] xs = new float[dx.Length];
            float[] ys = new float[dy.Length];
            for (int i = 0; i < dx.Length; i++)
            {
                xs[i] = tr.X(dx[i]);
                ys[i] = tr.Y(dy[i]);
            }
            // 380nm 与 700nm 的连线（"紫边线"）也算光谱轨迹的一部分
            DrawClippedPolyline(canvas, _style.LocusColor, (float)_style.LocusWidth,
                new[] { xs[xs.Length - 1], xs[0] }, new[] { ys[ys.Length - 1], ys[0] }, tr);
            DrawClippedPolyline(canvas, _style.LocusColor, (float)_style.LocusWidth, xs, ys, tr);

        }

        /// <summary>
        /// 逐段做 Liang–Barsky 裁剪后画折线。手动轴范围（非完整视野）时，
        /// 光谱轨迹/黑体轨迹会延伸到画面外，不裁剪就会在图上画出长长的斜线。
        /// </summary>
        private static void DrawClippedPolyline(IPlotCanvas canvas, Color color, float width,
                                                float[] xs, float[] ys, PlotTransform tr)
        {
            double left = tr.Left, top = tr.Top, right = tr.Left + tr.Width, bottom = tr.Top + tr.Height;
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                double x0 = xs[i], y0 = ys[i], x1 = xs[i + 1], y1 = ys[i + 1];
                if (ClipSegment(ref x0, ref y0, ref x1, ref y1, left, top, right, bottom))
                    canvas.DrawLine(color, width, (float)x0, (float)y0, (float)x1, (float)y1);
            }
        }

        private static bool ClipSegment(ref double x0, ref double y0, ref double x1, ref double y1,
                                        double left, double top, double right, double bottom)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double t0 = 0, t1 = 1;
            double[] p = { -dx, dx, -dy, dy };
            double[] q = { x0 - left, right - x0, y0 - top, bottom - y0 };
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-12)
                {
                    if (q[i] < 0) return false;
                    continue;
                }
                double r = q[i] / p[i];
                if (p[i] < 0)
                {
                    if (r > t1) return false;
                    if (r > t0) t0 = r;
                }
                else
                {
                    if (r < t0) return false;
                    if (r < t1) t1 = r;
                }
            }
            double nx0 = x0 + t0 * dx, ny0 = y0 + t0 * dy;
            double nx1 = x0 + t1 * dx, ny1 = y0 + t1 * dy;
            x0 = nx0; y0 = ny0; x1 = nx1; y1 = ny1;
            return true;
        }

        /// <summary>
        /// 光谱轨迹的波长数字：沿"远离光谱重心"的方向外移；越界则改为向内，
        /// 并且与已放置标签做矩形碰撞检测，避免 380/660/700 这类端点挤在一起。
        /// </summary>
        private void DrawWavelengthLabels(IPlotCanvas canvas, PlotTransform tr)
        {
            DiagramStyle s = _style;
            // 先放两个端点（380/700）再放其余，避免红端点被 640/660 的框挤掉；
            // 640/660/680 在红端过密，直接不标（原版底图也是这样处理的）。
            double[] wl = new double[]
            {
                380, 700, 400, 420, 440, 460, 480, 500, 520, 540, 560, 580, 600, 620
            };

            float[] dx, dy;
            PlotSpace.SpectralLocus(s.Kind, out dx, out dy, true);
            double cx = 0, cy = 0;
            for (int i = 0; i < dx.Length; i++) { cx += dx[i]; cy += dy[i]; }
            if (dx.Length > 0) { cx /= dx.Length; cy /= dy.Length; }

            List<RectangleD> placed = new List<RectangleD>();
            AxisBounds bounds = tr.Bounds;
            double off = Math.Max(15 * canvas.PixelScale, s.WavelengthFontSize * _fs * 0.85);
            double pad = 2 * canvas.PixelScale;

            for (int i = 0; i < wl.Length; i++)
            {
                double x, y;
                if (!Colorimetry.LocusPointAt(wl[i], out x, out y)) continue;
                double ddx, ddy;
                PlotSpace.ToDiagram(s.Kind, x, y, out ddx, out ddy);
                // 手动缩小视野时，视野外的波长不去标（否则会飘到画布边缘）
                if (bounds.Width > 0 && bounds.Height > 0 &&
                    (ddx < bounds.XMin - bounds.Width * 0.15 || ddx > bounds.XMax + bounds.Width * 0.15 ||
                     ddy < bounds.YMin - bounds.Height * 0.15 || ddy > bounds.YMax + bounds.Height * 0.15))
                    continue;
                float px = tr.X(ddx), py = tr.Y(ddy);

                double nx = ddx - cx, ny = ddy - cy;
                double norm = Math.Sqrt(nx * nx + ny * ny);
                if (norm < 1e-9) { nx = 0; ny = 1; norm = 1; }
                nx /= norm; ny /= norm;

                string text = wl[i].ToString("0");
                SizeD size = canvas.MeasureText(text, s.FontFamily, s.WavelengthFontSize * _fs, false);
                double w = size.Width, h = size.Height;

                RectangleD rect;
                if (!TryLabelRect(tr, px, py, nx, ny, off, w, h, pad, out rect))
                {
                    // 外侧放不下（例如底边 380nm），翻到内侧
                    if (!TryLabelRect(tr, px, py, -nx, -ny, off + 4 * canvas.PixelScale, w, h, pad, out rect))
                        continue;
                }

                bool overlaps = false;
                for (int k = 0; k < placed.Count; k++)
                {
                    RectangleD other = placed[k];
                    if (rect.Left < other.Right && rect.Right > other.Left &&
                        rect.Top < other.Bottom && rect.Bottom > other.Top)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps) continue;

                placed.Add(rect);
                // 位图上每个波长都有刻度线：在轨迹点处沿外法线方向画一小段刻度
                double tick = s.WavelengthFontSize * _fs * 0.55;
                float t0x = (float)(px + nx * 2), t0y = (float)(py - ny * 2);
                float t1x = (float)(px + nx * tick), t1y = (float)(py - ny * tick);
                canvas.DrawLine(s.LocusColor, 1.6f, t0x, t0y, t1x, t1y);
                canvas.DrawText(text, LabelColor(tr, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), s.FontFamily, s.WavelengthFontSize * _fs, false,
                    rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0,
                    TextAlignH.Center, TextAlignV.Middle, 0);
            }
        }

        /// <summary>尝试把标签放在 (px,py) 沿 (nx,ny) 方向 off 像素处，并检查是否落在绘图区内。</summary>
        private static bool TryLabelRect(PlotTransform tr, float px, float py, double nx, double ny,
                                         double off, double w, double h, double pad, out RectangleD rect)
        {
            double centerX = px + nx * off;
            double centerY = py - ny * off;
            rect = new RectangleD(centerX - w / 2.0 - pad, centerY - h / 2.0 - pad, w + pad * 2, h + pad * 2);
            return rect.Left >= tr.Left && rect.Right <= tr.Left + tr.Width &&
                   rect.Top >= tr.Top && rect.Bottom <= tr.Top + tr.Height;
        }

        private void DrawSrgbTriangle(IPlotCanvas canvas, PlotTransform tr)
        {
            float[] dx, dy;
            PlotSpace.SrgbTriangle(_style.Kind, out dx, out dy);
            float[] xs = new float[4];
            float[] ys = new float[4];
            for (int i = 0; i < 3; i++)
            {
                xs[i] = tr.X(dx[i]);
                ys[i] = tr.Y(dy[i]);
            }
            xs[3] = xs[0]; ys[3] = ys[0];
            canvas.DrawPolyline(_style.SrgbColor, 1.0, xs, ys);
        }

        private void DrawBlackbody(IPlotCanvas canvas, PlotTransform tr)
        {
            DiagramStyle s = _style;
            double[,] table = Colorimetry.Cmf;

            // 曲线（对数采样，靠近高温端更密）
            double[] temps = PlanckianLocus.Temperatures(1800, 25000, 160);
            float[] xs = new float[temps.Length];
            float[] ys = new float[temps.Length];
            for (int i = 0; i < temps.Length; i++)
            {
                double x, y;
                var c = PlanckianLocus.Chromaticity(temps[i], table);
                x = c.x; y = c.y;
                double dx, dy;
                PlotSpace.ToDiagram(s.Kind, x, y, out dx, out dy);
                xs[i] = tr.X(dx);
                ys[i] = tr.Y(dy);
            }
            DrawClippedPolyline(canvas, s.BlackbodyColor, 2.4f, xs, ys, tr);
            if (!s.ShowBlackbodyTicks) return;

            // 轨迹低色温端（∞）的标记，和位图一致
            {
                var cInf = PlanckianLocus.Chromaticity(1800, table);
                double idxx, idyy;
                PlotSpace.ToDiagram(s.Kind, cInf.x, cInf.y, out idxx, out idyy);
                canvas.DrawText("∞", s.BlackbodyColor, s.FontFamily, s.CctFontSize * _fs, false,
                    tr.X(idxx) + 10 * canvas.PixelScale, tr.Y(idyy) + 6 * canvas.PixelScale,
                    TextAlignH.Left, TextAlignV.Middle, 0);
            }

            double[] labels = new double[] { 2000, 2500, 3000, 4000, 5000, 6000, 8000, 10000 };
            List<RectangleD> placed = new List<RectangleD>();
            for (int i = 0; i < labels.Length; i++)
            {
                double x, y;
                var c = PlanckianLocus.Chromaticity(labels[i], table);
                x = c.x; y = c.y;
                double dx, dy;
                PlotSpace.ToDiagram(s.Kind, x, y, out dx, out dy);

                // 等温线方向：黑体轨迹法线方向（用相邻温度点近似切线）
                double x2, y2;
                var c2 = PlanckianLocus.Chromaticity(labels[i] * 1.03, table);
                x2 = c2.x; y2 = c2.y;
                double dx2, dy2;
                PlotSpace.ToDiagram(s.Kind, x2, y2, out dx2, out dy2);
                double tx = dx2 - dx, ty = dy2 - dy;
                double tn = Math.Sqrt(tx * tx + ty * ty);
                if (tn < 1e-9) continue;
                double nx = -ty / tn, ny = tx / tn;

                // 位图上的等温线是"穿过轨迹的长刻度"，标签放在刻度末端外侧
                double half = 0.030;
                float ax = tr.X(dx - nx * half), ay = tr.Y(dy - ny * half);
                float bx = tr.X(dx + nx * half), by = tr.Y(dy + ny * half);
                canvas.DrawLine(s.BlackbodyColor, 1.8, ax, ay, bx, by);

                string text = labels[i].ToString("0");
                double fsz = s.CctFontSize * _fs;
                SizeD tsz = canvas.MeasureText(text, s.FontFamily, fsz, false);
                // 标签放在刻度末端外侧（朝 +n 方向再让半个字高），奇偶交替上下，避免互相压住
                double unitsPerPx = Math.Max(1e-9, Math.Abs(tr.Y(1) - tr.Y(0)));
                double off = 0.010 + (tsz.Height * 0.5 + 4 * canvas.PixelScale) / unitsPerPx;
                double side = (i % 2 == 0) ? 1.0 : -1.0;
                double tx0 = tr.X(dx + nx * (half + off) * side);
                double ty0 = tr.Y(dy + ny * (half + off) * side);
                RectangleD box = new RectangleD(tx0 - tsz.Width / 2, ty0 - tsz.Height / 2, tsz.Width, tsz.Height);
                bool hit = false;
                for (int k = 0; k < placed.Count; k++)
                {
                    if (Overlaps(box, placed[k])) { hit = true; break; }
                }
                if (hit) continue;                     // 视野变小时温度数字会挤在一起，重叠就跳过
                placed.Add(box);
                canvas.DrawText(text, s.BlackbodyColor, s.FontFamily, fsz, false,
                    tx0, ty0, TextAlignH.Center, TextAlignV.Middle, 0);
            }
        }

        // ---------------------------------------------------------------- 数据

        private void DrawCurves(IPlotCanvas canvas, PlotTransform tr)
        {
            DiagramStyle s = _style;
            for (int i = 0; i < _curves.Count; i++)
            {
                CieCurve curve = _curves[i];
                if (curve == null || curve.Result == null) continue;
                double dx, dy;
                PlotSpace.ToDiagram(s.Kind, curve.Result.x, curve.Result.y, out dx, out dy);
                float px = tr.X(dx), py = tr.Y(dy);
                if (!InsidePlot(tr, px, py)) continue;
                if (s.HighlightSelection && curve.Selected)
                    canvas.DrawEllipse(s.SelectionColor, 2.4, px - (s.MarkerSize + 5) * canvas.PixelScale,
                        py - (s.MarkerSize + 5) * canvas.PixelScale, (s.MarkerSize + 5) * 2 * canvas.PixelScale,
                        (s.MarkerSize + 5) * 2 * canvas.PixelScale);
                double cr = s.MarkerSize * canvas.PixelScale;
                DrawMarker(canvas, curve.Shape, px, py, cr, curve.Color);
                // 光谱曲线的标记也要有描边（之前只给坐标点画了描边，曲线标记看着像没有边框）
                DrawMarkerOutline(canvas, curve.Shape, px, py, cr + 0.6 * canvas.PixelScale, curve.OutlineColor,
                                  (float)Math.Max(1.3, 1.8 * canvas.PixelScale));
                if (s.ShowPointLabels)
                {
                    DrawPointLabel(canvas, tr, curve.Name, px, py);
                }
            }
        }

        private void DrawPoints(IPlotCanvas canvas, PlotTransform tr)
        {
            DiagramStyle s = _style;
            for (int i = 0; i < _points.Count; i++)
            {
                CiePoint pt = _points[i];
                if (pt == null) continue;
                double dx, dy;
                PlotSpace.ToDiagram(s.Kind, pt.x, pt.y, out dx, out dy);
                float px = tr.X(dx), py = tr.Y(dy);
                if (!InsidePlot(tr, px, py)) continue;   // 视野外的点不画到框外
                if (s.HighlightSelection && pt.Selected)
                    canvas.DrawEllipse(s.SelectionColor, 2.4, px - (s.MarkerSize + 5) * canvas.PixelScale,
                        py - (s.MarkerSize + 5) * canvas.PixelScale, (s.MarkerSize + 5) * 2 * canvas.PixelScale,
                        (s.MarkerSize + 5) * 2 * canvas.PixelScale);
                double mr = s.MarkerSize * canvas.PixelScale;
                DrawMarker(canvas, pt.Shape, px, py, mr, pt.Color);
                DrawMarkerOutline(canvas, pt.Shape, px, py, mr + 0.6 * canvas.PixelScale, pt.OutlineColor,
                                  (float)Math.Max(1.3, 1.8 * canvas.PixelScale));
                if (s.ShowPointLabels)
                {
                    DrawPointLabel(canvas, tr, pt.Name, px, py);
                }
            }
        }

        /// <summary>判断像素坐标是否在绘图区内（视野外的数据点不画到框外面去）。</summary>
        /// <summary>两个矩形是否重叠（用于标签避让）。</summary>
        private void DrawPointLabel(IPlotCanvas canvas, PlotTransform tr, string name, double px, double py)
        {
            DiagramStyle s = _style;
            double x = px + (s.MarkerSize + 4) * canvas.PixelScale;
            double y = py - (s.MarkerSize + 3) * canvas.PixelScale;
            SizeD size = canvas.MeasureText(name, s.FontFamily, s.LabelFontSize * _fs, false);
            canvas.DrawText(name, LabelColor(tr, x + size.Width / 2, y - size.Height / 2),
                s.FontFamily, s.LabelFontSize * _fs, false, x, y, TextAlignH.Left, TextAlignV.Bottom, 0);
        }

        private Color LabelColor(PlotTransform tr, double px, double py)
        {
            if (!_style.BlackBackground) return _style.TextColor;
            double dx = tr.Bounds.XMin + (px - tr.Left) / tr.Width * tr.Bounds.Width;
            double dy = tr.Bounds.YMax - (py - tr.Top) / tr.Height * tr.Bounds.Height;
            if (!PlotSpace.InsideSpectrum(_style.Kind, dx, dy)) return Color.White;
            Color color = ChromaBackground.SampleColor(_style.Kind, dx, dy);
            double brightness = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
            return brightness >= 128 ? Color.Black : Color.White;
        }

        private static bool Overlaps(RectangleD a, RectangleD b)
        {
            double pad = 2.0;
            return a.Left < b.Left + b.Width + pad && a.Left + a.Width + pad > b.Left &&
                   a.Top < b.Top + b.Height + pad && a.Top + a.Height + pad > b.Top;
        }
        private static bool InsidePlot(PlotTransform tr, float px, float py)
        {
            float pad = 1.0f;
            return px >= tr.Left - pad && px <= tr.Left + tr.Width + pad &&
                   py >= tr.Top - pad && py <= tr.Top + tr.Height + pad;
        }
        private static void DrawMarker(IPlotCanvas canvas, MarkerShape shape, float cx, float cy, double r, Color color)
        {
            switch (shape)
            {
                case MarkerShape.Square:
                    canvas.FillRect(color, cx - r, cy - r, r * 2, r * 2);
                    break;
                case MarkerShape.Triangle:
                    canvas.FillPolygon(color, new[] { cx, (float)(cx - r), (float)(cx + r) },
                                              new[] { (float)(cy - r), (float)(cy + r), (float)(cy + r) });
                    break;
                case MarkerShape.Diamond:
                    canvas.FillPolygon(color, new[] { cx, (float)(cx + r), cx, (float)(cx - r) },
                                              new[] { (float)(cy - r), cy, (float)(cy + r), cy });
                    break;
                case MarkerShape.Cross:
                    canvas.DrawLine(color, r * 0.5, cx - r, cy, cx + r, cy);
                    canvas.DrawLine(color, r * 0.5, cx, cy - r, cx, cy + r);
                    break;
                case MarkerShape.Star:
                    {
                        float[] xs = new float[10], ys = new float[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double ang = -Math.PI / 2 + i * Math.PI / 5;
                            double rr = (i % 2 == 0) ? r : r * 0.45;
                            xs[i] = (float)(cx + rr * Math.Cos(ang));
                            ys[i] = (float)(cy + rr * Math.Sin(ang));
                        }
                        canvas.FillPolygon(color, xs, ys);
                    }
                    break;
                default:
                    canvas.FillEllipse(color, cx, cy, r, r);
                    break;
            }
        }

        /// <summary>按形状画描边（数据点的外圈颜色）。</summary>
        private static void DrawMarkerOutline(IPlotCanvas canvas, MarkerShape shape, float cx, float cy,
                                              double r, Color color, float width)
        {
            switch (shape)
            {
                case MarkerShape.Square:
                    canvas.DrawRect(color, width, cx - r, cy - r, r * 2, r * 2);
                    break;
                case MarkerShape.Triangle:
                    canvas.DrawPolygon(color, width, new[] { cx, (float)(cx - r), (float)(cx + r) },
                                                      new[] { (float)(cy - r), (float)(cy + r), (float)(cy + r) });
                    break;
                case MarkerShape.Diamond:
                    canvas.DrawPolygon(color, width, new[] { cx, (float)(cx + r), cx, (float)(cx - r) },
                                                      new[] { (float)(cy - r), cy, (float)(cy + r), cy });
                    break;
                case MarkerShape.Cross:
                case MarkerShape.Star:
                    break;   // 十字/星形本身就是线条，不再额外描边
                default:
                    canvas.DrawEllipse(color, width, cx - r, cy - r, r * 2, r * 2);
                    break;
            }
        }

        // ---------------------------------------------------------------- 图例/标题

        private void DrawLegend(IPlotCanvas canvas, PlotTransform tr)
        {
            DiagramStyle s = _style;
            List<string> names = new List<string>();
            List<Color> colors = new List<Color>();
            for (int i = 0; i < _curves.Count; i++)
            {
                if (_curves[i] == null) continue;
                names.Add(_curves[i].Name);
                colors.Add(_curves[i].Color);
            }
            for (int i = 0; i < _points.Count; i++)
            {
                if (_points[i] == null) continue;
                names.Add(_points[i].Name);
                colors.Add(_points[i].Color);
            }
            if (names.Count == 0) return;

            double fs = s.LegendFontSize * _fs;
            double lineH = Math.Max(16 * canvas.PixelScale, fs * 1.55);
            double pad = Math.Max(8 * canvas.PixelScale, fs * 0.55);
            double maxW = 0;
            for (int i = 0; i < names.Count; i++)
            {
                SizeD sz = canvas.MeasureText(names[i], s.FontFamily, fs, false);
                if (sz.Width > maxW) maxW = sz.Width;
            }
            double boxW = maxW + fs * 2.8;
            double boxH = names.Count * lineH + pad * 2;
            double x = tr.Left + tr.Width - boxW - 10 * canvas.PixelScale;
            double y = s.LegendBottomRight
                ? tr.Top + tr.Height - boxH - 10 * canvas.PixelScale
                : tr.Top + 10 * canvas.PixelScale;

            canvas.FillRect(Color.FromArgb(238, s.Background), x, y, boxW, boxH);
            canvas.DrawRect(s.LegendBorder, 0.8, x, y, boxW, boxH);
            for (int i = 0; i < names.Count; i++)
            {
                double cy = y + pad + lineH * i + lineH / 2;
                canvas.FillEllipse(colors[i], x + fs * 0.75, cy - fs * 0.22, fs * 0.45, fs * 0.45);
                canvas.DrawText(names[i], s.TextColor, s.FontFamily, fs, false,
                    x + fs * 1.5, cy, TextAlignH.Left, TextAlignV.Middle, 0);
            }
        }

        private void DrawTitle(IPlotCanvas canvas, int width, double plotTop)
        {
            DiagramStyle s = _style;
            canvas.DrawText(PlotSpace.Title(s.Kind), s.TextColor, s.FontFamily, s.TitleFontSize, true,
                width / 2.0, Math.Max(6 * canvas.PixelScale, plotTop - 28 * canvas.PixelScale),
                TextAlignH.Center, TextAlignV.Top, 0);
        }
    }
}
