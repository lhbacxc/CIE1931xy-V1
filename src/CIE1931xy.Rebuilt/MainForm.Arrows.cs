using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CIE1931xy.Core;
using CIE1931xy.Core.Rendering;
using CIE1931xy.UI;

namespace CIE1931xy
{
    internal sealed partial class MainForm
    {
        private readonly List<CieArrow> _arrows = new List<CieArrow>();
        private Button _btnAddArrow, _btnManageArrows;
        private ToolStripMenuItem _menuAddArrow, _menuManageArrows;

        private List<ArrowEndpoint> GetArrowEndpoints(bool includeHidden)
        {
            List<ArrowEndpoint> endpoints = new List<ArrowEndpoint>();
            foreach (CieCurve curve in _curves)
                if (curve != null) AddEndpoint(endpoints, new ArrowEndpoint(curve), includeHidden);
            foreach (CiePoint point in _points)
                if (point != null) AddEndpoint(endpoints, new ArrowEndpoint(point), includeHidden);
            return endpoints;
        }

        private void AddEndpoint(List<ArrowEndpoint> endpoints, ArrowEndpoint endpoint, bool includeHidden)
        {
            double x, y;
            if (!endpoint.TryGetXy(out x, out y)) return;
            if (!includeHidden)
            {
                if (!endpoint.Visible) return;
                PlotSpace.ToDiagram(_style.Kind, x, y, out x, out y);
                AxisBounds b = _style.Bounds;
                if (!double.IsFinite(x) || !double.IsFinite(y) || x < b.XMin || x > b.XMax || y < b.YMin || y > b.YMax) return;
            }
            endpoints.Add(endpoint);
        }

        private void PruneArrows()
        {
            _arrows.RemoveAll(delegate (CieArrow arrow)
            {
                return arrow == null || arrow.Start == null || arrow.End == null ||
                    !arrow.Start.BelongsTo(_curves, _points) || !arrow.End.BelongsTo(_curves, _points);
            });
        }

        private void UpdateArrowActions()
        {
            bool canAdd = GetArrowEndpoints(false).Count >= 2;
            if (_btnAddArrow != null) _btnAddArrow.Enabled = canAdd;
            if (_menuAddArrow != null) _menuAddArrow.Enabled = canAdd;
            if (_btnManageArrows != null) _btnManageArrows.Enabled = _arrows.Count > 0;
            if (_menuManageArrows != null) _menuManageArrows.Enabled = _arrows.Count > 0;
        }

        private void AddArrow()
        {
            List<ArrowEndpoint> endpoints = GetArrowEndpoints(false);
            if (endpoints.Count < 2) { SetStatus("请先绘制至少两个可见的数据点。"); return; }
            using (ArrowEditorDialog dialog = new ArrowEditorDialog(endpoints, _style.Kind, null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                PlaceArrowBesidePoints(dialog.Result);
                _arrows.Add(dialog.Result);
                UpdateArrowActions(); ScheduleRender();
                SetStatus("已添加箭头：" + dialog.Result.Start.Name + " → " + dialog.Result.End.Name);
            }
        }

        private void EditArrow(CieArrow arrow)
        {
            using (ArrowEditorDialog dialog = new ArrowEditorDialog(GetArrowEndpoints(true), _style.Kind, arrow))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                int index = _arrows.IndexOf(arrow);
                if (index < 0) return;
                _arrows[index] = dialog.Result;
                ScheduleRender(); SetStatus("已更新箭头。");
            }
        }

        private void ManageArrows()
        {
            PruneArrows();
            using (Form dialog = CreateDialog("管理箭头", new Size(760, 380)))
            {
                dialog.Name = "ArrowManager";
                dialog.MinimumSize = new Size(600, 320);
                DataGridView grid = CreateGrid(new[] { "起点 → 终点", "类型", "线宽", "颜色", "状态" },
                                               new[] { 280, 72, 60, 90, 120 });
                grid.Name = "ArrowList";
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                grid.Columns[0].FillWeight = 220;
                Button add = MakeBarButton("添加箭头", false);
                Button edit = MakeBarButton("编辑", false);
                Button remove = MakeBarButton("删除", false);
                Button close = MakeBarButton("关闭", false);
                close.DialogResult = DialogResult.Cancel;
                FlowLayoutPanel actions = MakeToolbar();
                actions.Controls.Add(add); actions.Controls.Add(edit); actions.Controls.Add(remove);
                FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
                    FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
                bottom.Controls.Add(close);
                Label tip = new Label { Name = "ArrowManagerNote", Dock = DockStyle.Bottom, AutoSize = false, Padding = new Padding(12, 6, 12, 6),
                    Text = "双击箭头行可编辑；隐藏端点时箭头暂时隐藏，删除端点时一并删除箭头。" };
                Action refresh = delegate
                {
                    int selected = grid.CurrentRow == null ? 0 : grid.CurrentRow.Index;
                    grid.Rows.Clear();
                    foreach (CieArrow arrow in _arrows)
                    {
                        string state = arrow.Start.Visible && arrow.End.Visible ? "显示" : "端点已隐藏";
                        int row = grid.Rows.Add(arrow.Start.Name + " → " + arrow.End.Name,
                            arrow.Kind == ArrowKind.Straight ? "直线" : "弯曲",
                            arrow.LineWidth.ToString("0.#", WF), ColorText(arrow.Color), state);
                        grid.Rows[row].Tag = arrow;
                        grid.Rows[row].Cells[3].Style.BackColor = arrow.Color;
                        grid.Rows[row].Cells[3].Style.ForeColor = arrow.Color.GetBrightness() < 0.45 ? Color.White : Color.Black;
                    }
                    if (grid.Rows.Count > 0) grid.CurrentCell = grid.Rows[Math.Min(selected, grid.Rows.Count - 1)].Cells[0];
                    add.Enabled = GetArrowEndpoints(false).Count >= 2;
                    edit.Enabled = remove.Enabled = grid.Rows.Count > 0;
                    UpdateArrowActions();
                };
                Action editSelected = delegate
                {
                    if (grid.CurrentRow == null) return;
                    EditArrow((CieArrow)grid.CurrentRow.Tag); refresh();
                };
                Action deleteSelected = delegate
                {
                    List<CieArrow> selected = new List<CieArrow>();
                    foreach (DataGridViewRow row in grid.SelectedRows) selected.Add((CieArrow)row.Tag);
                    foreach (CieArrow arrow in selected) _arrows.Remove(arrow);
                    refresh(); ScheduleRender(); SetStatus("已删除 " + selected.Count + " 条箭头。");
                };
                add.Click += delegate { AddArrow(); refresh(); };
                edit.Click += delegate { editSelected(); };
                remove.Click += delegate { deleteSelected(); };
                grid.CellDoubleClick += delegate (object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) editSelected(); };
                grid.KeyDown += delegate (object sender, KeyEventArgs e)
                {
                    if (e.KeyData == Keys.Delete) { e.Handled = true; e.SuppressKeyPress = true; deleteSelected(); }
                };
                dialog.Controls.Add(actions); dialog.Controls.Add(tip); dialog.Controls.Add(bottom);
                dialog.Controls.Add(grid); grid.BringToFront();
                dialog.Layout += delegate { FitWrappedLabel(tip); };
                dialog.CancelButton = close;
                refresh(); dialog.ResumeLayout(true); dialog.ShowDialog(this);
            }
        }

        private static string ColorText(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }
}
