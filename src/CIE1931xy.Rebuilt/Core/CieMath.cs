using System;

namespace CIE1931xy.Core;

/// <summary>数据点间隔（对应界面上的两个单选按钮）。</summary>
internal enum DataSpacing
{
    /// <summary>未选择（原版此时弹出"请选择数据格式"提示）。</summary>
    None = 0,

    /// <summary>每个数据点间隔 1 nm。</summary>
    OneNm = 1,

    /// <summary>每个数据点间隔 5 nm。</summary>
    FiveNm = 5
}

/// <summary>
/// 原版 <c>btnCalc_Click</c> 的全部数值计算逻辑：按波长偏移量对齐 CIE 1931 表、
/// 对光谱功率分布做积分得到三刺激值，再换算色品坐标、UCS 1976 u'v' 与 RGB，
/// 并按原版的"4 位小数、看第 5 位、满 5 进位"规则取整。
/// 纯计算，无任何界面依赖，便于单元测试。
/// </summary>
internal static class CieMath
{
    /// <summary>三刺激值积分结果与派生量。</summary>
    internal sealed class Result
    {
        internal double TSV_X;
        internal double TSV_Y;
        internal double TSV_Z;
        internal double CC_x;
        internal double CC_y;
        internal double UCS_up;
        internal double UCS_vp;
        internal double RGB_r;
        internal double RGB_g;
        internal double RGB_b;
        internal int PeakLambda;
        internal double PeakInt;
        /// <summary>光谱数据首行波长在 CIE 表里的行号（360 nm 起算）。</summary>
        internal int Offset;
    }

    /// <summary>计算失败的类别（界面据此选择提示文字）。</summary>
    internal enum Status
    {
        Ok = 0,
        /// <summary>两个单选按钮都没选中。</summary>
        NoFormatSelected,
        /// <summary>相邻数据点间隔与所选格式不符。</summary>
        SpacingMismatch
    }

