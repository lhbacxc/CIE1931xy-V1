using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace CIE1931xy.Core;

/// <summary>
/// 黑体轨迹覆盖层的可调参数。
/// <para>
/// 想改观感（色温范围、标签温度、刻度长度、颜色、字体）就改这里，或在运行时改
/// <see cref="Form1"/> 里持有的那个实例；<b>Ctrl+P</b> 打开/关闭叠加层。
/// </para>
/// </summary>
internal sealed class PlanckianOverlayOptions
{
    /// <summary>轨迹起始色温（K）。</summary>
    internal double MinKelvin = PlanckianLocus.DefaultTempMinK;

    /// <summary>轨迹结束色温（K）。原版底图的曲线约到 14300 K 就偏离物理，25000 K 以内最稳。</summary>
    internal double MaxKelvin = PlanckianLocus.DefaultTempMaxK;

    /// <summary>折线采样段数（对数间隔采样，低温端更密）。</summary>
    internal int Samples = 240;

    /// <summary>要画等温线刻度并标注的色温（K）；设为 <c>null</c> 则只画轨迹。</summary>
    internal double[] LabelKelvin = PlanckianLocus.SuggestedLabelTemperatures;

    /// <summary>等温线半长（xy 单位）。总长 = 2 倍；0.05 ≈ 原版的"长刻度"，0.006 ≈ 现代细刻度。</summary>
    internal double TickHalfLength = 0.05;

    /// <summary>
    /// 标签相对轨迹点的法向偏移（xy 单位），是 <see cref="LabelKelvin"/> 的默认值。
    /// 负值 = 放在 −n 侧（轨迹上方），正值 = +n 侧（轨迹下方，原图里 1500/2000 就在温暖色区那一侧）。
    /// </summary>
    internal double LabelOffsetXy = -0.062;

    /// <summary>
    /// 每个温度各自的偏移覆盖值（与 <see cref="LabelKelvin"/> 一一对应，可为 null）。
    /// 默认让 1500/2000 K 落到轨迹下方（和原版底图的排布一致，也避开蓝色波长数字）。
    /// </summary>
    internal double[] LabelOffsetsXy = { 0.030, 0.038, -0.062, -0.062, -0.062, -0.062, -0.062 };

    /// <summary>轨迹标题文字（原图为 <c>T_c(K)</c>）；下标部分见 <see cref="CaptionSubscript"/>。</summary>
    internal string Caption = "T";

    /// <summary>下标文字（原图为 <c>c</c>，表示相关色温）；留空则不画下标。</summary>
    internal string CaptionSubscript = "c";

    /// <summary>下标之后的文字（原图为 <c>(K)</c>）。</summary>
    internal string CaptionSuffix = "(K)";

    /// <summary>标题位置（xy 单位）。</summary>
    internal PointF CaptionPosition = new PointF(0.31f, 0.49f);

    /// <summary>是否绘制标题。</summary>
    internal bool DrawCaption = true;

    /// <summary>轨迹颜色。</summary>
    internal Color CurveColor = Color.Black;

    /// <summary>轨迹线宽。</summary>
    internal float CurveWidth = 1.6f;

    /// <summary>等温线颜色。</summary>
    internal Color TickColor = Color.Black;

    /// <summary>等温线线宽。</summary>
    internal float TickWidth = 1.2f;

    /// <summary>标签颜色。</summary>
    internal Color LabelColor = Color.Black;

