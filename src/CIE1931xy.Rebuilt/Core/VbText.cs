using System;
using System.Globalization;

namespace CIE1931xy.Core;

/// <summary>
/// VB.NET 运行库（Microsoft.VisualBasic）里被原版程序用到的那几个函数的最小等价实现。
/// 原版由 VB 编译器生成的代码全部依赖这些函数；这里改成纯 C# 实现，去掉对
/// Microsoft.VisualBasic 程序集的依赖，同时保持相同的语义，便于后续维护。
/// </summary>
internal static class VbText
{
    /// <summary>VB 的 <c>Len()</c>：字符数（不是字节数）。</summary>
    internal static int Len(string s) => s == null ? 0 : s.Length;

    /// <summary>
    /// VB 的 <c>Mid(s, Start, Length)</c>：1 起始下标；<paramref name="start"/> 超出字符串时返回空串，
    /// 不足 <paramref name="length"/> 时截断到末尾。<c>Start &lt; 1</c> 或 <c>Length &lt; 0</c> 抛异常（与 VB 一致）。
    /// </summary>
    internal static string Mid(string s, int start, int length)
    {
        if (start < 1)
            throw new ArgumentException("start 不能小于 1", nameof(start));
        if (length < 0)
            throw new ArgumentException("length 不能小于 0", nameof(length));
        if (s == null || start > s.Length)
            return string.Empty;
        int count = Math.Min(length, s.Length - start + 1);
        return count <= 0 ? string.Empty : s.Substring(start - 1, count);
    }

    /// <summary>
    /// 原版用 VB 的 <c>Asc()</c> 做字符校验（返回第一个字符的编码）。
    /// 这里直接返回 Unicode 码点：对 ASCII 输入与原版完全一致，对中文等非 ASCII 输入
    /// 原版会抛异常，这里则安全地返回码点（更好，也更符合"输入非法字符"提示的本意）。
    /// </summary>
    internal static int Asc(string s)
    {
        if (string.IsNullOrEmpty(s))
            throw new ArgumentException("字符串为空", nameof(s));
        return s[0];
    }

    /// <summary>
    /// 原版把数值直接写进文本框用的 <c>Double.ToString()</c>。
    /// .NET Framework（原版目标）默认只保留 15 位有效数字（等价于 "G15"），
    /// 而 .NET Core 3.0 以后默认输出"能最短往返"的 17 位数字，两者显示结果不同。
    /// 为了和原版界面显示的字符串逐字一致，这里显式使用 "G15"。
    /// </summary>
    internal static string FormatDouble(double v) => v.ToString("G15", CultureInfo.CurrentCulture);

    /// <summary>VB 的 <c>Conversions.ToDouble()</c>：按当前区域设置解析，失败抛 <see cref="FormatException"/>。</summary>
    internal static double ToDouble(string s)
    {
        return double.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture);
    }

    /// <summary>VB 的 <c>Conversion.Int()</c>：向负无穷取整（= Math.Floor）。</summary>
    internal static double Int(double v) => Math.Floor(v);

    /// <summary>VB 的 <c>Conversion.Fix()</c>：向零取整（= Math.Truncate）。</summary>
    internal static double Fix(double v) => Math.Truncate(v);

    /// <summary>
    /// 原版用 VB 的 <c>Utils.CopyArray(src, New T(n) {})</c> 扩容数组：先按较小长度复制，再返回新数组。
    /// </summary>
    internal static T[] Grow<T>(T[] source, int newLength)
    {
        T[] destination = new T[newLength];
        Array.Copy(source, destination, Math.Min(source.Length, newLength));
        return destination;
    }
}
