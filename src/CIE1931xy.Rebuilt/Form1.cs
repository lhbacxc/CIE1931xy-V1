using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CIE1931xy.Core;

namespace CIE1931xy
{
	/// <summary>
	/// 原版 <c>Form1</c> 的全部界面逻辑。与原版反编译代码逐段对应、行为一致，
	/// 但把数值计算拆到了 <see cref="CieMath"/>、文本解析拆到了 <see cref="SpectralInput"/>、
	/// 数据表拆到了 <see cref="CieData"/>，本文件只负责"界面 + 状态"。
	/// <para>
	/// 在还原的基础上新增了两个可选功能（不影响原版行为）：
	/// <list type="bullet">
	/// <item><b>Ctrl+B</b>：在"带黑体辐射轨迹"/"不带黑体辐射轨迹"两张原版背景图之间切换；</item>
	/// <item><b>Ctrl+P</b>：叠加/取消<b>代码实时计算</b>的黑体轨迹（<see cref="PlanckianOverlay"/>，可改色温范围、刻度、颜色）。</item>
	/// </list>
	/// </para>
	/// </summary>
	public partial class Form1 : Form
	{
		#region 与原版同名的状态字段

		private double[,] cie1931xyz;
		private double[,] ccxy1931;
		private double Data_Row;
		private int Offset;
		// 原版还有个 DrawNum 字段：声明了但从未使用（反编译可见），这里不再保留，避免 CS0169 警告。
		private int CalcNum;
		private int PeakLambda;
		private double CC_x;
		private double CC_y;
		private double TSV_X;
		private double TSV_Y;
		private double TSV_Z;
		private double UCS_up;
		private double UCS_vp;
		private double PeakInt;
		private double rgb_r;
		private double rgb_g;
		private double rgb_b;
		private int Axes_Width;
		private int Axes_Height;
		private int Grid_Width;
		private double pixelperxy;
		private Point O;
		private int LangFlag;
		private string[] strDataInput;
		private double[] strTSV_X;
		private double[] strTSV_Y;
		private double[] strTSV_Z;
		private double[] strUCS_up;
		private double[] strUCS_vp;
		private string[,] LabelText;
		private string[,] ButtonText;
		private string[] GroupBoxText;
		private string[,] MessageTitleText;
		private string[,] MessageInfoText;
		private string[,] MessageExclamText;
		private string[,] MessageErrorText;
		private string[] HelpText;
		private double[,] pdata;
		private Bitmap Canvas;
		private Bitmap Bgimage;
		private string strFilename;

		#endregion

		#region 重建版新增的状态

		/// <summary>已经"描绘"过的色坐标（xy 单位）；重绘时按顺序补画，避免切换背景/叠加层时丢失。</summary>
		private readonly List<PointF> drawnPoints = new List<PointF>();

		/// <summary>黑体轨迹叠加层的样式参数（色温范围、刻度、颜色都在这里改）。</summary>
		private readonly PlanckianOverlayOptions planckianOptions = new PlanckianOverlayOptions();

		/// <summary>是否叠加代码绘制的黑体轨迹（Ctrl+P 切换，或启动参数 --planckian）。</summary>
		private bool showPlanckianOverlay = Program.StartWithPlanckian;

		#endregion

		public Form1()
		{
			Load += Form1_Load;

			cie1931xyz = (double[,])CieData.Cie1931xyz.Clone();
			ccxy1931 = (double[,])CieData.Ccxy1931.Clone();
			pdata = new double[SpectralInput.MaxRows, 2];
			strDataInput = new string[11];
			strTSV_X = new double[11];
			strTSV_Y = new double[11];
			strTSV_Z = new double[11];
			strUCS_up = new double[11];
			strUCS_vp = new double[11];
			LabelText = (string[,])CieData.LabelText.Clone();
			ButtonText = (string[,])CieData.ButtonText.Clone();
			GroupBoxText = (string[])CieData.GroupBoxText.Clone();
			MessageTitleText = (string[,])CieData.MessageTitleText.Clone();
			MessageInfoText = (string[,])CieData.MessageInfoText.Clone();
			MessageExclamText = (string[,])CieData.MessageExclamText.Clone();
			MessageErrorText = (string[,])CieData.MessageErrorText.Clone();
			HelpText = (string[])CieData.HelpText.Clone();

			Canvas = (Bitmap)AppResources.Diagram.Clone();
			Bgimage = (Bitmap)Canvas.Clone();
			InitializeComponent();
			KeyPreview = true;
			KeyDown += Form1_KeyDown;
		}

