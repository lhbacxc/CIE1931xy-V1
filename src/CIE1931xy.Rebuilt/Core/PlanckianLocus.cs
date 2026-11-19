// =====================================================================================
//  PlanckianLocus.cs  —  CIE 1931 xy 色度图上的黑体轨迹（Planckian locus）
//
//  目标框架：.NET 8 与 .NET Framework 4.x（4.7.3+，需要 System.ValueTuple；本项目其余部分无依赖）
//  依赖：仅 System（无 System.Drawing、无第三方包）
//
//  典型用法（WinForms）：
//      double[,] mf = Form1.cie1931xyz;                       // [471,4] = { λ(nm), x̄, ȳ, z̄ }
//      double[] xs, ys;
//      PlanckianLocus.Curve(1000, 25000, 64, mf, out xs, out ys);   // 64 点折线（对数温度分布）
//      for (int i = 0; i < xs.Length; i++)
//          g.DrawLine(pen, X2Px(xs[i]), Y2Px(ys[i]), X2Px(xs[i+1]), Y2Px(ys[i+1]));
//
//  设计约定：
//    * 所有色度坐标 (x, y) 均为 CIE 1931 2° 观察者、未归一化的 xy 单位（0..0.8）。
//    * 所有温度单位为开尔文 K；所有波长对外接口用 nm，内部积分换算为 m。
//    * 曲线/线段返回值为纯 double，不绑定任何绘图库，方便换成 PointF 或 GDI+ 变换。
// =====================================================================================

namespace CIE1931xy.Core
{
    using System;

    /// <summary>
    /// 黑体（Planckian）轨迹与相关色温（CCT）计算工具。
    /// <para>
    /// 物理模型：绝对黑体的光谱辐射亮度按普朗克定律
    /// <c>M(λ,T) = c1 / (λ^5 · (exp(c2/(λT)) - 1))</c>，
    /// 其中 λ 以米为单位。将 M(λ,T) 与 CIE 1931 2° 配色函数逐波长加权求和得到三刺激值
    /// (X,Y,Z)，再归一化为色度坐标 <c>x = X/(X+Y+Z)</c>、<c>y = Y/(X+Y+Z)</c>；
    /// 温度变化时这些点连成的曲线即黑体轨迹。
    /// </para>
    /// <para>
    /// 由于 x、y 是比值，c1 在归一化时被约去，因此 c1 的取值不影响色度坐标
    /// （本文仍保留 <see cref="C1"/> 以便需要绝对辐亮度的场合直接使用 <see cref="PlanckRadiance"/>）。
    /// 真正决定数值精度的是第二辐射常数 <see cref="C2"/>：CIE 采用 ITS-90 温标下的
    /// 1.4388e-2 m·K（旧值 1.4380e-2 会让 6500 K 附近的 x、y 各偏移约 ±0.0002）。
    /// </para>
    /// </summary>
    public static class PlanckianLocus
    {
        // ---------------------------------------------------------------------------------
        // 物理常数
        // ---------------------------------------------------------------------------------

        /// <summary>第一辐射常数 c1 = 2πhc² ≈ 3.741771852e-16 W·m²。仅影响绝对辐亮度；色度坐标与它无关。</summary>
        public const double C1 = 3.741771852e-16;

        /// <summary>
        /// 第二辐射常数 c2 = hc/k = 1.4388e-2 m·K（CIE 15 / ITS-90 采用的数值）。
        /// 这是与已发布 Planckian 轨迹数值（2856 K ≈ 0.44757,0.40745 等）保持一致的关键常数，
        /// 若改用 1.4380e-2 会使 6500 K 处 x 减少约 0.0002、y 增加约 0.0002。
        /// </summary>
        public const double C2 = 1.4388e-2;

        // ---------------------------------------------------------------------------------
        // 缺省积分范围与温度范围（可直接在调用处改成别的值）
        // ---------------------------------------------------------------------------------

        /// <summary>缺省积分下限波长，nm（CIE 1931 2° 表的起点）。</summary>
        public const double LambdaMinNm = 360.0;

        /// <summary>缺省积分上限波长，nm（CIE 1931 2° 表的终点）。</summary>
        public const double LambdaMaxNm = 830.0;

        /// <summary>缺省最低色温，K（蜡烛/钠灯量级）。</summary>
        public const double DefaultTempMinK = 1000.0;

