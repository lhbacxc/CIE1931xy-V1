using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using CIE1931xy.Core;
using CIE1931xy.Core.Export;
using CIE1931xy.Core.Rendering;
using CIE1931xy.UI;

namespace CIE1931xy
{
    /// <summary>
    /// 新版主界面（2.0.0）：
    /// 左侧 = 数据页签（光谱曲线 / 三刺激值 / 色度坐标 / 坐标点）+ 样式设置；
    /// 右侧 = 色度图画布 + 工具条 + 状态栏。
    /// 经典界面仍保留在 Form1（启动参数 --classic）。
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int ButtonSpacing = 6;
        // ---- 数据
        private readonly List<CieCurve> _curves = new List<CieCurve>();
        private readonly List<CiePoint> _points = new List<CiePoint>();
        private readonly List<string> _messages = new List<string>();

        // ---- 样式
        private readonly DiagramStyle _style = new DiagramStyle();
        // 导出尺寸固定（用户要求删除画布尺寸输入，不再需要调节）
        private const int ExportWidth = 1400;
        private const int ExportHeight = 1400;
        private int _exportDpi = 300;
        private string _lastExportName = "CIE1931色度图.png";
        private static readonly Color PointColor = Color.FromArgb(0xD6, 0x27, 0x28);

        // ---- 控件
        private TabControl _tabs;
        private DataGridView _gridCurves;
        private DataGridView _gridXyz;

        private DataGridView _gridPoints;
        private PictureBox _canvas;
        private Panel _canvasHost;
        private Timer _renderTimer;
        private ToolStripStatusLabel _status;
        private Label _hint;
        private ComboBox _cmbKind;
        private ComboBox _cmbBackground;
        private CheckBox _chkBlackbody;
        private CheckBox _chkGrid;
        private CheckBox _chkWavelength;
        private CheckBox _chkLegend;
        private CheckBox _chkLabels;
        private NumericUpDown _numDpi;
        private Button _btnPaste;
        private Button _btnImportSpectrum;
        private Button _btnImportPoints;
        private Button _btnExportImage;
        private Button _btnExportData;
        private bool _suppressUiEvents;

        public MainForm()
        {
            BuildUi();
            // 启动时不要示例数据（用户要求：打开软件默认无数据），示例仍可从“载入示例”按钮加载
            ApplyStyleDefaults();
            UpdateTables();
            ScheduleRender();
        }

        // ================================================================ 界面

        private void BuildUi()
        {
            // 所有控件加入后才执行首次 DPI 缩放；提前布局会把 96dpi 基准
            // 更新为当前 DPI，导致后来创建的按钮只放大字体、不放大边界。
            SuspendLayout();
            Text = "CIE1931 色度分析器 2.0 — CIE1931xy Rebuilt";
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(0xF4, 0xF5, 0xF7);
            ClientSize = new Size(1200, 800);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = AppResources.Icon; } catch { }
            // 按 DPI 缩放（配合 Program 里的 PerMonitorV2），文字在高分屏上才清晰；
            // 逻辑尺寸按 96dpi 设计，系统会自动放大到实际 DPI。
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AllowDrop = true;
            DragEnter += MainForm_DragEnter;
            DragDrop += MainForm_DragDrop;

            MenuStrip menu = BuildMenu();
            Controls.Add(menu);
            MainMenuStrip = menu;

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterWidth = 1;
            split.IsSplitterFixed = false;
            split.BackColor = Color.FromArgb(0xDD, 0xE1, 0xE6);
            Controls.Add(split);
            split.BringToFront();

            // ---- 左：数据页签 + 样式
            split.Panel1.BackColor = Color.FromArgb(0xFA, 0xFB, 0xFC);
            split.Panel1.Padding = new Padding(10, 8, 8, 8);

            Panel stylePanel = BuildStylePanel();
            stylePanel.Dock = DockStyle.Bottom;
            split.Panel1.Controls.Add(stylePanel);

            split.Panel1.Controls.Add(BuildActionBar());
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Padding = new Point(14, 5);
            _gridCurves = CreateGrid(new[] { "名称", "形状", "颜色", "描边" },
                                     new[] { 150, 76, 76, 76 });
            _gridXyz = CreateGrid(new[] { "名称", "波长范围", "点数", "峰值波长", "X", "Y", "Z" },
                                  new[] { 130, 96, 46, 70, 96, 96, 96 });