		/// <summary>当前"xy → 像素"的映射参数（与原版 btnDrawPoint_Click 中的公式一致）。</summary>
		private DiagramMapping Mapping => new DiagramMapping(O.X, O.Y, Axes_Height, pixelperxy);

		#region 语言 / 界面文字

		/// <summary>
		/// 把当前语言（<see cref="LangFlag"/>：2=中文，3=英文）的第 1 列刷新到各控件。
		/// 原版把这段代码在 Form1_Load 与 btnLang_Click 里各抄了一遍，这里合并成一个方法。
		/// </summary>
		private void ApplyLanguageTexts()
		{
			for (int i = 0; i <= 4; i++)
				LabelText[i, 1] = LabelText[i, LangFlag];
			for (int i = 0; i <= 9; i++)
				ButtonText[i, 1] = ButtonText[i, LangFlag];
			for (int i = 0; i <= 5; i++)
				MessageTitleText[i, 1] = MessageTitleText[i, LangFlag];
			for (int i = 0; i <= 2; i++)
				MessageInfoText[i, 1] = MessageInfoText[i, LangFlag];
			for (int i = 0; i <= 1; i++)
			{
				MessageExclamText[i, 1] = MessageExclamText[i, LangFlag];
				MessageErrorText[i, 1] = MessageErrorText[i, LangFlag];
			}
			GroupBoxText[1] = GroupBoxText[LangFlag];
			HelpText[1] = HelpText[LangFlag];

			Text = MessageTitleText[5, 1];
			lblDataInput.Text = LabelText[0, 1];
			lblResult.Text = LabelText[1, 1];
			// 这三个标签在数据表里中文是"一列一个字"（"色\r\n坐\r\n标"），在 26 px 高的
			// 固定标签里会显示不全。中文时去掉手工换行、把标签加宽到 42 px 自动折行；
			// 英文（"CIE\r\n1931"）保持原来的两行样式。
			ApplyResultLabel(lblCIE, LabelText[2, 1]);
			ApplyResultLabel(lblTSV, LabelText[3, 1]);
			ApplyResultLabel(lblUCS, LabelText[4, 1]);
			btnLang.Text = ButtonText[0, 1];
			btnHelp.Text = ButtonText[1, 1];
			btnCalc.Text = ButtonText[2, 1];
			btnDrawPoint.Text = ButtonText[3, 1];
			btnClrInput.Text = ButtonText[4, 1];
			btnClrRec.Text = ButtonText[5, 1];
			btnClrPoint.Text = ButtonText[6, 1];
			btnExpGraph.Text = ButtonText[7, 1];
			RadioBtn1nm.Text = ButtonText[8, 1];
			RadioBtn5nm.Text = ButtonText[9, 1];
			GrpBoxDataFormat.Text = GroupBoxText[1];

			// 中文比英文长，"清空输入/每1nm一个数据/均匀色空间"等文字在原版设计的
			// 固定宽高里会显示不全；这里按控件实际尺寸自动微调字号，保证文字完整可见。
			FitTextsToBounds();
		}

		/// <summary>
		/// 结果区左侧三个窄标签（色坐标 / 三刺激值 / 均匀色空间）。
		/// 中文去手工换行并加宽到 42 px，英文保留 "CIE\r\n1931" 的两行样式。
		/// </summary>
		private void ApplyResultLabel(Label label, string text)
		{
			label.AutoSize = false;
			if (LangFlag == 3 && text.IndexOf('\n') >= 0)
			{
				// 中文用两三个字的短标签，42 px 宽一行放得下，不会被压成肉眼难辨的小字。
				string compact = ReferenceEquals(label, lblCIE) ? "色度"
					: ReferenceEquals(label, lblTSV) ? "三刺激"
					: "均匀色";
				label.Text = compact;
				label.Width = 42;
				label.Height = 26;
			}
			else
			{
				label.Text = text;
				label.Width = 31;
				label.Height = 26;
			}
			label.TextAlign = ContentAlignment.MiddleLeft;
		}