        /// <summary>缺省最高色温，K（蓝天空量级）。</summary>
        public const double DefaultTempMaxK = 25000.0;

        /// <summary>
        /// 建议标注的温度列表（K）。按 CIE 惯例标注整数温度；UI 里可只取其中若干项，
        /// 或改成本字段的副本后自由增删。
        /// </summary>
        public static readonly double[] SuggestedLabelTemperatures =
            new double[] { 1500.0, 2000.0, 2500.0, 3000.0, 4000.0, 6000.0, 10000.0 };

        // ---------------------------------------------------------------------------------
        // 普朗克光谱辐射亮度
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// 普朗克定律给出的黑体光谱辐射亮度 M(λ,T)，单位 W·m⁻³（每米波长间隔）。
        /// </summary>
        /// <param name="lambdaMetres">波长，单位米（如 550e-9）。</param>
        /// <param name="tempK">绝对温度，单位 K，必须 &gt; 0。</param>
        public static double PlanckRadiance(double lambdaMetres, double tempK)
        {
            if (!(lambdaMetres > 0.0))
                throw new ArgumentOutOfRangeException("lambdaMetres", "波长必须为正值（单位：米）。");
            if (!(tempK > 0.0))
                throw new ArgumentOutOfRangeException("tempK", "绝对温度必须为正值（单位：K）。");

            double lam2 = lambdaMetres * lambdaMetres;
            double lam5 = lam2 * lam2 * lambdaMetres;          // λ^5
            double x = C2 / (lambdaMetres * tempK);            // c2/(λT)
            return C1 / (lam5 * ExpMinusOne(x));
        }

        /// <summary>
        /// 计算 exp(x)-1，在 x 很小时用泰勒展开避免相消误差。
        /// 典型调用点 x 在 1e-2..80 之间，此函数主要是为了在极端高温/长波（x 很小）时保持稳定。
        /// </summary>
        private static double ExpMinusOne(double x)
        {
            if (x > 1e-6)
                return Math.Exp(x) - 1.0;
            // exp(x)-1 = x + x²/2 + x³/6 + O(x⁴)
            return x * (1.0 + x * (0.5 + x * (1.0 / 6.0)));
        }

        // ---------------------------------------------------------------------------------
        // 三刺激值积分与色度坐标
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// 将黑体光谱按配色函数积分得到归一化前的三刺激值 (X,Y,Z)。
        /// <para>采用梯形法则，支持任意（不限于 1 nm）的等距或不等距波长表：λ 取自表的第一列。</para>
        /// </summary>
        /// <param name="tempK">绝对温度，K。</param>
        /// <param name="cie1931xyz">配色函数表 [n,4]：{波长 nm, x̄, ȳ, z̄}，波长升序。</param>
        /// <param name="X">输出：X 三刺激值（相对尺度）。</param>
        /// <param name="Y">输出：Y 三刺激值（相对尺度）。</param>
        /// <param name="Z">输出：Z 三刺激值（相对尺度）。</param>
        public static void Tristimulus(double tempK, double[,] cie1931xyz,
                                       out double X, out double Y, out double Z)
        {
            ValidateTable(cie1931xyz);

            int n = cie1931xyz.GetLength(0);
            double sumX = 0.0, sumY = 0.0, sumZ = 0.0;

            double lamPrev = cie1931xyz[0, 0] * 1e-9;                    // nm -> m
            double mPrev = PlanckRadiance(lamPrev, tempK);

            for (int i = 1; i < n; i++)
            {
                double lam = cie1931xyz[i, 0] * 1e-9;
                double m = PlanckRadiance(lam, tempK);
                double half = 0.5 * (lam - lamPrev);                     // 梯形权重的一半

                sumX += half * (mPrev * cie1931xyz[i - 1, 1] + m * cie1931xyz[i, 1]);
                sumY += half * (mPrev * cie1931xyz[i - 1, 2] + m * cie1931xyz[i, 2]);
                sumZ += half * (mPrev * cie1931xyz[i - 1, 3] + m * cie1931xyz[i, 3]);

                lamPrev = lam;
                mPrev = m;
            }

            X = sumX;
            Y = sumY;
            Z = sumZ;
        }

