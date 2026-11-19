using System;
using System.Collections.Generic;
using System.Drawing;

namespace CIE1931xy.Core
{
    /// <summary>
    /// 色度计算核心：光谱→XYZ→xy、xy/uv 互转、黑体轨迹、相关色温 (CCT + Δuv)。
    ///
    /// 口径说明：光谱→XYZ 默认沿用原版程序（对 1nm 数据逐点求和，不做 dλ、不归一化），
    /// 因此显示出来的 X/Y/Z 数值与经典 Form1 完全一致（oracle 9 用例可回归验证）。
    /// </summary>
    internal static class Colorimetry
    {
        /// <summary>第二辐射常数 (m·K)，CIE 15 / ITS-90。</summary>
        public const double C2 = 1.4388e-2;

        /// <summary>色匹配函数表：行 = {波长, x̄, ȳ, z̄}，360–830 nm 步长 1 nm。</summary>
        public static double[,] Cmf
        {
            get { return CieData.Cie1931xyz; }
        }

        private static readonly object SyncRoot = new object();
        private static double[] s_locusX;
        private static double[] s_locusY;
        private static double[] s_locusWl;
        private static double[] s_bbX;
        private static double[] s_bbY;
        private static double[] s_bbU;
        private static double[] s_bbV;
        private static double[] s_bbT;

        // ---------------------------------------------------------------- 光谱 → XYZ

        /// <summary>对任意波长网格积分（线性插值 CMF），返回 X/Y/Z（未归一化，与原版口径一致）。</summary>
        public static void SpectrumToXyz(double[] wl, double[] val, out double X, out double Y, out double Z)
        {
            X = 0; Y = 0; Z = 0;
            if (wl == null || val == null || wl.Length == 0) return;
            double[,] cmf = Cmf;
            int n = cmf.GetLength(0);
            for (int i = 0; i < wl.Length; i++)
            {
                double lambda = wl[i];
                if (lambda < cmf[0, 0] || lambda > cmf[n - 1, 0]) continue;
                double xb, yb, zb;
                Interpolate(cmf, lambda, out xb, out yb, out zb);
                X += val[i] * xb;
                Y += val[i] * yb;
                Z += val[i] * zb;
            }
        }

        /// <summary>同 <see cref="SpectrumToXyz"/>，但用梯形法（CIEdraw 口径），可选 Y=100 归一化。</summary>
        public static void SpectrumToXyzTrapezoid(double[] wl, double[] val, bool normalizeY100,
                                                  out double X, out double Y, out double Z)
        {
            X = 0; Y = 0; Z = 0;
            if (wl == null || val == null || wl.Length < 2) return;
            double[,] cmf = Cmf;
            int n = cmf.GetLength(0);
            double prevW = 0, prevX = 0, prevY = 0, prevZ = 0;
            bool hasPrev = false;
            for (int i = 0; i < wl.Length; i++)
            {
                double lambda = wl[i];
                if (lambda < cmf[0, 0] || lambda > cmf[n - 1, 0]) continue;
                double xb, yb, zb;
                Interpolate(cmf, lambda, out xb, out yb, out zb);
                double sx = val[i] * xb, sy = val[i] * yb, sz = val[i] * zb;
                if (hasPrev)
                {
                    double d = (lambda - prevW) * 0.5;
                    X += d * (sx + prevX);
                    Y += d * (sy + prevY);
                    Z += d * (sz + prevZ);
                }
                prevW = lambda; prevX = sx; prevY = sy; prevZ = sz;
                hasPrev = true;
            }
            if (normalizeY100 && Y > 0)
            {
                double k = 100.0 / Y;
                X *= k; Y *= k; Z *= k;
            }
        }

        private static void Interpolate(double[,] cmf, double lambda, out double x, out double y, out double z)
        {
            int n = cmf.GetLength(0);
            int idx = (int)(lambda - cmf[0, 0]);
            if (idx < 0) idx = 0;
            if (idx >= n - 1) { x = cmf[n - 1, 1]; y = cmf[n - 1, 2]; z = cmf[n - 1, 3]; return; }
            double x0 = cmf[idx, 0], x1 = cmf[idx + 1, 0];
            double t = (x1 - x0) < 1e-12 ? 0 : (lambda - x0) / (x1 - x0);
            x = cmf[idx, 1] + t * (cmf[idx + 1, 1] - cmf[idx, 1]);
            y = cmf[idx, 2] + t * (cmf[idx + 1, 2] - cmf[idx, 2]);
            z = cmf[idx, 3] + t * (cmf[idx + 1, 3] - cmf[idx, 3]);
        }

        // ---------------------------------------------------------------- 色度换算

        public static void XyzToXy(double X, double Y, double Z, out double x, out double y)
        {
            double sum = X + Y + Z;
            if (Math.Abs(sum) < 1e-15) { x = 0; y = 0; return; }
            x = X / sum;
            y = Y / sum;
        }

