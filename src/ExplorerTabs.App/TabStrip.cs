using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    /// <summary>Only local pointer tracking. No OLE drag/drop, global hooks, or file-drop interception.</summary>
    internal sealed class TabStrip : FlowLayoutPanel
    {
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer scroll = new Timer { Interval = 60 };
        private TabButton? source;
        private Control? trailing;
        private Point origin;
        private Point pointer;
        private bool dragging;
        private int insertion;
        private bool adjusting;
        private int maxRows = 3;
        private Func<WindowSession, string?>? directoryLookup;
        private const int MinimumTabWidth = 110, MaximumTabWidth = 280;
        public int PreferredChromeHeight { get; private set; } = 36;
        public event Action? PreferredChromeHeightChanged;
        private int Pixels(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96F));
        private void SetStripHeight(int height)
        {
            if (PreferredChromeHeight == height) return;
            PreferredChromeHeight = height;
            if (Dock != DockStyle.Fill) Height = height;
            PreferredChromeHeightChanged?.Invoke();
        }
        public bool IsInteracting => source != null || Controls.Cast<Control>().Any(c => c.ContextMenuStrip?.Visible == true);
        public event Action<WindowSession>? Selected;
        public event Action<WindowSession>? MiddleClose;
        public event Action<WindowSession, int>? MoveRequested;
        public event Action<bool>? InteractionChanged;

        public TabStrip()
        {
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = false;
            AutoScroll = true;
            DoubleBuffered = true;
            scroll.Tick += (_, __) => AutoScrollDrag();
        }
        // The host reserves only a 3 px top band for visuals; the first 4 px of the strip
        // (and its tabs) still answer HTTOP so the usable top resize zone stays ~7 px.
        private bool TryTopResize(ref Message m, Control control)
        {
            if (m.Msg != 0x0084) return false;
            var form = FindForm();
            if (form == null || form.WindowState == FormWindowState.Maximized) return false;
            long value = m.LParam.ToInt64();
            var screen = new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff));
            if (control.PointToClient(screen).Y >= Pixels(4)) return false;
            m.Result = new IntPtr(12); return true;
        }
        protected override void WndProc(ref Message m)
        {
            if (TryTopResize(ref m, this)) return;
            base.WndProc(ref m);
        }

        public void Render(IReadOnlyList<WindowSession> sessions, WindowSession? active,
            Func<WindowSession, ContextMenuStrip> createMenu,
            Func<WindowSession, string?>? directoryLookup = null)
        {
            CancelPointer();
            this.directoryLookup = directoryLookup;
            var offset = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);
            SuspendLayout();
            try
            {
                tips.RemoveAll();
                foreach (Control control in Controls.Cast<Control>().Where(c => c is TabButton).ToArray())
                {
                    var menu = control.ContextMenuStrip;
                    Controls.Remove(control);
                    control.Dispose();
                    menu?.Dispose();
                }
                foreach (var session in sessions)
                {
                    var button = new TabButton(this, session)
                    {
                        Text = (session.State == SessionState.Attached ? "" : "⚠ ") + session.DisplayTitle,
                        Height = Pixels(32), Margin = new Padding(Pixels(2), 0, Pixels(2), 0),
                        IsActive = active == session, FlatStyle = FlatStyle.Flat, AutoEllipsis = true,
                        UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 6, 0),
                        BackColor = active == session ? HostTheme.Canvas : HostTheme.Chrome,
                        AccessibleName = session.DisplayTitle,
                        AccessibleDescription = "右键管理；悬停显示关闭按钮；中键关闭 Explorer。"
                    };
                    button.FlatAppearance.BorderSize = active == session ? 1 : 0;
                    button.FlatAppearance.BorderColor = Color.FromArgb(74, 127, 202);
                    button.ContextMenuStrip = createMenu(session);
                    tips.SetToolTip(button, TooltipFor(session, directoryLookup?.Invoke(session)));
                    button.Width = MeasureTabWidth(button);
                    Controls.Add(button);
                }
                if (trailing != null && Controls.Contains(trailing)) Controls.SetChildIndex(trailing, Controls.Count - 1);
                // UpdateRows sizes the viewport before publishing the new scroll extent.
                // A transient horizontal scrollbar at the old height can leave vertical scrolling stuck on.
            }
            finally { ResumeLayout(false); UpdateRows(); AutoScrollPosition = offset; }
        }

        /// <summary>Keeps a non-tab action (new tab) flowing directly after the last tab.</summary>
        public void SetTrailingControl(Control control)
        {
            trailing = control;
            if (!Controls.Contains(control)) Controls.Add(control);
            UpdateRows();
        }

        public void Reveal(WindowSession session)
        {
            var button = Controls.OfType<TabButton>().FirstOrDefault(b => b.Session == session);
            if (button == null) return;
            if (WrapContents) { ScrollControlIntoView(button); return; }
            int offset = -AutoScrollPosition.X;
            if (button.Left < Padding.Left) offset += button.Left - Padding.Left;
            else if (button.Right > ClientSize.Width - Padding.Right)
                offset += button.Right - (ClientSize.Width - Padding.Right);
            AutoScrollPosition = new Point(Math.Max(0, offset), 0);
        }

        public void ConfigureLayout(bool wrap, int rows)
        { CancelPointer(); WrapContents = wrap; maxRows = Math.Max(2, Math.Min(8, rows)); AutoScrollMinSize = Size.Empty; AutoScrollPosition = Point.Empty; UpdateRows(); PerformLayout(); }
        // Match the actual single-line painter, including folder glyph, close column and text padding.
        private int MeasureTabWidth(Control button)
        {
            int text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            return Math.Max(Pixels(MinimumTabWidth), Math.Min(Pixels(MaximumTabWidth), text + Pixels(63)));
        }
        private int AssignWrappedRows(int available)
        {
            int rows = Controls.Count == 0 ? 0 : 1, used = 0;
            Control? previous = null;
            foreach (Control c in Controls)
            {
                if (c != trailing) c.Width = Math.Min(MeasureTabWidth(c), Math.Max(1, available - c.Margin.Horizontal));
                SetFlowBreak(c, false);
                int extent = c.Width + c.Margin.Horizontal;
                if (used > 0 && used + extent > available)
                { if (previous != null) SetFlowBreak(previous, true); rows++; used = 0; }
                used += extent; previous = c;
            }
            return rows;
        }
        private void UpdateRows()
        {
            if (adjusting) return;
            adjusting = true;
            SuspendLayout();
            try
            {
                SizeTrailing();
                if (!WrapContents)
                {
                    foreach (Control c in Controls)
                    { if (c == trailing) continue; c.Width = MeasureTabWidth(c); SetFlowBreak(c, false); }
                    int extent = Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal) + Padding.Horizontal;
                    SetStripHeight(Pixels(36) + (extent > Width ? SystemInformation.HorizontalScrollBarHeight : 0));
                    AutoScrollMinSize = new Size(extent, 0);
                }
                else
                {
                    // ClientSize already excludes any currently visible scrollbar. Start with the
                    // full viewport, then reserve a vertical scrollbar only when rows exceed the cap.
                    int available = Math.Max(1, Width - Padding.Horizontal);
                    int rows = AssignWrappedRows(available);
                    if (rows > maxRows) rows = AssignWrappedRows(Math.Max(1, available - SystemInformation.VerticalScrollBarWidth));
                    int rowHeight = Controls.Count == 0 ? Pixels(32) : Controls.Cast<Control>().Max(c => c.Height + c.Margin.Vertical);
                    SetStripHeight(Math.Max(Pixels(36), Padding.Vertical + rowHeight * Math.Min(maxRows, rows)));
                    AutoScrollMinSize = new Size(0, Padding.Vertical + rows * rowHeight);
                }
            }
            finally { ResumeLayout(true); adjusting = false; }
        }
        private void SizeTrailing()
        {
            if (trailing == null) return;
            trailing.Size = new Size(Pixels(32), Pixels(32));
            trailing.Margin = new Padding(Pixels(4), 0, Pixels(2), 0);
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (!adjusting) UpdateRows(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (!adjusting) UpdateRows(); }
        protected override void OnDpiChangedAfterParent(EventArgs e)
        { base.OnDpiChangedAfterParent(e); if (!adjusting) UpdateRows(); }

        internal void BeginPointer(TabButton button, MouseButtons buttons, Point point)
        {
            CancelPointer();
            if (buttons != MouseButtons.Left) return;
            source = button;
            origin = pointer = point;
            dragging = false;
            InteractionChanged?.Invoke(true);
        }

        internal void MovePointer(TabButton button, Point point)
        {
            if (source != button) return;
            pointer = point;
            if (!dragging && TabManagementPolicy.StartsDrag(origin, pointer, SystemInformation.DragSize))
            {
                dragging = true;
                Cursor = Cursors.SizeWE;
                scroll.Start();
            }
            if (dragging) UpdateInsertion();
        }

        internal bool IsDragging(TabButton button) => source == button && dragging;
        internal TabMove? EndPointer(TabButton button, MouseButtons buttons, Point point)
        {
            TabMove? move = null;
            if (source == button && dragging && buttons == MouseButtons.Left && ClientRectangle.Contains(point))
            {
                pointer = point;
                UpdateInsertion();
                move = new TabMove(button.Session, insertion);
            }
            CancelPointer();
            return move;
        }

        internal void CancelPointer()
        {
            bool wasInteracting = source != null;
            source = null;
            dragging = false;
            scroll.Stop();
            Cursor = Cursors.Default;
            Invalidate();
            if (wasInteracting) InteractionChanged?.Invoke(false);
        }

        internal void PublishMove(TabMove move) => MoveRequested?.Invoke(move.Session, move.Index);
        internal static string TooltipFor(WindowSession session, string? directory) =>
            session.DisplayTitle + "\nExplorer：" + session.Title + "\n目录：" +
            (string.IsNullOrWhiteSpace(directory) ? "未缓存" : directory);
        internal void RefreshTip(TabButton button) =>
            tips.SetToolTip(button, TooltipFor(button.Session, directoryLookup?.Invoke(button.Session)));
        private void Queue(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            // Finish the native mouse message/capture release before destroying tab button HWNDs.
            BeginInvoke(new Action(() => { if (!IsDisposed) action(); }));
        }
        internal void Select(WindowSession session) => Selected?.Invoke(session);
        internal void Close(WindowSession session) => MiddleClose?.Invoke(session);

        private TabButton[] Remaining() => Controls.OfType<TabButton>().Where(b => b != source).ToArray();
        private void UpdateInsertion()
        {
            var remaining = Remaining();
            insertion = WrapContents ? remaining.TakeWhile(b => pointer.Y > b.Bottom ||
                (pointer.Y >= b.Top && pointer.Y <= b.Bottom && pointer.X > b.Left + b.Width / 2)).Count() :
                TabManagementPolicy.InsertionIndex(remaining.Select(b => b.Left + b.Width / 2), pointer.X);
            Invalidate();
        }
        private void AutoScrollDrag()
        {
            if (source == null || !dragging) return;
            if (!source.Capture) { CancelPointer(); return; }
            pointer = PointToClient(MousePosition);
            int edge = Math.Max(16, SystemInformation.DragSize.Width * 2);
            if (pointer.Y < 0 || pointer.Y >= ClientSize.Height) return;
            int direction = WrapContents ? (pointer.Y < edge ? -1 : pointer.Y >= ClientSize.Height - edge ? 1 : 0) : pointer.X < edge ? -1 : pointer.X >= ClientSize.Width - edge ? 1 : 0;
            if (direction != 0)
            {
                if (WrapContents) direction = pointer.Y < edge ? -1 : pointer.Y >= ClientSize.Height - edge ? 1 : 0;
                AutoScrollPosition = WrapContents ? new Point(0, Math.Max(0, -AutoScrollPosition.Y + direction * 24)) : new Point(Math.Max(0, -AutoScrollPosition.X + direction * 24), 0);
                UpdateInsertion();
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!dragging || source == null) return;
            var remaining = Remaining();
            int x = insertion < remaining.Length ? remaining[insertion].Left - 2 :
                remaining.Length > 0 ? remaining[remaining.Length - 1].Right + 2 : source.Left - 2;
            using (var pen = new Pen(Color.FromArgb(40, 110, 210), 3))
                {
                    int y = WrapContents ? (insertion < remaining.Length ? remaining[insertion].Top : remaining.Length > 0 ? remaining.Last().Top : source.Top) : Padding.Top;
                    e.Graphics.DrawLine(pen, Math.Max(1, x), y, Math.Max(1, x), Math.Min(ClientSize.Height - 4, y + 32));
                }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelPointer(); scroll.Dispose(); tips.Dispose();
                foreach (Control control in Controls) control.ContextMenuStrip?.Dispose();
            }
            base.Dispose(disposing);
        }

        internal sealed class TabMove
        {
            public WindowSession Session { get; }
            public int Index { get; }
            public TabMove(WindowSession session, int index) { Session = session; Index = index; }
        }

        internal sealed class TabButton : Button
        {
            private readonly TabStrip strip;
            private bool suppressClick;
            private bool releasingMouse;
            public WindowSession Session { get; }
            public bool IsActive { get; set; }
            private bool hovered, closeHovered, closePressed;
            public TabButton(TabStrip strip, WindowSession session)
            { this.strip = strip; Session = session; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
            private bool ShowCloseButton => hovered || closePressed;
            internal Rectangle CloseButtonBounds
            {
                get
                {
                    int size = strip.Pixels(16);
                    return new Rectangle(Width - strip.Pixels(18), (Height - size) / 2, size, size);
                }
            }
            internal bool HitClose(Point point) =>
                Session.State == SessionState.Attached && ShowCloseButton && CloseButtonBounds.Contains(point);
            protected override void OnMouseEnter(EventArgs e)
            { hovered = true; strip.RefreshTip(this); Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e)
            { hovered = closeHovered = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Parent?.BackColor ?? HostTheme.Chrome);
                Color fill = IsActive ? HostTheme.Canvas : hovered ? Color.FromArgb(219, 227, 239) : HostTheme.Chrome;
                using (var brush = new SolidBrush(fill)) using (var round = HostTheme.Round(new RectangleF(0, 0, Width - 1, Height + 5), 6)) g.FillPath(brush, round);
                if (IsActive) using (var pen = new Pen(HostTheme.Accent, 2)) g.DrawLine(pen, 10, Height - 2, Width - 10, Height - 2);
                else using (var pen = new Pen(Color.FromArgb(204, 214, 227))) g.DrawLine(pen, Width - 1, 9, Width - 1, Height - 9);
                int icon = strip.Pixels(16), left = strip.Pixels(10);
                using (var pen = new Pen(IsActive ? HostTheme.Accent : Color.FromArgb(115, 131, 153), 1.3F))
                { int y = (Height - icon) / 2; g.DrawRectangle(pen, left, y + 4, icon, icon - 5); g.DrawLines(pen, new[] { new Point(left, y + 4), new Point(left, y + 1), new Point(left + icon / 2, y + 1), new Point(left + icon / 2 + 2, y + 4) }); }
                TextRenderer.DrawText(g, Text, Font, new Rectangle(left + icon + strip.Pixels(7), 0, Width - left - icon - strip.Pixels(37), Height), HostTheme.Text,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (ShowCloseButton)
                {
                    var close = CloseButtonBounds;
                    if (closeHovered)
                        using (var brush = new SolidBrush(Color.FromArgb(238, 222, 224)))
                        using (var path = HostTheme.Round(new RectangleF(close.X - 2, close.Y - 2, close.Width + 4, close.Height + 4), 4))
                            g.FillPath(brush, path);
                    HostTheme.Glyph(g, UiGlyph.Close, close,
                        closeHovered ? Color.FromArgb(166, 49, 49) : Color.FromArgb(115, 131, 153));
                }
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
            }
            private Point StripPoint(Point point) => strip.PointToClient(PointToScreen(point));
            protected override void OnMouseDown(MouseEventArgs e)
            {
                suppressClick = false;
                closePressed = e.Button == MouseButtons.Left && HitClose(e.Location);
                if (!closePressed) strip.BeginPointer(this, e.Button, StripPoint(e.Location));
                base.OnMouseDown(e);
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool overClose = HitClose(e.Location);
                if (overClose != closeHovered) { closeHovered = overClose; Invalidate(); }
                if (!closePressed) strip.MovePointer(this, StripPoint(e.Location));
                base.OnMouseMove(e);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (closePressed)
                {
                    bool close = e.Button == MouseButtons.Left && HitClose(e.Location);
                    closePressed = false;
                    suppressClick = true; // a press on the close glyph can never become a tab selection
                    Invalidate();
                    base.OnMouseUp(e);
                    if (close) strip.Queue(() => strip.Close(Session));
                    return;
                }
                suppressClick = suppressClick || strip.IsDragging(this);
                var move = strip.EndPointer(this, e.Button, StripPoint(e.Location));
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Middle) strip.Queue(() => strip.Close(Session));
                else if (move != null) strip.Queue(() => strip.PublishMove(move));
            }
            protected override void OnClick(EventArgs e)
            {
                // WinForms may fire Click BEFORE MouseUp; checking the live gesture is essential.
                if (suppressClick || closePressed || strip.IsDragging(this) || (e is MouseEventArgs mouse && mouse.Button != MouseButtons.Left)) return;
                base.OnClick(e);
                strip.Queue(() => strip.Select(Session));
            }
            protected override void OnMouseCaptureChanged(EventArgs e)
            {
                // A cancelled gesture (Esc / lost capture) must not turn into a later selection click.
                if (!Capture && !releasingMouse)
                {
                    suppressClick = suppressClick || closePressed || strip.IsDragging(this);
                    closePressed = false;
                    // Another tab may receive capture when this one is disposed/rebuilt.
                    if (strip.source == this) strip.CancelPointer();
                }
                base.OnMouseCaptureChanged(e);
            }
            protected override void WndProc(ref Message message)
            {
                if (strip.TryTopResize(ref message, this)) return;
                // Some button styles/native paths release capture before managed MouseUp. Preserve
                // the gesture through that release; genuine external capture loss still cancels it.
                bool mouseUp = message.Msg == 0x202; // WM_LBUTTONUP
                if (mouseUp) releasingMouse = true;
                try { base.WndProc(ref message); }
                finally
                {
                    if (mouseUp)
                    {
                        releasingMouse = false;
                        if (!Capture && strip.source == this) strip.CancelPointer();
                    }
                }
            }
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Escape && (strip.IsInteracting || closePressed))
                { suppressClick = true; closePressed = false; strip.CancelPointer(); Capture = false; return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }
    }
}