        /// <summary>
        /// 返回 <see cref="Tristimulus"/> 的结果元组，便于函数式书写。
        /// </summary>
        public static (double X, double Y, double Z) TristimulusXyz(double tempK, double[,] cie1931xyz)
        {
            double X, Y, Z;
            Tristimulus(tempK, cie1931xyz, out X, out Y, out Z);
            return (X, Y, Z);
        }

        /// <summary>
        /// 计算温度 <paramref name="tempK"/> 的黑体色度坐标 (x, y)，即黑体轨迹上的点。
        /// <para>归一化：x = X/(X+Y+Z)，y = Y/(X+Y+Z)。</para>
        /// </summary>
        /// <param name="tempK">绝对温度，K（建议 1000..25000）。</param>
        /// <param name="cie1931xyz">配色函数表 [n,4]：{波长 nm, x̄, ȳ, z̄}。</param>
        public static (double x, double y) Chromaticity(double tempK, double[,] cie1931xyz)
        {
            double X, Y, Z;
            Tristimulus(tempK, cie1931xyz, out X, out Y, out Z);

            double sum = X + Y + Z;
            if (!(sum > 0.0) || double.IsInfinity(sum))
                throw new InvalidOperationException("三刺激值之和无效，请检查配色函数表与温度范围。");

            return (X / sum, Y / sum);
        }

        // ---------------------------------------------------------------------------------
        // 折线 / 批量点
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// 计算一组给定温度对应的 xy 点，返回 [n,2] 数组（第 0 列为 x，第 1 列为 y）。
        /// 这是最底层的批量接口，标签位置、自定义温度序列都可用它。
        /// </summary>
        public static double[,] CurvePoints(double[] tempsK, double[,] cie1931xyz)
        {
            if (tempsK == null)
                throw new ArgumentNullException("tempsK");
            ValidateTable(cie1931xyz);

            double[,] pts = new double[tempsK.Length, 2];
            for (int i = 0; i < tempsK.Length; i++)
            {
                double X, Y, Z;
                Tristimulus(tempsK[i], cie1931xyz, out X, out Y, out Z);
                double sum = X + Y + Z;
                if (!(sum > 0.0))
                    throw new InvalidOperationException("温度 " + tempsK[i].ToString() + " K 的三刺激值之和无效。");
                pts[i, 0] = X / sum;
                pts[i, 1] = Y / sum;
            }
            return pts;
        }

        /// <summary>
        /// 生成温度采样序列。默认按对数等距采样：ΔT 随 T 增大而增大，
        /// 这样轨迹上相邻点的弧长更均匀（1000..25000 K 跨 25 倍，线性采样会把点全挤在高温端）。
        /// </summary>
        /// <param name="tempMinK">起始温度，K。</param>
        /// <param name="tempMaxK">终止温度，K。</param>
        /// <param name="samples">点数，&gt;= 2。</param>
        /// <param name="logSpaced">true（缺省）= 对 T 取对数等距；false = 线性等距。</param>
        public static double[] Temperatures(double tempMinK, double tempMaxK, int samples, bool logSpaced = true)
        {
            if (!(tempMinK > 0.0) || !(tempMaxK > 0.0))
                throw new ArgumentOutOfRangeException("tempMinK", "温度必须为正值（单位：K）。");
            if (tempMaxK <= tempMinK)
                throw new ArgumentOutOfRangeException("tempMaxK", "最高温度必须大于最低温度。");
            if (samples < 2)
                throw new ArgumentOutOfRangeException("samples", "采样点数至少为 2。");

            double[] t = new double[samples];
            if (logSpaced)
            {
                double lnMin = Math.Log(tempMinK);
                double lnMax = Math.Log(tempMaxK);
                for (int i = 0; i < samples; i++)
                    t[i] = Math.Exp(lnMin + (lnMax - lnMin) * i / (samples - 1.0));
            }
            else
            {
                for (int i = 0; i < samples; i++)
                    t[i] = tempMinK + (tempMaxK - tempMinK) * i / (samples - 1.0);
            }
            return t;
        }

        /// <summary>
        /// 返回黑体轨迹折线点 [samples,2]（列 0 = x，列 1 = y），温度按对数等距。
        /// </summary>
        /// <param name="tempMinK">起始温度，K。</param>
        /// <param name="tempMaxK">终止温度，K。</param>
        /// <param name="samples">折线点数（绘图一般 48~96 足够平滑）。</param>
        /// <param name="cie1931xyz">配色函数表 [n,4]。</param>
        public static double[,] Curve(double tempMinK, double tempMaxK, int samples, double[,] cie1931xyz)
        {
            return Curve(tempMinK, tempMaxK, samples, cie1931xyz, true);
        }