        /// <summary>CIE 1976 UCS：u' = 4x/(-2x+12y+3)，v' = 9y/(-2x+12y+3)。</summary>
        public static void XyToUv(double x, double y, out double u, out double v)
        {
            double d = -2.0 * x + 12.0 * y + 3.0;
            if (Math.Abs(d) < 1e-15) { u = 0; v = 0; return; }
            u = 4.0 * x / d;
            v = 9.0 * y / d;
        }

        public static bool UvToXy(double u, double v, out double x, out double y)
        {
            double d = 6.0 * u - 16.0 * v + 12.0;
            if (Math.Abs(d) < 1e-15) { x = 0; y = 0; return false; }
            x = 9.0 * u / d;
            y = 4.0 * v / d;
            return true;
        }

        /// <summary>CIE 1960 UCS：u = 4X/(X+15Y+3Z)，v = 6Y/(X+15Y+3Z)。</summary>
        public static void XyToUv1960(double x, double y, out double u, out double v)
        {
            double d = -2.0 * x + 12.0 * y + 3.0;   // 与 4x/(X+15Y+3Z) 同式
            if (Math.Abs(d) < 1e-15) { u = 0; v = 0; return; }
            u = 4.0 * x / d;
            v = 6.0 * y / d;
        }

        public static bool Uv1960ToXy(double u, double v, out double x, out double y)
        {
            double d = 2.0 * u - 8.0 * v + 4.0;
            if (Math.Abs(d) < 1e-15) { x = 0; y = 0; return false; }
            x = 3.0 * u / d;
            y = 2.0 * v / d;
            return true;
        }

        // ---------------------------------------------------------------- 轨迹与黑体

        /// <summary>光谱轨迹（x,y），按 1nm 排列；第 i 个点对应 <see cref="LocusWavelengths"/>。</summary>
        public static double[] SpectralLocusX { get { EnsureLocus(); return s_locusX; } }
        public static double[] SpectralLocusY { get { EnsureLocus(); return s_locusY; } }
        public static double[] LocusWavelengths { get { EnsureLocus(); return s_locusWl; } }

        private static void EnsureLocus()
        {
            if (s_locusX != null) return;
            lock (SyncRoot)
            {
                if (s_locusX != null) return;
                double[,] cmf = Cmf;
                int n = cmf.GetLength(0);
                double[] xs = new double[n];
                double[] ys = new double[n];
                double[] ws = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double X = cmf[i, 1], Y = cmf[i, 2], Z = cmf[i, 3];
                    double x, y;
                    XyzToXy(X, Y, Z, out x, out y);
                    xs[i] = x; ys[i] = y; ws[i] = cmf[i, 0];
                }
                s_locusY = ys; s_locusX = xs; s_locusWl = ws;
            }
        }

        /// <summary>色匹配函数在给定波长处的 (x,y)（用于波长标注）。</summary>
        public static bool LocusPointAt(double wavelength, out double x, out double y)
        {
            EnsureLocus();
            double[] ws = s_locusWl;
            if (wavelength < ws[0] || wavelength > ws[ws.Length - 1]) { x = 0; y = 0; return false; }
            int idx = (int)Math.Round(wavelength - ws[0]);
            if (idx < 0) idx = 0;
            if (idx >= ws.Length) idx = ws.Length - 1;
            x = s_locusX[idx];
            y = s_locusY[idx];
            return true;
        }

        /// <summary>黑体轨迹（CIE 1960 uv，1000–25000 K，对数采样 400 点），缓存。</summary>
        private static void EnsureBlackbody()
        {
            if (s_bbU != null) return;
            lock (SyncRoot)
            {
                if (s_bbU != null) return;
                const int count = 801;
                double[] temps = PlanckianLocus.Temperatures(
                    PlanckianLocus.DefaultTempMinK, PlanckianLocus.DefaultTempMaxK, count);
                double[,] table = Cmf;
                double[] us = new double[temps.Length];
                double[] vs = new double[temps.Length];
                double[] xs = new double[temps.Length];
                double[] ys = new double[temps.Length];
                for (int i = 0; i < temps.Length; i++)
                {
                    var xyz = PlanckianLocus.TristimulusXyz(temps[i], table);
                    double x, y, u, v;
                    XyzToXy(xyz.X, xyz.Y, xyz.Z, out x, out y);
                    XyToUv1960(x, y, out u, out v);
                    xs[i] = x; ys[i] = y; us[i] = u; vs[i] = v;
                }
                s_bbT = temps; s_bbX = xs; s_bbY = ys; s_bbU = us; s_bbV = vs;
            }
        }

