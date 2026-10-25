using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabHost.Core;

namespace ExplorerTabHost.App
{
    /// <summary>Only local pointer tracking. No OLE drag/drop, global hooks, or file-drop interception.</summary>
    internal sealed class TabStrip : FlowLayoutPanel
    {
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer scroll = new Timer { Interval = 60 };
        private TabButton? source;
        private Point origin;
        private Point pointer;
        private bool dragging;
        private int insertion;
        public bool IsInteracting => source != null || Controls.Cast<Control>().Any(c => c.ContextMenuStrip?.Visible == true);
        public event Action<WindowSession>? Selected;
        public event Action<WindowSession>? MiddleDetach;
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

        public void Render(IReadOnlyList<WindowSession> sessions, WindowSession? active,
            Func<WindowSession, ContextMenuStrip> createMenu)
        {
            CancelPointer();
            int offset = -AutoScrollPosition.X;
            SuspendLayout();
            try
            {
                tips.RemoveAll();
                foreach (Control control in Controls.Cast<Control>().ToArray())
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
                        Width = 210, Height = 30, FlatStyle = FlatStyle.Flat, AutoEllipsis = true,
                        UseMnemonic = false,
                        BackColor = active == session ? Color.FromArgb(210, 226, 249) : Color.White,
                        AccessibleName = session.DisplayTitle,
                        AccessibleDescription = "拖动排序；右键管理；中键分离为独立窗口，不关闭 Explorer。"
                    };
                    button.ContextMenuStrip = createMenu(session);
                    tips.SetToolTip(button, session.DisplayTitle + "\nExplorer：" + session.Title +
                        "\n拖动排序 · 右键管理 · 中键分离（不关闭）");
                    Controls.Add(button);
                }
                // Include outer padding explicitly; the flow layout extent alone can stop short of the last button.
                AutoScrollMinSize = new Size(Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal) + Padding.Horizontal, 0);
            }
            finally { ResumeLayout(true); AutoScrollPosition = new Point(offset, 0); }
        }

        public void Reveal(WindowSession session)
        {
            var button = Controls.OfType<TabButton>().FirstOrDefault(b => b.Session == session);
            if (button == null) return;
            int offset = -AutoScrollPosition.X;
            if (button.Left < Padding.Left) offset += button.Left - Padding.Left;
            else if (button.Right > ClientSize.Width - Padding.Right)
                offset += button.Right - (ClientSize.Width - Padding.Right);
            AutoScrollPosition = new Point(Math.Max(0, offset), 0);
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
        private void Queue(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            // Finish the native mouse message/capture release before destroying tab button HWNDs.
            BeginInvoke(new Action(() => { if (!IsDisposed) action(); }));
        }
        internal void Select(WindowSession session) => Selected?.Invoke(session);
        internal void Detach(WindowSession session) => MiddleDetach?.Invoke(session);

        private TabButton[] Remaining() => Controls.OfType<TabButton>().Where(b => b != source).ToArray();
        private void UpdateInsertion()
        {
            insertion = TabManagementPolicy.InsertionIndex(Remaining().Select(b => b.Left + b.Width / 2), pointer.X);
            Invalidate();
        }
        private void AutoScrollDrag()
        {
            if (source == null || !dragging) return;
            if (!source.Capture) { CancelPointer(); return; }
            pointer = PointToClient(MousePosition);
            int edge = Math.Max(16, SystemInformation.DragSize.Width * 2);
            if (pointer.Y < 0 || pointer.Y >= ClientSize.Height) return;
            int direction = pointer.X < edge ? -1 : pointer.X >= ClientSize.Width - edge ? 1 : 0;
            if (direction != 0)
            {
                AutoScrollPosition = new Point(Math.Max(0, -AutoScrollPosition.X + direction * 24), 0);
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
                e.Graphics.DrawLine(pen, Math.Max(1, x), Padding.Top, Math.Max(1, x), Math.Min(ClientSize.Height - 4, Padding.Top + 30));
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
            public TabButton(TabStrip strip, WindowSession session) { this.strip = strip; Session = session; }
            private Point StripPoint(Point point) => strip.PointToClient(PointToScreen(point));
            protected override void OnMouseDown(MouseEventArgs e)
            {
                suppressClick = false;
                strip.BeginPointer(this, e.Button, StripPoint(e.Location));
                base.OnMouseDown(e);
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                strip.MovePointer(this, StripPoint(e.Location));
                base.OnMouseMove(e);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                suppressClick = suppressClick || strip.IsDragging(this);
                var move = strip.EndPointer(this, e.Button, StripPoint(e.Location));
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Middle) strip.Queue(() => strip.Detach(Session));
                else if (move != null) strip.Queue(() => strip.PublishMove(move));
            }
            protected override void OnClick(EventArgs e)
            {
                // WinForms may fire Click BEFORE MouseUp; checking the live gesture is essential.
                if (suppressClick || strip.IsDragging(this) || (e is MouseEventArgs mouse && mouse.Button != MouseButtons.Left)) return;
                base.OnClick(e);
                strip.Queue(() => strip.Select(Session));
            }
            protected override void OnMouseCaptureChanged(EventArgs e)
            {
                // A cancelled gesture (Esc / lost capture) must not turn into a later selection click.
                if (!Capture && !releasingMouse)
                {
                    suppressClick = suppressClick || strip.IsDragging(this);
                    // Another tab may receive capture when this one is disposed/rebuilt.
                    if (strip.source == this) strip.CancelPointer();
                }
                base.OnMouseCaptureChanged(e);
            }
            protected override void WndProc(ref Message message)
            {
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
                if (keyData == Keys.Escape && strip.IsInteracting)
                { suppressClick = true; strip.CancelPointer(); Capture = false; return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }
    }
}