        /// <summary>
        /// 返回黑体轨迹折线点 [samples,2]，可指定线性/对数等距。
        /// </summary>
        public static double[,] Curve(double tempMinK, double tempMaxK, int samples,
                                      double[,] cie1931xyz, bool logSpaced)
        {
            return CurvePoints(Temperatures(tempMinK, tempMaxK, samples, logSpaced), cie1931xyz);
        }

        /// <summary>
        /// 折线形式的黑体轨迹：同时输出 x 数组与 y 数组（便于直接喂给 drawing 代码）。
        /// </summary>
        public static void Curve(double tempMinK, double tempMaxK, int samples, double[,] cie1931xyz,
                                 out double[] xs, out double[] ys)
        {
            double[,] pts = Curve(tempMinK, tempMaxK, samples, cie1931xyz, true);
            int n = pts.GetLength(0);
            xs = new double[n];
            ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = pts[i, 0];
                ys[i] = pts[i, 1];
            }
        }

        // ---------------------------------------------------------------------------------
        // 等温线（isotemperature / isotherm）刻度
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// 轨迹在温度 <paramref name="tempK"/> 处的单位法向 n，即等温线（isotemperature line）的方向。
        /// <para>
        /// 求法：对温度做数值中心差分（ΔT = max(1 K, 0.1%·T)）得到切线 t（指向温度升高的方向），
        /// 再取 n = (-t.y, t.x)，即把 t 逆时针旋转 90°。
        /// </para>
        /// <para>
        /// 实测方向（1000..25000 K，CIE 1931 xy）：轨迹的 x 随温度单调减小，故 n.y = t.x 恒为负数，
        /// 即法向始终朝下、背向白色点（D65 在轨迹上方）。n.x = -t.y 在轨迹顶点处变号——
        /// y 最大点约在 2250 K（y ≈ 0.4153）：低于该温度时 n.x 为负（法向指向左下），
        /// 高于该温度时 n.x 为正（法向指向右下）。需要把标签放到另一侧时取 -n 即可。
        /// </para>
        /// </summary>
        /// <param name="tempK">温度，K。</param>
        /// <param name="cie1931xyz">配色函数表 [n,4]。</param>
        public static (double nx, double ny) IsothermNormal(double tempK, double[,] cie1931xyz)
        {
            double tx, ty, nx, ny;
            Tangents(tempK, cie1931xyz, out tx, out ty, out nx, out ny);
            return (nx, ny);
        }

        /// <summary>
        /// 轨迹在温度 T 处的单位切线 t，指向温度升高的方向（低温端 → 高温端）。
        /// 等温线与之垂直：n = (-t.y, t.x)，见 <see cref="IsothermNormal"/>。
        /// </summary>
        public static (double tx, double ty) LocusTangent(double tempK, double[,] cie1931xyz)
        {
            double tx, ty, nx, ny;
            Tangents(tempK, cie1931xyz, out tx, out ty, out nx, out ny);
            return (tx, ty);
        }

        /// <summary>
        /// 一次中心差分同时给出单位切线 (t) 与单位法向 (n = (-t.y, t.x))。
        /// ΔT 取 max(1 K, 0.1%·T)，使极低温处不至于因步长过小而放大数值噪声。
        /// </summary>
        private static void Tangents(double tempK, double[,] cie1931xyz,
                                     out double tx, out double ty, out double nx, out double ny)
        {
            ValidateTable(cie1931xyz);
            if (!(tempK > 0.0))
                throw new ArgumentOutOfRangeException("tempK", "绝对温度必须为正值（单位：K）。");

            double dT = Math.Max(1.0, tempK * 1e-3);
            double tLo = Math.Max(1.0, tempK - dT);
            double tHi = tempK + dT;

            double X, Y, Z;

            Tristimulus(tLo, cie1931xyz, out X, out Y, out Z);
            double s = X + Y + Z;
            double ax = X / s, ay = Y / s;

            Tristimulus(tHi, cie1931xyz, out X, out Y, out Z);
            s = X + Y + Z;
            double bx = X / s, by = Y / s;

            tx = bx - ax;
            ty = by - ay;
            double len = Math.Sqrt(tx * tx + ty * ty);
            if (!(len > 0.0))
                throw new InvalidOperationException("无法在该温度处求得轨迹切线（差分长度为 0）。");

            tx /= len;
            ty /= len;
            nx = -ty;              // 切线逆时针旋转 90°
            ny = tx;
        }

