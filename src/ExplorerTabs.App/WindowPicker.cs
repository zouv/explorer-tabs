using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal sealed class WindowPicker : ShellDialog
    {
        private readonly ListView list = new ListView();
        private readonly HashSet<long> attached;
        public IReadOnlyList<WindowIdentity> SelectedWindows { get; private set; } = Array.Empty<WindowIdentity>();

        public WindowPicker(IEnumerable<long> attachedHandles)
        {
            attached = new HashSet<long>(attachedHandles);
            Text = "选择要加入的 Explorer 窗口";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(820, 508); StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; BackColor = Color.White;
            list.Dock = DockStyle.Fill;
            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = true; list.BorderStyle = BorderStyle.FixedSingle;
            list.Columns.Add("窗口标题", 320);
            list.Columns.Add("PID / HWND", 210);
            list.Columns.Add("DPI awareness", 150);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 10, 0, 0), FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };
            var add = new ActionButton("加入所选", true) { Width = 104, Height = 30 };
            var refresh = new ActionButton("刷新") { Width = 84, Height = 30 };
            var cancel = new ActionButton("取消") { Width = 84, Height = 30, DialogResult = DialogResult.Cancel };
            add.Click += (_, __) =>
            {
                SelectedWindows = list.SelectedItems.Cast<ListViewItem>().Select(item => (WindowIdentity)item.Tag).ToArray();
                if (SelectedWindows.Count == 0) return;
                DialogResult = DialogResult.OK;
            };
            refresh.Click += (_, __) => RefreshWindows();
            bottom.Controls.AddRange(new Control[] { add, cancel, refresh });
            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 10), BackColor = Color.White };
            content.Controls.Add(list); content.Controls.Add(bottom);
            Controls.Add(content);
            AcceptButton = add; CancelButton = cancel;
            RefreshWindows();
            DialogStyle.Apply(this);
        }

        private void RefreshWindows()
        {
            list.Items.Clear();
            foreach (var window in NativeWindowApi.DiscoverExplorers().Where(window => !attached.Contains(window.Handle)))
            {
                var item = new ListViewItem(window.Title) { Tag = window };
                item.SubItems.Add($"{window.ProcessId} / 0x{window.Handle:X}");
                item.SubItems.Add(NativeWindowApi.DescribeDpi(window.Hwnd));
                list.Items.Add(item);
            }
        }
    }
}
