using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        // Native WS_HSCROLL / WS_VSCROLL bars are deliberately disabled: they are tall, follow
        // the system theme and can feed into each other (a transient horizontal bar shrinks the
        // viewport height, which then keeps a vertical scrollbar stuck on). The strip lays its
        // children out manually and paints its own thin overlay bars instead.
        private int scrollX, scrollY;
        private int contentWidth, contentHeight, viewportWidth, viewportHeight, rowHeight;
        private int[] tabWidths = Array.Empty<int>();
        private int[] tabRows = Array.Empty<int>();
        private bool horizontalScrollVisible, verticalScrollVisible;
        private bool scrollbarDragging, scrollbarHovered, draggingHorizontalBar;
        private Point scrollbarDragStart;
        private int scrollbarDragValue;
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
            AutoScroll = false;
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
                // UpdateRows measures every tab and positions it before the new scroll extent
                // is applied, so a transient old extent can never resurrect a stale scrollbar.
            }
            finally { ResumeLayout(false); UpdateRows(); }
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
            if (WrapContents)
            {
                int top = Padding.Top, bottom = top + Math.Max(1, viewportHeight);
                if (button.Top < top) ApplyScroll(0, scrollY - (top - button.Top));
                else if (button.Bottom > bottom) ApplyScroll(0, scrollY + button.Bottom - bottom);
                return;
            }
            int left = Padding.Left, right = left + Math.Max(1, viewportWidth);
            if (button.Left < left) ApplyScroll(scrollX - (left - button.Left), 0);
            else if (button.Right > right) ApplyScroll(scrollX + button.Right - right, 0);
        }

        public void ConfigureLayout(bool wrap, int rows)
        {
            CancelPointer();
            maxRows = Math.Max(2, Math.Min(8, rows));
            scrollX = scrollY = 0;
            if (WrapContents != wrap) WrapContents = wrap;
            UpdateRows();
            PerformLayout();
        }
        // Match the actual single-line painter, including folder glyph, close column and text padding.
        private int MeasureTabWidth(Control button)
        {
            int text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            return Math.Max(Pixels(MinimumTabWidth), Math.Min(Pixels(MaximumTabWidth), text + Pixels(63)));
        }
        private int MeasureWrapped(TabButton[] tabs, int available, int trailingExtent)
        {
            tabWidths = new int[tabs.Length];
            tabRows = new int[tabs.Length];
            int row = 0, used = 0, lastRowStart = 0;
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                int width = Math.Min(MeasureTabWidth(tab), Math.Max(1, available - tab.Margin.Horizontal));
                tabWidths[i] = width;
                int extent = width + tab.Margin.Horizontal;
                if (used > 0 && used + extent > available) { row++; used = 0; lastRowStart = i; }
                tabRows[i] = row;
                used += extent;
            }
            if (tabs.Length > 0 && used + trailingExtent > available)
            {
                // Keep the new-tab action hugging the last tab instead of wrapping alone. Prefer
                // shaving the last tab a little; only move the tab down when it cannot shrink
                // below the minimum width any further.
                int last = tabs.Length - 1;
                int overflow = used + trailingExtent - available;
                if (tabWidths[last] - overflow >= Pixels(MinimumTabWidth)) tabWidths[last] -= overflow;
                else
                {
                    if (lastRowStart < last) { row++; tabRows[last] = row; }
                    int lastExtent = tabWidths[last] + tabs[last].Margin.Horizontal;
                    if (lastExtent + trailingExtent > available)
                        tabWidths[last] = Math.Max(1, available - tabs[last].Margin.Horizontal - trailingExtent);
                }
            }
            return tabs.Length == 0 ? 0 : row + 1;
        }
        private void UpdateRows()
        {
            if (adjusting) return;
            adjusting = true;
            SuspendLayout();
            try
            {
                SizeTrailing();
                var tabs = Controls.OfType<TabButton>().ToArray();
                int trailingExtent = trailing == null ? 0 : trailing.Width + trailing.Margin.Horizontal;
                rowHeight = tabs.Length == 0 && trailing == null ? Pixels(32) :
                    tabs.Select(t => t.Height + t.Margin.Vertical)
                        .Concat(trailing == null ? Enumerable.Empty<int>() : new[] { trailing.Height + trailing.Margin.Vertical })
                        .Max();
                if (WrapContents)
                {
                    int available = Math.Max(1, ClientSize.Width - Padding.Horizontal);
                    int rows = MeasureWrapped(tabs, available, trailingExtent);
                    if (rows > maxRows)
                        rows = MeasureWrapped(tabs, Math.Max(1, available - ScrollbarThickness), trailingExtent);
                    verticalScrollVisible = rows > maxRows;
                    horizontalScrollVisible = false;
                    viewportWidth = Math.Max(1, available - (verticalScrollVisible ? ScrollbarThickness : 0));
                    viewportHeight = rowHeight * Math.Min(maxRows, Math.Max(1, rows));
                    contentWidth = 0;
                    contentHeight = rowHeight * rows;
                    scrollX = 0;
                    scrollY = Math.Max(0, Math.Min(scrollY, Math.Max(0, contentHeight - viewportHeight)));
                    SetStripHeight(Math.Max(Pixels(36), Padding.Vertical + viewportHeight));
                }
                else
                {
                    tabWidths = new int[tabs.Length];
                    tabRows = new int[tabs.Length];
                    int extent = 0;
                    for (int i = 0; i < tabs.Length; i++)
                    {
                        tabWidths[i] = MeasureTabWidth(tabs[i]);
                        extent += tabWidths[i] + tabs[i].Margin.Horizontal;
                    }
                    if (trailing != null) extent += trailingExtent;
                    contentWidth = extent;
                    viewportWidth = Math.Max(1, ClientSize.Width - Padding.Horizontal);
                    viewportHeight = Math.Max(1, ClientSize.Height - Padding.Vertical);
                    contentHeight = 0;
                    horizontalScrollVisible = contentWidth > viewportWidth;
                    verticalScrollVisible = false;
                    scrollY = 0;
                    scrollX = Math.Max(0, Math.Min(scrollX, Math.Max(0, contentWidth - viewportWidth)));
                    SetStripHeight(Pixels(36));
                }
                PositionChildren(tabs);
            }
            finally { ResumeLayout(true); adjusting = false; Invalidate(); }
        }
        private void PositionChildren(TabButton[] tabs)
        {
            int x = Padding.Left - scrollX, currentRow = -1;
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                int row = WrapContents ? tabRows[i] : 0;
                if (row != currentRow) { x = Padding.Left - scrollX; currentRow = row; }
                tab.Bounds = new Rectangle(x + tab.Margin.Left, Padding.Top + row * rowHeight - scrollY,
                    tabWidths[i], tab.Height);
                x += tabWidths[i] + tab.Margin.Horizontal;
            }
            if (trailing != null)
            {
                int row = WrapContents && tabs.Length > 0 ? tabRows[tabs.Length - 1] : 0;
                trailing.Bounds = new Rectangle(x + trailing.Margin.Left, Padding.Top + row * rowHeight - scrollY,
                    trailing.Width, trailing.Height);
            }
        }
        private void SizeTrailing()
        {
            if (trailing == null) return;
            trailing.Size = new Size(Pixels(32), Pixels(32));
            trailing.Margin = new Padding(Pixels(4), 0, Pixels(2), 0);
        }

        private int ScrollbarThickness => Pixels(4);
        private Rectangle HorizontalBarBounds
        {
            get
            {
                // Keep the bar inside the spare pixels below the tab row so it never covers text.
                int slack = Math.Max(2, ClientSize.Height - Padding.Vertical - Pixels(32));
                int thickness = Math.Min(ScrollbarThickness, slack);
                return new Rectangle(Padding.Left, ClientSize.Height - thickness,
                    Math.Max(1, ClientSize.Width - Padding.Horizontal), thickness);
            }
        }
        private Rectangle VerticalBarBounds
        {
            get
            {
                int thickness = ScrollbarThickness;
                return new Rectangle(ClientSize.Width - thickness, Padding.Top, thickness,
                    Math.Max(1, Math.Min(viewportHeight, ClientSize.Height - Padding.Vertical)));
            }
        }
        private Rectangle HorizontalThumbBounds
        {
            get
            {
                var track = HorizontalBarBounds;
                int size = Math.Min(track.Width, Math.Max(Pixels(20),
                    (int)((long)viewportWidth * track.Width / Math.Max(1, contentWidth))));
                int range = Math.Max(0, contentWidth - viewportWidth);
                int x = range == 0 ? track.Left : track.Left + (int)((long)scrollX * (track.Width - size) / range);
                return new Rectangle(x, track.Top, Math.Max(1, size), track.Height);
            }
        }
        private Rectangle VerticalThumbBounds
        {
            get
            {
                var track = VerticalBarBounds;
                int size = Math.Min(track.Height, Math.Max(Pixels(20),
                    (int)((long)viewportHeight * track.Height / Math.Max(1, contentHeight))));
                int range = Math.Max(0, contentHeight - viewportHeight);
                int y = range == 0 ? track.Top : track.Top + (int)((long)scrollY * (track.Height - size) / range);
                return new Rectangle(track.Left, y, track.Width, Math.Max(1, size));
            }
        }
        private void ApplyScroll(int x, int y)
        {
            int maxX = Math.Max(0, contentWidth - viewportWidth);
            int maxY = Math.Max(0, contentHeight - viewportHeight);
            x = Math.Max(0, Math.Min(x, maxX)); y = Math.Max(0, Math.Min(y, maxY));
            if (x == scrollX && y == scrollY) return;
            scrollX = x; scrollY = y;
            PositionChildren(Controls.OfType<TabButton>().ToArray());
            Invalidate();
        }
        private bool ScrollByWheel(int delta)
        {
            int x = scrollX, y = scrollY;
            if (WrapContents)
            { if (verticalScrollVisible) ApplyScroll(0, y - Math.Sign(delta) * Math.Max(Pixels(16), rowHeight)); }
            else if (horizontalScrollVisible) ApplyScroll(x - Math.Sign(delta) * Pixels(60), 0);
            return x != scrollX || y != scrollY;
        }
        private bool HitScrollbar(Point point) =>
            (horizontalScrollVisible && HorizontalBarBounds.Contains(point)) ||
            (verticalScrollVisible && VerticalBarBounds.Contains(point));
        private void BeginScrollbarDrag(Point point)
        {
            if (horizontalScrollVisible && HorizontalBarBounds.Contains(point))
            {
                scrollbarDragging = true; draggingHorizontalBar = true; Capture = true;
                if (!HorizontalThumbBounds.Contains(point)) CenterHorizontalThumb(point.X);
            }
            else if (verticalScrollVisible && VerticalBarBounds.Contains(point))
            {
                scrollbarDragging = true; draggingHorizontalBar = false; Capture = true;
                if (!VerticalThumbBounds.Contains(point)) CenterVerticalThumb(point.Y);
            }
            else return;
            scrollbarDragStart = point;
            scrollbarDragValue = draggingHorizontalBar ? scrollX : scrollY;
            Invalidate();
        }
        private void CenterHorizontalThumb(int x)
        {
            var track = HorizontalBarBounds; var thumb = HorizontalThumbBounds;
            int range = track.Width - thumb.Width, scrollRange = Math.Max(0, contentWidth - viewportWidth);
            if (range <= 0 || scrollRange == 0) return;
            int target = x - track.Left - thumb.Width / 2;
            ApplyScroll((int)((long)target * scrollRange / range), 0);
        }
        private void CenterVerticalThumb(int y)
        {
            var track = VerticalBarBounds; var thumb = VerticalThumbBounds;
            int range = track.Height - thumb.Height, scrollRange = Math.Max(0, contentHeight - viewportHeight);
            if (range <= 0 || scrollRange == 0) return;
            int target = y - track.Top - thumb.Height / 2;
            ApplyScroll(0, (int)((long)target * scrollRange / range));
        }
        private void DragScrollbar(Point point)
        {
            if (draggingHorizontalBar)
            {
                var track = HorizontalBarBounds; var thumb = HorizontalThumbBounds;
                int range = track.Width - thumb.Width, scrollRange = Math.Max(0, contentWidth - viewportWidth);
                if (range <= 0 || scrollRange == 0) return;
                ApplyScroll(scrollbarDragValue + (int)((long)(point.X - scrollbarDragStart.X) * scrollRange / range), 0);
            }
            else
            {
                var track = VerticalBarBounds; var thumb = VerticalThumbBounds;
                int range = track.Height - thumb.Height, scrollRange = Math.Max(0, contentHeight - viewportHeight);
                if (range <= 0 || scrollRange == 0) return;
                ApplyScroll(0, scrollbarDragValue + (int)((long)(point.Y - scrollbarDragStart.Y) * scrollRange / range));
            }
        }
        private void EndScrollbarDrag()
        {
            if (!scrollbarDragging) return;
            scrollbarDragging = false; Capture = false; Invalidate();
        }
        internal bool HasHorizontalScrollbar() => horizontalScrollVisible;
        internal bool HasVerticalScrollbar() => verticalScrollVisible;
        internal Point ScrollPosition() => new Point(scrollX, scrollY);
        internal void ResetScrollPosition()
        {
            if (scrollX == 0 && scrollY == 0) return;
            scrollX = scrollY = 0;
            PositionChildren(Controls.OfType<TabButton>().ToArray());
            Invalidate();
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (!adjusting) UpdateRows(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (!adjusting) UpdateRows(); }
        protected override void OnDpiChangedAfterParent(EventArgs e)
        { base.OnDpiChangedAfterParent(e); if (!adjusting) UpdateRows(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (adjusting) return;
            // Let FlowLayoutPanel do its own arrangement first, then apply the measured rows and
            // the custom scroll offset so framework-triggered layouts cannot undo them.
            base.OnLayout(e);
            UpdateRows();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && HitScrollbar(e.Location)) { BeginScrollbarDrag(e.Location); return; }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (scrollbarDragging) { DragScrollbar(e.Location); return; }
            bool over = HitScrollbar(e.Location);
            if (over != scrollbarHovered) { scrollbarHovered = over; Cursor = over ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (scrollbarDragging && e.Button == MouseButtons.Left) { EndScrollbarDrag(); return; }
            base.OnMouseUp(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            if (!scrollbarDragging && scrollbarHovered) { scrollbarHovered = false; Cursor = Cursors.Default; Invalidate(); }
            base.OnMouseLeave(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture && scrollbarDragging) { scrollbarDragging = false; Invalidate(); }
            base.OnMouseCaptureChanged(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            bool scrolled = ScrollByWheel(e.Delta);
            base.OnMouseWheel(e);
            if (scrolled && e is HandledMouseEventArgs handled) handled.Handled = true;
        }

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
            (string.IsNullOrWhiteSpace(directory) ? "未缓存" : FolderTarget.Display(directory!));
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
            if (WrapContents)
            {
                int direction = pointer.Y < edge ? -1 : pointer.Y >= ClientSize.Height - edge ? 1 : 0;
                if (direction == 0) return;
                ApplyScroll(0, scrollY + direction * Pixels(24));
            }
            else
            {
                int direction = pointer.X < edge ? -1 : pointer.X >= ClientSize.Width - edge ? 1 : 0;
                if (direction == 0) return;
                ApplyScroll(scrollX + direction * Pixels(24), 0);
            }
            UpdateInsertion();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (dragging && source != null)
            {
                var remaining = Remaining();
                int x = insertion < remaining.Length ? remaining[insertion].Left - 2 :
                    remaining.Length > 0 ? remaining[remaining.Length - 1].Right + 2 : source.Left - 2;
                using (var pen = new Pen(Color.FromArgb(40, 110, 210), 3))
                {
                    int y = WrapContents ? (insertion < remaining.Length ? remaining[insertion].Top : remaining.Length > 0 ? remaining.Last().Top : source.Top) : Padding.Top;
                    e.Graphics.DrawLine(pen, Math.Max(1, x), y, Math.Max(1, x), Math.Min(ClientSize.Height - 4, y + 32));
                }
            }
            PaintScrollbars(e.Graphics);
        }
        private void PaintScrollbars(Graphics g)
        {
            if (horizontalScrollVisible) PaintScrollbar(g, HorizontalThumbBounds, scrollbarDragging && draggingHorizontalBar);
            if (verticalScrollVisible) PaintScrollbar(g, VerticalThumbBounds, scrollbarDragging && !draggingHorizontalBar);
        }
        private void PaintScrollbar(Graphics g, Rectangle bounds, bool active)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color color = active ? Color.FromArgb(215, 78, 96, 122) :
                    scrollbarHovered ? Color.FromArgb(185, 96, 114, 141) : Color.FromArgb(135, 115, 131, 153);
                using (var brush = new SolidBrush(color))
                using (var path = HostTheme.Round(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    Math.Max(1F, Math.Min(bounds.Width, bounds.Height) / 2F)))
                    g.FillPath(brush, path);
            }
            finally { g.Restore(state); }
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
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                bool scrolled = strip.ScrollByWheel(e.Delta);
                base.OnMouseWheel(e);
                if (scrolled && e is HandledMouseEventArgs handled) handled.Handled = true;
            }
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