        /// <summary>
        /// 等温线刻度线段：以轨迹点为中心、沿法向两侧各延伸 <paramref name="halfLength"/>（xy 单位）。
        /// 返回 (x1,y1,x2,y2)，其中点 1 = P - h·n，点 2 = P + h·n。
        /// <para>例：<c>var s = PlanckianLocus.IsothermSegment(6500, mf, 0.006);</c>
        /// 在 6500 K 处画一条总长 0.012 xy（≈ 9 px @ 750 px/xy）的等温线刻度。</para>
        /// </summary>
        /// <param name="tempK">温度，K。</param>
        /// <param name="cie1931xyz">配色函数表 [n,4]。</param>
        /// <param name="halfLength">半长（xy 单位）；总长为 2 倍。负值等价于把两端对调。</param>
        public static (double x1, double y1, double x2, double y2) IsothermSegment(
            double tempK, double[,] cie1931xyz, double halfLength)
        {
            double X, Y, Z;
            Tristimulus(tempK, cie1931xyz, out X, out Y, out Z);
            double s = X + Y + Z;
            double px = X / s, py = Y / s;

            (double nx, double ny) n = IsothermNormal(tempK, cie1931xyz);

            return (px - halfLength * n.nx, py - halfLength * n.ny,
                    px + halfLength * n.nx, py + halfLength * n.ny);
        }

        /// <summary>
        /// 标签定位建议：轨迹点沿法向 n 再偏移 <paramref name="offsetXy"/> 后的位置。
        /// 正值 = 沿 n（一般朝下、背离白色点，见 <see cref="IsothermNormal"/>），负值 = 另一侧。
        /// 绘图时再把 xy 换算成像素（本项目的映射是 px = x·750、py = 675 - y·750）。
        /// </summary>
        public static (double x, double y) LabelAnchor(double tempK, double[,] cie1931xyz, double offsetXy)
        {
            double X, Y, Z;
            Tristimulus(tempK, cie1931xyz, out X, out Y, out Z);
            double s = X + Y + Z;
            (double nx, double ny) n = IsothermNormal(tempK, cie1931xyz);
            return (X / s + offsetXy * n.nx, Y / s + offsetXy * n.ny);
        }

        // ---------------------------------------------------------------------------------
        // 相关色温（CCT）与最近点搜索
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// McCamy (1992) 近似 CCT 公式，适用于 2000..12500 K 附近、且点靠近黑体轨迹的情形。
        /// <para>CCT = -449n³ + 3525n² - 6823.3n + 5520.33，其中 n = (x-0.3320)/(y-0.1858)。</para>
        /// <para>若 y ≈ 0.1858（分母趋零，对应极紫端），返回 <see cref="double.NaN"/>。</para>
        /// </summary>
        public static double CorrelatedColorTemperature(double x, double y)
        {
            double denom = y - 0.1858;
            if (Math.Abs(denom) < 1e-9)
                return double.NaN;

            double n = (x - 0.3320) / denom;
            return -449.0 * n * n * n + 3525.0 * n * n - 6823.3 * n + 5520.33;
        }

        /// <summary>
        /// 在 1000..25000 K 范围内搜索黑体轨迹上离给定色度点最近的点，返回该点温度。
        /// 用于 UI 里“点击色度图，读出相关色温”。
        /// </summary>
        public static double NearestTemperature(double x, double y, double[,] cie1931xyz)
        {
            return NearestTemperature(x, y, cie1931xyz, DefaultTempMinK, DefaultTempMaxK);
        }