    /// <summary>标签/标题字体。</summary>
    internal Font LabelFont = new Font("Microsoft Sans Serif", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>标签字号（绘制文字用 <see cref="LabelFont"/>，标题略大一点）。</summary>
    internal float CaptionFontSize = 9.5f;
}

/// <summary>
/// 把 <see cref="PlanckianLocus"/> 算出的黑体轨迹画到色品图上。
/// <para>
/// 坐标映射用的是 <see cref="DiagramMapping"/>，与"描绘坐标"按钮完全同一套，
/// 所以轨迹、等温线、标签和用户描的点永远对齐（含原版 (59,12) 的绘图原点偏移）。
/// </para>
/// <para>
/// 原版的轨迹是"烘焙"在背景位图里的死图；这里是按普朗克定律实时算出来的，
/// 因此范围/刻度/颜色都可以改，这就是留给后续优化的入口。
/// </para>
/// </summary>
internal static class PlanckianOverlay
{
    /// <summary>绘制轨迹 + 等温线 + 温度标签 + 标题。</summary>
    /// <param name="g">目标画布。</param>
    /// <param name="map">xy → 像素映射。</param>
    /// <param name="cie1931xyz">配色函数表 <c>[n,4]</c>（<see cref="CieData.Cie1931xyz"/>）。</param>
    /// <param name="options">样式参数；传 <c>null</c> 用默认值。</param>
    internal static void Draw(Graphics g, DiagramMapping map, double[,] cie1931xyz,
        PlanckianOverlayOptions options = null)
    {
        if (g == null)
            throw new ArgumentNullException(nameof(g));
        if (cie1931xyz == null)
            throw new ArgumentNullException(nameof(cie1931xyz));
        if (options == null)
            options = new PlanckianOverlayOptions();

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        DrawCurve(g, map, cie1931xyz, options);

        if (options.LabelKelvin == null)
            return;

        using (Pen tickPen = new Pen(options.TickColor, options.TickWidth))
        using (SolidBrush textBrush = new SolidBrush(options.LabelColor))
        using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
        {
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;

            for (int index = 0; index < options.LabelKelvin.Length; index++)
            {
                double kelvin = options.LabelKelvin[index];
                if (kelvin < options.MinKelvin || kelvin > options.MaxKelvin)
                    continue;

                (double x1, double y1, double x2, double y2) tick =
                    PlanckianLocus.IsothermSegment(kelvin, cie1931xyz, options.TickHalfLength);
                g.DrawLine(tickPen,
                    map.Px(tick.x1), map.Py(tick.y1),
                    map.Px(tick.x2), map.Py(tick.y2));

                double offset = options.LabelOffsetXy;
                if (options.LabelOffsetsXy != null && index < options.LabelOffsetsXy.Length)
                    offset = options.LabelOffsetsXy[index];

                (double lx, double ly) anchor =
                    PlanckianLocus.LabelAnchor(kelvin, cie1931xyz, offset);
                string text = kelvin.ToString("0", CultureInfo.InvariantCulture);
                g.DrawString(text, options.LabelFont, textBrush,
                    map.Px(anchor.lx), map.Py(anchor.ly), format);
            }

            if (options.DrawCaption)
                DrawCaption(g, map, textBrush, options);
        }
    }

    /// <summary>画标题 <c>T_c(K)</c>：普通字 + 下标 + 后缀依次排布，宽度用实际测量值推进。</summary>
    private static void DrawCaption(Graphics g, DiagramMapping map, Brush brush, PlanckianOverlayOptions options)
    {
        if (string.IsNullOrEmpty(options.Caption) && string.IsNullOrEmpty(options.CaptionSuffix))
            return;

        using (Font main = new Font(options.LabelFont.FontFamily, options.CaptionFontSize,
                   FontStyle.Regular, GraphicsUnit.Point))
        using (Font sub = new Font(options.LabelFont.FontFamily, options.CaptionFontSize * 0.72f,
                   FontStyle.Regular, GraphicsUnit.Point))
        {
            StringFormat format = StringFormat.GenericTypographic;
            float x = map.Px(options.CaptionPosition.X);
            float y = map.Py(options.CaptionPosition.Y);
            float subDrop = main.GetHeight(g) * 0.30f;

            if (!string.IsNullOrEmpty(options.Caption))
                x = DrawPart(g, options.Caption, main, brush, format, x, y);
            if (!string.IsNullOrEmpty(options.CaptionSubscript))
                x = DrawPart(g, options.CaptionSubscript, sub, brush, format, x, y + subDrop);
            if (!string.IsNullOrEmpty(options.CaptionSuffix))
                DrawPart(g, options.CaptionSuffix, main, brush, format, x, y);
        }
    }

    private static float DrawPart(Graphics g, string text, Font font, Brush brush, StringFormat format,
        float x, float y)
    {
        g.DrawString(text, font, brush, x, y, format);
        return x + g.MeasureString(text, font, PointF.Empty, format).Width;
    }

    private static void DrawCurve(Graphics g, DiagramMapping map, double[,] cie1931xyz,
        PlanckianOverlayOptions options)
    {
        if (options.Samples < 2 || options.MaxKelvin <= options.MinKelvin)
            return;

        PlanckianLocus.Curve(options.MinKelvin, options.MaxKelvin, options.Samples,
            cie1931xyz, out double[] xs, out double[] ys);
        if (xs == null || ys == null || xs.Length < 2)
            return;

        Point[] pixels = new Point[xs.Length];
        for (int i = 0; i < xs.Length; i++)
            pixels[i] = new Point(map.Px(xs[i]), map.Py(ys[i]));

        using (Pen pen = new Pen(options.CurveColor, options.CurveWidth))
        {
            pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, pixels);
        }
    }
}

