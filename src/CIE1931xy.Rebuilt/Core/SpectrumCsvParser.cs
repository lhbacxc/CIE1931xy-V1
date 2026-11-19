using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CIE1931xy.Core
{
    /// <summary>
    /// 光谱数据文本解析器（CIEdraw v1.0.0 兼容）。
    ///
    /// 自动处理：分隔符自动识别（, ; Tab | 空格）、说明行跳过、表头识别、
    /// 编号/备注列忽略、波长列自动识别（260–1400 nm，先剔除 &lt;100/&gt;1500 的异常值）、
    /// 重复波长取平均、非法字符给出行号。
    /// </summary>
    internal static class SpectrumCsvParser
    {
        /// <summary>按 UTF-8(BOM) → UTF-8 → GBK(936) → Latin-1 顺序尝试解码，兼容中文 CSV。</summary>
        public static string ReadTextFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            // 严格 UTF-8：遇到非法字节会抛异常（默认的替换回退会让 GBK 文件“看起来”成功，全是问号）
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { }

            // 中文 Windows 下 Excel 另存的 csv 基本都是 GBK/936
            try { return Encoding.GetEncoding(936).GetString(bytes); }
            catch (Exception) { }

            return Encoding.GetEncoding("latin-1").GetString(bytes);
        }

        private static bool IsBlank(string s)
        {
            return string.IsNullOrEmpty(s);
        }

        private static string Clean(string field)
        {
            string s = field.Trim(new char[] { '\u3000', ' ', '\t' }).Trim('"', '\'', '\u3000', ' ');
            return s;
        }

        private static bool TryNum(string field, out double value)
        {
            string t = Clean(field).Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static char DetectDelimiter(string line)
        {
            if (line.IndexOf(',') >= 0) return ',';
            if (line.IndexOf(';') >= 0) return ';';
            if (line.IndexOf('\t') >= 0) return '\t';
            if (line.IndexOf('|') >= 0) return '|';
            return '\0';   // 空格分隔
        }

        private static List<string> SplitFields(string line, char delim)
        {
            List<string> result = new List<string>();
            if (delim == '\0')
            {
                foreach (string token in line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    result.Add(token);
                }
                return result;
            }
            StringBuilder sb = new StringBuilder();
            bool inQuote = false;
            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }
                if (c == delim && !inQuote)
                {
                    result.Add(sb.ToString());
                    sb.Length = 0;
                    continue;
                }
                sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }

        /// <summary>解析光谱文本，返回曲线列表（可能多条）。</summary>
        public static ParseResult Parse(string text, string defaultName = "光谱")
        {
            ParseResult result = new ParseResult();
            if (IsBlank(text))
            {
                result.Errors.Add("输入内容为空。");
                return result;
            }

            List<int> lineNos = new List<int>();
            List<List<string>> rows = new List<List<string>>();
            int nCols = 0;

            string[] allLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < allLines.Length; i++)
            {
                string line = allLines[i].Trim();
                if (line.Length == 0) continue;
                List<string> fields = SplitFields(line, DetectDelimiter(line));
                rows.Add(fields);
                lineNos.Add(i + 1);
                if (fields.Count > nCols) nCols = fields.Count;
            }
            if (rows.Count == 0)
            {
                result.Errors.Add("输入内容为空。");
                return result;
            }

            // 只有“首字段为数值”的行参与列统计（仪器文件的参数说明行不污染统计）
            List<int> dataRowIdx = new List<int>();
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Count >= 2)
                {
                    double dummy;
                    if (TryNum(rows[r][0], out dummy)) dataRowIdx.Add(r);
                }
            }

            int[] colOk = new int[nCols];
            int[] colFail = new int[nCols];
            for (int k = 0; k < dataRowIdx.Count; k++)
            {
                List<string> fields = rows[dataRowIdx[k]];
                for (int j = 0; j < fields.Count; j++)
                {
                    double dummy;
                    if (TryNum(fields[j], out dummy)) colOk[j]++;
                    else colFail[j]++;
                }
            }

            // 波长列识别：先剔除物理异常值，再判断是否落在 260–1400 nm
            List<double>[] colValues = new List<double>[nCols];
            for (int j = 0; j < nCols; j++) colValues[j] = new List<double>();
            for (int k = 0; k < dataRowIdx.Count; k++)
            {
                List<string> fields = rows[dataRowIdx[k]];
                for (int j = 0; j < fields.Count; j++)
                {
                    double v;
                    if (TryNum(fields[j], out v)) colValues[j].Add(v);
                }
            }

            int wlCol = 0;
            bool wlFound = false;
            for (int j = 0; j < nCols; j++)
            {
                List<double> vals = new List<double>();
                for (int k = 0; k < colValues[j].Count; k++)
                {
                    double v = colValues[j][k];
                    if (v >= 100.0 && v <= 1500.0) vals.Add(v);
                }
                if (vals.Count >= 3)
                {
                    double min = vals[0], max = vals[0];
                    for (int k = 1; k < vals.Count; k++)
                    {
                        if (vals[k] < min) min = vals[k];
                        if (vals[k] > max) max = vals[k];
                    }
                    if (min >= 260.0 && max <= 1400.0)
                    {
                        wlCol = j;
                        wlFound = true;
                        break;
                    }
                }
            }
            if (!wlFound) wlCol = 0;

            HashSet<int> curveCols = new HashSet<int>();
            HashSet<int> textCols = new HashSet<int>();
            for (int j = wlCol + 1; j < nCols; j++)
            {
                int total = colOk[j] + colFail[j];
                if (total > 0 && (double)colOk[j] / total >= 0.8) curveCols.Add(j);
                else if (total > 0) textCols.Add(j);
            }
            for (int j = 0; j < wlCol; j++)
            {
                if (colOk[j] + colFail[j] > 0) textCols.Add(j);
            }
            if (curveCols.Count == 0)
            {
                for (int j = wlCol + 1; j < nCols; j++)
                {
                    if (colOk[j] > 0) { curveCols.Add(j); break; }
                }
            }
            if (curveCols.Count == 0)
            {
                result.Errors.Add("未找到强度列：请确认数据为「波长 强度」两列（可用逗号/分号/Tab/空格分隔）。");
                return result;
            }

            // 按列收集数据
            Dictionary<int, List<double>> colWl = new Dictionary<int, List<double>>();
            Dictionary<int, List<double>> colVal = new Dictionary<int, List<double>>();
            Dictionary<int, string> colName = new Dictionary<int, string>();
            Dictionary<int, string> headerNames = new Dictionary<int, string>();
            bool headerSeen = false;
            int noteRows = 0;

            for (int r = 0; r < rows.Count; r++)
            {
                List<string> fields = rows[r];
                int lineNo = lineNos[r];
                bool allEmpty = true;
                for (int i = 0; i < fields.Count; i++) if (!IsBlank(fields[i])) { allEmpty = false; break; }
                if (allEmpty) continue;

                if (fields.Count == 1)
                {
                    double dummy;
                    if (!TryNum(fields[0], out dummy)) { noteRows++; continue; }
                    result.Errors.Add("第 " + lineNo + " 行只有波长一列数值，缺少强度列，已跳过。");
                    continue;
                }

                double firstNum;
                if (!TryNum(fields[0], out firstNum))
                {
                    if (!headerSeen)
                    {
                        bool allText = true;
                        for (int i = 0; i < fields.Count; i++)
                        {
                            double dummy;
                            if (TryNum(fields[i], out dummy)) { allText = false; break; }
                        }
                        if (allText)
                        {
                            for (int i = 1; i < fields.Count; i++)
                            {
                                string name = Clean(fields[i]);
                                if (name.Length == 0 || IsGenericValueName(name)) continue;
                                headerNames[i] = name;
                            }
                            continue;
                        }
                    }
                    noteRows++;
                    continue;
                }
                headerSeen = true;

                if (wlCol >= fields.Count)
                {
                    result.Errors.Add("第 " + lineNo + " 行缺少波长列数据（第 " + (wlCol + 1) + " 列）。");
                    continue;
                }
                double wl;
                if (!TryNum(fields[wlCol], out wl))
                {
                    result.Errors.Add("第 " + lineNo + " 行波长不是合法数值：" + Clean(fields[wlCol]));
                    continue;
                }

                List<int> cols = new List<int>(curveCols);
                cols.Sort();
                for (int ci = 0; ci < cols.Count; ci++)
                {
                    int col = cols[ci];
                    if (col >= fields.Count)
                    {
                        result.Errors.Add("第 " + lineNo + " 行缺少第 " + (col + 1) + " 列数据。");
                        continue;
                    }
                    double val;
                    if (!TryNum(fields[col], out val))
                    {
                        result.Errors.Add("第 " + lineNo + " 行第 " + (col + 1) + " 列不是合法数值：" + Clean(fields[col]));
                        continue;
                    }
                    if (!colWl.ContainsKey(col))
                    {
                        colWl[col] = new List<double>();
                        colVal[col] = new List<double>();
                        string name;
                        if (headerNames.TryGetValue(col, out name)) colName[col] = name;
                        else if (col == 1) colName[col] = defaultName;
                        else colName[col] = defaultName + "-列" + (col + 1);
                    }
                    colWl[col].Add(wl);
                    colVal[col].Add(val);
                }
            }

            if (noteRows > 0) result.Warnings.Add("已自动跳过 " + noteRows + " 行说明/表头文本。");
            if (textCols.Count > 0) result.Warnings.Add("识别到 " + textCols.Count + " 个非数据列（备注/编号/序号等），已自动忽略。");

            if (colWl.Count == 0)
            {
                if (result.Errors.Count == 0) result.Errors.Add("未解析到任何有效光谱数据行，请检查格式。");
                return result;
            }

            List<int> sortedCols = new List<int>(colWl.Keys);
            sortedCols.Sort();
            for (int i = 0; i < sortedCols.Count; i++)
            {
                int col = sortedCols[i];
                string name = colName.ContainsKey(col) ? colName[col] : (defaultName + "-列" + (col + 1));
                CieCurve curve = ValidateCurve(name, colWl[col].ToArray(), colVal[col].ToArray(),
                                               result.Warnings, result.Errors);
                if (curve != null) result.Curves.Add(curve);
            }
            return result;
        }

        private static bool IsGenericValueName(string name)
        {
            string[] generic = { "强度", "intensity", "value", "values", "值", "相对强度", "radiance" };
            string low = name.ToLowerInvariant();
            for (int i = 0; i < generic.Length; i++) if (low == generic[i]) return true;
            return false;
        }

        /// <summary>单条曲线校验：合法性过滤、异常波长剔除、重复波长取平均、排序。</summary>
        private static CieCurve ValidateCurve(string name, double[] wlIn, double[] valIn,
                                              List<string> warnings, List<string> errors)
        {
            List<double> wl = new List<double>();
            List<double> val = new List<double>();
            int bad = 0;
            for (int i = 0; i < wlIn.Length; i++)
            {
                if (double.IsNaN(wlIn[i]) || double.IsInfinity(wlIn[i]) ||
                    double.IsNaN(valIn[i]) || double.IsInfinity(valIn[i]) || wlIn[i] <= 0)
                {
                    bad++;
                    continue;
                }
                wl.Add(wlIn[i]);
                val.Add(valIn[i]);
            }
            if (bad > 0) warnings.Add("曲线「" + name + "」剔除 " + bad + " 个非法点（空值/非数值/非正波长）。");

            int odd = 0;
            List<double> wl2 = new List<double>();
            List<double> val2 = new List<double>();
            for (int i = 0; i < wl.Count; i++)
            {
                if (wl[i] < 100.0 || wl[i] > 1500.0) { odd++; continue; }
                wl2.Add(wl[i]);
                val2.Add(val[i]);
            }
            if (odd > 0) warnings.Add("曲线「" + name + "」剔除 " + odd + " 个异常波长点（<100 或 >1500 nm，疑似仪器导出错误）。");
            wl = wl2;
            val = val2;

            int negative = 0;
            for (int i = 0; i < val.Count; i++) if (val[i] < 0) negative++;
            if (negative > 0) warnings.Add("曲线「" + name + "」存在 " + negative + " 个负强度值（已保留，请确认数据是否正确）。");

            if (wl.Count == 0)
            {
                errors.Add("曲线「" + name + "」剔除非法点后无有效数据。");
                return null;
            }

            // 排序 + 重复波长取平均
            double[][] pairs = new double[wl.Count][];
            for (int i = 0; i < wl.Count; i++) pairs[i] = new double[] { wl[i], val[i], i };
            Array.Sort(pairs, delegate (double[] a, double[] b)
            {
                int cmp = a[0].CompareTo(b[0]);
                return cmp != 0 ? cmp : a[2].CompareTo(b[2]);
            });

            List<double> outWl = new List<double>();
            List<double> outVal = new List<double>();
            int i2 = 0;
            int dup = 0;
            while (i2 < pairs.Length)
            {
                double w = pairs[i2][0];
                double sum = 0;
                int n = 0;
                while (i2 < pairs.Length && Math.Abs(pairs[i2][0] - w) < 1e-9)
                {
                    sum += pairs[i2][1];
                    n++;
                    i2++;
                }
                if (n > 1) dup += n - 1;
                outWl.Add(w);
                outVal.Add(sum / n);
            }
            if (dup > 0) warnings.Add("曲线「" + name + "」存在 " + dup + " 个重复波长，已按平均值合并。");

            if (outWl[0] > 830.0 || outWl[outWl.Count - 1] < 360.0)
            {
                errors.Add("曲线「" + name + "」波长范围 " + outWl[0].ToString("0") + "–" +
                           outWl[outWl.Count - 1].ToString("0") + " nm 超出色匹配函数范围 (360–830 nm)。");
                return null;
            }
            if (outWl[0] > 360.0 || outWl[outWl.Count - 1] < 830.0)
            {
                warnings.Add("曲线「" + name + "」波长范围 " + outWl[0].ToString("0") + "–" +
                             outWl[outWl.Count - 1].ToString("0") + " nm 未完全覆盖 360–830 nm，缺失区间按 0 处理。");
            }
            if (outWl.Count < 3)
            {
                errors.Add("曲线「" + name + "」有效点数仅 " + outWl.Count + " 个，至少需要 3 个点。");
                return null;
            }

            CieCurve curve = new CieCurve(name, outWl.ToArray(), outVal.ToArray());
            return curve;
        }

        /// <summary>判断文本像光谱还是像色坐标（用于拖放/导入时自动分派）。</summary>
        public static bool LooksLikeCoordinates(string text)
        {
            if (IsBlank(text)) return false;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int numericRows = 0;
            int wideRows = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                List<string> f = SplitFields(line, DetectDelimiter(line));
                if (f.Count >= 2)
                {
                    wideRows++;
                    double a, b;
                    if (TryNum(f[f.Count - 2], out a) && TryNum(f[f.Count - 1], out b)) numericRows++;
                }
            }
            if (wideRows == 0) return false;
            // 坐标：几乎没有“波长”量级（>300）的数值
            bool hasWaveLengthScale = false;
            for (int i = 0; i < lines.Length; i++)
            {
                List<string> f = SplitFields(lines[i].Trim(), DetectDelimiter(lines[i].Trim()));
                for (int j = 0; j < f.Count; j++)
                {
                    double v;
                    if (TryNum(f[j], out v) && v >= 300.0 && v <= 900.0) { hasWaveLengthScale = true; break; }
                }
                if (hasWaveLengthScale) break;
            }
            if (hasWaveLengthScale) return false;

            // 多列数据（例如「名称,x,y,强度」）也要认出来：只要行内存在相邻两列满足
            // 0–1 且 x+y≤1.05，就当作坐标行（以前只数 2/3 列，4 列文件会被误判成光谱）。
            int coordRows = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                List<string> f = SplitFields(line, DetectDelimiter(line));
                for (int a = 0; a + 1 < f.Count; a++)
                {
                    double x, y;
                    if (TryNum(f[a], out x) && TryNum(f[a + 1], out y) &&
                        x >= 0 && x <= 1 && y >= 0 && y <= 1 && x + y <= 1.05)
                    {
                        coordRows++;
                        break;
                    }
                }
            }
            return coordRows >= Math.Max(1, wideRows / 2);
        }
    }
}
