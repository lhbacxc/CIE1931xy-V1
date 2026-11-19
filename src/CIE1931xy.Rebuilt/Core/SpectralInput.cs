using System;
using System.Collections.Generic;

namespace CIE1931xy.Core;

/// <summary>解析结果的分类（界面据此决定弹哪条提示）。</summary>
internal enum ParseStatus
{
    Ok = 0,
    /// <summary>输入为空（去掉尾部换行后长度 0）。</summary>
    Empty,
    /// <summary>出现非法字符。</summary>
    IllegalCharacter,
    /// <summary>某一行无法拆成"波长 功率"两列。</summary>
    RowFormatError
}

/// <summary>解析失败时的细节。</summary>
internal sealed class ParseFailure
{
    internal ParseStatus Status;
    /// <summary>非法字符（仅 <see cref="ParseStatus.IllegalCharacter"/> 有效）。</summary>
    internal char Character;
    /// <summary>非法字符的码点（仅 <see cref="ParseStatus.IllegalCharacter"/> 有效）。</summary>
    internal int CharacterCode;

    internal static ParseFailure Of(ParseStatus status) => new ParseFailure { Status = status };
}

/// <summary>
/// 原版 <c>btnCalc_Click</c> 前半段的文本解析：把文本框里"每行 波长 功率"的文本
/// （制表符/空格/逗号都可作分隔符）解析成 [472,2] 的光谱数据数组。
/// 保留了原版的全部边界行为，包括几个已知的怪癖：
/// <list type="bullet">
/// <item>数据末尾的成对 CRLF 会被全部去掉；</item>
/// <item>首个字符不是数字时，会从第一个"换行后紧跟数字"的位置开始截取（自动跳过表头）；</item>
/// <item>最后一行没有换行符时仍会被解析；</item>
/// <item>原版用 <c>num2 = 1</c> 作循环条件（<c>1 &lt; 长度</c>），所以只剩 1 个字符的尾巴会被忽略。</item>
/// </list>
/// 相对原版做了两处安全性改进（正常输入下结果完全一致）：<br/>
/// 1) 输入只剩一个 <c>"\n"</c> 时原版会因 <c>Mid(text, 0, 1)</c> 抛异常崩溃，这里按"空输入"处理；<br/>
/// 2) 数字解析失败（例如 <c>1.2.3</c>）原版会抛未捕获的 FormatException，这里按"格式错误"提示。
/// </summary>
internal static class SpectralInput
{
    /// <summary>原版数据数组的容量：471 行 + 1 行余量。</summary>
    internal const int MaxRows = 472;

    /// <summary>
    /// 解析输入文本。
    /// </summary>
    /// <param name="input">文本框内容。</param>
    /// <param name="pdata">解析出的 [472,2] 数组（失败时也返回已解析部分，便于排查）。</param>
    /// <param name="dataRow">原版 <c>Data_Row</c>：最后一行数据的下标。</param>
    /// <param name="failure">失败细节，成功时为 <c>null</c>。</param>
    internal static bool TryParse(string input, out double[,] pdata, out int dataRow, out ParseFailure failure)
    {
        pdata = new double[MaxRows, 2];
        dataRow = 0;
        failure = null;

        string text = input ?? string.Empty;
        text = text.Replace("\t", " ");
        text = text.Replace(" ", " ");
        text = text.Replace(",", " ");

        // 去掉末尾成对的 CRLF（原版循环：去掉 len-1 处的 '\n' 与 len-2 处的 '\r'）
        while (text.Length >= 2 && text[text.Length - 1] == '\n' && text[text.Length - 2] == '\r')
            text = text.Substring(0, text.Length - 2);

        int len = text.Length;
        if (len == 0)
        {
            failure = ParseFailure.Of(ParseStatus.Empty);
            return false;
        }

        // 首字符不是数字 → 从第一个"换行后紧跟数字"的位置开始（跳过表头）
        if (!IsDigit(text[0]))
        {
            for (int i = 0; i < len; i++)
            {
                if (text[i] == '\n' && i + 1 < len && IsDigit(text[i + 1]))
                {
                    text = text.Substring(i + 1);
                    len = text.Length;
                    break;
                }
            }
        }

        // 字符白名单：数字 . 空格 制表符 换行 回车 E e
        for (int i = 0; i < len; i++)
        {
            char c = text[i];
            if (!(IsDigit(c) || c == '.' || c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == 'E' || c == 'e'))
            {
                failure = new ParseFailure
                {
                    Status = ParseStatus.IllegalCharacter,
                    Character = c,
                    CharacterCode = c
                };
                return false;
            }
        }

        int num10 = 0;
        // 原版：num2 恒为 1，条件 num2 < num → 等价于 text.Length > 1
        while (text.Length > 1)
        {
            int sp = text.IndexOf(' ');
            int crlf = text.IndexOf("\r\n", StringComparison.Ordinal);

            if (sp > 0 && crlf > 0)
            {
                if (!TryReadRow(text, sp, crlf, out double lambda, out double power))
                {
                    failure = ParseFailure.Of(ParseStatus.RowFormatError);
                    return false;
                }
                pdata[num10, 0] = lambda;
                pdata[num10, 1] = power;
                text = text.Substring(crlf + 2);
                num10++;
                continue;
            }

            if (sp > 0 && crlf == -1)
            {
                if (!TryReadRowLast(text, sp, out double lambda, out double power))
                {
                    failure = ParseFailure.Of(ParseStatus.RowFormatError);
                    return false;
                }
                pdata[num10, 0] = lambda;
                pdata[num10, 1] = power;
                break;
            }

            if (sp == -1 && crlf > 0)
            {
                failure = ParseFailure.Of(ParseStatus.RowFormatError);
                return false;
            }

            // 原版此处 continue 会死循环（例如文本以空格开头）；这里直接判为格式错误。
            failure = ParseFailure.Of(ParseStatus.RowFormatError);
            return false;
        }

        dataRow = num10;
        return true;
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static bool TryReadRow(string text, int sp, int crlf, out double lambda, out double power)
    {
        lambda = 0.0;
        power = 0.0;
        try
        {
            lambda = VbText.ToDouble(VbText.Mid(text, 1, sp));
            power = VbText.ToDouble(VbText.Mid(text, 1 + sp + 1, crlf - sp - 1));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadRowLast(string text, int sp, out double lambda, out double power)
    {
        lambda = 0.0;
        power = 0.0;
        try
        {
            lambda = VbText.ToDouble(VbText.Mid(text, 1, sp));
            power = VbText.ToDouble(VbText.Mid(text, 1 + sp + 1, text.Length - sp - 1));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
