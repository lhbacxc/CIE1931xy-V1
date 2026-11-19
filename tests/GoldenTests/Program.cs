using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CIE1931xy.Core;

namespace CIE1931xy.GoldenTests
{
    /// <summary>
    /// 用"对照组"（对原版 exe 做反射调用得到的 oracle 结果）验证重建版的计算内核。
    /// <para>
    /// 用法：<c>GoldenTests.exe --oracle &lt;oracle_results.json&gt; [--report &lt;out.md&gt;]</c>
    /// </para>
    /// oracle_results.json 由 rea-work\oracle 下的反射程序生成，里面记录了每个用例
    /// 输入文件、数值字段、文本框字符串和 DataGridView 快照。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            string oraclePath = null;
            string reportPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--oracle" && i + 1 < args.Length)
                {
                    oraclePath = args[++i];
                }
                else if (args[i] == "--report" && i + 1 < args.Length)
                {
                    reportPath = args[++i];
                }
                else
                {
                    Console.Error.WriteLine("未知参数: " + args[i]);
                    return 2;
                }
            }

            if (oraclePath == null)
            {
                Console.Error.WriteLine("用法: GoldenTests --oracle <oracle_results.json> [--report <out.md>]");
                return 2;
            }

            // 原版程序运行时是中文区域设置；ToString()/ToDouble() 都受它影响。
            CultureInfo.CurrentCulture = new CultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");

            string json = File.ReadAllText(oraclePath, Encoding.UTF8);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                string baseDir = Path.GetDirectoryName(Path.GetFullPath(oraclePath));
                string exeName = root.TryGetProperty("exe_name", out JsonElement exeEl) ? exeEl.GetString() : "(unknown)";

                StringBuilder report = new StringBuilder();
                report.AppendLine("# Golden check — " + exeName);
                report.AppendLine();
                string relativeOracle = Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(oraclePath)).Replace('\\', '/');
                report.AppendLine("oracle: `" + relativeOracle + "`");
                report.AppendLine();
                report.AppendLine("| 用例 | 结果 | 差异数 |");
                report.AppendLine("|---|---|---|");

                int total = 0;
                int failed = 0;

                foreach (JsonElement c in root.GetProperty("cases").EnumerateArray())
                {
                    total++;
                    string name = c.GetProperty("name").GetString();
                    List<string> diffs = new List<string>();

                    if (c.TryGetProperty("steps", out JsonElement steps))
                    {
                        // 多步用例：在同一个窗体上连续计算，验证 CalcNum 累加不影响数值结果。
                        int stepIndex = 0;
                        foreach (JsonElement step in steps.EnumerateArray())
                        {
                            stepIndex++;
                            string stepName = step.TryGetProperty("name", out JsonElement sn) ? sn.GetString() : ("#" + stepIndex);
                            string stepInput = step.GetProperty("input").GetString();
                            int stepNm = step.GetProperty("nm").GetInt32();
                            string stepPath = Path.Combine(baseDir, stepInput);
                            if (!File.Exists(stepPath))
                            {
                                diffs.Add("步骤" + stepIndex + " 缺少输入文件 " + stepPath);
                                continue;
                            }
                            string stepText = File.ReadAllText(stepPath, Encoding.UTF8);
                            RunCase(stepText, stepNm, step.GetProperty("fields"), step.GetProperty("textboxes"), diffs,
                                "步骤" + stepIndex + "(" + stepName + ") ");
                        }
                    }
                    else
                    {
                        string inputFile = c.GetProperty("input").GetString();
                        int nm = c.GetProperty("nm").GetInt32();
                        string inputPath = Path.Combine(baseDir, inputFile);
                        if (!File.Exists(inputPath))
                        {
                            diffs.Add("缺少输入文件 " + inputPath);
                        }
                        else
                        {
                            string text = File.ReadAllText(inputPath, Encoding.UTF8);
                            RunCase(text, nm, c.GetProperty("fields"), c.GetProperty("textboxes"), diffs, "");
                        }
                    }

                    if (diffs.Count == 0)
                    {
                        report.AppendLine("| " + name + " | **PASS** | 0 |");
                        Console.WriteLine("[PASS] " + name);
                    }
                    else
                    {
                        failed++;
                        report.AppendLine("| " + name + " | **FAIL** | " + diffs.Count + " |");
                        Console.WriteLine("[FAIL] " + name + " (" + diffs.Count + " 处差异)");
                        foreach (string d in diffs)
                        {
                            Console.WriteLine("       " + d);
                            report.AppendLine("|   | " + Escape(d) + " |  |");
                        }
                    }
                }

                report.AppendLine();
                report.AppendLine(failed == 0
                    ? "**全部通过**：" + total + " 个用例与原版逐值一致。"
                    : "**有 " + failed + " / " + total + " 个用例存在差异。**");

                Console.WriteLine();
                Console.WriteLine("用例 " + total + " 个，失败 " + failed + " 个。");

                if (reportPath != null)
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
                    Console.WriteLine("报告已写入 " + Path.GetFullPath(reportPath));
                }

                return failed == 0 ? 0 : 1;
            }
        }

        private static void RunCase(string text, int nm, JsonElement expected, JsonElement expectedBoxes, List<string> diffs, string prefix)
        {
            bool parsed = SpectralInput.TryParse(text, out double[,] pdata, out int dataRow, out ParseFailure failure);
            if (!parsed)
            {
                diffs.Add(prefix + "解析失败: " + failure.Status);
                return;
            }

            double expectedDataRow = expected.GetProperty("Data_Row").GetDouble();
            Check(prefix + "Data_Row", expectedDataRow, dataRow, diffs);

            DataSpacing spacing = nm == 5 ? DataSpacing.FiveNm : DataSpacing.OneNm;
            CieMath.Status status = CieMath.Calculate(pdata, dataRow, spacing, out CieMath.Result result);
            if (status != CieMath.Status.Ok)
            {
                diffs.Add(prefix + "计算返回 " + status + "（期望 Ok）");
                return;
            }

            Check(prefix + "TSV_X", expected.GetProperty("TSV_X").GetDouble(), result.TSV_X, diffs);
            Check(prefix + "TSV_Y", expected.GetProperty("TSV_Y").GetDouble(), result.TSV_Y, diffs);
            Check(prefix + "TSV_Z", expected.GetProperty("TSV_Z").GetDouble(), result.TSV_Z, diffs);
            Check(prefix + "CC_x", expected.GetProperty("CC_x").GetDouble(), result.CC_x, diffs);
            Check(prefix + "CC_y", expected.GetProperty("CC_y").GetDouble(), result.CC_y, diffs);
            Check(prefix + "UCS_up", expected.GetProperty("UCS_up").GetDouble(), result.UCS_up, diffs);
            Check(prefix + "UCS_vp", expected.GetProperty("UCS_vp").GetDouble(), result.UCS_vp, diffs);
            Check(prefix + "PeakInt", expected.GetProperty("PeakInt").GetDouble(), result.PeakInt, diffs);
            Check(prefix + "PeakLambda", expected.GetProperty("PeakLambda").GetDouble(), result.PeakLambda, diffs);
            Check(prefix + "Offset", expected.GetProperty("Offset").GetDouble(), result.Offset, diffs);

            CheckString(prefix + "txtBoxCIEx", expectedBoxes.GetProperty("txtBoxCIEx").GetString(), VbText.FormatDouble(result.CC_x), diffs);
            CheckString(prefix + "txtBoxCIEy", expectedBoxes.GetProperty("txtBoxCIEy").GetString(), VbText.FormatDouble(result.CC_y), diffs);
            CheckString(prefix + "txtBoxTSVX", expectedBoxes.GetProperty("txtBoxTSVX").GetString(), VbText.FormatDouble(result.TSV_X), diffs);
            CheckString(prefix + "txtBoxTSVY", expectedBoxes.GetProperty("txtBoxTSVY").GetString(), VbText.FormatDouble(result.TSV_Y), diffs);
            CheckString(prefix + "txtBoxTSVZ", expectedBoxes.GetProperty("txtBoxTSVZ").GetString(), VbText.FormatDouble(result.TSV_Z), diffs);
            CheckString(prefix + "txtBoxUCSup", expectedBoxes.GetProperty("txtBoxUCSup").GetString(), VbText.FormatDouble(result.UCS_up), diffs);
            CheckString(prefix + "txtBoxUCSvp", expectedBoxes.GetProperty("txtBoxUCSvp").GetString(), VbText.FormatDouble(result.UCS_vp), diffs);
        }

        private static void Check(string field, double expected, double actual, List<string> diffs)
        {
            bool ok = (double.IsNaN(expected) && double.IsNaN(actual)) || expected == actual;
            if (!ok)
            {
                diffs.Add(field + ": 期望 " + Format(expected) + " / 实际 " + Format(actual) + " (Δ=" + Format(actual - expected) + ")");
            }
        }

        private static void CheckString(string field, string expected, string actual, List<string> diffs)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                diffs.Add(field + ": 期望 \"" + expected + "\" / 实际 \"" + actual + "\"");
            }
        }

        private static string Format(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static string Escape(string s) => s.Replace("|", "\\|");
    }
}
