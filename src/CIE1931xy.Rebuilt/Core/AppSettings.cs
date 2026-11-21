using System;
using System.IO;
using CIE1931xy.Core.Rendering;

namespace CIE1931xy.Core
{
    /// <summary>新版工作台的持久化偏好；不包含数据、临时选择或渲染缓存。</summary>
    internal sealed class AppSettings
    {
        public int Version { get; set; } = 1;
        public DiagramKind DiagramKind { get; set; } = DiagramKind.Xy1931;
        public bool BlackBackground { get; set; }
        public bool ShowBlackbody { get; set; } = true;
        public bool ShowGrid { get; set; } = true;
        public bool ShowReferenceLine { get; set; }
        public bool ShowWavelengthLabels { get; set; }
        public bool ShowLegend { get; set; }
        public bool ShowPointLabels { get; set; }
        public string FontFamily { get; set; } = "Times New Roman";
        public string ChineseFontFamily { get; set; } = "SimSun";
        public double AxisFontSize { get; set; } = 26;
        public double TickFontSize { get; set; } = 22;
        public double WavelengthFontSize { get; set; } = 20;
        public double LabelFontSize { get; set; } = 20;
        public double LegendFontSize { get; set; } = 20;
        public double CctFontSize { get; set; } = 17;
        public string XyAxisTitleX { get; set; } = "";
        public string XyAxisTitleY { get; set; } = "";
        public string UvAxisTitleX { get; set; } = "";
        public string UvAxisTitleY { get; set; } = "";
        public double AxisTitleGapX { get; set; } = 16;
        public double AxisTitleGapY { get; set; } = 16;
        public int ExportDpi { get; set; } = 300;
        public string LastExportName { get; set; } = "CIE1931色度图.png";

        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(DiagramKind), DiagramKind)) DiagramKind = DiagramKind.Xy1931;
            FontFamily = Text(FontFamily, "Times New Roman", 128);
            ChineseFontFamily = Text(ChineseFontFamily, "SimSun", 128);
            AxisFontSize = Range(AxisFontSize, 8, 64, 26);
            TickFontSize = Range(TickFontSize, 8, 64, 22);
            WavelengthFontSize = Range(WavelengthFontSize, 8, 64, 20);
            LabelFontSize = Range(LabelFontSize, 8, 64, 20);
            LegendFontSize = Range(LegendFontSize, 8, 64, 20);
            CctFontSize = Range(CctFontSize, 8, 64, 17);
            XyAxisTitleX = Text(XyAxisTitleX, "", 80);
            XyAxisTitleY = Text(XyAxisTitleY, "", 80);
            UvAxisTitleX = Text(UvAxisTitleX, "", 80);
            UvAxisTitleY = Text(UvAxisTitleY, "", 80);
            AxisTitleGapX = Range(AxisTitleGapX, 0, 80, 16);
            AxisTitleGapY = Range(AxisTitleGapY, 0, 80, 16);
            if (ExportDpi < 72 || ExportDpi > 1200) ExportDpi = 300;
            LastExportName = Text(LastExportName, "CIE1931色度图.png", 240);
            if (LastExportName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                LastExportName = "CIE1931色度图.png";
        }

        private static double Range(double value, double min, double max, double fallback)
        {
            return double.IsFinite(value) && value >= min && value <= max ? value : fallback;
        }

        private static string Text(string value, string fallback, int length)
        {
            value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return value.Length <= length ? value : value.Substring(0, length);
        }

        public void ApplyTo(DiagramStyle style)
        {
            style.Kind = DiagramKind;
            style.SetBlackBackground(BlackBackground);
            style.ShowBlackbody = ShowBlackbody;
            style.ShowGrid = ShowGrid;
            style.ShowClosingLine = ShowReferenceLine;
            style.ShowWavelengthLabels = ShowWavelengthLabels;
            style.ShowLegend = ShowLegend;
            style.ShowPointLabels = ShowPointLabels;
            style.FontFamily = FontFamily;
            style.ChineseFontFamily = ChineseFontFamily;
            style.AxisFontSize = AxisFontSize;
            style.TickFontSize = TickFontSize;
            style.WavelengthFontSize = WavelengthFontSize;
            style.LabelFontSize = LabelFontSize;
            style.LegendFontSize = LegendFontSize;
            style.CctFontSize = CctFontSize;
            style.XyAxisTitleX = XyAxisTitleX;
            style.XyAxisTitleY = XyAxisTitleY;
            style.UvAxisTitleX = UvAxisTitleX;
            style.UvAxisTitleY = UvAxisTitleY;
            style.AxisTitleGapX = AxisTitleGapX;
            style.AxisTitleGapY = AxisTitleGapY;
        }

        public static AppSettings Capture(DiagramStyle style, int dpi, string exportName)
        {
            return new AppSettings
            {
                DiagramKind = style.Kind, BlackBackground = style.BlackBackground,
                ShowBlackbody = style.ShowBlackbody, ShowGrid = style.ShowGrid,
                ShowReferenceLine = style.ShowClosingLine, ShowWavelengthLabels = style.ShowWavelengthLabels,
                ShowLegend = style.ShowLegend, ShowPointLabels = style.ShowPointLabels,
                FontFamily = style.FontFamily, ChineseFontFamily = style.ChineseFontFamily,
                AxisFontSize = style.AxisFontSize, TickFontSize = style.TickFontSize,
                WavelengthFontSize = style.WavelengthFontSize, LabelFontSize = style.LabelFontSize,
                LegendFontSize = style.LegendFontSize, CctFontSize = style.CctFontSize,
                XyAxisTitleX = style.XyAxisTitleX, XyAxisTitleY = style.XyAxisTitleY,
                UvAxisTitleX = style.UvAxisTitleX, UvAxisTitleY = style.UvAxisTitleY,
                AxisTitleGapX = style.AxisTitleGapX, AxisTitleGapY = style.AxisTitleGapY,
                ExportDpi = dpi, LastExportName = exportName
            };
        }
    }
}
