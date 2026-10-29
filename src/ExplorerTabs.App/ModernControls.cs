using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ExplorerTabs.App
{
    internal enum UiGlyph { NewTab, AddWindow, DetachWindow, Search, Favorites, Session, Settings, Cancel, Minimize, Maximize, Restore, Close, Diagnostics, App, TabBar, Keyboard, Folder }
    internal static class HostTheme
    {
        internal static readonly Color Canvas = Color.FromArgb(247, 249, 252);
        internal static readonly Color Chrome = Color.FromArgb(233, 238, 245);
        internal static readonly Color Text = Color.FromArgb(43, 56, 76);
        internal static readonly Color Accent = Color.FromArgb(42, 103, 211);
        internal static readonly Color DialogBorder = Color.FromArgb(196, 208, 226);
        internal static readonly Color MutedText = Color.FromArgb(91, 104, 123);
        internal static readonly Color FieldBorder = Color.FromArgb(207, 218, 234);
        internal static readonly Color SidebarHover = Color.FromArgb(237, 242, 249);
        internal static readonly Color NoteBackground = Color.FromArgb(244, 248, 253);
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
                    case UiGlyph.Favorites:
                        // Bookmark ribbon: this button opens the saved-places list, it does not save immediately.
                        g.DrawLines(pen, new[] { new Point(6, 3), new Point(18, 3), new Point(18, 21), new Point(12, 16), new Point(6, 21), new Point(6, 3) });
                        g.DrawLine(pen, 9, 7, 15, 7); break;
                    case UiGlyph.Session:
                        // History clock (save / restore / reopen recent session).
                        g.DrawArc(pen, 4, 4, 16, 16, 40, 300);
                        g.DrawLines(pen, new[] { new Point(4, 8), new Point(4, 4), new Point(8, 4) });
                        g.DrawLine(pen, 12, 8, 12, 12.5F); g.DrawLine(pen, 12, 12.5F, 15, 14); break;
                    case UiGlyph.AddWindow:
                        // A window with an arrow entering it: join an existing Explorer.
                        g.DrawRectangle(pen, 3, 5, 12, 14);
                        g.DrawLine(pen, 13, 12, 21, 12);
                        g.DrawLines(pen, new[] { new Point(18, 9), new Point(21, 12), new Point(18, 15) }); break;
                    case UiGlyph.DetachWindow:
                        // A window with an arrow leaving it: separate back into a standalone Explorer.
                        g.DrawRectangle(pen, 3, 8, 13, 13);
                        g.DrawLine(pen, 11, 13, 21, 3);
                        g.DrawLines(pen, new[] { new Point(15, 3), new Point(21, 3), new Point(21, 9) }); break;
                    case UiGlyph.Settings:
                        DrawGear(g, brush); break;
                    case UiGlyph.TabBar:
                        g.DrawRectangle(pen, 3, 6, 18, 13);
                        g.DrawLine(pen, 3, 10, 21, 10);
                        g.DrawLine(pen, 9, 10, 9, 19); g.DrawLine(pen, 15, 10, 15, 19); break;
                    case UiGlyph.Keyboard:
                        g.DrawRectangle(pen, 3, 7, 18, 11);
                        g.DrawLine(pen, 6, 11, 8, 11); g.DrawLine(pen, 10, 11, 12, 11); g.DrawLine(pen, 14, 11, 16, 11); g.DrawLine(pen, 18, 11, 19, 11);
                        g.DrawLine(pen, 7, 14, 17, 14); break;
                    case UiGlyph.Folder:
                        g.DrawLines(pen, new[] { new Point(3, 7), new Point(3, 18), new Point(21, 18), new Point(21, 8), new Point(12, 8), new Point(10, 6), new Point(3, 6), new Point(3, 7) }); break;
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
        private static void DrawGear(Graphics g, Brush brush)
        {
            // Solid gear with a center hole reads as settings; the old thin radial spokes looked like a sun.
            const int teeth = 8;
            var points = new PointF[teeth * 4];
            for (int i = 0; i < teeth; i++)
            {
                double a = i * Math.PI * 2 / teeth;
                points[i * 4] = Polar(a - 0.21, 7.1F);
                points[i * 4 + 1] = Polar(a - 0.12, 10.2F);
                points[i * 4 + 2] = Polar(a + 0.12, 10.2F);
                points[i * 4 + 3] = Polar(a + 0.21, 7.1F);
            }
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                path.AddPolygon(points);
                path.AddEllipse(9.1F, 9.1F, 5.8F, 5.8F);
                g.FillPath(brush, path);
            }
        }
        private static PointF Polar(double angle, float radius) =>
            new PointF(12 + (float)Math.Cos(angle) * radius, 12 + (float)Math.Sin(angle) * radius);
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
            SetStyle(ControlStyles.ResizeRedraw, true);
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
    /// <summary>Borderless dialog base: rounded region and a solid 1px border ring.</summary>
    internal class ShellDialog : Form
    {
        protected ShellDialog()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
    }
    internal sealed class DialogTitleBar : Panel
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        public DialogTitleBar(Form form)
        {
            Dock = DockStyle.Top; Height = 38; BackColor = Color.White; TabStop = false;
            var title = new Label
            {
                Text = form.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0), ForeColor = HostTheme.Text, BackColor = Color.White,
                Font = new Font(form.Font.FontFamily, 10F), UseMnemonic = false
            };
            var close = new IconButton(UiGlyph.Close, "关闭") { Caption = true, Dock = DockStyle.Right, Width = 44, Height = 38, Margin = Padding.Empty };
            close.Click += (_, __) => form.Close();
            Controls.Add(title); Controls.Add(close);
            title.MouseDown += Drag; MouseDown += Drag;
            Paint += (_, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(226, 232, 240)))
                    e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            };
        }
        private void Drag(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var form = FindForm(); if (form == null) return;
            ReleaseCapture(); SendMessage(form.Handle, 0xA1, new IntPtr(2), IntPtr.Zero); // WM_NCLBUTTONDOWN / HTCAPTION
        }
    }
    /// <summary>Left navigation item: flat, icon + label, accent selection bar.</summary>
    internal sealed class SidebarButton : Button
    {
        private bool hovered, selected;
        public UiGlyph Glyph { get; }
        public bool Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }
        public SidebarButton(UiGlyph glyph, string text)
        {
            Glyph = glyph; Text = text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; AutoSize = false;
            Size = new Size(170, 42); Margin = new Padding(0, 2, 0, 2); TabStop = true; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent?.BackColor ?? HostTheme.Canvas);
            int radius = Math.Max(4, (int)Math.Round(8 * DeviceDpi / 96F));
            Color fill = selected ? Color.White : hovered ? HostTheme.SidebarHover : Color.Transparent;
            if (fill != Color.Transparent)
                using (var brush = new SolidBrush(fill))
                using (var path = HostTheme.Round(new RectangleF(1, 1, Width - 2, Height - 2), radius)) g.FillPath(brush, path);
            if (selected)
                using (var brush = new SolidBrush(HostTheme.Accent))
                using (var path = HostTheme.Round(new RectangleF(1, 10, 3, Height - 20), 1.5F)) g.FillPath(brush, path);
            int icon = Math.Max(16, (int)Math.Round(18 * DeviceDpi / 96F));
            int left = Math.Max(12, (int)Math.Round(16 * DeviceDpi / 96F));
            var color = selected ? HostTheme.Accent : HostTheme.Text;
            HostTheme.Glyph(g, Glyph, new Rectangle(left, (Height - icon) / 2, icon, icon), color);
            int textLeft = left + icon + Math.Max(8, (int)Math.Round(10 * DeviceDpi / 96F));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(textLeft, 0, Width - textLeft - 8, Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
    /// <summary>Owner-drawn checkbox used by the settings surface (one consistent 18px box).</summary>
    internal sealed class ThemedCheckBox : CheckBox
    {
        private bool hovered;
        public ThemedCheckBox() : this("") { }
        public ThemedCheckBox(string text)
        {
            Text = text; AutoSize = false; Height = 30; FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent?.BackColor ?? Color.White);
            int box = Math.Max(16, (int)Math.Round(18 * DeviceDpi / 96F));
            int x = 1, y = (Height - box) / 2;
            var border = !Enabled ? Color.FromArgb(220, 226, 235) : Checked ? HostTheme.Accent : hovered ? Color.FromArgb(160, 178, 203) : HostTheme.FieldBorder;
            using (var brush = new SolidBrush(Checked ? HostTheme.Accent : Color.White))
            using (var path = HostTheme.Round(new RectangleF(x, y, box, box), 4))
            using (var pen = new Pen(border, 1F)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
            if (Checked)
                using (var pen = new Pen(Color.White, Math.Max(1.6F, box / 10F)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(x + box * 0.24F, y + box * 0.52F), new PointF(x + box * 0.44F, y + box * 0.72F), new PointF(x + box * 0.78F, y + box * 0.30F) });
            int textLeft = x + box + Math.Max(8, (int)Math.Round(9 * DeviceDpi / 96F));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft - 2), Height),
                Enabled ? HostTheme.Text : Color.FromArgb(150, 160, 175),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(textLeft, 4, Math.Max(1, Width - textLeft - 4), Height - 8));
        }
    }
    /// <summary>1px rounded field frame; hosts a borderless combo/text/stepper with consistent vertical centring.</summary>
    internal sealed class FieldHost : Panel
    {
        private readonly Control content;
        public FieldHost(Control content)
        {
            this.content = content;
            BackColor = Color.White;
            Height = Math.Max(30, (int)Math.Round(30 * DeviceDpi / 96F));
            Padding = new Padding(8, 0, 6, 0);
            content.BackColor = Color.White;
            content.Enter += (_, __) => Invalidate();
            content.Leave += (_, __) => Invalidate();
            Controls.Add(content);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int preferred = Math.Max(18, content.PreferredSize.Height);
            int height = Math.Min(Math.Max(18, Height - 2), preferred);
            content.Bounds = new Rectangle(Padding.Left, Math.Max(1, (Height - height) / 2),
                Math.Max(1, Width - Padding.Horizontal), height);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var color = content.Focused ? HostTheme.Accent : HostTheme.FieldBorder;
            using (var path = HostTheme.Round(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 6F))
            using (var pen = new Pen(color, 1F))
            {
                var smoothing = e.Graphics.SmoothingMode; var offset = e.Graphics.PixelOffsetMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.PixelOffsetMode = PixelOffsetMode.None;
                e.Graphics.DrawPath(pen, path);
                e.Graphics.PixelOffsetMode = offset; e.Graphics.SmoothingMode = smoothing;
            }
        }
    }
    /// <summary>Preset chip with a selected state (used for new-tab directories).</summary>
    internal sealed class PresetButton : Button
    {
        private bool hovered, selected;
        public bool Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }
        public PresetButton(string text)
        {
            Text = text; AutoSize = false; Size = new Size(96, 30); Margin = new Padding(0, 0, 8, 0);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent?.BackColor ?? Color.White);
            Color fill = selected ? Color.FromArgb(232, 240, 253) : hovered ? HostTheme.SidebarHover : Color.White;
            Color border = selected ? HostTheme.Accent : HostTheme.FieldBorder;
            using (var brush = new SolidBrush(fill))
            using (var path = HostTheme.Round(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 6F))
            using (var pen = new Pen(border, 1F)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, selected ? HostTheme.Accent : HostTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }
    internal static class DialogStyle
    {
        private const int CornerRadius = 12;
        private static float Radius(int dpi) => Math.Max(4F, (float)Math.Round(CornerRadius * dpi / 96F));
        private static GraphicsPath RoundedPath(Size size, float radius, int inset = 0) =>
            HostTheme.Round(new RectangleF(inset, inset, Math.Max(1, size.Width - inset * 2F), Math.Max(1, size.Height - inset * 2F)),
                Math.Max(1F, radius - inset));

        /// <summary>Replaces the native thick dialog frame with a borderless rounded chrome and solid 1px ring.</summary>
        public static void Apply(Form form)
        {
            form.FormBorderStyle = FormBorderStyle.None;
            form.ShowIcon = false;
            form.Icon = null;
            // The form background itself is the border ring. The docked title bar/content are
            // clipped to the inner rounded path, so the ring is exactly 1px everywhere and is
            // never covered by square child corners (the old stroked border failed at arcs).
            form.Padding = new Padding(1);
            form.BackColor = HostTheme.DialogBorder;
            form.Controls.Add(new DialogTitleBar(form));
            form.HandleCreated += (_, __) => { Round(form); ShapeChildren(form); };
            form.Shown += (_, __) => { Round(form); ShapeChildren(form); };
            form.SizeChanged += (_, __) => { if (!form.IsHandleCreated) return; Round(form); ShapeChildren(form); };
            form.Layout += (_, __) => { if (form.IsHandleCreated) ShapeChildren(form); };
            form.ControlAdded += (_, __) => { if (form.IsHandleCreated) ShapeChildren(form); };
            ShapeChildren(form);
        }
        private static void Round(Form form)
        {
            if (!form.IsHandleCreated || form.WindowState == FormWindowState.Minimized ||
                form.ClientSize.Width < 3 || form.ClientSize.Height < 3) return;
            using (var path = RoundedPath(form.ClientSize, Radius(form.DeviceDpi)))
                form.Region = new Region(path);
        }
        private static void ShapeChildren(Form form)
        {
            // The title bar and content are square, docked controls; their corners would cover
            // the rounded border exactly where it turns. Clip every direct child to the same
            // inner edge as the window region (shifted into the child's coordinates). Corners
            // outside a child's rectangle are clipped by its own window bounds.
            int w = form.ClientSize.Width, h = form.ClientSize.Height;
            if (w < 3 || h < 3) return;
            using (var inner = RoundedPath(form.ClientSize, Math.Max(1F, Radius(form.DeviceDpi) - 1F), 1))
            {
                foreach (Control child in form.Controls)
                {
                    using (var path = (GraphicsPath)inner.Clone())
                    using (var shift = new Matrix())
                    {
                        shift.Translate(-child.Left, -child.Top);
                        path.Transform(shift);
                        child.Region = new Region(path);
                    }
                }
            }
        }
    }
    internal sealed class ToolbarDivider : Control
    {
        public ToolbarDivider() { Size = new Size(12, 30); Margin = Padding.Empty; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(210, 219, 232))) e.Graphics.DrawLine(pen, Width / 2, 7, Width / 2, Height - 7); }
    }
}
