using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExplorerTabs.Core;
namespace ExplorerTabs.App
{
    internal sealed class PickerEntry
    {
        public string Title { get; }
        public string Detail { get; }
        public object Value { get; }
        public PickerEntry(string title, string detail, object value) { Title = title; Detail = detail; Value = value; }
    }
    internal sealed class QuickPickerDialog : ShellDialog
    {
        private readonly TextBox query = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 10.5F), Margin = new Padding(0, 0, 0, 10) };
        private readonly ListBox results = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 52, BorderStyle = BorderStyle.None, BackColor = Color.White };
        private readonly Label count = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(102, 113, 131), Padding = new Padding(2, 8, 0, 0) };
        private readonly IReadOnlyList<PickerEntry> entries;
        public object? SelectedValue => (results.SelectedItem as PickerEntry)?.Value;
        public QuickPickerDialog(string title, IReadOnlyList<PickerEntry> entries)
        {
            this.entries = entries; Text = title; Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(660, 498); MinimumSize = new Size(480, 320); BackColor = Color.FromArgb(247, 249, 252);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 16, 10), ColumnCount = 1, RowCount = 3, BackColor = BackColor };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.Controls.Add(query, 0, 0); layout.Controls.Add(results, 0, 1); layout.Controls.Add(count, 0, 2); Controls.Add(layout);
            query.AccessibleName = "输入标签名或目录关键字"; results.AccessibleName = "匹配列表";
            query.HandleCreated += (_, __) => SetCue(query.Handle, "输入标签名或目录关键字…");
            query.TextChanged += (_, __) => Filter(); query.KeyDown += QueryKey;
            results.DoubleClick += (_, __) => AcceptSelection();
            results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AcceptSelection(); e.SuppressKeyPress = true; } };
            results.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                var item = (PickerEntry)results.Items[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? Color.FromArgb(227, 237, 251) : Color.White)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (selected) using (var accent = new SolidBrush(HostTheme.Accent)) e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, item.Title, Font, new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 6, e.Bounds.Width - 28, 22), Color.FromArgb(31, 43, 60), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, item.Detail, Font, new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 28, e.Bounds.Width - 28, 19), Color.FromArgb(101, 112, 130), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                using (var pen = new Pen(Color.FromArgb(236, 240, 246))) e.Graphics.DrawLine(pen, e.Bounds.Left + 14, e.Bounds.Bottom - 1, e.Bounds.Right - 14, e.Bounds.Bottom - 1);
            };
            Shown += (_, __) => query.Focus(); Filter();
            DialogStyle.Apply(this);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, string lparam);
        private static void SetCue(IntPtr hwnd, string text) => SendMessage(hwnd, 0x1501, new IntPtr(1), text);
        private void Filter()
        {
            results.BeginUpdate(); results.Items.Clear();
            foreach (var entry in entries.Where(e => TabSearchPolicy.Matches(e.Title, e.Detail, query.Text))) results.Items.Add(entry);
            if (results.Items.Count > 0) results.SelectedIndex = 0;
            results.EndUpdate(); count.Text = $"{results.Items.Count} / {entries.Count} 项 · ↑↓ 选择 · Enter 打开 · Esc 取消";
        }
        private void QueryKey(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                if (results.Items.Count > 0) results.SelectedIndex = Math.Max(0, Math.Min(results.Items.Count - 1, results.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1)));
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter) { AcceptSelection(); e.SuppressKeyPress = true; }
        }
        private void AcceptSelection() { if (SelectedValue != null) DialogResult = DialogResult.OK; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        { if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; } return base.ProcessCmdKey(ref msg, keys); }
    }
}