    /// <summary>
    /// 执行原版 <c>btnCalc_Click</c> 里的峰值查找 + 积分 + 换算。
    /// </summary>
    /// <param name="pdata">光谱数据 [472,2]，第 0 列波长、第 1 列功率。</param>
    /// <param name="dataRow">原版 <c>Data_Row</c>：最后一行数据的下标（可能比实际行数大 1，见解析器说明）。</param>
    /// <param name="spacing">数据点间隔。</param>
    internal static Status Calculate(double[,] pdata, int dataRow, DataSpacing spacing, out Result result)
    {
        result = new Result();
        double tsvX = 0.0, tsvY = 0.0, tsvZ = 0.0;
        double[,] cie = CieData.Cie1931xyz;

        // ---- 峰值（峰值波长 & 峰值强度）：原版从第 0 行开始比较 ----
        int peakLambda = (int)Math.Round(pdata[0, 0]);
        double peakInt = pdata[0, 1];
        int lastRow = (int)Math.Round((double)dataRow);
        for (int i = 0; i <= lastRow; i++)
        {
            if (pdata[i, 1] > peakInt)
            {
                peakInt = pdata[i, 1];
                peakLambda = (int)Math.Round(pdata[i, 0]);
            }
        }

        int offset = 0;
        int limit;
        if (spacing == DataSpacing.FiveNm)
        {
            for (int i = 0; i <= dataRow - 1; i++)
            {
                if ((int)Math.Round(pdata[i + 1, 0] - pdata[i, 0]) != 5)
                    return Status.SpacingMismatch;
            }

            offset = FindOffset5nm(pdata, dataRow);

            if (pdata[0, 0] >= cie[0, 0])
            {
                limit = (int)Math.Round(Math.Min((double)dataRow, (double)(470 - offset) / 5.0));
                for (int i = 0; i <= limit; i++)
                {
                    tsvX += pdata[i, 1] * cie[i * 5 + offset, 1];
                    tsvY += pdata[i, 1] * cie[i * 5 + offset, 2];
                    tsvZ += pdata[i, 1] * cie[i * 5 + offset, 3];
                }
            }
            else
            {
                limit = (int)Math.Round(Math.Min(94.0, dataRow - (double)offset));
                for (int i = 0; i <= limit; i++)
                {
                    tsvX += pdata[i + offset, 1] * cie[i * 5, 1];
                    tsvY += pdata[i + offset, 1] * cie[i * 5, 2];
                    tsvZ += pdata[i + offset, 1] * cie[i * 5, 3];
                }
            }
        }
        else if (spacing == DataSpacing.OneNm)
        {
            for (int i = 0; i <= dataRow - 1; i++)
            {
                if ((int)Math.Round(pdata[i + 1, 0] - pdata[i, 0]) != 1)
                    return Status.SpacingMismatch;
            }

            offset = FindOffset1nm(pdata, dataRow);

            if (pdata[0, 0] >= cie[0, 0])
            {
                limit = (int)Math.Round(Math.Min((double)dataRow, (double)(470 - offset)));
                for (int i = 0; i <= limit; i++)
                {
                    tsvX += pdata[i, 1] * cie[i + offset, 1];
                    tsvY += pdata[i, 1] * cie[i + offset, 2];
                    tsvZ += pdata[i, 1] * cie[i + offset, 3];
                }
            }
            else
            {
                limit = (int)Math.Round(Math.Min(470.0, dataRow - (double)offset));
                for (int i = 0; i <= limit; i++)
                {
                    tsvX += pdata[i + offset, 1] * cie[i, 1];
                    tsvY += pdata[i + offset, 1] * cie[i, 2];
                    tsvZ += pdata[i + offset, 1] * cie[i, 3];
                }
            }
        }
        else
        {
            return Status.NoFormatSelected;
        }

        // ---- 色品坐标 ----
        double sum = tsvX + tsvY + tsvZ;
        double ccx = tsvX / sum;
        double ccy = tsvY / sum;

        // ---- UCS 1976 u'v' ----
        double udenom = tsvX + 15.0 * tsvY + 3.0 * tsvZ;
        double up = 4.0 * tsvX / udenom;
        double vp = 9.0 * tsvY / udenom;

        // ---- 原版的 4 位小数取整：看第 5 位，满 5 进 1 ----
        ccx = Round4(ccx);
        ccy = Round4(ccy);
        up = Round4(up);
        vp = Round4(vp);

        // ---- RGB（MacAdam 由 xy 反算 RGB 的近似式，原版逐字保留）----
        double rr = 1.19 / (0.16634 + 0.53366 * ccy) + 0.87 * (1.19 * (0.29 + 0.53366 * ccx) + (0.2 - 1.20063 * ccx) * (0.16634 + 0.53366 * ccy)) / ((0.16634 + 0.53366 * ccy) * (-0.234003 - 0.452935 * ccx + 0.0587026 * ccy + 0.0364116 * ccx * ccy));
        double rg = (1.19 * (0.29 + 0.53366 * ccx) + (0.2 - 1.20063 * ccx) * (0.16634 + 0.53366 * ccy)) / (0.234003 + 0.452935 * ccx - 0.0587026 * ccy - 0.0364116 * ccx * ccy);
        double rb = 1.0 - rr - rg;
        rr = Round4(rr);
        rg = Round4(rg);
        rb = Round4(rb);

        result.TSV_X = tsvX;
        result.TSV_Y = tsvY;
        result.TSV_Z = tsvZ;
        result.CC_x = ccx;
        result.CC_y = ccy;
        result.UCS_up = up;
        result.UCS_vp = vp;
        result.RGB_r = rr;
        result.RGB_g = rg;
        result.RGB_b = rb;
        result.PeakLambda = peakLambda;
        result.PeakInt = peakInt;
        result.Offset = offset;
        return Status.Ok;
    }

    /// <summary>
    /// 原版的取整技巧：<c>Conversion.Int(v * 100000) % 10 &gt;= 5</c> 时进 1，否则截断，
    /// 最终保留 4 位小数（<see cref="double.NaN"/> 会原样传回）。
    /// </summary>
    internal static double Round4(double v)
    {
        if (Math.Floor(v * 100000.0) % 10.0 >= 5.0)
            return Math.Truncate(v * 10000.0 + 1.0) / 10000.0;
        return Math.Truncate(v * 10000.0) / 10000.0;
    }

    private static int FindOffset5nm(double[,] pdata, int dataRow)
    {
        double[,] cie = CieData.Cie1931xyz;
        int offset = 0;
        if (pdata[0, 0] >= cie[0, 0])
        {
            for (int i = 0; i <= 470; i++)
            {
                if (pdata[0, 0] == cie[i, 0])
                {
                    offset = i;
                    break;
                }
            }
        }
        else
        {
            int lastRow = (int)Math.Round((double)dataRow);
            for (int i = 0; i <= lastRow; i++)
            {
                if (pdata[i, 0] == cie[0, 0])
                {
                    offset = i;
                    break;
                }
            }
        }
        return offset;
    }

    private static int FindOffset1nm(double[,] pdata, int dataRow)
    {
        // 1 nm 与 5 nm 的偏移量查找逻辑在原版里完全相同。
        return FindOffset5nm(pdata, dataRow);
    }
}