		/// <summary>把控件树里所有文字标签/按钮的字号收敛到"能放下"的最大字号。</summary>
		private void FitTextsToBounds()
		{
			FitTexts(this);
		}

		private static void FitTexts(Control parent)
		{
			foreach (Control c in parent.Controls)
			{
				if (c.Controls.Count > 0)
					FitTexts(c);

				if (string.IsNullOrEmpty(c.Text))
					continue;
				if (!(c is Button) && !(c is CheckBox) && !(c is RadioButton) && !(c is Label))
					continue;

				int availW = Math.Max(8, c.ClientSize.Width - 6);
				int availH = Math.Max(8, c.ClientSize.Height - 2);
				if (TextFits(c.Text, c.Font, availW, availH))
					continue;

				// 先缩字号（下限 6.25pt）
				Font fitted = null;
				for (float size = c.Font.SizeInPoints - 0.25f; size >= 6.25f; size -= 0.25f)
				{
					Font candidate = new Font(c.Font.FontFamily, size, c.Font.Style);
					if (TextFits(c.Text, candidate, availW, availH))
					{
						fitted = candidate;
						break;
					}
					candidate.Dispose();
				}
				if (fitted != null)
				{
					c.Font = fitted;
					continue;
				}

				// 缩到最小仍放不下：放开 AutoSize 把控件撑到文字实际大小，避免底部被切一半
				Size need = TextRenderer.MeasureText(c.Text, c.Font);
				c.AutoSize = false;
				int w = Math.Max(c.Width, need.Width + 10);
				int h = Math.Max(c.Height, need.Height + 6);
				if (w != c.Width || h != c.Height)
				{
					int newLeft = c.Left;
					if (c.Parent != null && newLeft + w > c.Parent.ClientSize.Width - 2)
						newLeft = Math.Max(0, c.Parent.ClientSize.Width - 2 - w);
					c.SetBounds(newLeft, c.Top, w, h);
				}
			}
		}

		private static bool TextFits(string text, Font font, int width, int height)
		{
			Size size = TextRenderer.MeasureText(text, font, new Size(width, 0), TextFormatFlags.WordBreak);
			return size.Width <= width && size.Height <= height;
		}

		private void btnLang_Click(object sender, EventArgs e)
		{
			if (LangFlag == 2)
			{
				LangFlag = 3;
				ApplyLanguageTexts();
			}
			else if (LangFlag == 3)
			{
				LangFlag = 2;
				ApplyLanguageTexts();
			}
		}

		#endregion

		private void Form1_Load(object sender, EventArgs e)
		{
			RadioBtn1nm.Checked = true;
			Axes_Width = 600;
			Axes_Height = (int)Math.Round(600.0 * 9.0 / 8.0);
			Grid_Width = (int)Math.Round(600.0 / 40.0);
			pixelperxy = 0.8 / 600.0;
			// 原版在构造函数里写的是 O = new Point(59, 12)：底图上坐标轴原点正好在这个像素位置。
			// 这里必须保留，否则"描绘坐标"画出的点会整体偏左上 59 px / 12 px。
			O = new Point(59, 12);
			LangFlag = 2;
			DataGridViewRec.AllowUserToResizeRows = false;
			for (int i = 1; i <= 10; i++)
				DataGridViewRec.Rows.Add(i, "", "", "", "");

			// 原版这三个标签只有 31 px 宽（英文 "CIE 1931" 两行刚好）；
			// 中文"三刺激值/均匀色空间"会被压成挤在一起的小字。往右加宽 11 px
			// （右边是 x=209 的文本框，不会重叠），中文就能正常换行显示。
			ApplyLanguageTexts();

			// 启动参数 --planckian 时，一开机就把"代码实时计算"的黑体轨迹画上去（等价于按了 Ctrl+P）。
			if (showPlanckianOverlay)
				RenderCanvas();
		}

		#region 计算

