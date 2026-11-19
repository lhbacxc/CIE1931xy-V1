using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CIE1931xy.UI
{
    // 只替换绘制，继续使用原生按钮/复选框的输入、自动尺寸与可访问性。
    internal sealed class RoundedButton : Button
    {
        private readonly RoundedState _state;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _state = new RoundedState(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RoundedAppearance.PaintButton(this, e.Graphics, _state, BackColor,
                FlatAppearance.BorderColor, TextAlign, ShowFocusCues, ShowKeyboardCues);
        }
    }

    internal sealed class RoundedToggle : CheckBox
    {
        private readonly RoundedState _state;

        public RoundedToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _state = new RoundedState(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = Checked ? FlatAppearance.CheckedBackColor : BackColor;
            RoundedAppearance.PaintButton(this, e.Graphics, _state, fill,
                Checked ? fill : FlatAppearance.BorderColor, TextAlign,
                ShowFocusCues, ShowKeyboardCues);
        }
    }

    internal sealed class RoundedComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            RoundedAppearance.PaintNativeFrame(this, m);
        }
    }

    internal sealed class RoundedNumber : NumericUpDown
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            RoundedAppearance.PaintNativeFrame(this, m);
        }
    }

    internal sealed class RoundedState
    {
        public bool Hovered { get; private set; }
        public bool Pressed { get; private set; }

        public RoundedState(Control control)
        {
            control.MouseEnter += delegate { Hovered = true; control.Invalidate(); };
            control.MouseLeave += delegate { Hovered = false; control.Invalidate(); };
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Pressed = true;
                control.Invalidate();
            };
            control.MouseUp += delegate { Pressed = false; control.Invalidate(); };
            control.MouseCaptureChanged += delegate { Pressed = false; control.Invalidate(); };
            control.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space) { Pressed = true; control.Invalidate(); }
            };
            control.KeyUp += delegate { Pressed = false; control.Invalidate(); };
            control.GotFocus += delegate { control.Invalidate(); };
            control.LostFocus += delegate { Pressed = false; control.Invalidate(); };
        }
    }

    internal static class RoundedAppearance
    {
        private const int CornerRadius = 4;

        private static GraphicsPath CreateOutline(Control control, RectangleF bounds)
        {
            float radius = Math.Min(control.LogicalToDeviceUnits(CornerRadius),
                Math.Min(bounds.Width, bounds.Height) / 2F);
            float diameter = radius * 2F;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static RectangleF BorderBounds(Control control, float stroke)
        {
            return new RectangleF(stroke / 2F, stroke / 2F,
                control.ClientSize.Width - stroke, control.ClientSize.Height - stroke);
        }

        public static void PaintButton(Control control, Graphics graphics, RoundedState state,
            Color fill, Color border, ContentAlignment alignment, bool focusCues, bool keyboardCues)
        {
            if (control.ClientSize.Width < 2 || control.ClientSize.Height < 2) return;
            graphics.Clear(control.Parent == null ? SystemColors.Control : control.Parent.BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (!control.Enabled) fill = SystemColors.Control;
            else if (state.Pressed) fill = ControlPaint.Dark(fill, 0.08F);
            else if (state.Hovered) fill = fill == Color.White
                ? Color.FromArgb(0xF4, 0xF7, 0xFC) : ControlPaint.Light(fill, 0.08F);

            float stroke = Math.Max(1F, control.DeviceDpi / 96F);
            using (GraphicsPath path = CreateOutline(control, BorderBounds(control, stroke)))
            using (Brush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(control.Enabled ? border : SystemColors.ControlDark, stroke))
            {
                graphics.FillPath(brush, path);
                graphics.DrawPath(pen, path);
            }

            Rectangle textBounds = Rectangle.FromLTRB(control.Padding.Left,
                control.Padding.Top, control.ClientSize.Width - control.Padding.Right,
                control.ClientSize.Height - control.Padding.Bottom);
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                | TextFormatFlags.PreserveGraphicsClipping;
            flags |= alignment == ContentAlignment.MiddleLeft ? TextFormatFlags.Left
                : alignment == ContentAlignment.MiddleRight ? TextFormatFlags.Right
                : TextFormatFlags.HorizontalCenter;
            if (!keyboardCues) flags |= TextFormatFlags.HidePrefix;
            Color foreground = control.Enabled ? control.ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(graphics, control.Text, control.Font, textBounds, foreground, flags);
            if (control.Focused && focusCues)
            {
                Rectangle focus = control.ClientRectangle;
                focus.Inflate(-control.LogicalToDeviceUnits(4), -control.LogicalToDeviceUnits(4));
                ControlPaint.DrawFocusRectangle(graphics, focus, foreground, fill);
            }
        }

        public static void PaintNativeFrame(Control control, Message message)
        {
            // 原生下拉/数值输入继续自行绘制内容，仅遮住方角并补上圆角边框。
            const int WmPaint = 0x000F;
            const int WmPrint = 0x0317;
            const int WmPrintClient = 0x0318;
            if (message.Msg != WmPaint && message.Msg != WmPrint && message.Msg != WmPrintClient)
                return;
            if (!control.IsHandleCreated || control.ClientSize.Width < 2 || control.ClientSize.Height < 2)
                return;
            using (Graphics graphics = message.Msg == WmPaint
                ? Graphics.FromHwnd(control.Handle) : Graphics.FromHdc(message.WParam))
            {
                float stroke = Math.Max(1F, control.DeviceDpi / 96F);
                using (GraphicsPath outline = CreateOutline(control, BorderBounds(control, stroke)))
                using (GraphicsPath corners = new GraphicsPath(FillMode.Alternate))
                using (Brush background = new SolidBrush(control.Parent == null
                    ? SystemColors.Control : control.Parent.BackColor))
                using (Pen border = new Pen(Color.FromArgb(0xD5, 0xDA, 0xE1), stroke))
                {
                    corners.AddRectangle(control.ClientRectangle);
                    corners.AddPath(outline, false);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.FillPath(background, corners);
                    graphics.DrawPath(border, outline);
                }
            }
        }
    }
}