        /// <summary>
        /// 相关色温（McCamy 邻近 + 1960 uv 最近点细化，与 CIEdraw 口径一致）。
        /// reliable 表示 Δuv ≤ 0.05。
        /// </summary>
        public static bool CorrelatedColorTemperature(double x, double y, out double cct, out double duv, out bool reliable)
        {
            cct = double.NaN;
            duv = double.NaN;
            reliable = false;
            EnsureBlackbody();
            if (s_bbU == null || s_bbU.Length < 2) return false;

            double u, v;
            XyToUv1960(x, y, out u, out v);

            int best = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < s_bbU.Length; i++)
            {
                double du = u - s_bbU[i];
                double dv = v - s_bbV[i];
                double d = du * du + dv * dv;
                if (d < bestDist) { bestDist = d; best = i; }
            }

            // 在最近邻附近做 ±150 K / 10 K 细化（用相邻采样插值温度）
            double tBest = s_bbT[best];
            double step = best + 1 < s_bbT.Length ? s_bbT[best + 1] - s_bbT[best] : s_bbT[best] - s_bbT[best - 1];
            if (step <= 0 || double.IsNaN(step)) step = 50;

            double lo = Math.Max(PlanckianLocus.DefaultTempMinK, tBest - 150);
            double hi = Math.Min(PlanckianLocus.DefaultTempMaxK, tBest + 150);
            const double coarse = 10.0;
            int samples = Math.Max(2, (int)Math.Round((hi - lo) / coarse) + 1);
            for (int i = 0; i < samples; i++)
            {
                double t = lo + i * (hi - lo) / (samples - 1);
                var xyz = PlanckianLocus.TristimulusXyz(t, Cmf);
                double px, py, pu, pv;
                XyzToXy(xyz.X, xyz.Y, xyz.Z, out px, out py);
                XyToUv1960(px, py, out pu, out pv);
                double du = u - pu, dv = v - pv;
                double d = du * du + dv * dv;
                if (d < bestDist) { bestDist = d; tBest = t; }
            }

            var xyz2 = PlanckianLocus.TristimulusXyz(tBest, Cmf);
            double qx, qy, qu, qv;
            XyzToXy(xyz2.X, xyz2.Y, xyz2.Z, out qx, out qy);
            XyToUv1960(qx, qy, out qu, out qv);
            double du2 = u - qu, dv2 = v - qv;
            double dist = Math.Sqrt(du2 * du2 + dv2 * dv2);

            cct = tBest;
            duv = dist;
            reliable = dist <= 0.05;
            return true;
        }

        // ---------------------------------------------------------------- 结果装配

        /// <summary>计算单条曲线结果（XYZ、xy、u'v'、峰值、点数、步长、CCT）。</summary>
        public static CurveResult AnalyzeCurve(CieCurve curve)
        {
            CurveResult r = new CurveResult();
            if (curve == null || curve.Wl == null || curve.Wl.Length == 0) return r;

            double X, Y, Z;
            SpectrumToXyz(curve.Wl, curve.Val, out X, out Y, out Z);
            r.X = X; r.Y = Y; r.Z = Z;
            double x, y, u, v;
            XyzToXy(X, Y, Z, out x, out y);
            XyToUv(x, y, out u, out v);
            r.x = x; r.y = y; r.u = u; r.v = v;
            r.Count = curve.Wl.Length;
            r.WlMin = curve.Wl[0];
            r.WlMax = curve.Wl[curve.Wl.Length - 1];
            r.Step = MedianStep(curve.Wl);

            double peakVal = curve.Val[0];
            double peakWl = curve.Wl[0];
            for (int i = 1; i < curve.Val.Length; i++)
            {
                if (curve.Val[i] > peakVal) { peakVal = curve.Val[i]; peakWl = curve.Wl[i]; }
            }
            r.PeakValue = peakVal;
            r.PeakWavelength = peakWl;

            double cct, duv;
            bool reliable;
            if (CorrelatedColorTemperature(x, y, out cct, out duv, out reliable))
            {
                r.Cct = cct; r.Duv = duv; r.CctReliable = reliable;
            }
            return r;
        }

        /// <summary>计算某个色坐标点的派生量（u'v'、CCT/Δuv）。</summary>
        public static void FillPointDerived(CiePoint pt)
        {
            if (pt == null) return;
            double cct, duv;
            bool reliable;
            if (CorrelatedColorTemperature(pt.x, pt.y, out cct, out duv, out reliable))
            {
                pt.Cct = cct; pt.Duv = duv; pt.CctReliable = reliable;
            }
        }

        private static double MedianStep(double[] wl)
        {
            if (wl.Length < 2) return 0;
            List<double> diffs = new List<double>(wl.Length - 1);
            for (int i = 1; i < wl.Length; i++)
            {
                double d = wl[i] - wl[i - 1];
                if (d > 0) diffs.Add(d);
            }
            if (diffs.Count == 0) return 0;
            diffs.Sort();
            return diffs[diffs.Count / 2];
        }
    }
}
