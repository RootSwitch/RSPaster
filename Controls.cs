// Owner-drawn controls that follow the Canvas Suite design language: flat
// components, 1px borders, 4-6px radii, accent-colored focus, no gradients.
//
// WinForms will not theme its stock chrome - a NumericUpDown keeps system
// colored spin buttons and a CheckBox keeps a system glyph, both of which look
// wrong on 25 of the 29 palettes. So the pieces that would give the game away
// are painted here instead, and every one of them reads Th.T at paint time so
// a theme switch is a repaint rather than a control rebuild.
//
// C# 5 only (in-box csc).

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace RSPaster
{
    // The one DPI scale factor, sampled once at startup after the process
    // declares itself DPI-aware. Every hand-placed pixel dimension goes through
    // S(). Declaring awareness without scaling the layout would trade "blurry
    // at 150%" for "tiny at 150%", which is worse; the two ship together.
    public static class Dpi
    {
        public static double Factor = 1.0;

        public static void Init()
        {
            IntPtr screen = IntPtr.Zero;
            using (Graphics g = Graphics.FromHwnd(screen))
                Factor = g.DpiX / 96.0;
            if (Factor < 1.0) Factor = 1.0;
        }

        public static int S(int logicalPx)
        {
            return (int)Math.Round(logicalPx * Factor);
        }
    }

    public static class Draw
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
        }

        // Border rectangles are inset by 1px so the 1px pen is not clipped.
        public static void FillBorderRound(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            Rectangle inner = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (GraphicsPath p = Round(inner, radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
        }
    }

    // A TextBox whose scroll position can be observed. An EDIT control reports
    // scrolling only through notifications its WinForms wrapper does not
    // surface, so the messages that can move the caret or the view are caught
    // here and republished as one event.
    public class ScrollAwareTextBox : TextBox
    {
        const int WM_VSCROLL = 0x0115;
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_KEYDOWN = 0x0100;
        const int WM_CHAR = 0x0102;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_LBUTTONUP = 0x0202;
        const int WM_MOUSEMOVE = 0x0200;
        const int WM_PASTE = 0x0302;
        const int WM_SIZE = 0x0005;

        const int EM_GETLINECOUNT = 0x00BA;
        const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        const int EM_LINESCROLL = 0x00B6;

        public event EventHandler ViewChanged;

        protected override void WndProc(ref Message m)
        {
            // An EDIT control only breaks lines on CRLF. Text copied from a
            // Linux shell, an SSH session or a web page is usually LF-only, and
            // pasting it raw shows the whole script as one unreadable line -
            // which defeats the point of reviewing it before it is typed. The
            // keystroke engine normalizes again on the way out, so this only
            // changes what is displayed, never what is sent.
            if (m.Msg == WM_PASTE)
            {
                // Setting SelectedText ignores ReadOnly, so without this check
                // Ctrl+V could edit the text mid-run while typing is active -
                // exactly when the box is locked. The native EDIT control
                // refuses pastes when read-only; so do we.
                if (ReadOnly) return;
                string text = null;
                try
                {
                    if (Clipboard.ContainsText()) text = Clipboard.GetText();
                }
                catch (System.Runtime.InteropServices.ExternalException) { }   // clipboard locked
                if (text != null)
                {
                    SelectedText = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                    if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
                    return;
                }
            }

            base.WndProc(ref m);
            switch (m.Msg)
            {
                case WM_VSCROLL:
                case WM_MOUSEWHEEL:
                case WM_KEYDOWN:
                case WM_CHAR:
                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                case WM_MOUSEMOVE:
                case WM_PASTE:
                case WM_SIZE:
                    if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
                    break;
            }
        }

        // Wrapped lines, not logical lines - which is what the scrollbar needs.
        public int VisualLineCount
        {
            get
            {
                if (!IsHandleCreated) return 1;
                return Math.Max(1, (int)Native.SendMessage(Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero));
            }
        }

        public int FirstVisibleLine
        {
            get
            {
                if (!IsHandleCreated) return 0;
                return (int)Native.SendMessage(Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public int LineHeight
        {
            get { return Math.Max(1, TextRenderer.MeasureText("Ag", Font).Height); }
        }

        public int VisibleLines
        {
            get { return Math.Max(1, ClientSize.Height / LineHeight); }
        }

        public void ScrollToLine(int line)
        {
            if (!IsHandleCreated) return;
            int delta = line - FirstVisibleLine;
            if (delta != 0)
                Native.SendMessage(Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(delta));
        }
    }

    // A flat scrollbar in theme colors. Windows will not recolor an EDIT
    // control's non-client scrollbars - SetWindowTheme and the dark-mode
    // ordinals are both ignored for them - so the stock bars are switched off
    // and this is painted in their place. It is also the only way to match the
    // 29 palettes, which a two-state OS scrollbar could never do.
    public class ThemedScrollBar : Control
    {
        static int MinThumb { get { return Dpi.S(24); } }
        static int Pad { get { return Dpi.S(2); } }

        int _max = 1;
        int _large = 1;
        int _value;
        bool _dragging;
        int _dragOffset;
        bool _hover;

        public event EventHandler UserScrolled;

        public ThemedScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = Dpi.S(12);
            Th.Changed += OnThemeChanged;
        }

        void OnThemeChanged(object sender, EventArgs e) { Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Th.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        public void Configure(int total, int visible, int value)
        {
            _max = Math.Max(1, total);
            _large = Math.Max(1, visible);
            _value = Clamp(value);
            Invalidate();
        }

        public int Value { get { return _value; } }
        int MaxValue { get { return Math.Max(0, _max - _large); } }
        bool Scrollable { get { return _max > _large; } }
        int Clamp(int v) { return v < 0 ? 0 : (v > MaxValue ? MaxValue : v); }

        int TrackHeight { get { return Math.Max(1, Height - Pad * 2); } }

        int ThumbHeight
        {
            get
            {
                if (!Scrollable) return 0;
                return Math.Max(MinThumb, (int)((long)TrackHeight * _large / _max));
            }
        }

        int ThumbTop
        {
            get
            {
                if (MaxValue <= 0) return Pad;
                return Pad + (int)((long)(TrackHeight - ThumbHeight) * _value / MaxValue);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Scrollable)
            {
                int top = ThumbTop, h = ThumbHeight;
                if (e.Y >= top && e.Y < top + h)
                {
                    _dragging = true;
                    _dragOffset = e.Y - top;
                }
                else
                {
                    SetValue(_value + (e.Y < top ? -_large : _large));
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                int span = TrackHeight - ThumbHeight;
                if (span > 0)
                    SetValue((int)((long)(e.Y - _dragOffset - Pad) * MaxValue / span));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            SetValue(_value - (e.Delta / 120) * 3);
            base.OnMouseWheel(e);
        }

        void SetValue(int v)
        {
            int clamped = Clamp(v);
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
            if (UserScrolled != null) UserScrolled(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Th.T;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(t.Input))
                e.Graphics.FillRectangle(b, ClientRectangle);
            if (!Scrollable) return;

            Color thumb = (_hover || _dragging) ? t.Accent : Th.Mix(t.Input, t.TxtDim, 0.55);
            Draw.FillRound(e.Graphics,
                new Rectangle(Pad, ThumbTop, Width - Pad * 2, ThumbHeight),
                (Width - Pad * 2) / 2, thumb);
        }
    }

    // A 1px-bordered, input-colored container for a borderless child control.
    // This is the only way to get a themed border on a TextBox: the control
    // draws its own system border otherwise, and BorderStyle has no color.
    public class InputHost : Panel
    {
        Control _child;
        bool _focused;

        public InputHost(Control child, int padX, int padY)
        {
            _child = child;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(padX, padY, padX, padY);
            child.Dock = DockStyle.Fill;
            Controls.Add(child);
            child.GotFocus += OnChildFocus;
            child.LostFocus += OnChildBlur;
            Th.Changed += OnThemeChanged;
            OnThemeChanged(null, EventArgs.Empty);   // the child starts system-colored
        }

        void OnChildFocus(object sender, EventArgs e) { _focused = true; Invalidate(); }
        void OnChildBlur(object sender, EventArgs e) { _focused = false; Invalidate(); }

        void OnThemeChanged(object sender, EventArgs e)
        {
            _child.BackColor = Th.T.Input;
            _child.ForeColor = Th.T.Txt;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Th.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Parent != null ? Parent.BackColor : Th.T.Panel))
                e.Graphics.FillRectangle(b, ClientRectangle);
            Draw.FillBorderRound(e.Graphics, ClientRectangle, 4, Th.T.Input,
                                 _focused ? Th.T.Accent : Th.T.Border);
        }
    }

    public class ThemedButton : Button
    {
        public bool Primary;
        bool _hover;

        public ThemedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Th.Changed += OnThemeChanged;
        }

        void OnThemeChanged(object sender, EventArgs e) { Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Th.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Th.T;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Parent != null ? Parent.BackColor : t.Panel))
                e.Graphics.FillRectangle(bg, ClientRectangle);

            Color fill, border, text;
            if (Primary)
            {
                // .btn-primary: accent fill, white text, brightness(1.1) hover.
                fill = _hover && Enabled ? Th.Brighten(t.Accent, 1.1) : t.Accent;
                border = fill;
                text = Th.OnColor(t.Accent);
            }
            else
            {
                fill = t.Panel2;
                border = _hover && Enabled ? t.Accent : t.Border;
                text = t.Txt;
            }
            if (!Enabled)
            {
                // button:disabled { opacity: 0.5 }
                fill = Th.Mix(fill, Parent != null ? Parent.BackColor : t.Panel, 0.5);
                border = Th.Mix(t.Border, Parent != null ? Parent.BackColor : t.Panel, 0.5);
                text = t.TxtDim;
            }

            Draw.FillBorderRound(e.Graphics, ClientRectangle, 4, fill, border);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }
    }

    // input[type=checkbox] { accent-color: var(--se-accent); width:15px; height:15px }
    public class ThemedCheck : CheckBox
    {
        static int Box { get { return Dpi.S(15); } }
        static int Gap { get { return Dpi.S(8); } }
        bool _hover;

        public ThemedCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Th.Changed += OnThemeChanged;
        }

        void OnThemeChanged(object sender, EventArgs e) { Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Th.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        // AutoSize cannot be trusted once we take over painting, so callers size
        // the control from its text instead. Measure with the same flags OnPaint
        // draws with, or the label comes out clipped.
        public void SizeToText()
        {
            Size s = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Size = new Size(Box + Gap + s.Width + Dpi.S(6), Math.Max(Box + 4, s.Height + Dpi.S(6)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Th.T;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Parent != null ? Parent.BackColor : t.Panel))
                e.Graphics.FillRectangle(bg, ClientRectangle);

            int top = (Height - Box) / 2;
            Rectangle box = new Rectangle(0, top, Box, Box);
            Color border = Checked ? t.Accent : (_hover && Enabled ? t.Accent : t.Border);
            Color fill = Checked ? t.Accent : t.Input;
            if (!Enabled)
            {
                fill = Th.Mix(fill, Parent != null ? Parent.BackColor : t.Panel, 0.5);
                border = Th.Mix(border, Parent != null ? Parent.BackColor : t.Panel, 0.5);
            }
            Draw.FillBorderRound(e.Graphics, box, 3, fill, border);

            if (Checked)
            {
                // Glyph geometry in fifteenths of the box, so it scales with it.
                float u = box.Width / 15f;
                using (Pen pen = new Pen(Th.OnColor(t.Accent), Math.Max(2f, 2f * u)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(pen, new PointF[] {
                        new PointF(box.Left + 3 * u, box.Top + 7 * u),
                        new PointF(box.Left + 6 * u, box.Top + 10 * u),
                        new PointF(box.Left + 11 * u, box.Top + 4 * u)
                    });
                }
            }

            Rectangle textRect = new Rectangle(Box + Gap, 0, Width - Box - Gap, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect,
                Enabled ? t.Txt : t.TxtDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Numeric field with painted spin arrows. Replaces NumericUpDown, whose
    // spin buttons render in system colors no matter what you set.
    public class SpinBox : Control
    {
        static int ArrowW { get { return Dpi.S(18); } }

        TextBox _box;
        int _min = 0;
        int _max = 100;
        int _hoverArrow;   // 0 none, 1 up, 2 down
        bool _enabled = true;

        public event EventHandler ValueChanged;

        // Hides Control.Enabled on purpose. Truly disabling the control
        // disables the child TextBox, and a disabled EDIT ignores BackColor
        // and paints system gray - a bright hole in 20 of the 29 palettes.
        // Instead the child stays enabled but read-only, and the chrome is
        // painted dimmed.
        public new bool Enabled
        {
            get { return _enabled; }
            set
            {
                _enabled = value;
                if (_box != null)
                {
                    _box.ReadOnly = !value;
                    _box.TabStop = value;
                    _box.ForeColor = value ? Th.T.Txt : Th.T.TxtDim;
                    _box.BackColor = Th.T.Input;   // re-assert: ReadOnly toggles reset it
                }
                TabStop = value;
                Invalidate();
            }
        }

        public SpinBox(int min, int max, int value)
        {
            _min = min;
            _max = max;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.TextAlign = HorizontalAlignment.Left;
            _box.Text = value.ToString(CultureInfo.InvariantCulture);
            _box.GotFocus += Repaint;
            _box.LostFocus += OnBoxBlur;
            _box.KeyPress += OnBoxKeyPress;
            _box.TextChanged += OnBoxTextChanged;
            Controls.Add(_box);
            Th.Changed += OnThemeChanged;
            OnThemeChanged(null, EventArgs.Empty);   // the child starts system-colored
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            _box.BackColor = Th.T.Input;
            _box.ForeColor = _enabled ? Th.T.Txt : Th.T.TxtDim;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Th.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        public override Font Font
        {
            get { return base.Font; }
            set { base.Font = value; if (_box != null) { _box.Font = value; Relayout(); } }
        }

        // The form font arrives ambiently after parenting, which skips the
        // setter above - sync the child here too.
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_box != null) { _box.Font = Font; Relayout(); }
        }

        public int Value
        {
            get
            {
                int n;
                if (int.TryParse(_box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    return Clamp(n);
                return _min;
            }
            set
            {
                _box.Text = Clamp(value).ToString(CultureInfo.InvariantCulture);
            }
        }

        int Clamp(int n) { return n < _min ? _min : (n > _max ? _max : n); }

        void Repaint(object sender, EventArgs e) { Invalidate(); }

        void OnBoxBlur(object sender, EventArgs e)
        {
            // Normalize whatever was typed once the field is left, so an empty
            // or out-of-range box never silently reports the minimum.
            _box.Text = Value.ToString(CultureInfo.InvariantCulture);
            Invalidate();
        }

        void OnBoxKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        }

        void OnBoxTextChanged(object sender, EventArgs e)
        {
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        void Relayout()
        {
            if (_box == null) return;
            int h = _box.PreferredHeight;
            _box.SetBounds(Dpi.S(7), Math.Max(1, (Height - h) / 2),
                           Math.Max(10, Width - ArrowW - Dpi.S(9)), h);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_enabled) return;
            int was = _hoverArrow;
            _hoverArrow = 0;
            if (e.X >= Width - ArrowW - 1)
                _hoverArrow = e.Y < Height / 2 ? 1 : 2;
            if (was != _hoverArrow) Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hoverArrow != 0) { _hoverArrow = 0; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!_enabled) return;
            if (e.X >= Width - ArrowW - 1)
            {
                Value = Value + (e.Y < Height / 2 ? 1 : -1);
                _box.Focus();
                _box.SelectionStart = _box.Text.Length;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_enabled && _box.Focused) Value = Value + (e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Th.T;
            Color back = Parent != null ? Parent.BackColor : t.Panel;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(back))
                e.Graphics.FillRectangle(bg, ClientRectangle);

            // Disabled = same shapes, everything blended toward the panel,
            // mirroring the 0.5-opacity treatment the suite gives buttons.
            Color border = _enabled ? (_box.Focused ? t.Accent : t.Border)
                                    : Th.Mix(t.Border, back, 0.5);
            Color fill = _enabled ? t.Input : Th.Mix(t.Input, back, 0.5);
            Draw.FillBorderRound(e.Graphics, ClientRectangle, 4, fill, border);

            Color arrowIdle = _enabled ? t.TxtDim : Th.Mix(t.TxtDim, back, 0.5);
            int cx = Width - ArrowW / 2 - Dpi.S(3);
            int gap = Dpi.S(5);
            DrawArrow(e.Graphics, cx, Height / 2 - gap, true, _hoverArrow == 1 && _enabled ? t.Accent : arrowIdle);
            DrawArrow(e.Graphics, cx, Height / 2 + gap, false, _hoverArrow == 2 && _enabled ? t.Accent : arrowIdle);
        }

        static void DrawArrow(Graphics g, int cx, int cy, bool up, Color color)
        {
            Point[] pts = up
                ? new Point[] { new Point(cx - 4, cy + 2), new Point(cx + 4, cy + 2), new Point(cx, cy - 3) }
                : new Point[] { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) };
            using (SolidBrush b = new SolidBrush(color)) g.FillPolygon(b, pts);
        }
    }

    // Menu chrome. ProfessionalColorTable is the only supported way to recolor
    // ToolStrip surfaces without painting every item by hand.
    public class ThemeColorTable : ProfessionalColorTable
    {
        public ThemeColorTable() { UseSystemColors = false; }

        public override Color ToolStripDropDownBackground { get { return Th.T.Panel; } }
        public override Color MenuBorder { get { return Th.T.Border; } }
        public override Color MenuItemBorder { get { return Th.T.Accent; } }
        public override Color MenuItemSelected { get { return Th.T.Panel2; } }
        public override Color MenuItemSelectedGradientBegin { get { return Th.T.Panel2; } }
        public override Color MenuItemSelectedGradientEnd { get { return Th.T.Panel2; } }
        public override Color MenuItemPressedGradientBegin { get { return Th.T.Panel; } }
        public override Color MenuItemPressedGradientMiddle { get { return Th.T.Panel; } }
        public override Color MenuItemPressedGradientEnd { get { return Th.T.Panel; } }
        public override Color ImageMarginGradientBegin { get { return Th.T.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Th.T.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Th.T.Panel; } }
        public override Color CheckBackground { get { return Th.T.Accent; } }
        public override Color CheckSelectedBackground { get { return Th.T.Accent; } }
        public override Color CheckPressedBackground { get { return Th.T.Accent; } }
        public override Color ButtonSelectedBorder { get { return Th.T.Accent; } }
        public override Color SeparatorDark { get { return Th.T.Border; } }
        public override Color SeparatorLight { get { return Th.T.Border; } }
    }

    public static class MenuTheme
    {
        // Applied on open as well as on build, because a theme switch made from
        // inside the menu has to recolor the menu that is still on screen.
        public static void Apply(ToolStrip strip)
        {
            strip.BackColor = Th.T.Panel;
            strip.ForeColor = Th.T.Txt;
            ApplyItems(strip.Items);
        }

        static void ApplyItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = Th.T.Panel;
                item.ForeColor = Th.T.Txt;
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems)
                {
                    mi.DropDown.BackColor = Th.T.Panel;
                    ApplyItems(mi.DropDownItems);
                }
            }
        }
    }

    // Chrome that belongs to the OS rather than to WinForms. Neither of these
    // can be given an arbitrary color - Windows offers a dark variant and a
    // light one - so each palette picks whichever side it sits on. Without this
    // a dark theme still shows white scrollbars and a white title bar, which is
    // the detail that makes the whole window look unthemed.
    public static class OsChrome
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE_PRE_20H1 = 19;

        public static bool IsDark(Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 < 0.5;
        }

        public static void ApplyTitleBar(Form form)
        {
            if (form == null || !form.IsHandleCreated) return;
            int on = IsDark(Th.T.Panel) ? 1 : 0;
            // Attribute 20 is the documented one; builds before 20H1 used 19.
            if (Native.DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4) != 0)
                Native.DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_PRE_20H1, ref on, 4);
        }

        // Called once at startup: without it the per-window calls below do
        // nothing. AllowDark(1) opts in without overriding the user's setting.
        public static void EnableDarkModeSupport()
        {
            try { Native.SetPreferredAppMode(1 /* AllowDark */); }
            catch (EntryPointNotFoundException) { }   // pre-1809
            catch (DllNotFoundException) { }
        }

        public static void ApplyScrollBars(Control c)
        {
            if (c == null || !c.IsHandleCreated) return;
            bool dark = IsDark(Th.T.Input);
            try { Native.AllowDarkModeForWindow(c.Handle, dark); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            Native.SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            // An EDIT control caches its scrollbar theme; it only re-reads it on
            // WM_THEMECHANGED, and the bars live in the non-client area so a
            // plain Invalidate never reaches them.
            Native.SendMessage(c.Handle, Native.WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero);
            Native.RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero,
                Native.RDW_FRAME | Native.RDW_INVALIDATE | Native.RDW_UPDATENOW);
        }
    }

    public static class Brand
    {
        // The clipboard mark, drawn rather than shipped as an .ico so it can be
        // recolored per theme and so the repo carries no binary asset.
        public static void PaintMark(Graphics g, Rectangle r, Color body, Color clip)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float unit = r.Width / 16f;
            RectangleF board = new RectangleF(
                r.X + unit * 2.5f, r.Y + unit * 3f, unit * 11f, unit * 11.5f);
            using (GraphicsPath p = Draw.Round(
                new Rectangle((int)board.X, (int)board.Y, (int)board.Width, (int)board.Height),
                Math.Max(1, (int)(unit * 1.6f))))
            using (Pen pen = new Pen(body, Math.Max(1f, unit * 1.35f)))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, p);
            }
            // The clip at the top, in the second brand color.
            RectangleF tab = new RectangleF(r.X + unit * 5.5f, r.Y + unit * 1.6f, unit * 5f, unit * 3f);
            using (GraphicsPath p = Draw.Round(
                new Rectangle((int)tab.X, (int)tab.Y, (int)tab.Width, (int)tab.Height),
                Math.Max(1, (int)unit)))
            using (SolidBrush b = new SolidBrush(clip))
                g.FillPath(b, p);
            // Caret inside the board: this one types rather than pastes.
            using (Pen pen = new Pen(clip, Math.Max(1f, unit * 1.3f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new PointF[] {
                    new PointF(r.X + unit * 5.6f, r.Y + unit * 7.4f),
                    new PointF(r.X + unit * 8.1f, r.Y + unit * 9.4f),
                    new PointF(r.X + unit * 5.6f, r.Y + unit * 11.4f)
                });
                g.DrawLine(pen, r.X + unit * 9.2f, r.Y + unit * 11.6f,
                                r.X + unit * 12.2f, r.Y + unit * 11.6f);
            }
        }

        // Bitmap.GetHicon() hands back a handle the Icon wrapper does not own,
        // so callers must DestroyIcon the previous one on every theme switch or
        // the process bleeds GDI handles.
        public static Icon CreateIcon(int size, out IntPtr handle)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    Brand.PaintMark(g, new Rectangle(0, 0, size, size), Th.T.LogoA, Th.T.LogoB);
                }
                handle = bmp.GetHicon();
                return Icon.FromHandle(handle);
            }
        }
    }
}
