using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExplorerTabs.App
{
    internal sealed partial class MainForm
    {
        private readonly ToolTip chromeTips = new ToolTip { AutoPopDelay = 7000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };
        private Panel? chrome;
        private Icon? applicationIcon;
        private IDisposable? nativeMenuScope;
        private IconButton? maximizeButton;
        private int ChromePixels(int logical) => Math.Max(1, (int)Math.Round(logical * DeviceDpi / 96F));
        private void InitializeChrome()
        {
            // Keep native thick frame/system styles for resizing, snap and system keyboard commands;
            // no native caption; WM_NCCALCSIZE extends content through the remaining frame.
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ExplorerTabs.AppIcon.ico"))
            { if (stream != null) using (var icon = new Icon(stream)) { applicationIcon = (Icon)icon.Clone(); Icon = applicationIcon; } }
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Padding = new Padding(1); BackColor = HostTheme.Chrome; DoubleBuffered = true;
            chrome = new Panel { Dock = DockStyle.Top, Height = ChromePixels(36), BackColor = HostTheme.Chrome };
            var brandArea = new Panel { Dock = DockStyle.Left, Width = ChromePixels(36) };
            var brand = new IconButton(UiGlyph.App, "ExplorerTabs · 系统菜单") { Dock = DockStyle.Top, Height = ChromePixels(36), TabStop = false, Margin = Padding.Empty };
            brandArea.Controls.Add(brand);
            brand.Click += (_, __) => ShowHostSystemMenu(brand.PointToScreen(new Point(0, brand.Height)));
            chromeTips.SetToolTip(brand, "ExplorerTabs · 窗口菜单");
            var captions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = ChromePixels(126), Height = ChromePixels(36), WrapContents = false, Padding = Padding.Empty, Margin = Padding.Empty };
            var minimize = new IconButton(UiGlyph.Minimize, "最小化") { Caption = true };
            maximizeButton = new IconButton(UiGlyph.Maximize, "最大化 / 还原") { Caption = true };
            var close = new IconButton(UiGlyph.Close, "恢复窗口并退出") { Caption = true };
            foreach (var b in new[] { minimize, maximizeButton, close }) { b.Size = new Size(ChromePixels(42), ChromePixels(34)); b.Margin = Padding.Empty; chromeTips.SetToolTip(b, b.Text); captions.Controls.Add(b); }
            minimize.Click += (_, __) => WindowState = FormWindowState.Minimized;
            maximizeButton.Click += (_, __) => ToggleMaximize();
            close.Click += (_, __) => Close();
            tabs.Dock = DockStyle.Fill; tabs.BackColor = HostTheme.Chrome;
            tabs.MouseDown += ChromeMouseDown; tabs.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleMaximize(); };
            chrome.Controls.Add(tabs); chrome.Controls.Add(captions); chrome.Controls.Add(brandArea);
            tabs.PreferredChromeHeightChanged += () => { if (chrome != null) chrome.Height = tabs.PreferredChromeHeight; };
            chrome.Height = tabs.PreferredChromeHeight;
            Controls.Add(chrome); Controls.SetChildIndex(chrome, Controls.Count - 1);
            Resize += (_, __) => { if (maximizeButton != null) { maximizeButton.Glyph = WindowState == FormWindowState.Maximized ? UiGlyph.Restore : UiGlyph.Maximize; maximizeButton.Invalidate(); } };
        }
        private void ToggleMaximize() => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        private void ChromeMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
            else if (e.Button == MouseButtons.Right) ShowHostSystemMenu(tabs.PointToScreen(e.Location));
        }
        private void ShowHostSystemMenu(Point point)
        {
            using (EnterModal())
            {
                IntPtr menu = GetSystemMenu(Handle, false);
                const uint enabled = 0, disabled = 1;
                EnableMenuItem(menu, 0xF120, WindowState == FormWindowState.Maximized ? enabled : disabled);
                EnableMenuItem(menu, 0xF030, WindowState == FormWindowState.Maximized ? disabled : enabled);
                int command = TrackPopupMenu(menu, 0x0100 | 0x0002, point.X, point.Y, 0, Handle, IntPtr.Zero);
                if (command != 0) PostMessage(Handle, 0x0112, new IntPtr(command), IntPtr.Zero);
            }
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style = (cp.Style | 0x000F0000) & ~0x00C00000; return cp; } // thickframe, sysmenu, min/max; no native caption
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var margins = new FrameMargins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);
        }
        internal static int ChromeHitTest(Point point, Size size, int border, bool maximized)
        {
            if (maximized) return 1;
            bool l = point.X < border, r = point.X >= size.Width - border, t = point.Y < border, b = point.Y >= size.Height - border;
            if (t && l) return 13; if (t && r) return 14; if (b && l) return 16; if (b && r) return 17;
            if (l) return 10; if (r) return 11; if (t) return 12; if (b) return 15; return 1;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0211 && nativeMenuScope == null && !disposingUi) nativeMenuScope = EnterModal();
            if (m.Msg == 0x0212) { var scope = nativeMenuScope; nativeMenuScope = null; scope?.Dispose(); }
            if (m.Msg == 0x0083) { m.Result = IntPtr.Zero; return; }
            // The thick frame is retained for native sizing, not for default frame painting.
            // DefWindowProc can repaint it over our client area when activation changes.
            if (m.Msg == 0x0085) { m.Result = IntPtr.Zero; return; } // WM_NCPAINT
            if (m.Msg == 0x0086) // WM_NCACTIVATE: preserve native state, suppress frame drawing.
            {
                m.Result = DefWindowProc(Handle, m.Msg, m.WParam, new IntPtr(-1));
                InvalidateChrome(); return;
            }
            if (m.Msg == 0x0084)
            {
                long value = m.LParam.ToInt64(); var screen = new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff));
                var p = PointToClient(screen);
                m.Result = new IntPtr(ChromeHitTest(p, ClientSize, ChromePixels(6), WindowState == FormWindowState.Maximized)); return;
            }
            if (m.Msg == 0x0024)
            {
                base.WndProc(ref m);
                var info = Marshal.PtrToStructure<MinMaxInfo>(m.LParam); var screen = Screen.FromHandle(Handle);
                info.MaxPosition = new NativeChromePoint { X = screen.WorkingArea.Left - screen.Bounds.Left, Y = screen.WorkingArea.Top - screen.Bounds.Top };
                info.MaxSize = new NativeChromePoint { X = screen.WorkingArea.Width, Y = screen.WorkingArea.Height };
                info.MinTrackSize = new NativeChromePoint { X = MinimumSize.Width, Y = MinimumSize.Height };
                Marshal.StructureToPtr(info, m.LParam, false); return;
            }
            base.WndProc(ref m);
        }
        private void InvalidateChrome()
        {
            Invalidate(); chrome?.Invalidate(true); commandToolbar?.Invalidate(true);
        }
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [StructLayout(LayoutKind.Sequential)] private struct FrameMargins { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeChromePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativeChromePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref FrameMargins margins);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
        [DllImport("user32.dll")] private static extern bool EnableMenuItem(IntPtr menu, uint item, uint flags);
        [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    }
}