        /// <summary>
        /// 在 [tempMinK, tempMaxK] 内搜索轨迹上距 (x,y) 最近的点（先对数粗扫，再黄金分割细化）。
        /// </summary>
        public static double NearestTemperature(double x, double y, double[,] cie1931xyz,
                                               double tempMinK, double tempMaxK)
        {
            ValidateTable(cie1931xyz);
            if (!(tempMinK > 0.0) || !(tempMaxK > tempMinK))
                throw new ArgumentOutOfRangeException("tempMinK", "温度范围无效。");

            double bestT, bestD;

            // 1) 对数粗扫 96 点，定位局部极小所在区间
            int coarse = 96;
            double lnMin = Math.Log(tempMinK);
            double lnMax = Math.Log(tempMaxK);
            bestT = tempMinK;
            bestD = double.MaxValue;
            int bestIdx = 0;
            double[] grid = new double[coarse];
            for (int i = 0; i < coarse; i++)
            {
                grid[i] = Math.Exp(lnMin + (lnMax - lnMin) * i / (coarse - 1.0));
                double d = SquaredDistance(grid[i], x, y, cie1931xyz);
                if (d < bestD)
                {
                    bestD = d;
                    bestT = grid[i];
                    bestIdx = i;
                }
            }

            // 2) 在极小值两侧的区间内做黄金分割（不假设区间外单调）
            int lo2 = Math.Max(0, bestIdx - 1);
            int hi2 = Math.Min(coarse - 1, bestIdx + 1);
            if (hi2 > lo2)
            {
                double a = grid[lo2];
                double b = grid[hi2];
                const double gr = 0.6180339887498949;   // (√5-1)/2
                double c = b - gr * (b - a);
                double d2 = a + gr * (b - a);
                double fc = SquaredDistance(c, x, y, cie1931xyz);
                double fd = SquaredDistance(d2, x, y, cie1931xyz);
                for (int it = 0; it < 48; it++)
                {
                    if (fc < fd)
                    {
                        b = d2; d2 = c; fd = fc;
                        c = b - gr * (b - a);
                        fc = SquaredDistance(c, x, y, cie1931xyz);
                    }
                    else
                    {
                        a = c; c = d2; fc = fd;
                        d2 = a + gr * (b - a);
                        fd = SquaredDistance(d2, x, y, cie1931xyz);
                    }
                }
                double t = 0.5 * (a + b);
                double dt = SquaredDistance(t, x, y, cie1931xyz);
                if (dt < bestD)
                {
                    bestD = dt;
                    bestT = t;
                }
            }

            return bestT;
        }

        /// <summary>
        /// 返回色度点 (x,y) 到黑体轨迹的最短距离（xy 单位），并给出最近点的温度。
        /// 可用于判断“该点是否落在黑体轨迹附近”，或做美术稿/理论曲线的一致性校验。
        /// </summary>
        public static double DistanceToLocus(double x, double y, double[,] cie1931xyz, out double tempK)
        {
            tempK = NearestTemperature(x, y, cie1931xyz);
            double X, Y, Z;
            Tristimulus(tempK, cie1931xyz, out X, out Y, out Z);
            double s = X + Y + Z;
            double dx = X / s - x;
            double dy = Y / s - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>短重载：不关心温度时只取距离。</summary>
        public static double DistanceToLocus(double x, double y, double[,] cie1931xyz)
        {
            double t;
            return DistanceToLocus(x, y, cie1931xyz, out t);
        }

        private static double SquaredDistance(double tempK, double x, double y, double[,] cie1931xyz)
        {
            double X, Y, Z;
            Tristimulus(tempK, cie1931xyz, out X, out Y, out Z);
            double s = X + Y + Z;
            double dx = X / s - x;
            double dy = Y / s - y;
            return dx * dx + dy * dy;
        }

        // ---------------------------------------------------------------------------------
        // 表校验
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// 校验配色函数表：二维、至少 2 行 4 列、波长严格升序。失败时抛 <see cref="ArgumentException"/>。
        /// </summary>
        public static void ValidateTable(double[,] cie1931xyz)
        {
            if (cie1931xyz == null)
                throw new ArgumentNullException("cie1931xyz", "配色函数表不能为 null。");
            if (cie1931xyz.Rank != 2)
                throw new ArgumentException("配色函数表必须是二维数组 [n,4]。", "cie1931xyz");

            int n = cie1931xyz.GetLength(0);
            if (n < 2 || cie1931xyz.GetLength(1) < 4)
                throw new ArgumentException("配色函数表至少需要 2 行 4 列：{λ(nm), x̄, ȳ, z̄}。", "cie1931xyz");

            for (int i = 1; i < n; i++)
            {
                if (!(cie1931xyz[i, 0] > cie1931xyz[i - 1, 0]))
                    throw new ArgumentException("配色函数表的波长列必须严格升序（单位 nm）。", "cie1931xyz");
            }
        }
    }
}
