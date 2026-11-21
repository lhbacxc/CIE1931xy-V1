using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace CIE1931xy.Core.Rendering
{
    /// <summary>中英文混排的字体分段、宽度与公共基线，供 GDI 和 SVG 共用。</summary>
    internal sealed class PlotTextLayout : IDisposable
    {
        internal sealed class Run
        {
            public string Text;
            public string Family;
            public Font Font;
            public double Left;
            public double Width;
            public double Ascent;
        }

        public readonly List<Run> Runs = new List<Run>();
        public double Width { get; private set; }
        public double Height { get; private set; }
        public double Ascent { get; private set; }

        private PlotTextLayout() { }

        public static PlotTextLayout Create(Graphics graphics, string text, string latinFamily,
                                            string chineseFamily, double sizePixels, bool bold)
        {
            PlotTextLayout layout = new PlotTextLayout();
            if (string.IsNullOrEmpty(text)) return layout;
            if (string.IsNullOrWhiteSpace(latinFamily)) latinFamily = "Times New Roman";
            if (string.IsNullOrWhiteSpace(chineseFamily)) chineseFamily = latinFamily;
            StringBuilder part = new StringBuilder();
            bool chinese = false;
            try
            {
                foreach (Rune rune in text.EnumerateRunes())
                {
                    UnicodeCategory category = Rune.GetUnicodeCategory(rune);
                    bool inherits = category == UnicodeCategory.NonSpacingMark ||
                        category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.EnclosingMark;
                    bool nextChinese = inherits && part.Length > 0 ? chinese : IsChinese(rune.Value);
                    if (part.Length > 0 && chinese != nextChinese)
                    {
                        layout.AddRun(graphics, part.ToString(), chinese ? chineseFamily : latinFamily, sizePixels, bold);
                        part.Clear();
                    }
                    chinese = nextChinese;
                    part.Append(rune.ToString());
                }
                if (part.Length > 0)
                    layout.AddRun(graphics, part.ToString(), chinese ? chineseFamily : latinFamily, sizePixels, bold);
                double descent = 0;
                foreach (Run run in layout.Runs)
                {
                    FontStyle style = run.Font.Style;
                    double em = run.Font.FontFamily.GetEmHeight(style);
                    descent = Math.Max(descent, run.Font.Size * run.Font.FontFamily.GetCellDescent(style) / em);
                }
                layout.Height = Math.Max(layout.Height, layout.Ascent + descent);
                return layout;
            }
            catch
            {
                layout.Dispose();
                throw;
            }
        }

        private static bool IsChinese(int codePoint)
        {
            return (codePoint >= 0x2E80 && codePoint <= 0xA4CF) ||
                   (codePoint >= 0xF900 && codePoint <= 0xFAFF) ||
                   (codePoint >= 0xFE30 && codePoint <= 0xFE4F) ||
                   (codePoint >= 0xFF01 && codePoint <= 0xFF60) ||
                   (codePoint >= 0xFFE0 && codePoint <= 0xFFE6) ||
                   (codePoint >= 0x20000 && codePoint <= 0x323AF);
        }

        private void AddRun(Graphics graphics, string text, string family, double sizePixels, bool bold)
        {
            Font font;
            try { font = new Font(family, (float)Math.Max(1, sizePixels), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
            catch (ArgumentException)
            {
                font = new Font(FontFamily.GenericSerif, (float)Math.Max(1, sizePixels), FontStyle.Regular, GraphicsUnit.Pixel);
            }
            Run run = new Run { Text = text, Family = family, Font = font, Left = Width };
            Runs.Add(run);
            using (StringFormat format = CreateFormat())
            {
                SizeF size = graphics.MeasureString(text, font, PointF.Empty, format);
                run.Width = size.Width;
                run.Ascent = font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);
                Width += run.Width;
                Height = Math.Max(Height, size.Height);
                Ascent = Math.Max(Ascent, run.Ascent);
            }
        }

        public static StringFormat CreateFormat()
        {
            StringFormat format = new StringFormat(StringFormat.GenericTypographic);
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        public double LeftOffset(TextAlignH align)
        {
            return align == TextAlignH.Right ? -Width : align == TextAlignH.Center ? -Width / 2 : 0;
        }

        public double BaselineOffset(TextAlignV align)
        {
            return Ascent - (align == TextAlignV.Bottom ? Height : align == TextAlignV.Middle ? Height / 2 : 0);
        }

        public void Dispose()
        {
            foreach (Run run in Runs) run.Font.Dispose();
        }
    }
}
