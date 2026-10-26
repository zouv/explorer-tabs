using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal sealed class WindowPicker : Form
    {
        private readonly ListView list = new ListView();
        private readonly HashSet<long> attached;
        public IReadOnlyList<WindowIdentity> SelectedWindows { get; private set; } = Array.Empty<WindowIdentity>();

        public WindowPicker(IEnumerable<long> attachedHandles)
        {
            attached = new HashSet<long>(attachedHandles);
            Text = "选择要加入的 Explorer 窗口";
            Width = 780; Height = 420;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            list.Dock = DockStyle.Fill;
            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = true;
            list.Columns.Add("窗口标题", 320);
            list.Columns.Add("PID / HWND", 210);
            list.Columns.Add("DPI awareness", 150);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
            var add = new Button { Text = "加入所选", AutoSize = true };
            var refresh = new Button { Text = "刷新", AutoSize = true };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            add.Click += (_, __) =>
            {
                SelectedWindows = list.SelectedItems.Cast<ListViewItem>().Select(item => (WindowIdentity)item.Tag).ToArray();
                if (SelectedWindows.Count == 0) return;
                DialogResult = DialogResult.OK;
            };
            refresh.Click += (_, __) => RefreshWindows();
            bottom.Controls.AddRange(new Control[] { add, refresh, cancel });
            Controls.Add(list); Controls.Add(bottom);
            AcceptButton = add; CancelButton = cancel;
            RefreshWindows();
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
