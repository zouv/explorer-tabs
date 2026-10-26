using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ExplorerTabHost.App
{
    internal enum UiGlyph { NewTab, AddWindow, Search, Favorites, Session, Windows, Settings, Cancel, Minimize, Maximize, Restore, Close, Diagnostics, App }
    internal static class HostTheme
    {
        internal static readonly Color Canvas = Color.FromArgb(247, 249, 252);
        internal static readonly Color Chrome = Color.FromArgb(233, 238, 245);
        internal static readonly Color Text = Color.FromArgb(43, 56, 76);
        internal static readonly Color Accent = Color.FromArgb(42, 103, 211);
        internal static void StyleMenu(ContextMenuStrip menu)
        { menu.Renderer = new ToolStripProfessionalRenderer(new HostMenuColors()); menu.ShowImageMargin = false; menu.BackColor = Color.FromArgb(253, 254, 255); menu.ForeColor = Text; }
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath(); float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static void Glyph(Graphics g, UiGlyph glyph, Rectangle bounds, Color color)
        {
            var state = g.Save();
            g.TranslateTransform(bounds.Left, bounds.Top); g.ScaleTransform(bounds.Width / 24F, bounds.Height / 24F);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.7F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case UiGlyph.NewTab: g.DrawLine(pen, 12, 5, 12, 19); g.DrawLine(pen, 5, 12, 19, 12); break;
                    case UiGlyph.Minimize: g.DrawLine(pen, 6, 13, 18, 13); break;
                    case UiGlyph.Close: case UiGlyph.Cancel: g.DrawLine(pen, 6, 6, 18, 18); g.DrawLine(pen, 18, 6, 6, 18); break;
                    case UiGlyph.Maximize: g.DrawRectangle(pen, 6, 6, 12, 12); break;
                    case UiGlyph.Restore: g.DrawRectangle(pen, 5, 9, 10, 10); g.DrawLines(pen, new[] { new Point(9, 7), new Point(9, 5), new Point(19, 5), new Point(19, 15), new Point(17, 15) }); break;
                    case UiGlyph.Search: g.DrawEllipse(pen, 4, 4, 12, 12); g.DrawLine(pen, 14, 14, 20, 20); break;
                    case UiGlyph.Favorites: g.DrawPolygon(pen, new[] { new Point(12, 3), new Point(15, 9), new Point(21, 10), new Point(17, 15), new Point(18, 21), new Point(12, 18), new Point(6, 21), new Point(7, 15), new Point(3, 10), new Point(9, 9) }); break;
                    case UiGlyph.Session: g.DrawRectangle(pen, 5, 8, 14, 12); g.DrawLine(pen, 7, 5, 17, 5); g.DrawLine(pen, 10, 2, 14, 2); g.DrawLine(pen, 8, 12, 16, 12); break;
                    case UiGlyph.AddWindow: g.DrawRectangle(pen, 3, 5, 15, 14); g.DrawLine(pen, 3, 9, 18, 9); g.DrawLine(pen, 18, 13, 18, 21); g.DrawLine(pen, 14, 17, 22, 17); break;
                    case UiGlyph.Windows: g.DrawRectangle(pen, 3, 3, 7, 7); g.DrawRectangle(pen, 14, 3, 7, 7); g.DrawRectangle(pen, 3, 14, 7, 7); g.DrawRectangle(pen, 14, 14, 7, 7); break;
                    case UiGlyph.Settings:
                        g.DrawEllipse(pen, 7, 7, 10, 10); g.DrawEllipse(pen, 10, 10, 4, 4);
                        for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, 12 + (float)Math.Cos(a) * 7, 12 + (float)Math.Sin(a) * 7, 12 + (float)Math.Cos(a) * 9, 12 + (float)Math.Sin(a) * 9); } break;
                    case UiGlyph.Diagnostics: g.DrawLines(pen, new[] { new Point(3, 12), new Point(7, 12), new Point(10, 5), new Point(14, 19), new Point(17, 12), new Point(21, 12) }); break;
                    case UiGlyph.App:
                        using (var bg = new SolidBrush(Accent)) using (var r = Round(new RectangleF(1, 1, 22, 22), 5)) g.FillPath(bg, r);
                        using (var pale = new SolidBrush(Color.FromArgb(169, 202, 255))) g.FillRectangle(pale, 7, 5, 11, 3);
                        using (var pale = new SolidBrush(Color.FromArgb(216, 231, 255))) g.FillRectangle(pale, 5, 8, 15, 10);
                        using (var white = new SolidBrush(Color.White)) using (var r = Round(new RectangleF(4, 10, 16, 10), 2)) g.FillPath(white, r);
                        using (var gold = new SolidBrush(Color.FromArgb(255, 209, 111))) g.FillPolygon(gold, new[] { new Point(8, 14), new Point(12, 14), new Point(13, 15), new Point(18, 15), new Point(17, 19), new Point(8, 19) });
                        using (var active = new Pen(Accent, 1)) g.DrawLine(active, 6, 12, 10, 12); break;
                }
            }
            g.Restore(state);
        }
    }
    internal sealed class IconButton : Button
    {
        private bool hovered, pressed;
        public UiGlyph Glyph { get; set; }
        public bool Caption { get; set; }
        public bool Primary { get; set; }
        public IconButton(UiGlyph glyph, string name)
        {
            Glyph = glyph; Text = name; AccessibleName = name;
            AccessibleRole = AccessibleRole.PushButton;
            AutoSize = false; Size = new Size(32, 30); Margin = new Padding(1, 1, 1, 1);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent?.BackColor ?? HostTheme.Canvas);
            bool danger = Caption && Glyph == UiGlyph.Close && hovered;
            Color bg = danger ? Color.FromArgb(220, 48, 65) : pressed ? Color.FromArgb(209, 222, 241) : hovered ? Color.FromArgb(222, 232, 245) : Primary ? Color.FromArgb(228, 237, 253) : Color.Transparent;
            if (bg != Color.Transparent) using (var brush = new SolidBrush(bg)) using (var path = HostTheme.Round(new RectangleF(1, 1, Width - 2, Height - 2), Caption ? 2 : 6)) g.FillPath(brush, path);
            int size = (int)(18 * DeviceDpi / 96F);
            HostTheme.Glyph(g, Glyph, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), !Enabled ? Color.FromArgb(166, 175, 189) : danger ? Color.White : Primary ? HostTheme.Accent : HostTheme.Text);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
    internal sealed class ActionButton : Button
    {
        private bool hovered;
        public bool Primary { get; set; }
        public ActionButton(string text, bool primary = false)
        { Text = text; AccessibleName = text; Primary = primary; AutoSize = false; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Size = new Size(150, 36); SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent?.BackColor ?? HostTheme.Canvas); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = HostTheme.Round(new RectangleF(1, 1, Width - 3, Height - 3), 7))
            using (var brush = new SolidBrush(!Enabled ? Color.FromArgb(226, 232, 241) : Primary ? hovered ? Color.FromArgb(31, 86, 183) : HostTheme.Accent : hovered ? Color.FromArgb(232, 239, 250) : Color.White))
            using (var pen = new Pen(Primary ? HostTheme.Accent : Color.FromArgb(207, 218, 234))) { g.FillPath(brush, path); g.DrawPath(pen, path); }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Primary && Enabled ? Color.White : HostTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
    internal sealed class WelcomePanel : Panel
    {
        private readonly ActionButton create = new ActionButton("新建标签", true);
        private readonly ActionButton join = new ActionButton("加入已有窗口");
        public WelcomePanel(Action newTab, Action addWindow)
        {
            Dock = DockStyle.Fill; BackColor = HostTheme.Canvas; DoubleBuffered = true;
            Controls.Add(create); Controls.Add(join); create.Click += (_, __) => newTab(); join.Click += (_, __) => addWindow();
            AccessibleName = "Explorer 工作空间";
        }
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent); int scale = DeviceDpi;
            int width = 150 * scale / 96, height = 36 * scale / 96, gap = 12 * scale / 96;
            int x = (ClientSize.Width - width * 2 - gap) / 2, y = ClientSize.Height / 2 + 16 * scale / 96;
            create.SetBounds(x, y, width, height); join.SetBounds(x + width + gap, y, width, height);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; float s = DeviceDpi / 96F; int center = ClientSize.Height / 2;
            int icon = (int)(54 * s); HostTheme.Glyph(g, UiGlyph.App, new Rectangle((Width - icon) / 2, center - (int)(156 * s), icon, icon), HostTheme.Accent);
            using (var font = new Font(Font.FontFamily, 19F, FontStyle.Bold)) TextRenderer.DrawText(g, "您的 Explorer 工作空间", font, new Rectangle(0, center - (int)(84 * s), Width, (int)(40 * s)), HostTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "一个窗口，多个常驻文件夹。切换标签，保留各自的浏览状态。", Font, new Rectangle(0, center - (int)(38 * s), Width, (int)(30 * s)), Color.FromArgb(108, 122, 143), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "Ctrl+T  新建   ·   Ctrl+P  搜索   ·   拖动标签排序", Font, new Rectangle(0, center + (int)(76 * s), Width, (int)(30 * s)), Color.FromArgb(131, 144, 163), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        public void SetPending(bool pending) { create.Enabled = join.Enabled = !pending; }
    }
    internal sealed class HostMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(253, 254, 255);
        public override Color MenuBorder => Color.FromArgb(207, 218, 234);
        public override Color MenuItemBorder => Color.FromArgb(216, 229, 247);
        public override Color MenuItemSelected => Color.FromArgb(229, 238, 252);
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override Color SeparatorDark => Color.FromArgb(225, 231, 240);
        public override Color SeparatorLight => Color.Transparent;
    }
    internal sealed class ToolbarDivider : Control
    {
        public ToolbarDivider() { Size = new Size(12, 30); Margin = Padding.Empty; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(210, 219, 232))) e.Graphics.DrawLine(pen, Width / 2, 7, Width / 2, Height - 7); }
    }
}
