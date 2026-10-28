using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExplorerTabs.Core;

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
        private void SyncTopResizeBand()
        {
            // The native top frame is pulled into the client (WM_NCCALCSIZE) so the opaque
            // system-painted strip disappears. Keep the same band height as host Padding; it is
            // painted in the tab-strip colour and handled as HTTOP in WM_NCHITTEST.
            int band = IsZoomed(Handle) ? 0 : ChromePixels(3);
            if (Padding.Top != band || Padding.Left != 0 || Padding.Right != 0 || Padding.Bottom != 0)
                Padding = new Padding(0, band, 0, 0);
        }
        private void InitializeChrome()
        {
            // Keep the native thick frame: Windows draws its thin DWM border and keeps the wider
            // invisible resize margin outside the client area, so docked children (including the
            // embedded Explorer) can never cover the resize hit region. Only the caption is removed.
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ExplorerTabs.AppIcon.ico"))
            { if (stream != null) using (var icon = new Icon(stream)) { applicationIcon = (Icon)icon.Clone(); Icon = applicationIcon; } }
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            DpiChanged += (_, __) => SyncTopResizeBand();
            BackColor = HostTheme.Chrome; DoubleBuffered = true;
            chrome = new Panel { Dock = DockStyle.Top, Height = ChromePixels(36), BackColor = HostTheme.Chrome };
            var brandArea = new Panel { Dock = DockStyle.Left, Width = ChromePixels(36) };
            var brand = new IconButton(UiGlyph.App, "ExplorerTabs · 应用菜单") { Dock = DockStyle.Top, Height = ChromePixels(36), TabStop = false, Margin = Padding.Empty };
            brandArea.Controls.Add(brand);
            brand.Click += (_, __) => ShowAppMenu(brand.PointToScreen(new Point(0, brand.Height)));
            chromeTips.SetToolTip(brand, "ExplorerTabs · 应用菜单");
            var captions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = ChromePixels(126), Height = ChromePixels(36), WrapContents = false, Padding = Padding.Empty, Margin = Padding.Empty };
            var minimize = new IconButton(UiGlyph.Minimize, "最小化") { Caption = true };
            maximizeButton = new IconButton(UiGlyph.Maximize, "最大化 / 还原") { Caption = true };
            var close = new IconButton(UiGlyph.Close, "关闭所有会话窗口并退出应用") { Caption = true };
            foreach (var b in new[] { minimize, maximizeButton, close }) { b.Size = new Size(ChromePixels(42), ChromePixels(34)); b.Margin = Padding.Empty; chromeTips.SetToolTip(b, b.Text); captions.Controls.Add(b); }
            minimize.Click += (_, __) => WindowState = FormWindowState.Minimized;
            maximizeButton.Click += (_, __) => ToggleMaximize();
            close.Click += (_, __) => RequestExitByClosingSessions();
            tabs.Dock = DockStyle.Fill; tabs.BackColor = HostTheme.Chrome;
            tabs.MouseDown += ChromeMouseDown;
            chrome.Controls.Add(tabs); chrome.Controls.Add(captions); chrome.Controls.Add(brandArea);
            tabs.PreferredChromeHeightChanged += () => { if (chrome != null) chrome.Height = tabs.PreferredChromeHeight; };
            chrome.Height = tabs.PreferredChromeHeight;
            Controls.Add(chrome); Controls.SetChildIndex(chrome, Controls.Count - 1);
            Resize += (_, __) => { if (maximizeButton != null) { maximizeButton.Glyph = WindowState == FormWindowState.Maximized ? UiGlyph.Restore : UiGlyph.Maximize; maximizeButton.Invalidate(); } };
        }
        private void ToggleMaximize() => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        private void ChromeMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // The first click starts the native move loop; the second click still arrives
                // with Clicks=2, so toggle here instead of relying on MouseDoubleClick.
                if (e.Clicks == 2) { ToggleMaximize(); return; }
                ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            }
            else if (e.Button == MouseButtons.Right) ShowAppMenu(tabs.PointToScreen(e.Location));
        }
        private ContextMenuStrip CreateAppMenu()
        {
            var menu = new ContextMenuStrip();
            var settingsItem = menu.Items.Add("设置…", null, (_, __) => QueueMenuAction(ShowSettings));
            settingsItem.Enabled = creation == null && workspaceOperation == null && !explorerTabs.IsBusy;
            ShowShortcut(settingsItem, ShortcutChord(HostCommand.Settings));
            menu.Items.Add(new ToolStripSeparator());
            var restoreItem = menu.Items.Add("关闭（分离所有窗口）", null, (_, __) => QueueMenuAction(CloseRestoringWindows));
            restoreItem.ToolTipText = "分离所有会话窗口为独立 Explorer，然后退出应用。";
            var closeItem = menu.Items.Add("关闭", null, (_, __) => QueueMenuAction(RequestExitByClosingSessions));
            ShowShortcut(closeItem, "Alt+F4");
            closeItem.ToolTipText = "关闭所有会话窗口并退出应用。";
            // About is informational: keep it in its own group at the bottom.
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("关于", null, (_, __) => QueueMenuAction(ShowAbout));
            menu.ShowItemToolTips = true;
            return menu;
        }
        private void ShowAppMenu(Point screenPoint)
        {
            if (closing || exitPreparing) return;
            ShowManagedMenuAtScreenPoint(CreateAppMenu(), screenPoint);
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style = (cp.Style | 0x000F0000) & ~0x00C00000; return cp; } // thickframe, sysmenu, min/max; no native caption
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0211 && nativeMenuScope == null && !disposingUi) nativeMenuScope = EnterModal();
            if (m.Msg == 0x0212) { var scope = nativeMenuScope; nativeMenuScope = null; scope?.Dispose(); }
            if (m.Msg == 0x0083) // WM_NCCALCSIZE
            {
                if (m.WParam == IntPtr.Zero) { base.WndProc(ref m); return; }
                var p = Marshal.PtrToStructure<NcCalcSizeParams>(m.LParam);
                int windowTop = p.Rc0.Top;
                base.WndProc(ref m);
                if (!IsZoomed(Handle))
                {
                    // Extend the client through the native top frame: its opaque system paint
                    // shows as a foreign-coloured strip above the tabs. Side / bottom frames stay.
                    p = Marshal.PtrToStructure<NcCalcSizeParams>(m.LParam);
                    p.Rc0.Top = windowTop;
                    Marshal.StructureToPtr(p, m.LParam, false);
                }
                return;
            }
            if (m.Msg == 0x0084) // WM_NCHITTEST
            {
                long value = m.LParam.ToInt64(); var screen = new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff));
                var p = PointToClient(screen); int band = Padding.Top;
                if (!IsZoomed(Handle) && p.Y >= 0 && p.Y < band)
                {
                    bool left = p.X < band, right = p.X >= ClientSize.Width - band;
                    m.Result = new IntPtr(left ? 13 : right ? 14 : 12); return;
                }
                base.WndProc(ref m); return;
            }
            if (m.Msg == 0x0086) // WM_NCACTIVATE
            {
                // Letting the default handler switch to the inactive frame paints an opaque
                // system-coloured strip across the top band on Windows 10. Keep the previous
                // frame state instead; the host chrome paints its own active visuals.
                m.Result = DefWindowProc(Handle, m.Msg, m.WParam, new IntPtr(-1)); return;
            }
            if (m.Msg == 0x0024) // WM_GETMINMAXINFO
            {
                // The native frame overhangs the work area when maximized, so keep the client
                // exactly equal to the work area instead of covering the taskbar.
                base.WndProc(ref m);
                var info = Marshal.PtrToStructure<MinMaxInfo>(m.LParam); var screen = Screen.FromHandle(Handle);
                int side = Math.Max(0, (Width - ClientSize.Width) / 2);
                int bottom = Math.Max(0, Height - ClientSize.Height);
                info.MaxPosition = new NativeChromePoint
                {
                    X = screen.WorkingArea.Left - screen.Bounds.Left - side,
                    Y = screen.WorkingArea.Top - screen.Bounds.Top - side
                };
                info.MaxSize = new NativeChromePoint
                {
                    X = screen.WorkingArea.Width + side * 2,
                    Y = screen.WorkingArea.Height + side + bottom
                };
                info.MinTrackSize = new NativeChromePoint { X = MinimumSize.Width, Y = MinimumSize.Height };
                Marshal.StructureToPtr(info, m.LParam, false); return;
            }
            base.WndProc(ref m);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SyncTopResizeBand();
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NcCalcSizeParams { public NativeRect Rc0, Rc1, Rc2; public IntPtr WindowPos; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeChromePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativeChromePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
    }
}
