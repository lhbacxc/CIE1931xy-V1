using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using CIE1931xy.Core;
using CIE1931xy.Core.Rendering;

namespace CIE1931xy.UI
{
    internal sealed class ArrowEditorDialog : Form
    {
        private sealed class EndpointChoice
        {
            public ArrowEndpoint Endpoint;
            public string Text;
            public override string ToString() { return Text; }
        }

        private readonly ComboBox _start, _end, _kind, _bend;
        private readonly NumericUpDown _width, _curvature;
        private readonly CheckBox _outline;
        private readonly Button _color, _outlineColor, _apply;
        private readonly Panel _preview;
        private readonly Label _validation;
        private Color _strokeColor, _borderColor;
        private bool _ready;
        private readonly CieArrow _original;
        public CieArrow Result { get; private set; }

        public ArrowEditorDialog(IList<ArrowEndpoint> endpoints, DiagramKind kind, CieArrow current)
        {
            SuspendLayout();
            Name = "ArrowEditor";
            Text = current == null ? "添加箭头" : "编辑箭头";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 620);
            MinimumSize = new Size(560, 480);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            CieArrow initial = current ?? new CieArrow();
            _original = initial.Clone();
            _strokeColor = initial.Color; _borderColor = initial.OutlineColor;
            _start = Combo("ArrowStart"); _end = Combo("ArrowEnd");
            for (int i = 0; i < endpoints.Count; i++)
            {
                ArrowEndpoint endpoint = endpoints[i];
                double x, y; endpoint.TryGetXy(out x, out y);
                PlotSpace.ToDiagram(kind, x, y, out x, out y);
                EndpointChoice choice = new EndpointChoice { Endpoint = endpoint,
                    Text = (endpoint.Point != null ? "坐标" : "光谱") + " " + (i + 1) + " · " + endpoint.Name +
                        " · (" + x.ToString("0.00000", CultureInfo.InvariantCulture) + ", " +
                        y.ToString("0.00000", CultureInfo.InvariantCulture) + ")" + (endpoint.Visible ? "" : " [已隐藏]") };
                _start.Items.Add(choice); _end.Items.Add(choice);
            }
            _kind = Combo("ArrowKind", "直线箭头", "弯曲箭头");
            _kind.SelectedIndex = (int)initial.Kind;
            _width = Number("ArrowWidth", 1, 12, (decimal)initial.LineWidth, 1);
            _curvature = Number("ArrowCurvature", 0, 100, (decimal)(initial.Curvature * 100), 0);
            _bend = Combo("ArrowBend", "向左弯", "向右弯");
            _bend.Width = 140; _bend.Dock = DockStyle.None; _bend.SelectedIndex = initial.BendLeft ? 0 : 1;
            _color = Button("ArrowColor", ""); _outlineColor = Button("ArrowOutlineColor", "");
            _outline = new CheckBox { Name = "ArrowOutline", Text = "启用细描边", AutoSize = true,
                Checked = initial.ShowOutline, Margin = new Padding(0, 6, 8, 0) };
            _apply = Button("ArrowApply", current == null ? "添加" : "应用");
            Button cancel = Button("ArrowCancel", "取消"); cancel.DialogResult = DialogResult.Cancel;

            TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Padding = new Padding(16, 12, 16, 8) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(fields, "起点", _start); AddRow(fields, "终点", _end);
            AddRow(fields, "箭头类型", _kind); AddRow(fields, "线宽", _width);
            AddRow(fields, "箭头颜色", _color);
            FlowLayoutPanel curveRow = Flow(_curvature, new Label { Text = "%", AutoSize = true, Margin = new Padding(2, 6, 14, 0) }, _bend);
            AddRow(fields, "弯曲程度 / 朝向", curveRow);
            AddRow(fields, "描边", Flow(_outline, _outlineColor));
            Label direction = new Label { AutoSize = true, Dock = DockStyle.Fill,
                Text = "参考方向：起点 → 终点；箭头放在点列旁边，添加后可在主图中拖动。" };
            AddWideRow(fields, direction);
            Label previewTitle = new Label { AutoSize = true, Text = "样式示意（端点位置以主图为准）", Margin = new Padding(0, 10, 0, 5) };
            AddWideRow(fields, previewTitle);
            _preview = new Panel { Name = "ArrowPreview", Dock = DockStyle.Fill, Height = 160,
                BackColor = Color.FromArgb(62, 70, 84), Margin = new Padding(0, 0, 0, 8) };
            AddWideRow(fields, _preview, 160);
            _validation = new Label { Name = "ArrowValidation", AutoSize = true, Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(150, 45, 40), Margin = new Padding(0, 4, 0, 0) };
            AddWideRow(fields, _validation);
            Label note = new Label { Name = "ArrowUsageNote", AutoSize = false, Dock = DockStyle.Bottom, Padding = new Padding(16, 8, 16, 8),
                Text = "左右弯按箭头前进方向观察。拖动只改变位置，保持与参考方向平行；箭头随图片导出，只保留在当前会话。" };
            Panel body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            body.Controls.Add(fields);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), WrapContents = false };
            buttons.Controls.Add(cancel); buttons.Controls.Add(_apply);
            Controls.Add(buttons); Controls.Add(note); Controls.Add(body); body.BringToFront();
            AcceptButton = _apply; CancelButton = cancel;

            foreach (ComboBox combo in new[] { _start, _end, _kind, _bend }) combo.SelectedIndexChanged += delegate { UpdatePreview(); };
            _width.ValueChanged += delegate { UpdatePreview(); };
            _curvature.ValueChanged += delegate { UpdatePreview(); };
            _outline.CheckedChanged += delegate { UpdatePreview(); };
            _color.Click += delegate { PickColor(false); };
            _outlineColor.Click += delegate { PickColor(true); };
            _apply.Click += delegate
            {
                CieArrow draft = ReadDraft();
                if (draft.ValidationError() != null) { UpdatePreview(); return; }
                Result = draft; DialogResult = DialogResult.OK;
            };
            _preview.Paint += PaintPreview;
            _preview.Resize += delegate { _preview.Invalidate(); };
            fields.Layout += delegate
            {
                int available = Math.Max(80, fields.ClientSize.Width - fields.Padding.Horizontal);
                foreach (Label label in new[] { direction, _validation }) label.MaximumSize = new Size(available, 0);
            };
            Layout += delegate
            {
                int width = Math.Max(80, ClientSize.Width - note.Padding.Horizontal);
                int height = TextRenderer.MeasureText(note.Text, note.Font, new Size(width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + note.Padding.Vertical;
                if (note.Height != height) note.Height = height;
            };
            SelectEndpoint(_start, initial.Start, endpoints, true);
            SelectEndpoint(_end, initial.End, endpoints, false);
            if (current == null && _start.SelectedIndex == _end.SelectedIndex && endpoints.Count >= 2)
                _end.SelectedIndex = (_start.SelectedIndex + 1) % endpoints.Count;
            _ready = true; UpdatePreview(); ResumeLayout(true);
        }

        private static ComboBox Combo(string name, params string[] items)
        {
            ComboBox combo = new RoundedComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill, IntegralHeight = false, MaxDropDownItems = 12, Margin = new Padding(0, 3, 0, 5) };
            if (items.Length > 0) { combo.Items.AddRange(items); combo.SelectedIndex = 0; }
            combo.DropDown += delegate
            {
                int width = combo.Width;
                foreach (object item in combo.Items) width = Math.Max(width, TextRenderer.MeasureText(item.ToString(), combo.Font).Width + 24);
                combo.DropDownWidth = Math.Min(width, Screen.FromControl(combo).WorkingArea.Width - 32);
            };
            return combo;
        }

        private static NumericUpDown Number(string name, decimal min, decimal max, decimal value, int decimals)
        {
            return new RoundedNumber { Name = name, Minimum = min, Maximum = max, Value = value,
                DecimalPlaces = decimals, Increment = decimals == 0 ? 5 : 0.5m, Width = 100,
                MinimumSize = new Size(100, 26), Margin = new Padding(0, 3, 0, 5) };
        }

        private static Button Button(string name, string text)
        {
            return new RoundedButton { Name = name, Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(88, 30), Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 3, 6, 5),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, UseVisualStyleBackColor = false };
        }

        private static FlowLayoutPanel Flow(params Control[] controls)
        {
            FlowLayoutPanel flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = true };
            flow.Controls.AddRange(controls); return flow;
        }

        private static void AddRow(TableLayoutPanel fields, string title, Control control)
        {
            int row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { AutoSize = true, Text = title, Margin = new Padding(0, 7, 14, 5) }, 0, row);
            fields.Controls.Add(control, 1, row);
        }

        private static void AddWideRow(TableLayoutPanel fields, Control control, int height = 0)
        {
            int row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(height == 0 ? SizeType.AutoSize : SizeType.Absolute, height));
            fields.Controls.Add(control, 0, row); fields.SetColumnSpan(control, 2);
        }

        private static void SelectEndpoint(ComboBox combo, ArrowEndpoint wanted, IList<ArrowEndpoint> endpoints, bool start)
        {
            for (int i = 0; i < endpoints.Count; i++)
                if (wanted != null && endpoints[i].SameSource(wanted)) { combo.SelectedIndex = i; return; }
            int selected = -1;
            for (int i = 0; i < endpoints.Count; i++)
                if (endpoints[i].Selected) { selected = i; if (start) break; }
            combo.SelectedIndex = selected >= 0 ? selected : endpoints.Count > 0 ? (start ? 0 : Math.Min(1, endpoints.Count - 1)) : -1;
        }

        private void PickColor(bool outline)
        {
            using (ColorDialog dialog = new ColorDialog { FullOpen = true, Color = outline ? _borderColor : _strokeColor })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (outline) _borderColor = dialog.Color; else _strokeColor = dialog.Color;
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            if (!_ready) return;
            bool curved = _kind.SelectedIndex == 1;
            _curvature.Enabled = _bend.Enabled = curved;
            _outlineColor.Enabled = _outline.Checked;
            SetColorButton(_color, _strokeColor); SetColorButton(_outlineColor, _borderColor);
            string error = ReadDraft().ValidationError();
            _validation.Text = error ?? "";
            _apply.Enabled = error == null;
            _preview.Invalidate();
        }

        private static void SetColorButton(Button button, Color color)
        {
            button.BackColor = color;
            button.ForeColor = color.GetBrightness() < 0.45 ? Color.White : Color.Black;
            button.Text = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private void PaintPreview(object sender, PaintEventArgs e)
        {
            if (!_ready) return;
            double scale = _preview.DeviceDpi / 96.0;
            CieArrow arrow = ReadDraft();
            double bend = arrow.Kind == ArrowKind.Curved ? arrow.Curvature : 0;
            double chord = Math.Min(_preview.Width - 80 * scale,
                Math.Max(30 * scale, _preview.Height - 88 * scale) / Math.Max(0.25, bend));
            PointF start = new PointF((float)((_preview.Width - chord) / 2), _preview.Height / 2F);
            PointF end = new PointF((float)((_preview.Width + chord) / 2), _preview.Height / 2F);
            using (GdiCanvas canvas = new GdiCanvas(e.Graphics, 1))
            {
                canvas.PushClip(new RectangleD(0, 0, _preview.Width, _preview.Height));
                try
                {
                    ArrowGeometry geometry;
                    if (arrow.ValidationError() == null && ArrowGeometry.TryCreate(arrow, start, end, 10 * scale, scale, out geometry))
                    {
                        CieArrow scaledArrow = arrow.Clone(); scaledArrow.LineWidth *= scale;
                        ArrowRenderer.DrawShape(canvas, scaledArrow, geometry, scale);
                    }
                    double radius = 3.5 * scale;
                    double rowY = arrow.Kind == ArrowKind.Curved && !arrow.BendLeft ? 18 * scale : _preview.Height - 18 * scale;
                    for (int i = 0; i < 5; i++)
                    {
                        double x = start.X + (end.X - start.X) * i / 4.0;
                        canvas.FillEllipse(Color.FromArgb(214, 39, 40), x, rowY, radius, radius);
                    }
                }
                finally { canvas.PopClip(); }
            }
            using (Brush text = new SolidBrush(Color.White))
            {
                double rowY = arrow.Kind == ArrowKind.Curved && !arrow.BendLeft ? 18 * scale : _preview.Height - 18 * scale;
                e.Graphics.DrawString("点列", Font, text, 10 * (float)scale, (float)rowY - Font.Height / 2F);
            }
        }

        private CieArrow ReadDraft()
        {
            return new CieArrow { Start = (_start.SelectedItem as EndpointChoice)?.Endpoint,
                End = (_end.SelectedItem as EndpointChoice)?.Endpoint, Kind = (ArrowKind)Math.Max(0, _kind.SelectedIndex),
                LineWidth = (double)_width.Value, Color = _strokeColor, Curvature = (double)_curvature.Value / 100,
                BendLeft = _bend.SelectedIndex == 0, ShowOutline = _outline.Checked, OutlineColor = _borderColor,
                OffsetX = _original.OffsetX, OffsetY = _original.OffsetY, Selected = _original.Selected };
        }
    }
}
