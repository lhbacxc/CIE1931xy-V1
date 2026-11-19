using System;
using System.Collections.Generic;
using System.Drawing;

namespace CIE1931xy.Core
{
    /// <summary>数据点标记形状（CIEdraw 支持圆形/方形/三角/菱形/叉/星）。</summary>
    internal enum MarkerShape
    {
        Circle = 0,
        Square = 1,
        Triangle = 2,
        Diamond = 3,
        Cross = 4,
        Star = 5
    }

    /// <summary>一条光谱曲线的计算结果（色度学量，均为原版口径）。</summary>
    internal sealed class CurveResult
    {
        public double X;
        public double Y;
        public double Z;
        public double x;
        public double y;
        public double u;      // CIE 1976 u'
        public double v;      // CIE 1976 v'
        public double PeakWavelength;
        public double PeakValue;
        public int Count;
        public double WlMin;
        public double WlMax;
        public double Step;
        public double Cct = double.NaN;      // 相关色温（K），NaN = 未算出
        public double Duv = double.NaN;      // 到黑体轨迹的距离（CIE 1960 uv）
        public bool CctReliable;

        public bool HasCct { get { return !double.IsNaN(Cct); } }
    }

    /// <summary>一条光谱曲线：波长、强度、显示属性与结果缓存。</summary>
    internal sealed class CieCurve
    {
        public string Name;
        public double[] Wl;
        public double[] Val;
        public Color Color = Color.FromArgb(31, 119, 180);
        public Color OutlineColor = Color.Black;
        public MarkerShape Shape = MarkerShape.Circle;
        public bool ShowLabel;
        public bool Selected;                 // 表格里选中时高亮（导出不计）          // 图上是否显示名称标签（默认关）
        public bool Valid = true;       // 计算是否成功
        public string Message = "";     // 失败原因（计算失败时）
        public CurveResult Result;

        public CieCurve()
        {
        }

        public CieCurve(string name, double[] wl, double[] val)
        {
            Name = name;
            Wl = wl;
            Val = val;
        }

        public int Count { get { return Wl == null ? 0 : Wl.Length; } }
    }

    /// <summary>一个色坐标点（CIEdraw 的“色坐标点”）。</summary>
    internal sealed class CiePoint
    {
        public string Name;
        public double x;
        public double y;
        public Color Color = Color.FromArgb(214, 39, 40);
        public Color OutlineColor = Color.Black;
        public MarkerShape Shape = MarkerShape.Circle;
        public bool ShowLabel;
        public bool Selected;                 // 表格里选中时高亮（导出不计）
        public double Cct = double.NaN;
        public double Duv = double.NaN;
        public bool CctReliable;

        public CiePoint()
        {
        }

        public CiePoint(string name, double x, double y)
        {
            Name = name;
            this.x = x;
            this.y = y;
        }

        /// <summary>CIE 1976 u'。</summary>
        public double U
        {
            get
            {
                double u, v;
                Colorimetry.XyToUv(x, y, out u, out v);
                return u;
            }
        }

        /// <summary>CIE 1976 v'。</summary>
        public double V
        {
            get
            {
                double u, v;
                Colorimetry.XyToUv(x, y, out u, out v);
                return v;
            }
        }
    }

    /// <summary>解析结果：成功项 + 警告 + 错误（错误文本直接给用户看，带行号）。</summary>
    internal sealed class ParseResult
    {
        public readonly List<CieCurve> Curves = new List<CieCurve>();
        public readonly List<CiePoint> Points = new List<CiePoint>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool Ok { get { return Errors.Count == 0; } }
    }
}
