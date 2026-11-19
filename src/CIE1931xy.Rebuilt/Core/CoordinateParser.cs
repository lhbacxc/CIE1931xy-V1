using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CIE1931xy.Core
{
    /// <summary>
    /// 色坐标文本解析器（CIEdraw 兼容）：
    /// 支持 "名称,x,y" 三列、"x,y" 两列，以及表头含 u'/v' 的 1976 u'v' 输入（自动换算回 xy）。
    /// </summary>
    internal static class CoordinateParser
    {
        private static string Clean(string field)
        {
            return field.Trim(new char[] { '\u3000', ' ', '\t' }).Trim('"', '\'', '\u3000', ' ');
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
            return '\0';
        }

        private static List<string> SplitFields(string line, char delim)
        {
            List<string> result = new List<string>();
            if (delim == '\0')
            {
                foreach (string token in line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    result.Add(token);
                return result;
            }
            StringBuilder sb = new StringBuilder();
            bool inQuote = false;
            foreach (char c in line)
            {
                if (c == '"') { inQuote = !inQuote; continue; }
                if (c == delim && !inQuote) { result.Add(sb.ToString()); sb.Length = 0; continue; }
                sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }

        private static bool HeaderSaysUv(string line)
        {
            string low = line.ToLowerInvariant();
            bool hasPrime = low.IndexOf('\u2032') >= 0 || low.IndexOf('\'') >= 0;
            return (low.IndexOf("u'") >= 0 || low.IndexOf("v'") >= 0 || (hasPrime && low.IndexOf('u') >= 0 && low.IndexOf('v') >= 0))
                   || low.IndexOf("1976") >= 0;
        }

        /// <summary>解析色坐标文本；返回点数与警告/错误。</summary>
        public static ParseResult Parse(string text)
        {
            ParseResult result = new ParseResult();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Errors.Add("输入内容为空。");
                return result;
            }

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // 先把"能解析成数字的行"收集起来：列数可能不止 2/3 列（例如 名称,x,y,强度），
            // 直接取最后两列会在多出来的数值列上取错，导致数据点位置完全不对。
            List<string[]> rows = new List<string[]>();
            List<int> rowNumbers = new List<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string[] fields = SplitFields(line, DetectDelimiter(line)).ToArray();
                if (fields.Length < 2) continue;
                double t;
                if (TryNum(fields[0], out t) || fields.Length >= 3)
                {
                    rows.Add(fields);
                    rowNumbers.Add(i + 1);
                }
            }
            if (rows.Count == 0)
            {
                result.Errors.Add("未解析到任何色坐标（需要「名称,x,y」「x,y」这样的两列或三列数据）。");
                return result;
            }

            // 表头：第一行不能当数值行用时视为表头；顺便判断是否是 1976 u'v' 输入。
            bool uvMode = false;
            bool skippedHeader = false;
            int firstNumericRow = 0;
            while (firstNumericRow < rows.Count)
            {
                double a, b;
                if (TryNum(rows[firstNumericRow][rows[firstNumericRow].Length - 2], out a) &&
                    TryNum(rows[firstNumericRow][rows[firstNumericRow].Length - 1], out b))
                    break;
                uvMode = HeaderSaysUv(string.Join(",", rows[firstNumericRow]));
                skippedHeader = true;
                firstNumericRow++;
                break;   // 只跳一行表头
            }
            if (uvMode) result.Warnings.Add("检测到 u'/v' 表头，按 1976 UCS 输入解析并自动换算为 x,y。");

            // 选中两列：在所有“数值列”里找一对，使绝大多数行满足 0≤x≤1、0≤y≤1、x+y≤1.05。
            int colX, colY;
            if (!PickColumns(rows, firstNumericRow, uvMode, out colX, out colY))
            {
                colX = rows[0].Length - 2;
                colY = rows[0].Length - 1;
                result.Warnings.Add("没能自动判断哪两列是色坐标，先按最后两列解析；如果不对请把表头写成「名称,x,y」。");
            }

            for (int r = firstNumericRow; r < rows.Count; r++)
            {
                string[] fields = rows[r];
                int rowNo = rowNumbers[r];
                double x, y;

                if (colX >= fields.Length || colY >= fields.Length ||
                    !TryNum(fields[colX], out x) || !TryNum(fields[colY], out y))
                {
                    result.Errors.Add("第 " + rowNo + " 行不是合法的色坐标（需要「名称,x,y」或「x,y」）。");
                    continue;
                }

                // 名称：取坐标列左边第一段非数字文字，没有就用「点N」
                string name = null;
                for (int k = 0; k < Math.Min(colX, fields.Length); k++)
                {
                    double t;
                    if (!TryNum(fields[k], out t))
                    {
                        name = Clean(fields[k]);
                        break;
                    }
                }
                if (string.IsNullOrEmpty(name)) name = "点" + (result.Points.Count + 1);

                if (uvMode)
                {
                    double ux = x, vy = y;
                    if (!Colorimetry.UvToXy(ux, vy, out x, out y))
                    {
                        result.Errors.Add("第 " + rowNo + " 行 u'/v' 无法换算为 xy（分母为 0）。");
                        continue;
                    }
                }

                if (x < 0 || x > 1 || y < 0 || y > 1)
                {
                    result.Errors.Add("第 " + rowNo + " 行色坐标超出 0–1 范围：(" + x.ToString("0.####") + ", " + y.ToString("0.####") + ")。");
                    continue;
                }
                if (x + y > 1.0 + 1e-9)
                {
                    result.Warnings.Add("第 " + rowNo + " 行 x+y>1，位于色域三角形之外（仍按给定坐标处理）。");
                }

                CiePoint pt = new CiePoint(name, x, y);
                Colorimetry.FillPointDerived(pt);
                result.Points.Add(pt);
            }

            if (result.Points.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add(skippedHeader ? "表头之后没有数据行。" : "未解析到任何色坐标。");
            return result;
        }

        /// <summary>
        /// 在多列数据里挑出 x、y 两列：优先选"所有行都在 0–1 且 x+y≤1.05"的列对；
        /// 都没有时退回数值最像坐标的那一对（取值全在 0–1）。
        /// </summary>
        private static bool PickColumns(List<string[]> rows, int start, bool uvMode, out int colX, out int colY)
        {
            int width = 0;
            for (int i = start; i < rows.Count; i++) width = Math.Max(width, rows[i].Length);
            colX = -1;
            colY = -1;
            if (width < 2) return false;

            // uv 输入一般就是最后两列
            if (uvMode)
            {
                colX = width - 2;
                colY = width - 1;
                return true;
            }

            double bestScore = -1;
            for (int i = 0; i < width - 1; i++)
            {
                for (int j = i + 1; j < width; j++)
                {
                    int ok = 0, total = 0;
                    double minSum = 0, maxSum = 0;
                    for (int r = start; r < rows.Count; r++)
                    {
                        if (j >= rows[r].Length) continue;
                        double a, b;
                        if (!TryNum(rows[r][i], out a) || !TryNum(rows[r][j], out b)) continue;
                        total++;
                        if (a >= 0 && a <= 1 && b >= 0 && b <= 1)
                        {
                            ok++;
                            double sum = a + b;
                            minSum = Math.Min(minSum, sum);
                            maxSum = Math.Max(maxSum, sum);
                        }
                    }
                    if (total == 0) continue;
                    double rate = (double)ok / total;
                    // 关键：x+y 必须基本 ≤ 1（色度三角形内部），否则多半是把波长/强度当成了 x,y
                    if (maxSum > 1.05 + 1e-9) rate *= 0.2;
                    // 同等条件下优先靠左、相邻的列对
                    double score = rate * 1000 - i * 2 - (j - i);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        colX = i;
                        colY = j;
                    }
                }
            }
            return colX >= 0 && bestScore > 0;
        }
    }
}