		private void btnCalc_Click(object sender, EventArgs e)
		{
			TSV_X = 0.0;
			TSV_Y = 0.0;
			TSV_Z = 0.0;

			if (!SpectralInput.TryParse(txtBoxDataInput.Text, out pdata, out int dataRow, out ParseFailure failure))
			{
				switch (failure.Status)
				{
					case ParseStatus.Empty:
						MessageBox.Show(MessageInfoText[0, 1], MessageTitleText[0, 1], MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
						return;
					case ParseStatus.IllegalCharacter:
						MessageBox.Show(MessageExclamText[0, 1] + failure.Character + "\r\n" + MessageExclamText[1, 1] + failure.CharacterCode,
							MessageExclamText[1, 1], MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						return;
					default:
						MessageBox.Show(MessageErrorText[1, 1], MessageTitleText[2, 1], MessageBoxButtons.OK, MessageBoxIcon.Hand);
						return;
				}
			}

			Data_Row = dataRow;

			DataSpacing spacing;
			if (RadioBtn5nm.Checked)
				spacing = DataSpacing.FiveNm;
			else if (RadioBtn1nm.Checked)
				spacing = DataSpacing.OneNm;
			else
				spacing = DataSpacing.None;

			CieMath.Status status = CieMath.Calculate(pdata, dataRow, spacing, out CieMath.Result result);
			if (status == CieMath.Status.NoFormatSelected)
			{
				MessageBox.Show(MessageInfoText[1, 1], MessageTitleText[0, 1], MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			if (status == CieMath.Status.SpacingMismatch)
			{
				MessageBox.Show(MessageErrorText[0, 1], MessageTitleText[2, 1], MessageBoxButtons.OK, MessageBoxIcon.Hand);
				return;
			}

			Offset = result.Offset;
			PeakLambda = result.PeakLambda;
			PeakInt = result.PeakInt;
			TSV_X = result.TSV_X;
			TSV_Y = result.TSV_Y;
			TSV_Z = result.TSV_Z;
			CC_x = result.CC_x;
			CC_y = result.CC_y;
			UCS_up = result.UCS_up;
			UCS_vp = result.UCS_vp;
			rgb_r = result.RGB_r;
			rgb_g = result.RGB_g;
			rgb_b = result.RGB_b;
			CalcNum++;

			txtBoxCIEx.Text = VbText.FormatDouble(CC_x);
			txtBoxCIEy.Text = VbText.FormatDouble(CC_y);
			txtBoxTSVX.Text = VbText.FormatDouble(TSV_X);
			txtBoxTSVY.Text = VbText.FormatDouble(TSV_Y);
			txtBoxTSVZ.Text = VbText.FormatDouble(TSV_Z);
			txtBoxUCSup.Text = VbText.FormatDouble(UCS_up);
			txtBoxUCSvp.Text = VbText.FormatDouble(UCS_vp);

			if (CalcNum > 10)
			{
				DataGridViewRec.Rows.Add(CalcNum, "", "", "", "");
				DataGridViewRec.Rows[CalcNum - 1].Cells[0].Value = CalcNum;
				strDataInput = VbText.Grow(strDataInput, CalcNum + 1);
				strTSV_X = VbText.Grow(strTSV_X, CalcNum + 1);
				strTSV_Y = VbText.Grow(strTSV_Y, CalcNum + 1);
				strTSV_Z = VbText.Grow(strTSV_Z, CalcNum + 1);
				strUCS_up = VbText.Grow(strUCS_up, CalcNum + 1);
				strUCS_vp = VbText.Grow(strUCS_vp, CalcNum + 1);
			}

			DataGridViewRec.CurrentCell = DataGridViewRec[0, CalcNum - 1];
			DataGridViewRec.Rows[CalcNum - 1].Cells[1].Value = CC_x;
			DataGridViewRec.Rows[CalcNum - 1].Cells[2].Value = CC_y;
			DataGridViewRec.Rows[CalcNum - 1].Cells[3].Value = PeakLambda;
			DataGridViewRec.Rows[CalcNum - 1].Cells[4].Value = PeakInt;
			strDataInput[CalcNum - 1] = txtBoxDataInput.Text;
			strTSV_X[CalcNum - 1] = TSV_X;
			strTSV_Y[CalcNum - 1] = TSV_Y;
			strTSV_Z[CalcNum - 1] = TSV_Z;
			strUCS_up[CalcNum - 1] = UCS_up;
			strUCS_vp[CalcNum - 1] = UCS_vp;
		}

		#endregion

		#region 画布 / 描绘坐标 / 导出

		/// <summary>在原版公式给出的位置画一个坐标点（光晕 + 实心黑点）。</summary>
		private void DrawMarker(Graphics g, PointF point)
		{
			int px = Mapping.Px(point.X);
			int py = Mapping.Py(point.Y);
			using (Pen halo = new Pen(Color.LightPink, 3f))
			{
				g.DrawEllipse(halo, px - 3, py - 3, 6, 6);
			}
			using (Pen rim = new Pen(Color.Black, 1f))
			{
				g.DrawEllipse(rim, px - 2, py - 2, 4, 4);
			}
			using (SolidBrush fill = new SolidBrush(Color.Black))
			{
				g.FillEllipse(fill, px - 2, py - 2, 4, 4);
			}
		}

		/// <summary>按"背景图 + 可选黑体轨迹叠加 + 已描绘的点"重建画布并显示。</summary>
		private void RenderCanvas()
		{
			Bitmap previous = Canvas;
			Canvas = (Bitmap)Bgimage.Clone();
			using (Graphics g = Graphics.FromImage(Canvas))
			{
				if (showPlanckianOverlay)
				{
					PlanckianOverlay.Draw(g, Mapping, cie1931xyz, planckianOptions);
				}
				foreach (PointF point in drawnPoints)
				{
					DrawMarker(g, point);
				}
			}
			picBoxCIEGraph.Image = Canvas;
			if (previous != null && !ReferenceEquals(previous, Canvas))
			{
				previous.Dispose();
			}
		}

		private void btnDrawPoint_Click(object sender, EventArgs e)
		{
			if (txtBoxCIEx.Text != "" && txtBoxCIEy.Text != "")
			{
				double x;
				double y;
				try
				{
					x = VbText.ToDouble(txtBoxCIEx.Text);
					y = VbText.ToDouble(txtBoxCIEy.Text);
				}
				catch (FormatException)
				{
					// 原版此处会抛未捕获异常；重建版改为给出提示。
					MessageBox.Show(MessageInfoText[2, 1], MessageTitleText[0, 1], MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
					picBoxCIEGraph.Image = Canvas;
					return;
				}
				drawnPoints.Add(new PointF((float)x, (float)y));
				RenderCanvas();
			}
			else
			{
				MessageBox.Show(MessageInfoText[2, 1], MessageTitleText[0, 1], MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				picBoxCIEGraph.Image = Canvas;
			}
		}

		private void btnClrPoint_Click(object sender, EventArgs e)
		{
			drawnPoints.Clear();
			Canvas = (Bitmap)Bgimage.Clone();
			picBoxCIEGraph.Image = Canvas;
		}

		private void btnExpGraph_Click(object sender, EventArgs e)
		{
			SaveFileDialog1.DefaultExt = "tiff";
			SaveFileDialog1.FileName = strFilename;
			SaveFileDialog1.Filter = "TIFF (*.tiff;*.tif)|*.tiff|BMP (*.bmp)|*.bmp|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg;*.jpe;*.jfif)|*.jpeg;|All Files (*.*)|*.*";
			SaveFileDialog1.FilterIndex = 1;
			SaveFileDialog1.OverwritePrompt = true;
			SaveFileDialog1.Title = MessageTitleText[3, 1];
			if (SaveFileDialog1.ShowDialog() == DialogResult.OK)
			{
				try
				{
					strFilename = SaveFileDialog1.FileName;
					Canvas.Save(strFilename);
				}
				catch (Exception ex)
				{
					MessageBox.Show(ex.Message, "CIE1931xy", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				}
			}
		}

		#endregion

		#region 清空 / 记录

		private void btnClrInput_Click(object sender, EventArgs e)
		{
			txtBoxDataInput.Clear();
		}

		private void btnClrRec_Click(object sender, EventArgs e)
		{
			if (CalcNum > 10)
			{
				int removed = 0;
				for (int i = CalcNum - 1; i >= 10; i--)
				{
					DataGridViewRec.Rows.RemoveAt(i);
					removed++;
				}
				CalcNum -= removed;
			}
			for (int i = 0; i <= 9; i++)
			{
				DataGridViewRec.Rows[i].Cells[0].Value = i + 1;
				DataGridViewRec.Rows[i].Cells[1].Value = "";
				DataGridViewRec.Rows[i].Cells[2].Value = "";
				DataGridViewRec.Rows[i].Cells[3].Value = "";
				DataGridViewRec.Rows[i].Cells[4].Value = "";
			}
			CalcNum = 0;
			DataGridViewRec.CurrentCell = DataGridViewRec[0, 0];
			strDataInput = new string[11];
			strTSV_X = new double[11];
			strTSV_Y = new double[11];
			strTSV_Z = new double[11];
			strUCS_up = new double[11];
			strUCS_vp = new double[11];
		}

		private void DataGridViewRec_CellClick(object sender, DataGridViewCellEventArgs e)
		{
			int rowIndex = e.RowIndex;
			if (rowIndex < 0)
				return;
			try
			{
				txtBoxCIEx.Text = DataGridViewRec.Rows[rowIndex].Cells[1].Value.ToString();
				txtBoxCIEy.Text = DataGridViewRec.Rows[rowIndex].Cells[2].Value.ToString();
				if (string.IsNullOrEmpty(DataGridViewRec.Rows[rowIndex].Cells[1].Value.ToString())
					| string.IsNullOrEmpty(DataGridViewRec.Rows[rowIndex].Cells[2].Value.ToString()))
				{
					txtBoxDataInput.Clear();
					txtBoxTSVX.Clear();
					txtBoxTSVY.Clear();
					txtBoxTSVZ.Clear();
					txtBoxUCSup.Clear();
					txtBoxUCSvp.Clear();
				}
				else
				{
					txtBoxDataInput.Text = strDataInput[rowIndex];
					txtBoxTSVX.Text = VbText.FormatDouble(strTSV_X[rowIndex]);
					txtBoxTSVY.Text = VbText.FormatDouble(strTSV_Y[rowIndex]);
					txtBoxTSVZ.Text = VbText.FormatDouble(strTSV_Z[rowIndex]);
					txtBoxUCSup.Text = VbText.FormatDouble(strUCS_up[rowIndex]);
					txtBoxUCSvp.Text = VbText.FormatDouble(strUCS_vp[rowIndex]);
				}
			}
			catch (Exception)
			{
				// 原版同样吞掉这里的异常（点表头、点空行等）。
			}
		}

		#endregion

		#region 帮助 / 导入文件

		private void btnHelp_Click(object sender, EventArgs e)
		{
			MessageBox.Show(HelpText[1], MessageTitleText[4, 1], MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}

		private void lblDataInput_DoubleClick(object sender, EventArgs e)
		{
			OpenFileDialog1.Filter = "Text Files|*.txt|All Files|*.*";
			OpenFileDialog1.FilterIndex = 0;
			if (OpenFileDialog1.ShowDialog() == DialogResult.OK)
			{
				try
				{
					txtBoxDataInput.Text = System.IO.File.ReadAllText(OpenFileDialog1.FileName);
				}
				catch (Exception ex)
				{
					MessageBox.Show(ex.Message, "CIE1931xy", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				}
			}
		}

		#endregion

		#region 重建版新增：背景切换 / 黑体轨迹叠加

		/// <summary>切换背景图：带黑体轨迹（原版 1.6.0.2）↔ 不带（原版 1.6.0.2a(2)）。</summary>
		private void ToggleDiagramVariant()
		{
			AppResources.WithBlackbody = !AppResources.WithBlackbody;
			picBoxCIEGraph.BackgroundImage = AppResources.Diagram;
			Bgimage = (Bitmap)AppResources.Diagram.Clone();
			RenderCanvas();
		}

		private void Form1_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Control && e.KeyCode == Keys.B)
			{
				ToggleDiagramVariant();
				e.Handled = true;
			}
			else if (e.Control && e.KeyCode == Keys.P)
			{
				showPlanckianOverlay = !showPlanckianOverlay;
				// 打开叠加层时，如果当前背景图里本来就"烘焙"了一条黑体轨迹（原版 1.6.0.2 那张），
				// 自动换成干净底图（原版 1.6.0.2a(2) 那张），免得两条轨迹叠在一起看不清。
				// 想对比"烘焙版 vs 代码版"随时按 Ctrl+B 切回去。
				if (showPlanckianOverlay && AppResources.WithBlackbody)
				{
					AppResources.WithBlackbody = false;
					picBoxCIEGraph.BackgroundImage = AppResources.Diagram;
					Bgimage = (Bitmap)AppResources.Diagram.Clone();
				}
				RenderCanvas();
				e.Handled = true;
			}
		}

		#endregion
	}
}
