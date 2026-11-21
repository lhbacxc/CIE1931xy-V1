using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using CIE1931xy.Core.Export;
using CIE1931xy.Core.Rendering;

namespace CIE1931xy.Core
{
    /// <summary>
    /// 无界面自检：渲染 + 导出各种格式，供 build.ps1 / CI 或命令行 <c>--selftest &lt;目录&gt;</c> 使用。
    /// 不依赖窗体，因此可以在没有桌面的环境里跑。
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string outDir, TextWriter log)
        {
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Path.GetTempPath(), "cie1931-selftest");
            Directory.CreateDirectory(outDir);

            List<CieCurve> curves = new List<CieCurve>();
            List<CiePoint> points = new List<CiePoint>();

            double[,] cmf = CieData.Cie1931xyz;
            int n = cmf.GetLength(0);
            double[] wl = new double[n];
            double[] val = new double[n];
            for (int i = 0; i < n; i++)
            {
                wl[i] = cmf[i, 0];
                val[i] = PlanckianLocus.PlanckRadiance(cmf[i, 0] * 1e-9, 6500.0);
            }
            CieCurve curve = new CieCurve("6500 K 黑体", wl, val);
            curve.Result = Colorimetry.AnalyzeCurve(curve);
            curves.Add(curve);

            CiePoint point = new CiePoint();
            point.Name = "D65 参考";
            point.x = 0.31271;
            point.y = 0.32902;
            Colorimetry.FillPointDerived(point);
            points.Add(point);

            int failures = 0;
            failures += Check("曲线 x", curve.Result.x, 0.3135, 0.002, log);
            failures += Check("曲线 y", curve.Result.y, 0.3236, 0.002, log);
            failures += Check("曲线 CCT", curve.Result.Cct, 6500, 120, log);
            failures += Check("点 u'", point.U, 0.1978, 0.002, log);
            failures += Check("点 v'", point.V, 0.4684, 0.002, log);

            // ---- 光谱解析自检：表头跳过、编号列忽略、说明行跳过、重复波长取平均、越界波长剔除
            string sampleCsv =
                "编号,波长,强度\r\n" +
                "1,500,0.5000\r\n" +
                "2,501,0.6000\r\n" +
                "说明：这一行应被跳过\r\n" +
                "3,501,0.8000\r\n" +
                "4,502,0.4000\r\n" +
                "5,90,0.2000\r\n";
            ParseResult parsed = SpectrumCsvParser.Parse(sampleCsv, "解析自检");
            failures += Check("解析：曲线数", parsed.Curves.Count, 1, 0, log);
            if (parsed.Curves.Count == 1)
            {
                CieCurve parsedCurve = parsed.Curves[0];
                failures += Check("解析：点数", parsedCurve.Wl.Length, 3, 0, log);
                if (parsedCurve.Wl.Length == 3)
                {
                    failures += Check("解析：最小波长", parsedCurve.Wl[0], 500, 0, log);
                    failures += Check("解析：重复波长取平均", parsedCurve.Val[1], 0.7, 1e-9, log);
                }
            }

            // ---- 坐标解析自检：名称列 + uv 换算 + CCT 推算
            ParseResult coord = CoordinateParser.Parse("名称,x,y\r\nD65,0.31271,0.32902");
            failures += Check("坐标：点数", coord.Points.Count, 1, 0, log);
            if (coord.Points.Count == 1)
            {
                failures += Check("坐标：u'", coord.Points[0].U, 0.1978, 0.002, log);
                failures += Check("坐标：CCT", coord.Points[0].Cct, 6500, 200, log);
            }

            foreach (DiagramKind kind in new[] { DiagramKind.Xy1931, DiagramKind.Uv1976 })
            {
                foreach (bool bitmap in new[] { true, false })
                {
                    DiagramStyle style = new DiagramStyle();
                    style.Kind = kind;
                    style.Bounds = PlotSpace.DefaultBounds(kind);
                    style.UseBitmapBackground = bitmap;
                    style.BitmapWithBlackbody = false;
                    style.ShowBlackbody = true;
                    style.ShowLegend = true;
                    style.ShowTitle = false;      // 标题已取消，自检也按成品的样式出图
                    style.ShowWavelengthLabels = true;

                    string tag = (kind == DiagramKind.Xy1931 ? "xy" : "uv") + (bitmap ? "-bitmap" : "-vector");
                    string png = Path.Combine(outDir, "selftest-" + tag + ".png");
                    string svg = Path.Combine(outDir, "selftest-" + tag + ".svg");
                    try
                    {
                        Exporter.SaveBitmap(style, curves, points, png, ImageFormat.Png, 900, 900, 144);
                        Exporter.SaveSvg(style, curves, points, svg, 900, 900);
                        failures += CheckFile(png, 20000, log);
                        failures += CheckFile(svg, 4000, log);
                    }
                    catch (Exception ex)
                    {
                        log.WriteLine("FAIL 渲染 " + tag + "：" + ex.Message);
                        failures++;
                    }
                }
            }

            try
            {
                string csv = Path.Combine(outDir, "selftest-result.csv");
                Exporter.SaveCsv(csv, curves, points);
                failures += CheckFile(csv, 200, log);
                string txt = Path.Combine(outDir, "selftest-report.txt");
                Exporter.SaveTxt(txt, curves, points, new DiagramStyle(), 900, 900);
                failures += CheckFile(txt, 400, log);
            }
            catch (Exception ex)
            {
                log.WriteLine("FAIL 导出数据：" + ex.Message);
                failures++;
            }

            failures += ArrowSelfTest.Run(outDir, log);
            log.WriteLine(failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED: " + failures);
            return failures;
        }

        private static int Check(string what, double actual, double expected, double tolerance, TextWriter log)
        {
            bool ok = !double.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance;
            log.WriteLine((ok ? "PASS  " : "FAIL  ") + what + " = " + actual.ToString("0.00000") +
                          "（期望 " + expected.ToString("0.00000") + "）");
            return ok ? 0 : 1;
        }

        private static int CheckFile(string path, long minBytes, TextWriter log)
        {
            FileInfo info = new FileInfo(path);
            bool ok = info.Exists && info.Length >= minBytes;
            log.WriteLine((ok ? "PASS  " : "FAIL  ") + Path.GetFileName(path) + " = " +
                          (info.Exists ? info.Length.ToString() : "缺失") + " 字节");
            return ok ? 0 : 1;
        }
    }
}