            _gridPoints = CreateGrid(new[] { "名称", "x", "y", "u'", "v'", "CCT", "Δuv" },
                                     new[] { 130, 70, 70, 70, 70, 70, 60 });
            _gridPoints.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _gridPoints.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.25f);
            _gridPoints.DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
            _gridPoints.Columns[0].FillWeight = 30f;
            for (int ci = 1; ci < _gridPoints.Columns.Count; ci++)
                _gridPoints.Columns[ci].FillWeight = 14f;
            AddTab(_tabs, "光谱曲线", _gridCurves);
            AddTab(_tabs, "三刺激值", _gridXyz);
            AddTab(_tabs, "坐标点", _gridPoints);

            // 双击首列就地改名；右键弹出"标记/颜色/标签/删除"菜单（等价于 CIEdraw 的表格管理）
            _gridCurves.CellDoubleClick += GridCurveCellDoubleClick;
            _gridPoints.CellDoubleClick += GridPointCellDoubleClick;
            _gridCurves.CellMouseDown += GridCurveCellMouseDown;
            _gridPoints.CellMouseDown += GridPointCellMouseDown;
            _tabs.SelectedIndexChanged += delegate { UpdateHint(); };
            _gridCurves.SelectionChanged += delegate { SyncSelectionFlags(); ScheduleRender(); };
            _gridCurves.MouseClick += GridMouseClick;
            _gridPoints.SelectionChanged += delegate { SyncSelectionFlags(); ScheduleRender(); };
            _gridPoints.MouseClick += GridMouseClick;
            _gridCurves.KeyDown += GridCurveKeyDown;
            _gridPoints.KeyDown += GridPointKeyDown;
            split.Panel1.Controls.Add(_tabs);
            _tabs.BringToFront();

            // ---- 右：工具条 + 画布
            split.Panel2.BackColor = Color.FromArgb(0xF4, 0xF5, 0xF7);
            split.Panel2.Padding = new Padding(8, 8, 8, 6);

            Panel canvasBar = BuildCanvasBar();
            canvasBar.Dock = DockStyle.Top;
            split.Panel2.Controls.Add(canvasBar);

            _canvasHost = new Panel();
            _canvasHost.Dock = DockStyle.Fill;
            _canvasHost.AutoScroll = false; // 画布用 Zoom 自适应，不出现滚动条
            _canvasHost.BackColor = Color.FromArgb(0xF4, 0xF5, 0xF7);
            _canvasHost.Padding = new Padding(4);
            _canvasHost.Resize += delegate { ScheduleRender(); };
            _canvas = new PictureBox();
            _canvas.Dock = DockStyle.Fill;
            _canvas.SizeMode = PictureBoxSizeMode.Zoom;
            _canvas.MouseClick += CanvasMouseClick;
            _canvas.BackColor = Color.White;
            _canvasHost.Controls.Add(_canvas);
            split.Panel2.Controls.Add(_canvasHost);
            canvasBar.SendToBack();

            // ---- 状态栏
            StatusStrip status = new StatusStrip();
            status.BackColor = Color.FromArgb(0xEE, 0xF1, 0xF5);
            status.SizingGrip = false;
            _status = new ToolStripStatusLabel("就绪");
            _status.Spring = true;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            status.Items.Add(_status);
            ToolStripStatusLabel version = new ToolStripStatusLabel("v2.0.0");
            version.ForeColor = Color.FromArgb(0x80, 0x89, 0x96);
            status.Items.Add(version);
            Controls.Add(status);

            _renderTimer = new Timer();
            _renderTimer.Interval = 140;
            _renderTimer.Tick += delegate { _renderTimer.Stop(); RenderNow(); };

            // 分割条位置/最小宽度必须在窗体已经布局之后设置，否则 SplitContainer 会拒绝
            Load += delegate
            {
                try
                {
                    split.Panel1MinSize = LogicalToDeviceUnits(380);
                    split.Panel2MinSize = LogicalToDeviceUnits(420);
                    split.SplitterDistance = LogicalToDeviceUnits(470);
                }
                catch (InvalidOperationException)
                {
                }

                // 命令行 --open <文件> 时自动导入（方便脚本/快捷方式直接打开数据）
                if (!string.IsNullOrEmpty(Program.StartupFile))
                {
                    try
                    {
                        ImportFiles(new[] { Program.StartupFile });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };
            ResumeLayout(true);
        }

        private void AddTab(TabControl tabs, string title, DataGridView grid)
        {
            TabPage page = new TabPage(title);
            page.BackColor = Color.White;
            page.Padding = new Padding(2);
            page.Controls.Add(grid);
            tabs.TabPages.Add(page);
        }

        private DataGridView CreateGrid(string[] headers, int[] widths)
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.ReadOnly = true;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = true;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(0xF0, 0xF3, 0xF7);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0x3A, 0x44, 0x52);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            g.ColumnHeadersHeight = 34;
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0x2F, 0x6F, 0xED);
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            g.RowTemplate.Height = 30;
            g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            g.GridColor = Color.FromArgb(0xEF, 0xF2, 0xF6);
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(0xFA, 0xFB, 0xFD);
            g.DefaultCellStyle.BackColor = Color.White;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(0xE6, 0xEC, 0xF8);
            for (int i = 0; i < headers.Length; i++)
            {
                DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
                col.HeaderText = headers[i];
                // 列宽不随窗体首次 AutoScale 自动放大，须从逻辑单位转换。
                col.Width = g.LogicalToDeviceUnits(widths[Math.Min(i, widths.Length - 1)]);
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                g.Columns.Add(col);
            }
            g.HandleCreated += delegate { FitGridHeaders(g); };
            g.DpiChangedAfterParent += delegate { FitGridHeaders(g); };
            return g;
        }

        private static void FitGridHeaders(DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                int width = TextRenderer.MeasureText(column.HeaderText,
                    grid.ColumnHeadersDefaultCellStyle.Font).Width
                    + grid.ColumnHeadersDefaultCellStyle.Padding.Horizontal + 4;
                column.MinimumWidth = Math.Max(5, width);
            }
        }

        private MenuStrip BuildMenu()
        {
            MenuStrip menu = new MenuStrip();
            menu.BackColor = Color.White;
            menu.Padding = new Padding(6, 2, 0, 2);

            ToolStripMenuItem file = new ToolStripMenuItem("文件(&F)");
            file.DropDownItems.Add(MakeItem("导入光谱文件…", delegate { ImportSpectrum(); }, Keys.Control | Keys.O));
            file.DropDownItems.Add(MakeItem("粘贴光谱/坐标…", delegate { PasteFromClipboard(); }, Keys.Control | Keys.Shift | Keys.V));
            file.DropDownItems.Add(MakeItem("输入色坐标…", delegate { InputCoordinates(); }, Keys.Control | Keys.Shift | Keys.O));
            file.DropDownItems.Add(MakeItem("从剪贴板粘贴…", delegate { PasteFromClipboard(); }, Keys.Control | Keys.V));
            file.DropDownItems.Add(MakeItem("批量修改名称…", delegate { BatchRename(); }, Keys.Control | Keys.H));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MakeItem("导出图片…", delegate { ExportImage(); }, Keys.Control | Keys.S));
            file.DropDownItems.Add(MakeItem("导出数据…", delegate { ExportData(); }, Keys.Control | Keys.E));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MakeItem("清空全部数据", delegate { ClearAll(); }, Keys.None));
            file.DropDownItems.Add(MakeItem("退出", delegate { Close(); }, Keys.None));

            ToolStripMenuItem view = new ToolStripMenuItem("视图(&V)");
            view.DropDownItems.Add(MakeItem("切换图型 CIE 1931 xy / CIE 1976 u'v'", delegate { ToggleKind(); }, Keys.Control | Keys.T));
            view.DropDownItems.Add(MakeItem("切换背景 位图 / 矢量", delegate { ToggleBackground(); }, Keys.Control | Keys.B));
            view.DropDownItems.Add(MakeItem("黑体轨迹线 开 / 关", delegate { ToggleBlackbody(); }, Keys.Control | Keys.P));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(MakeItem("图例", delegate { ToggleCheck(_chkLegend); }, Keys.None));
            view.DropDownItems.Add(MakeItem("波长标注", delegate { ToggleCheck(_chkWavelength); }, Keys.None));
            view.DropDownItems.Add(MakeItem("数据标签", delegate { ToggleCheck(_chkLabels); }, Keys.None));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(MakeItem("打开经典界面", delegate { OpenClassic(); }, Keys.None));

            ToolStripMenuItem help = new ToolStripMenuItem("帮助(&H)");
            help.DropDownItems.Add(MakeItem("使用说明", delegate { ShowHelp(); }, Keys.F1));
            help.DropDownItems.Add(MakeItem("关于", delegate { ShowAbout(); }, Keys.None));

            menu.Items.Add(file);
            menu.Items.Add(view);
            menu.Items.Add(help);
            return menu;
        }

        private static ToolStripMenuItem MakeItem(string text, EventHandler handler, Keys shortcut)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, null, handler);
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            item.ShowShortcutKeys = shortcut != Keys.None;
            return item;
        }

        private Panel BuildCanvasBar()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.WrapContents = true;
            bar.BackColor = Color.White;
            bar.Padding = new Padding(6, 8, 6, 2);

            _btnImportSpectrum = MakeBarButton("导入光谱", false);
            _btnImportSpectrum.Click += delegate { ImportSpectrum(); };
            _btnImportPoints = MakeBarButton("输入坐标", false);
            _btnImportPoints.Click += delegate { InputCoordinates(); };
            _btnPaste = MakeBarButton("粘贴", false);
            _btnPaste.Click += delegate { PasteData(); };
            _btnExportImage = MakeBarButton("导出图片", true);
            _btnExportImage.Click += delegate { ExportImage(); };
            _btnExportData = MakeBarButton("导出数据", true);
            _btnExportData.Click += delegate { ExportData(); };

            bar.Controls.Add(_btnImportSpectrum);
            bar.Controls.Add(_btnImportPoints);
            bar.Controls.Add(_btnPaste);
            bar.Controls.Add(_btnExportImage);
            bar.Controls.Add(_btnExportData);
            return bar;
        }

        private Button MakeSmallButton(string text, EventHandler onClick)
        {
            Button b = new RoundedButton();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(0xCF, 0xD6, 0xE0);
            b.BackColor = Color.White;
            b.ForeColor = Color.FromArgb(0x2B, 0x33, 0x3D);
            b.Size = new Size(88, 28);
            b.MinimumSize = b.Size;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(6, 4, 6, 4);
            b.Margin = new Padding(0, 0, ButtonSpacing, ButtonSpacing);
            b.Cursor = Cursors.Hand;
            b.Click += onClick;
            return b;
        }

        private static Button MakeBarButton(string text, bool accent)
        {
            Button b = new RoundedButton();
            b.Text = text;
            b.Size = new Size(88, 30);
            b.MinimumSize = b.Size;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(6, 4, 6, 4);
            b.Margin = new Padding(0, 0, ButtonSpacing, ButtonSpacing);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = accent ? Color.FromArgb(0x2F, 0x6F, 0xED) : Color.FromArgb(0xD5, 0xDA, 0xE1);
            b.BackColor = accent ? Color.FromArgb(0x2F, 0x6F, 0xED) : Color.White;
            b.ForeColor = accent ? Color.White : Color.FromArgb(0x2B, 0x33, 0x3D);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        /// <summary>页签上方的动作栏：改颜色 / 改描边 / 批量改名。</summary>
        private Panel BuildActionBar()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.WrapContents = true;
            bar.BackColor = Color.FromArgb(0xF7, 0xF9, 0xFC);
            bar.Padding = new Padding(8, 6, 8, 0);

            Button btnColor = MakeSmallButton("颜色", delegate { ApplyColorToSelection(false); });
            Button btnOutline = MakeSmallButton("描边", delegate { ApplyColorToSelection(true); });
            Button btnRename = MakeSmallButton("批量改名", delegate { BatchRename(); });
            bar.Controls.Add(btnColor);
            bar.Controls.Add(btnOutline);
            bar.Controls.Add(btnRename);
            return bar;
        }

        private Panel BuildStylePanel()
        {
            Panel panel = new Panel();
            panel.Height = 340;   // 留出底部快捷键提示，避免被裁
            panel.BackColor = Color.FromArgb(0xF0, 0xF3, 0xF7);
            panel.Padding = new Padding(12, 8, 12, 8);

            Label title = new Label();
            title.Text = "显示设置";
            title.Dock = DockStyle.Top;
            title.Height = 24;
            title.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(0x3A, 0x44, 0x52);
            panel.Controls.Add(title);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(flow);
            flow.BringToFront();
            // 设置行跟随侧栏宽度；提示按可用宽度换行，窄窗口仍可滚动查看。
            flow.Layout += delegate { FitStyleRows(flow); };

            // 图型：CIE 1931 xy 与 CIE 1976 u'v'（uv 图用矢量背景，位图底图只有 xy 那一张）
            _cmbKind = MakeCombo("CIE 1931 (x, y)", "CIE 1976 (u', v')");
            _cmbKind.SelectedIndexChanged += delegate { OnKindChanged(); };
            flow.Controls.Add(MakeRow("图型", _cmbKind));

            _cmbBackground = MakeCombo("位图底图", "矢量背景");
            _cmbBackground.SelectedIndexChanged += delegate { OnBackgroundChanged(); };
            flow.Controls.Add(MakeRow("背景", _cmbBackground));

            flow.Controls.Add(MakeRow("导出", MakeDpiRow()));

            _chkBlackbody = MakeToggle("黑体线", true);
            _chkGrid = MakeToggle("网格", true);
            _chkWavelength = MakeToggle("波长标注", false);
            _chkLegend = MakeToggle("图例", false);
            _chkLabels = MakeToggle("数据标签", false);

            FlowLayoutPanel row1 = MakeCheckRow(_chkBlackbody, _chkGrid);
            FlowLayoutPanel row2 = MakeCheckRow(_chkWavelength, _chkLegend, _chkLabels);
            row1.Margin = new Padding(0, ButtonSpacing, 0, 0);
            flow.Controls.Add(row1);
            flow.Controls.Add(row2);



            FlowLayoutPanel row3 = new FlowLayoutPanel();
            row3.FlowDirection = FlowDirection.LeftToRight;
            row3.AutoSize = true;
            row3.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row3.Margin = new Padding(0);
            Button btnClear = MakeSmallButton("清空全部数据");
            btnClear.Click += delegate { ClearAll(); };
            row3.Controls.Add(btnClear);
            flow.Controls.Add(row3);

            Label note = new Label();
            note.AutoSize = true;
            note.Margin = new Padding(0);
            note.ForeColor = Color.FromArgb(0x6B, 0x74, 0x80);
            note.Text = "快捷键：Ctrl+O 导入 · Ctrl+V 粘贴 · Ctrl+B 切背景 · Ctrl+P 黑体线\r\n表格里 Ctrl/Shift 多选，Delete 删除选中项";
            flow.Controls.Add(note);
            return panel;
        }

        private static void FitStyleRows(FlowLayoutPanel flow)
        {
            int width = Math.Max(1, flow.ClientSize.Width - flow.Padding.Horizontal
                - SystemInformation.VerticalScrollBarWidth);
            foreach (Control child in flow.Controls)
            {
                int rowWidth = Math.Max(1, width - child.Margin.Horizontal);
                if (child is Label label)
                    label.MaximumSize = new Size(rowWidth, 0);
                else
                {
                    child.MinimumSize = new Size(rowWidth, child.MinimumSize.Height);
                    child.Width = rowWidth;
                }
            }
        }

        private static ComboBox MakeCombo(params string[] items)
        {
            ComboBox combo = new RoundedComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string item in items) combo.Items.Add(item);
            combo.SelectedIndex = 0;
            combo.Width = 200;
            return combo;
        }

        private static Panel MakeRow(string label, Control control)
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.MinimumSize = new Size(0, 32);
            row.Width = 430;
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Margin = new Padding(0);

            Label lbl = new Label();
            lbl.Text = label;
            lbl.AutoSize = true;
            lbl.Anchor = AnchorStyles.Left;
            lbl.Margin = new Padding(0, 3, 12, 3);
            lbl.ForeColor = Color.FromArgb(0x55, 0x5F, 0x6D);
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            row.Controls.Add(lbl, 0, 0);

            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(0, 3, 0, 3);
            row.Controls.Add(control, 1, 0);
            return row;
        }

        private Panel MakeDpiRow()
        {
            FlowLayoutPanel host = new FlowLayoutPanel();
            host.AutoSize = true;
            host.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            host.WrapContents = false;

            _numDpi = MakeNumber(72, 1200, 300);
            _numDpi.Width = 80;
            _numDpi.Margin = new Padding(0, 0, 6, 0);
            _numDpi.ValueChanged += delegate { _exportDpi = (int)_numDpi.Value; };

            Label unit = new Label();
            unit.Text = "DPI";
            unit.AutoSize = true;
            unit.Margin = new Padding(0, 3, 0, 0);
            unit.TextAlign = ContentAlignment.MiddleLeft;
            unit.ForeColor = Color.FromArgb(0x55, 0x5F, 0x6D);

            host.Controls.Add(_numDpi);
            host.Controls.Add(unit);
            return host;
        }

        private static NumericUpDown MakeNumber(int min, int max, int value)
        {
            NumericUpDown num = new RoundedNumber();
            num.Minimum = min;
            num.Maximum = max;
            num.Value = Math.Min(max, Math.Max(min, value));
            num.Increment = 50;
            num.TextAlign = HorizontalAlignment.Right;
            return num;
        }

        /// <summary>坐标轴范围用的小数输入框（0–1 区间）。</summary>
        /// <summary>固定视野：与位图底图一致，(0,0)–(0.8,0.9)。</summary>
        private AxisBounds FixedBounds()
        {
            return _style != null && _style.Kind == DiagramKind.Uv1976
                ? new AxisBounds(0, 0.65, 0, 0.65)
                : new AxisBounds(0, 0.8, 0, 0.9);
        }

        private static AxisBounds OldFixedBounds()
        {
            return new AxisBounds(0, 0.8, 0, 0.9);
        }

        private FlowLayoutPanel MakeCheckRow(params CheckBox[] boxes)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = true;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.Margin = new Padding(0);
            foreach (CheckBox box in boxes) row.Controls.Add(box);
            return row;
        }

        private CheckBox MakeToggle(string text, bool initial)
        {
            CheckBox chk = new RoundedToggle();
            chk.Text = text;
            chk.Appearance = Appearance.Button;
            chk.TextAlign = ContentAlignment.MiddleCenter;
            chk.Size = new Size(text.Length > 3 ? 118 : 84, 28);
            chk.MinimumSize = chk.Size;
            chk.AutoSize = true;
            chk.Padding = new Padding(6, 4, 6, 4);
            chk.Margin = new Padding(0, 0, ButtonSpacing, ButtonSpacing);
            chk.FlatStyle = FlatStyle.Flat;
            chk.FlatAppearance.BorderSize = 1;
            chk.FlatAppearance.BorderColor = Color.FromArgb(0xD5, 0xDA, 0xE1);
            chk.FlatAppearance.CheckedBackColor = Color.FromArgb(0x2F, 0x6F, 0xED);
            chk.BackColor = Color.White;
            chk.Cursor = Cursors.Hand;
            chk.Checked = initial;
            chk.ForeColor = chk.Checked ? Color.White : Color.FromArgb(0x2B, 0x33, 0x3D);
            chk.CheckedChanged += delegate
            {
                chk.ForeColor = chk.Checked ? Color.White : Color.FromArgb(0x2B, 0x33, 0x3D);
                OnStyleChanged();
            };
            return chk;
        }

        private static Button MakeSmallButton(string text)
        {
            Button b = new RoundedButton();
            b.Text = text;
            b.Size = new Size(202, 28);
            b.MinimumSize = b.Size;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Margin = new Padding(0, 0, ButtonSpacing, ButtonSpacing);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(0xD5, 0xDA, 0xE1);
            b.BackColor = Color.White;
            b.ForeColor = Color.FromArgb(0x2B, 0x33, 0x3D);
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Padding = new Padding(8, 4, 8, 4);
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            return b;
        }

        // ================================================================ 数据

        private void ApplyStyleDefaults()
        {
            _style.ShowGrid = true;
            _style.ShowBlackbody = true;
            _style.ShowWavelengthLabels = false;
            _style.ShowLegend = false;
            _style.ShowPointLabels = false;
        }

        private void LoadSamples()
        {
            ApplyStyleDefaults();
            LoadSamplePlanckian();
        }

        private void LoadSamplePlanckian()
        {
            // 示例：6500 K 黑体光谱（360–830 nm，1 nm）
            double[,] cmf = CieData.Cie1931xyz;
            int n = cmf.GetLength(0);
            double[] wl = new double[n];
            double[] val = new double[n];
            for (int i = 0; i < n; i++)
            {
                double lambdaNm = cmf[i, 0];
                wl[i] = lambdaNm;
                val[i] = PlanckianLocus.PlanckRadiance(lambdaNm * 1e-9, 6500.0);
            }
            CieCurve curve = new CieCurve("示例 6500K 黑体", wl, val);
            curve.Color = Color.FromArgb(0x2F, 0x6F, 0xED);
            curve.Shape = MarkerShape.Circle;
            curve.ShowLabel = true;
            curve.Result = Colorimetry.AnalyzeCurve(curve);
            _curves.Clear();
            _curves.Add(curve);
            _points.Clear();
            AddMessage("已载入示例：6500 K 黑体光谱（x≈0.3135, y≈0.3236）。");
            UpdateTables();
            ScheduleRender();
        }

        private void ClearAll()
        {
            _curves.Clear();
            _points.Clear();
            _messages.Clear();
            UpdateTables();
            UpdateHint();
            ScheduleRender();
        }

        private void ImportSpectrum()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "光谱/坐标文件 (*.csv;*.txt;*.dat;*.tsv)|*.csv;*.txt;*.dat;*.tsv|所有文件 (*.*)|*.*";
                dlg.Multiselect = true;
                dlg.Title = "导入光谱文件（可多选）";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ImportFiles(dlg.FileNames);
            }
        }

        private void ImportPoints()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "色坐标文件 (*.csv;*.txt;*.dat;*.tsv)|*.csv;*.txt;*.dat;*.tsv|所有文件 (*.*)|*.*";
                dlg.Multiselect = true;
                dlg.Title = "导入色坐标文件（名称,x,y）";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ImportFiles(dlg.FileNames);
            }
        }

        private void ImportFiles(IEnumerable<string> files)
        {
            int addedCurves = 0;
            int addedPoints = 0;
            foreach (string file in files)
            {
                try
                {
                    string text = SpectrumCsvParser.ReadTextFile(file);
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (SpectrumCsvParser.LooksLikeCoordinates(text))
                    {
                        ParseResult r = CoordinateParser.Parse(text);
                        foreach (CiePoint p in r.Points)
                        {
                            p.Color = PointColor;
                            p.OutlineColor = Color.Black;
                            p.Shape = MarkerShape.Circle;
                            _points.Add(p); addedPoints++;
                        }
                        foreach (string w in r.Warnings) AddMessage(w);
                        foreach (string e in r.Errors) AddMessage("⚠ " + e);
                    }
                    else
                    {
                        ParseResult r = SpectrumCsvParser.Parse(text, name);
                        bool multiple = r.Curves.Count > 1;
                        foreach (CieCurve c in r.Curves)
                        {
                            c.Name = multiple ? c.Name : name;   // 多曲线用数据标签，单曲线用文件名
                            c.Color = NextColor();
                            c.Shape = MarkerShape.Circle;
                            c.OutlineColor = Color.Black;
                            c.Result = Colorimetry.AnalyzeCurve(c);
                            _curves.Add(c);
                            addedCurves++;
                        }
                        foreach (string w in r.Warnings) AddMessage(w);
                        foreach (string e in r.Errors) AddMessage("⚠ " + e);
                    }
                    AddMessage("已导入 " + name + "。");
                }
                catch (Exception ex)
                {
                    AddMessage("⚠ 读取失败：" + Path.GetFileName(file) + " — " + ex.Message);
                }
            }
            _style.Bounds = FixedBounds();
            UpdateTables();
            ShowRelevantTab();
            ScheduleRender();
            SetStatus("导入完成：新增曲线 " + addedCurves + " 条，坐标点 " + addedPoints + " 个。");
        }

        /// <summary>粘贴/输入数据：弹窗里粘贴文本，确定后按内容自动识别光谱或色坐标。</summary>
        private void PasteData()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "粘贴数据";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(520, 400);
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.Font = Font;

                Label tip = new Label();
                tip.Dock = DockStyle.Top;
                tip.Height = 46;
                tip.Padding = new Padding(10, 8, 10, 0);
                tip.ForeColor = Color.FromArgb(0x6B, 0x74, 0x80);
                tip.Text = "把数据粘贴到下面（可直接从 Excel 复制）：两列=波长/强度 或 色坐标 x,y；三列=名称,x,y；也支持带表头的 CSV。";
                dlg.Controls.Add(tip);

                Panel buttons = new Panel();
                buttons.Dock = DockStyle.Bottom;
                buttons.Height = 46;
                Button ok = new Button(); ok.Text = "确定"; ok.DialogResult = DialogResult.OK;
                ok.SetBounds(330, 8, 80, 30);
                Button cancel = new Button(); cancel.Text = "取消"; cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(418, 8, 80, 30);
                Button fromClip = new Button(); fromClip.Text = "读剪贴板"; fromClip.SetBounds(12, 8, 96, 30);
                buttons.Controls.Add(fromClip); buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
                dlg.Controls.Add(buttons);

                TextBox box = new TextBox();
                box.Multiline = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Dock = DockStyle.Fill;
                box.Font = new Font("Consolas", 10F);
                dlg.Controls.Add(box);
                box.BringToFront();

                string clip = null;
                try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); }
                catch (Exception) { }
                if (!string.IsNullOrEmpty(clip)) box.Text = clip;
                fromClip.Click += delegate
                {
                    try { if (Clipboard.ContainsText()) box.Text = Clipboard.GetText(); }
                    catch (Exception ex) { AddMessage("⚠ 读取剪贴板失败：" + ex.Message); }
                };

                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string text = box.Text;
                if (string.IsNullOrWhiteSpace(text)) { SetStatus("没有粘贴任何数据。"); return; }
                ParsePastedText(text, "粘贴数据");
            }
        }

        private void ParsePastedText(string text, string fallbackName)
        {
            if (SpectrumCsvParser.LooksLikeCoordinates(text))
            {
                ParseResult r = CoordinateParser.Parse(text);
                foreach (CiePoint p in r.Points)
                {
                    p.Color = PointColor;
                    p.OutlineColor = Color.Black;
                    p.Shape = MarkerShape.Circle;
                    _points.Add(p);
                }
                foreach (string w in r.Warnings) AddMessage(w);
                foreach (string e in r.Errors) AddMessage("⚠ " + e);
                SetStatus("已粘贴 " + r.Points.Count + " 个色坐标点。");
            }
            else
            {
                ParseResult r = SpectrumCsvParser.Parse(text, fallbackName);
                bool multiple = r.Curves.Count > 1;
                foreach (CieCurve c in r.Curves)
                {
                    c.Name = multiple ? c.Name : fallbackName;
                    c.Color = NextColor();
                    c.Shape = MarkerShape.Circle;
                    c.OutlineColor = Color.Black;
                    c.Result = Colorimetry.AnalyzeCurve(c);
                    _curves.Add(c);
                }
                foreach (string w in r.Warnings) AddMessage(w);
                foreach (string e in r.Errors) AddMessage("⚠ " + e);
                SetStatus("已粘贴 " + r.Curves.Count + " 条光谱曲线。");
            }
            UpdateTables();
            ShowRelevantTab();
            ScheduleRender();
        }

        private void PasteFromClipboard()
        {
            string text = null;
            try
            {
                if (Clipboard.ContainsText()) text = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                AddMessage("⚠ 读取剪贴板失败：" + ex.Message);
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("剪贴板里没有文本。");
                return;
            }
            if (SpectrumCsvParser.LooksLikeCoordinates(text))
            {
                ParseResult r = CoordinateParser.Parse(text);
                foreach (CiePoint p in r.Points)
                {
                    p.Color = PointColor;
                    p.OutlineColor = Color.Black;
                    p.Shape = MarkerShape.Circle;
                    _points.Add(p);
                }
                foreach (string w in r.Warnings) AddMessage(w);
                foreach (string e in r.Errors) AddMessage("⚠ " + e);
                SetStatus("已粘贴 " + r.Points.Count + " 个色坐标点。");
            }
            else
            {
                ParseResult r = SpectrumCsvParser.Parse(text);
                bool multiple = r.Curves.Count > 1;
                foreach (CieCurve c in r.Curves)
                {
                    if (!multiple) c.Name = "剪贴板数据";
                    c.Color = NextColor();
                    c.Shape = MarkerShape.Circle;
                    c.OutlineColor = Color.Black;
                    c.Result = Colorimetry.AnalyzeCurve(c);
                    _curves.Add(c);
                }
                foreach (string w in r.Warnings) AddMessage(w);
                foreach (string e in r.Errors) AddMessage("⚠ " + e);
                SetStatus("已粘贴 " + r.Curves.Count + " 条光谱曲线。");
            }
            UpdateTables();
            ScheduleRender();
        }

        /// <summary>导入数据的默认颜色：统一红色（用户要求），需要区分时用「颜色」按钮自己改。</summary>
        private Color NextColor()
        {
            return PointColor;
        }

        private MarkerShape NextShape()
        {
            MarkerShape[] shapes = { MarkerShape.Circle, MarkerShape.Square, MarkerShape.Triangle, MarkerShape.Diamond, MarkerShape.Star };
            return shapes[_curves.Count % shapes.Length];
        }

        // ================================================================ 表格/状态

        private static string ShapeName(MarkerShape shape)
        {
            switch (shape)
            {
                case MarkerShape.Square: return "方形";
                case MarkerShape.Triangle: return "三角";
                case MarkerShape.Diamond: return "菱形";
                case MarkerShape.Cross: return "叉";
                case MarkerShape.Star: return "星形";
                default: return "圆形";
            }
        }

        private void UpdateTables()
        {
            _gridCurves.Rows.Clear();
            _gridXyz.Rows.Clear();
            for (int i = 0; i < _curves.Count; i++)
            {
                CieCurve c = _curves[i];
                if (c == null || c.Result == null) continue;
                CurveResult r = c.Result;
                string range = F(r.WlMin) + "–" + F(r.WlMax);
                int row = _gridCurves.Rows.Add(c.Name, ShapeName(c.Shape), "", "");
                _gridCurves.Rows[row].Cells[2].Style.BackColor = c.Color;
                _gridCurves.Rows[row].Cells[2].Style.SelectionBackColor = c.Color;
                _gridCurves.Rows[row].Cells[3].Style.BackColor = c.OutlineColor;
                _gridCurves.Rows[row].Cells[3].Style.SelectionBackColor = c.OutlineColor;
                _gridXyz.Rows.Add(c.Name, range, r.Count.ToString(), F(r.PeakWavelength), F(r.X), F(r.Y), F(r.Z));
            }
            _gridPoints.Rows.Clear();
            for (int i = 0; i < _points.Count; i++)
            {
                CiePoint p = _points[i];
                if (p == null) continue;
                _gridPoints.Rows.Add(p.Name, F2(p.x), F2(p.y), F2(p.U), F2(p.V),
                                     double.IsNaN(p.Cct) ? "—" : p.Cct.ToString("0", WF),
                                     double.IsNaN(p.Duv) ? "" : p.Duv.ToString("0.0000", WF));
            }
            SyncSelectionFlags();
            UpdateHint();
        }

        /// <summary>把表格选中状态同步到数据模型（选中项在画布上高亮，导出时不高亮）。</summary>
        private void SyncSelectionFlags()
        {
            for (int i = 0; i < _curves.Count; i++)
                if (_curves[i] != null) _curves[i].Selected = false;
            for (int i = 0; i < _points.Count; i++)
                if (_points[i] != null) _points[i].Selected = false;
            foreach (DataGridViewRow row in _gridCurves.SelectedRows)
                if (row.Index >= 0 && row.Index < _curves.Count && _curves[row.Index] != null)
                    _curves[row.Index].Selected = true;
            foreach (DataGridViewRow row in _gridPoints.SelectedRows)
                if (row.Index >= 0 && row.Index < _points.Count && _points[row.Index] != null)
                    _points[row.Index].Selected = true;
        }

        private PlotTransform _lastTransform;          // 最近一次渲染的坐标变换（画布点击命中用）

        private static readonly System.Globalization.CultureInfo WF = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>画布点击：命中数据点就选中它，否则取消全部选中。</summary>
        private void CanvasMouseClick(object sender, MouseEventArgs e)
        {
            if (_canvas.Image == null || _lastTransform == null) { ClearSelection(); return; }

            // PictureBox 用 Zoom：把控件坐标换算回图片坐标
            Size img = _canvas.Image.Size;
            double scale = Math.Min((double)_canvas.ClientSize.Width / img.Width,
                                    (double)_canvas.ClientSize.Height / img.Height);
            double offX = (_canvas.ClientSize.Width - img.Width * scale) / 2.0;
            double offY = (_canvas.ClientSize.Height - img.Height * scale) / 2.0;
            double px = (e.X - offX) / scale;
            double py = (e.Y - offY) / scale;

            PlotTransform tr = _lastTransform;
            int hit = -1;
            double best = double.MaxValue;
            for (int i = 0; i < _points.Count; i++)
            {
                CiePoint pt = _points[i];
                if (pt == null) continue;
                double dx, dy;
                PlotSpace.ToDiagram(_style.Kind, pt.x, pt.y, out dx, out dy);
                double cx = tr.X(dx), cy = tr.Y(dy);
                double d = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                if (d < best) { best = d; hit = i; }
            }
            if (hit >= 0 && best <= 14)
            {
                SelectOnlyPoint(hit);
                return;
            }
            ClearSelection();
        }

        private void SelectOnlyPoint(int index)
        {
            _gridCurves.ClearSelection();
            _gridPoints.ClearSelection();
            if (index >= 0 && index < _gridPoints.Rows.Count)
                _gridPoints.Rows[index].Selected = true;
            SyncSelectionFlags();
            ScheduleRender();
        }

        /// <summary>表格里点空白处也取消选中。</summary>
        private void GridMouseClick(object sender, MouseEventArgs e)
        {
            DataGridView grid = sender as DataGridView;
            if (grid == null) return;
            DataGridView.HitTestInfo hit = grid.HitTest(e.X, e.Y);
            if (hit.Type == DataGridViewHitTestType.None)
                ClearSelection();
        }

        /// <summary>点击画布空白处：取消所有选中。</summary>
        /// <summary>导入后自动切到有内容的页签，避免看起来“什么都没导入”。</summary>
        private void ShowRelevantTab()
        {
            if (_curves.Count == 0 && _points.Count > 0) _tabs.SelectedIndex = 2;
            else if (_points.Count == 0 && _curves.Count > 0) _tabs.SelectedIndex = 0;
        }

        private void ClearSelection()
        {
            _gridCurves.ClearSelection();
            _gridPoints.ClearSelection();
            SyncSelectionFlags();
            ScheduleRender();
        }

        /// <summary>把选中项（没有选中时=全部）的颜色或描边统一改掉。</summary>
        private void ApplyColorToSelection(bool outline)
        {
            bool anyCurve = false, anyPoint = false;
            for (int i = 0; i < _curves.Count; i++) if (_curves[i] != null && _curves[i].Selected) anyCurve = true;
            for (int i = 0; i < _points.Count; i++) if (_points[i] != null && _points[i].Selected) anyPoint = true;
            bool useAll = !anyCurve && !anyPoint;

            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = outline ? Color.Black : PointColor;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                int n = 0;
                for (int i = 0; i < _curves.Count; i++)
                {
                    CieCurve c = _curves[i];
                    if (c == null || (!useAll && !c.Selected)) continue;
                    if (outline) c.OutlineColor = dlg.Color; else c.Color = dlg.Color;
                    n++;
                }
                for (int i = 0; i < _points.Count; i++)
                {
                    CiePoint pt = _points[i];
                    if (pt == null || (!useAll && !pt.Selected)) continue;
                    if (outline) pt.OutlineColor = dlg.Color; else pt.Color = dlg.Color;
                    n++;
                }
                UpdateTables();
                ScheduleRender();
                SetStatus((outline ? "已修改描边颜色：" : "已修改颜色：") + n + " 个数据" + (useAll ? "（全部）" : "（选中项）"));
            }
        }

        private static string F(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? "—" : v.ToString("0.####", WF);
        }

        private static string F2(double v)
        {
            return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string F4(double v)
        {
            return v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string F6(double v)
        {
            return v.ToString("0.000000", WF);
        }

        private void UpdateHint()
        {
            if (_hint == null) return;
            _hint.Text = "曲线 " + _curves.Count + " 条 · 坐标点 " + _points.Count + " 个 · 导出 " +
                         ExportWidth + "×" + ExportHeight + " px @" + _exportDpi + " DPI" +
                         (_messages.Count > 0 ? " ｜ " + _messages[_messages.Count - 1] : "");
        }

        private void AddMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _messages.Add(message);
            while (_messages.Count > 40) _messages.RemoveAt(0);
            SetStatus(message);
            UpdateHint();
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.Text = text;
            if (_hint != null) UpdateHint();
        }

        // ================================================================ 表格交互（改名 / 右键菜单）

        private void GridCurveCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= _curves.Count) return;
            CieCurve curve = _curves[e.RowIndex];
            if (curve == null) return;
            string name = Prompt("曲线名称", curve.Name);
            if (name == null) return;
            curve.Name = name.Trim().Length == 0 ? curve.Name : name.Trim();
            UpdateTables();
            ScheduleRender();
        }

        private void GridPointCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= _points.Count) return;
            CiePoint point = _points[e.RowIndex];
            if (point == null) return;
            string name = Prompt("坐标点名称", point.Name);
            if (name == null) return;
            point.Name = name.Trim().Length == 0 ? point.Name : name.Trim();
            UpdateTables();
            ScheduleRender();
        }

        private void GridCurveCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.RowIndex >= _curves.Count) return;
            CieCurve curve = _curves[e.RowIndex];
            if (curve == null) return;
            _gridCurves.ClearSelection();
            _gridCurves.Rows[e.RowIndex].Selected = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            AddShapeMenu(menu, delegate (MarkerShape shape) { curve.Shape = shape; UpdateTables(); ScheduleRender(); });
            ToolStripMenuItem color = new ToolStripMenuItem("颜色…");
            color.Click += delegate
            {
                using (ColorDialog dlg = new ColorDialog())
                {
                    dlg.Color = curve.Color;
                    dlg.FullOpen = true;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        curve.Color = dlg.Color;
                        UpdateTables();
                        ScheduleRender();
                    }
                }
            };
            menu.Items.Add(color);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem label = new ToolStripMenuItem("显示标签");
            label.Checked = curve.ShowLabel;
            label.Click += delegate
            {
                curve.ShowLabel = !curve.ShowLabel;
                if (curve.ShowLabel && !_chkLabels.Checked) _chkLabels.Checked = true;
                UpdateTables();
                ScheduleRender();
            };
            menu.Items.Add(label);
            ToolStripMenuItem rename = new ToolStripMenuItem("重命名…");
            rename.Click += delegate
            {
                string name = Prompt("曲线名称", curve.Name);
                if (name == null) return;
                curve.Name = name.Trim().Length == 0 ? curve.Name : name.Trim();
                UpdateTables();
                ScheduleRender();
            };
            menu.Items.Add(rename);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem remove = new ToolStripMenuItem("删除这条曲线");
            remove.Click += delegate
            {
                _curves.Remove(curve);
                UpdateTables();
                ScheduleRender();
            };
            menu.Items.Add(remove);
            menu.Show(_gridCurves, _gridCurves.PointToClient(Cursor.Position));
        }

        private void GridPointCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.RowIndex >= _points.Count) return;
            CiePoint point = _points[e.RowIndex];
            if (point == null) return;
            _gridPoints.ClearSelection();
            _gridPoints.Rows[e.RowIndex].Selected = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            AddShapeMenu(menu, delegate (MarkerShape shape) { point.Shape = shape; UpdateTables(); ScheduleRender(); });
            ToolStripMenuItem color = new ToolStripMenuItem("颜色…");
            color.Click += delegate
            {
                using (ColorDialog dlg = new ColorDialog())
                {
                    dlg.Color = point.Color;
                    dlg.FullOpen = true;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        point.Color = dlg.Color;
                        UpdateTables();
                        ScheduleRender();
                    }
                }
            };
            menu.Items.Add(color);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem label = new ToolStripMenuItem("显示标签");
            label.Checked = point.ShowLabel;
            label.Click += delegate
            {
                point.ShowLabel = !point.ShowLabel;
                if (point.ShowLabel && !_chkLabels.Checked) _chkLabels.Checked = true;
                UpdateTables();
                ScheduleRender();
            };
            menu.Items.Add(label);
            ToolStripMenuItem rename = new ToolStripMenuItem("重命名…");
            rename.Click += delegate
            {
                string name = Prompt("坐标点名称", point.Name);
                if (name == null) return;
                point.Name = name.Trim().Length == 0 ? point.Name : name.Trim();
                UpdateTables();
                ScheduleRender();
            };
            menu.Items.Add(rename);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem remove = new ToolStripMenuItem("删除这个坐标点");
            remove.Click += delegate
            {
                _points.Remove(point);
                UpdateTables();
                ScheduleRender();
            };
            menu.Items.Add(remove);
            menu.Show(_gridPoints, _gridPoints.PointToClient(Cursor.Position));
        }

        private static void AddShapeMenu(ContextMenuStrip menu, Action<MarkerShape> apply)
        {
            ToolStripMenuItem root = new ToolStripMenuItem("标记形状");
            string[] names = { "圆形", "方形", "三角", "菱形", "星形", "叉号" };
            MarkerShape[] shapes = { MarkerShape.Circle, MarkerShape.Square, MarkerShape.Triangle,
                                     MarkerShape.Diamond, MarkerShape.Star, MarkerShape.Cross };
            for (int i = 0; i < shapes.Length; i++)
            {
                MarkerShape shape = shapes[i];
                ToolStripMenuItem item = new ToolStripMenuItem(names[i]);
                item.Click += delegate { apply(shape); };
                root.DropDownItems.Add(item);
            }
            menu.Items.Add(root);
        }

        /// <summary>简易输入框（WinForms 没有内置 InputBox）。</summary>
        private void GridCurveKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete) return;
            List<int> rows = new List<int>();
            foreach (DataGridViewRow row in _gridCurves.SelectedRows)
                if (row.Index >= 0) rows.Add(row.Index);
            rows.Sort();
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (rows[i] < _curves.Count) _curves.RemoveAt(rows[i]);
            }
            e.Handled = true;
            UpdateTables();
            ScheduleRender();
            SetStatus("已删除 " + rows.Count + " 条曲线。");
        }

        private void GridPointKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete) return;
            List<int> rows = new List<int>();
            foreach (DataGridViewRow row in _gridPoints.SelectedRows)
                if (row.Index >= 0) rows.Add(row.Index);
            rows.Sort();
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (rows[i] < _points.Count) _points.RemoveAt(rows[i]);
            }
            e.Handled = true;
            UpdateTables();
            ScheduleRender();
            SetStatus("已删除 " + rows.Count + " 个坐标点。");
        }

        /// <summary>“输入坐标”：直接粘贴/输入 x,y 文本，不经过文件。</summary>
        private void InputCoordinates()
        {
            using (Form dlg = CreateDialog("输入坐标", new Size(420, 320)))
            {
                dlg.MinimumSize = new Size(360, 260);

                Label tip = new Label();
                tip.Dock = DockStyle.Top;
                tip.Height = 44;
                tip.Text = "每行一个点，支持「名称,x,y」或「x,y」；也可从 Excel 直接粘贴。";
                tip.Padding = new Padding(8, 6, 8, 0);
                tip.SizeChanged += delegate { FitWrappedLabel(tip); };
                tip.FontChanged += delegate { FitWrappedLabel(tip); };
                tip.DpiChangedAfterParent += delegate { FitWrappedLabel(tip); };
                dlg.Controls.Add(tip);

                TextBox box = new TextBox();
                box.Multiline = true;
                box.ScrollBars = ScrollBars.Vertical;
                box.Dock = DockStyle.Fill;
                box.Font = new Font("Consolas", 9.5F);
                dlg.Controls.Add(box);
                box.BringToFront();

                Button ok = MakeBarButton("确定", false);
                ok.DialogResult = DialogResult.OK;
                Button cancel = MakeBarButton("取消", false);
                cancel.DialogResult = DialogResult.Cancel;
                Panel buttons = MakeDialogButtons(ok, cancel);
                dlg.Controls.Add(buttons);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                dlg.ResumeLayout(true);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string text = box.Text;
                if (string.IsNullOrWhiteSpace(text)) return;
                ParseResult r = CoordinateParser.Parse(text);
                foreach (CiePoint p in r.Points) _points.Add(p);
                foreach (string w in r.Warnings) AddMessage(w);
                foreach (string e in r.Errors) AddMessage("⚠ " + e);
                UpdateTables();
                ShowRelevantTab();
                ScheduleRender();
                SetStatus("已输入 " + r.Points.Count + " 个色坐标点。");
            }
        }

        private string Prompt(string title, string defaultValue)
        {
            using (Form dlg = CreateDialog(title, new Size(360, 110)))
            {
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;

                TextBox box = new TextBox();
                box.SetBounds(16, 18, 328, 26);
                box.Text = defaultValue ?? "";
                box.SelectAll();
                dlg.Controls.Add(box);

                Button ok = MakeBarButton("确定", false);
                ok.DialogResult = DialogResult.OK;

                Button cancel = MakeBarButton("取消", false);
                cancel.DialogResult = DialogResult.Cancel;
                dlg.Controls.Add(MakeDialogButtons(ok, cancel));

                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                dlg.ResumeLayout(true);
                return dlg.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }

        private static Form CreateDialog(string title, Size clientSize)
        {
            Form dlg = new Form();
            dlg.SuspendLayout();
            dlg.AutoScaleDimensions = new SizeF(96F, 96F);
            dlg.AutoScaleMode = AutoScaleMode.Dpi;
            // 对话框使用独立的 96dpi 基准，避免默认缩放模式与固定边界混用。
            dlg.Font = new Font("Microsoft YaHei UI", 9F);
            dlg.Text = title;
            dlg.ClientSize = clientSize;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.MinimizeBox = false;
            dlg.MaximizeBox = false;
            return dlg;
        }

        private static void FitWrappedLabel(Label label)
        {
            int width = Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal);
            label.Height = TextRenderer.MeasureText(label.Text, label.Font,
                new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height
                + label.Padding.Vertical;
        }

        private static Panel MakeDialogButtons(Button ok, Button cancel)
        {
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(8);
            cancel.Margin = new Padding(0);
            ok.Margin = new Padding(0, 0, ButtonSpacing, 0);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            return buttons;
        }

        // ================================================================ 样式交互

        private void OnStyleChanged()
        {
            if (_suppressUiEvents) return;
            // 位图底图的坐标轴是烘焙在图片里的，无法改变范围；手动轴范围时自动切到矢量背景
            _style.ShowBlackbody = _chkBlackbody.Checked;
            _style.ShowGrid = _chkGrid.Checked;
            _style.ShowWavelengthLabels = _chkWavelength.Checked;
            _style.ShowLegend = _chkLegend.Checked;
            _style.ShowPointLabels = _chkLabels.Checked;
            // 位图底图：黑体线开关直接决定用哪张底图（原版带烘焙轨迹的那张 / 干净的那张），
            // 位图模式下不再叠加计算版轨迹，避免两条曲线重叠；矢量模式则由渲染器按开关现算。
            _style.BitmapWithBlackbody = _style.ShowBlackbody;
            _style.Bounds = FixedBounds();
            ScheduleRender();
        }

        private void OnKindChanged()
        {
            if (_suppressUiEvents) return;
            bool uv = _cmbKind.SelectedIndex == 1;
            _style.Kind = uv ? DiagramKind.Uv1976 : DiagramKind.Xy1931;
            _suppressUiEvents = true;
            int keep = _cmbBackground.SelectedIndex;
            _cmbBackground.Items.Clear();
            if (uv)
            {
                // 1976 图没有位图底图，只保留“矢量背景”
                _cmbBackground.Items.Add("矢量背景");
                _cmbBackground.SelectedIndex = 0;
                _style.UseBitmapBackground = false;
            }
            else
            {
                _cmbBackground.Items.Add("位图底图");
                _cmbBackground.Items.Add("矢量背景");
                _cmbBackground.SelectedIndex = keep < 2 ? keep : 0;
                _style.UseBitmapBackground = _cmbBackground.SelectedIndex == 0;
            }
            _suppressUiEvents = false;
            _style.Bounds = FixedBounds();
            ScheduleRender();
        }

        private void OnBackgroundChanged()
        {
            if (_suppressUiEvents) return;
            _style.UseBitmapBackground = _cmbBackground.SelectedIndex == 0;
            ScheduleRender();
        }

        private void ToggleKind()
        {
            _cmbKind.SelectedIndex = _cmbKind.SelectedIndex == 0 ? 1 : 0;
        }

        private void ToggleBackground()
        {
            _cmbBackground.SelectedIndex = _cmbBackground.SelectedIndex == 0 ? 1 : 0;
        }

        private void ToggleBlackbody()
        {
            _chkBlackbody.Checked = !_chkBlackbody.Checked;
        }

        private void ToggleCheck(CheckBox chk)
        {
            if (chk != null) chk.Checked = !chk.Checked;
        }

        private void ScheduleRender()
        {
            if (_renderTimer == null) return;
            _renderTimer.Stop();
            _renderTimer.Start();
        }

        private void RenderNow()
        {
            try
            {
                _exportDpi = (int)_numDpi.Value;

                // 预览按画布控件的实际像素出图（1:1，字号不缩小）；导出固定 1400×1400。
                int previewW = 1000;
                int previewH = 1000;
                if (_canvas.ClientSize.Width > 300 && _canvas.ClientSize.Height > 300)
                {
                    previewW = Math.Min(_canvas.ClientSize.Width, 2400);
                    previewH = Math.Min(_canvas.ClientSize.Height, 2400);
                }

                using (Bitmap bmp = Exporter.RenderToBitmap(_style, _curves, _points, previewW, previewH, 96, out _lastTransform))
                {
                    Bitmap old = _canvas.Image as Bitmap;
                    _canvas.SizeMode = PictureBoxSizeMode.Zoom;
                    _canvas.Image = (Bitmap)bmp.Clone();
                    if (old != null) old.Dispose();
                }
                UpdateHint();
            }
            catch (Exception ex)
            {
                AddMessage("⚠ 绘图失败：" + ex.Message);
            }
        }

        // ================================================================ 导出

        /// <summary>
        /// 批量修改名称：像 Word 的“查找/替换”，可选范围（曲线/坐标点）、区分大小写，
        /// 替换文本里写 {n} 会换成序号（1、2、3…）。
        /// </summary>
        private void BatchRename()
        {
            if (_curves.Count == 0 && _points.Count == 0)
            {
                SetStatus("没有可改名的数据。");
                return;
            }

            // 预填：选中的那条数据的名称 → 否则第一条（曲线优先）
            string initial = "";
            for (int i = 0; i < _curves.Count; i++)
                if (_curves[i] != null && _curves[i].Selected) { initial = _curves[i].Name; break; }
            if (initial.Length == 0)
                for (int i = 0; i < _points.Count; i++)
                    if (_points[i] != null && _points[i].Selected) { initial = _points[i].Name; break; }
            if (initial.Length == 0 && _curves.Count > 0 && _curves[0] != null) initial = _curves[0].Name;
            if (initial.Length == 0 && _points.Count > 0 && _points[0] != null) initial = _points[0].Name;

            using (Form dlg = CreateDialog("批量修改名称", new Size(440, 252)))
            {
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;

                Label l2 = new Label(); l2.AutoSize = true; l2.Text = "查找"; l2.Location = new Point(16, 20);
                TextBox find = new TextBox(); find.SetBounds(80, 17, 320, 24);
                find.Text = initial;

                Label l3 = new Label(); l3.AutoSize = true; l3.Text = "替换为"; l3.Location = new Point(16, 58);
                TextBox repl = new TextBox(); repl.SetBounds(80, 55, 320, 24);

                CheckBox matchCase = new CheckBox();
                matchCase.Text = "区分大小写";
                matchCase.SetBounds(80, 86, 120, 22);
                matchCase.AutoSize = true;

                Label tip = new Label();
                tip.SetBounds(16, 110, 408, 46);
                tip.ForeColor = Color.FromArgb(0x6B, 0x74, 0x80);
                tip.Text = "查找留空 = 全部改成“替换为”；替换文本里 {n} 会变成序号。全部数据（曲线+坐标点）一起处理。";

                Button ok = MakeBarButton("开始替换", false); ok.DialogResult = DialogResult.OK;
                Button cancel = MakeBarButton("取消", false); cancel.DialogResult = DialogResult.Cancel;

                dlg.Controls.Add(l2); dlg.Controls.Add(find);
                dlg.Controls.Add(l3); dlg.Controls.Add(repl);
                dlg.Controls.Add(matchCase); dlg.Controls.Add(tip);
                dlg.Controls.Add(MakeDialogButtons(ok, cancel));
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                dlg.ResumeLayout(true);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string needle = find.Text;
                string replacement = repl.Text;
                bool ignoreCase = !matchCase.Checked;
                int changed = 0;
                int index = 0;

                for (int i = 0; i < _curves.Count; i++)
                {
                    CieCurve c = _curves[i];
                    if (c == null) continue;
                    index++;
                    string updated = ReplaceName(c.Name, needle, replacement, ignoreCase, index);
                    if (updated != c.Name) { c.Name = updated; changed++; }
                }
                index = 0;
                for (int i = 0; i < _points.Count; i++)
                {
                    CiePoint pt = _points[i];
                    if (pt == null) continue;
                    index++;
                    string updated = ReplaceName(pt.Name, needle, replacement, ignoreCase, index);
                    if (updated != pt.Name) { pt.Name = updated; changed++; }
                }

                UpdateTables();
                ScheduleRender();
                SetStatus("批量改名完成：共修改 " + changed + " 个名称。");
            }
        }

        private static string ReplaceName(string name, string needle, string replacement, bool ignoreCase, int index)
        {
            if (string.IsNullOrEmpty(name)) name = "";
            string result;
            if (string.IsNullOrEmpty(needle))
            {
                result = replacement;                  // 查找留空 = 统一改名
            }
            else
            {
                StringComparison cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (name.IndexOf(needle, cmp) < 0) return name;
                result = ignoreCase
                    ? System.Text.RegularExpressions.Regex.Replace(name,
                        System.Text.RegularExpressions.Regex.Escape(needle), replacement,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    : name.Replace(needle, replacement);
            }
            return result.Replace("{n}", index.ToString());
        }

        private void ApplyExportName(string path)
        {
            if (!string.IsNullOrEmpty(path)) _lastExportName = Path.GetFileName(path);
        }

        private void ExportImage()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG 图片 (*.png)|*.png|TIFF 图片 (*.tif)|*.tif|SVG 矢量图 (*.svg)|*.svg";
                dlg.FilterIndex = 1;
                dlg.FileName = _lastExportName;
                dlg.OverwritePrompt = true;
                dlg.Title = "导出图片（PNG / TIFF / SVG）";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ApplyExportName(dlg.FileName);
                DiagramStyle exportStyle = _style.Clone();
                exportStyle.HighlightSelection = false;   // 导出不把“选中高亮”带进图里
                try
                {
                    string path = dlg.FileName;
                    if (dlg.FilterIndex == 3 || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    {
                        Exporter.SaveSvg(exportStyle, _curves, _points, path, ExportWidth, ExportHeight);
                        AddMessage("已导出 SVG 矢量图：" + Path.GetFileName(path));
                    }
                    else if (dlg.FilterIndex == 2 || path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                    {
                        Exporter.SaveBitmap(exportStyle, _curves, _points, path, ImageFormat.Tiff, ExportWidth, ExportHeight, _exportDpi);
                        AddMessage("已导出 TIFF：" + Path.GetFileName(path) + "（" + _exportDpi + " DPI）");
                    }
                    else
                    {
                        Exporter.SaveBitmap(exportStyle, _curves, _points, path, ImageFormat.Png, ExportWidth, ExportHeight, _exportDpi);
                        AddMessage("已导出 PNG：" + Path.GetFileName(path) + "（" + _exportDpi + " DPI）");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportData()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "结果 CSV (*.csv)|*.csv|计算报告 TXT (*.txt)|*.txt";
                dlg.FilterIndex = 1;
                dlg.FileName = Path.GetFileNameWithoutExtension(_lastExportName) + ".csv";
                dlg.OverwritePrompt = true;
                dlg.Title = "导出计算结果";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (dlg.FilterIndex == 2 || dlg.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        Exporter.SaveTxt(dlg.FileName, _curves, _points, _style, ExportWidth, ExportHeight);
                        AddMessage("已导出计算报告：" + Path.GetFileName(dlg.FileName));
                    }
                    else
                    {
                        Exporter.SaveCsv(dlg.FileName, _curves, _points);
                        AddMessage("已导出结果 CSV：" + Path.GetFileName(dlg.FileName));
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ================================================================ 拖放/菜单

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) ImportFiles(files);
        }

        private void OpenClassic()
        {
            // 经典界面按 96dpi 固定布局绘制，必须跑在 DpiUnaware 进程里才和原版 1.6.0.2 一致。
            // 主界面是 PerMonitorV2，所以单独起一个 --classic 进程，避免被 DPI 二次缩放（文字被切）。
            try
            {
                string exe = Application.ExecutablePath;
                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo(exe, "--classic");
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                System.Diagnostics.Process.Start(psi);
                return;
            }
            catch (Exception ex)
            {
                AddMessage("⚠ 无法单独启动经典界面，改为内嵌打开：" + ex.Message);
            }

            Form1 classic = new Form1();
            classic.AutoScaleMode = AutoScaleMode.None;
            classic.StartPosition = FormStartPosition.CenterParent;
            classic.Show(this);
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "1. 导入数据：文件 → 导入光谱文件 / 导入色坐标文件，或把 CSV/TXT 直接拖进窗口，或 Ctrl+V 粘贴。\n" +
                "   光谱格式：波长 强度（逗号/分号/Tab/空格分隔均可，自动识别表头与备注列）；\n" +
                "   坐标格式：名称,x,y（表头含 u'/v' 时按 1976 UCS 解析）。\n" +
                "2. 图型：顶部「图型」可在 CIE 1931 xy 与 CIE 1976 u'v' 之间切换。\n" +
                "3. 背景：「位图底图」= 原版程序的背景图；「矢量背景」= 程序实时计算的真彩色背景，导出的 SVG 是纯矢量。\n" +
                "4. 黑体线：实体按钮「黑体线」开/关（也可以用 Ctrl+P）。\n" +
                "5. 导出：图片固定 1400×1400 像素（DPI 越大分辨率越高），无需设置画布大小。\n" +
                "6. 导出：图片支持 PNG / TIFF / SVG；数据支持结果 CSV 与计算报告 TXT。\n\n" +
                "经典界面（原版 1.6.0.2 布局）可通过 视图 → 打开经典界面 打开，或启动参数 --classic。",
                "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "CIE1931 色度分析器 2.0.0\n\n" +
                "基于 nicklxg (2009) 的 CIE1931xy V.1.6.0.2 逆向重建版扩展：\n" +
                "· 保留原版计算口径与经典界面（Form1）\n" +
                "· 新增多曲线/多坐标点、双图型、矢量渲染、SVG 导出、CCT/Δuv\n\n" +
                "原程序作者：nicklxg 2009  E-mail: nicklxg@163.com\n" +
                "重建与扩展：CIE1931xy.Rebuilt",
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
