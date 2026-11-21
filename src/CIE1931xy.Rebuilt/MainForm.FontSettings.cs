using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using CIE1931xy.Core.Rendering;

namespace CIE1931xy
{
    internal sealed partial class MainForm
    {
        private sealed class FontChoice
        {
            public readonly string Name;
            public FontChoice(string name) { Name = name; }
            public override string ToString() { return Name == "SimSun" ? "宋体 (SimSun)" : Name; }
        }

        private static ComboBox MakeFontCombo(string name)
        {
            ComboBox combo = MakeCombo("");
            combo.Items.Clear();
            combo.MaxDropDownItems = 16;
            combo.DropDownWidth = 320;
            SortedSet<string> names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            using (InstalledFontCollection fonts = new InstalledFontCollection())
                foreach (FontFamily font in fonts.Families)
                    names.Add(font.Name == "宋体" ? "SimSun" : font.Name);
            names.Add("Times New Roman");
            names.Add("SimSun");
            names.Add(name);
            foreach (string fontName in names) combo.Items.Add(new FontChoice(fontName));
            SelectFont(combo, name);
            return combo;
        }

        private static void SelectFont(ComboBox combo, string name)
        {
            foreach (FontChoice choice in combo.Items)
                if (string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = choice;
                    return;
                }
        }

        private static double[] FontSizes(DiagramStyle style)
        {
            return new[] { style.AxisFontSize, style.TickFontSize, style.WavelengthFontSize,
                style.LabelFontSize, style.LegendFontSize, style.CctFontSize };
        }

        private void EditFonts()
        {
            using (Form dlg = CreateDialog("字体与字号", new Size(540, 540)))
            {
                dlg.MinimumSize = new Size(440, 420);
                Label tip = new Label();
                tip.Dock = DockStyle.Top;
                tip.Padding = new Padding(12, 8, 12, 8);
                tip.Text = "数字和字母默认 Times New Roman，汉字和中文标点默认宋体。可分别选择字体及文字大小，xy/u'v' 预览和图片导出同步。";
                tip.SizeChanged += delegate { FitWrappedLabel(tip); };
                tip.FontChanged += delegate { FitWrappedLabel(tip); };
                tip.DpiChangedAfterParent += delegate { FitWrappedLabel(tip); };
                dlg.Controls.Add(tip);

                FlowLayoutPanel fields = new FlowLayoutPanel();
                fields.Dock = DockStyle.Fill;
                fields.FlowDirection = FlowDirection.TopDown;
                fields.WrapContents = false;
                fields.AutoScroll = true;
                fields.Padding = new Padding(12, 4, 12, 4);
                fields.Layout += delegate { FitStyleRows(fields); };
                ComboBox latin = MakeFontCombo(_style.FontFamily);
                latin.Name = "LatinFont";
                ComboBox chinese = MakeFontCombo(_style.ChineseFontFamily);
                chinese.Name = "ChineseFont";
                fields.Controls.Add(MakeRow("数字/字母字体", latin));
                fields.Controls.Add(MakeRow("中文字体", chinese));

                string[] labels = { "坐标轴标题大小", "刻度数字大小", "波长标注大小", "数据标签大小", "图例文字大小", "黑体温度大小" };
                string[] keys = { "AxisFontSize", "TickFontSize", "WavelengthFontSize", "LabelFontSize", "LegendFontSize", "CctFontSize" };
                double[] values = FontSizes(_style);
                NumericUpDown[] sizes = new NumericUpDown[values.Length];
                for (int i = 0; i < sizes.Length; i++)
                {
                    sizes[i] = MakeNumber(8, 64, (int)Math.Max(8, Math.Min(64, values[i])));
                    sizes[i].Name = keys[i];
                    fields.Controls.Add(MakeRow(labels[i], sizes[i]));
                }

                Label sizeNote = new Label();
                sizeNote.AutoSize = true;
                sizeNote.ForeColor = SystemColors.GrayText;
                sizeNote.Text = "文字大小按画布比例显示；较长的标题会自动适应可用空间。";
                fields.Controls.Add(sizeNote);
                Panel preview = new Panel();
                preview.Name = "FontPreview";
                preview.Height = 66;
                preview.MinimumSize = new Size(0, 66);
                preview.BackColor = Color.White;
                preview.Paint += delegate (object sender, PaintEventArgs e)
                {
                    if (latin.SelectedItem == null || chinese.SelectedItem == null) return;
                    using (GdiCanvas canvas = new GdiCanvas(e.Graphics, 1))
                    {
                        canvas.ChineseFontFamily = ((FontChoice)chinese.SelectedItem).Name;
                        string text = "样品 Sample 123 · λ=550 nm";
                        string family = ((FontChoice)latin.SelectedItem).Name;
                        double size = 20 * preview.DeviceDpi / 96.0;
                        SizeD measured = canvas.MeasureText(text, family, size, false);
                        size *= Math.Min(1, Math.Max(1, preview.Width - 24) / Math.Max(1, measured.Width));
                        canvas.DrawText(text, Color.Black, family, size, false,
                            preview.Width / 2.0, preview.Height / 2.0, TextAlignH.Center, TextAlignV.Middle, 0);
                    }
                };
                latin.SelectedIndexChanged += delegate { preview.Invalidate(); };
                chinese.SelectedIndexChanged += delegate { preview.Invalidate(); };
                fields.Controls.Add(preview);
                dlg.Controls.Add(fields);
                fields.BringToFront();

                Button ok = MakeBarButton("应用", true);
                ok.DialogResult = DialogResult.OK;
                Button cancel = MakeBarButton("取消", false);
                cancel.DialogResult = DialogResult.Cancel;
                Button reset = MakeBarButton("恢复默认", false);
                reset.Click += delegate
                {
                    DiagramStyle defaults = new DiagramStyle();
                    SelectFont(latin, defaults.FontFamily);
                    SelectFont(chinese, defaults.ChineseFontFamily);
                    double[] defaultsSizes = FontSizes(defaults);
                    for (int i = 0; i < sizes.Length; i++) sizes[i].Value = (decimal)defaultsSizes[i];
                };
                FlowLayoutPanel buttons = (FlowLayoutPanel)MakeDialogButtons(ok, cancel);
                reset.Margin = new Padding(0, 0, ButtonSpacing, 0);
                buttons.Controls.Add(reset);
                dlg.Controls.Add(buttons);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                dlg.ResumeLayout(true);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _style.FontFamily = ((FontChoice)latin.SelectedItem).Name;
                _style.ChineseFontFamily = ((FontChoice)chinese.SelectedItem).Name;
                _style.AxisFontSize = (double)sizes[0].Value;
                _style.TickFontSize = (double)sizes[1].Value;
                _style.WavelengthFontSize = (double)sizes[2].Value;
                _style.LabelFontSize = (double)sizes[3].Value;
                _style.LegendFontSize = (double)sizes[4].Value;
                _style.CctFontSize = (double)sizes[5].Value;
                ScheduleSettingsSave();
                ScheduleRender();
                SetStatus("字体与文字大小已更新，预览和图片导出同步。");
            }
        }
    }
}
